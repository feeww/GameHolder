using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [Serializable]
    public class RewardTierSettings
    {
        public string Name;
        [Min(0)] public float Weight;
        [Min(.01f)] public float BonusPercent;
        public Color Color;
        public RewardTierSettings(string name, float weight, float bonus, Color color) { Name = name; Weight = weight; BonusPercent = bonus; Color = color; }
    }

    [Serializable]
    public class RewardSettings : ISerializationCallbackReceiver
    {
        [Tooltip("Number of reward cards offered per earned level. The player selects one card per level.")]
        [Range(1, RewardSelection.MaxChoices)] public int ChoicesPerLevel = 2;
        [Tooltip("Capacity can be reduced, but never exceeds two weapons.")]
        [Range(1, PlayerLoadout.Capacity)] public int MaxWeapons = 2;
        [Tooltip("Each slot independently rolls this chance. Full inventory or an exhausted weapon roster forces stat upgrades.")]
        [Range(0, 1)] public float NewWeaponChance = .5f;
        [Min(1)] public int ExperiencePerLevel = (int)ProgressionConstants.ExpPerLevelMultiplier;
        [Tooltip("Enable to reproduce the same reward sequence on every run. Disabled uses a fresh seed on Play and varies it on Restart.")]
        public bool UseFixedSeed;
        [Tooltip("Used when Use Fixed Seed is enabled.")]
        [Min(1)] public uint RandomSeed = 12345;
        [Tooltip("Starting weapon is included automatically. Owned assets are excluded. Empty entries and repeated assets are ignored.")]
        public CharacterWeaponDefinition[] Weapons = new CharacterWeaponDefinition[0];
        [Header("Upgrade Targets & Stats")]
        [Min(0)] public float CharacterTargetWeight = 1;
        [Min(0)] public float WeaponTargetWeight = 1;
        [Tooltip("Global filter, combined with Upgradable Stats on the selected character asset.")]
        public CharacterUpgradeStats CharacterStats = CharacterUpgradeStats.All;
        [Header("Rarity (relative weights; bonuses add to the original stat)")]
        [Tooltip("Add, remove or reorder tiers. Each tier has a display name, relative weight, additive bonus percent and label color. Zero weight disables a tier.")]
        public RewardTierSettings[] Rarities = DefaultRarities();
        private static RewardTierSettings[] DefaultRarities() => new[] {
            new RewardTierSettings("Common", 70, 5, new Color(.8f, .8f, .8f)),
            new RewardTierSettings("Rare", 25, 10, new Color(.25f, .6f, 1)),
            new RewardTierSettings("Epic", 5, 15, new Color(.8f, .35f, 1)) };

        // Preserve the original three serialized tiers when opening existing scenes.
        [SerializeField, HideInInspector] private int m_RarityVersion;
        [SerializeField, HideInInspector] private RewardTierSettings Common, Rare, Epic;
        public void OnBeforeSerialize() => m_RarityVersion = 1;
        public void OnAfterDeserialize()
        {
            if (m_RarityVersion != 0) return;
            if (Common != null || Rare != null || Epic != null)
            {
                var defaults = DefaultRarities();
                Rarities = new[] { Common ?? defaults[0], Rare ?? defaults[1], Epic ?? defaults[2] };
                for (int i = 0; i < Rarities.Length; i++)
                    if (string.IsNullOrWhiteSpace(Rarities[i].Name)) Rarities[i].Name = defaults[i].Name;
            }
            Common = Rare = Epic = null;
            m_RarityVersion = 1;
        }

        public bool TryValidate(CharacterDefinition character, CharacterWeaponDefinition startingWeapon, out string error)
        {
            if (ChoicesPerLevel < 1 || ChoicesPerLevel > RewardSelection.MaxChoices ||
                MaxWeapons < 1 || MaxWeapons > PlayerLoadout.Capacity || ExperiencePerLevel < 1 || UseFixedSeed && RandomSeed == 0 ||
                !FiniteNonnegative(NewWeaponChance) || NewWeaponChance > 1 || !FiniteNonnegative(CharacterTargetWeight) ||
                !FiniteNonnegative(WeaponTargetWeight) || !math.isfinite(CharacterTargetWeight + PlayerLoadout.Capacity * WeaponTargetWeight))
            { error = $"Rewards require 1-{RewardSelection.MaxChoices} choices, capacity 1-2, positive XP/seed and finite nonnegative weights."; return false; }
            float totalWeight = 0;
            if (Rarities != null)
                foreach (var tier in Rarities)
                {
                    if (!ValidTier(tier))
                    { error = "Each rarity requires a name, finite nonnegative weight, positive bonus percent and finite color."; return false; }
                    totalWeight += tier.Weight;
                }
            if (!math.isfinite(totalWeight) || totalWeight <= 0)
            { error = "Add at least one rarity with a positive weight."; return false; }
            if (character == null || startingWeapon == null)
            { error = "Rewards require a character and starting weapon asset."; return false; }
            var characterStats = CharacterStats & character.UpgradableStats & CharacterUpgradeStats.All;
            if (character.MagnetRadius <= 0) characterStats &= ~CharacterUpgradeStats.PickupRadius;
            int characterCount = CharacterTargetWeight > 0 ? math.countbits((uint)characterStats) : 0;
            int weaponCount = WeaponTargetWeight > 0 ? math.countbits((uint)RewardRoll.StatMask(CharacterUpgradeStats.None,
                startingWeapon.UpgradableStats & WeaponUpgradeStats.All, startingWeapon.ToConfig(), 1)) : 0;
            if (characterCount + weaponCount == 0)
            { error = "Enable at least one usable upgrade stat on the character or starting weapon, with a positive target weight. Zero pickup radius/damage and inapplicable weapon stats are excluded."; return false; }
            error = null;
            return true;
        }
        private static bool FiniteNonnegative(float value) => math.isfinite(value) && value >= 0;
        private static bool ValidTier(RewardTierSettings tier) => tier != null && !string.IsNullOrWhiteSpace(tier.Name) &&
            FiniteNonnegative(tier.Weight) && math.isfinite(tier.BonusPercent) && tier.BonusPercent > 0 && math.all(math.isfinite(ToColor(tier.Color)));

        public List<CharacterWeaponDefinition> GetWeapons(CharacterWeaponDefinition startingWeapon)
        {
            var weapons = new List<CharacterWeaponDefinition> { startingWeapon };
            if (Weapons != null)
                foreach (var weapon in Weapons) if (weapon != null && !weapons.Contains(weapon)) weapons.Add(weapon);
            return weapons;
        }
        public BlobAssetReference<RewardCatalog> BuildCatalog(CharacterDefinition character, CharacterWeaponDefinition startingWeapon,
            Func<WeaponDefinition, int> materialIndex = null, IReadOnlyList<ArtifactDefinition> artifacts = null, ArtifactChestSettings artifactChests = null)
        {
            if (!TryValidate(character, startingWeapon, out string error)) throw new InvalidOperationException(error);
            if (artifacts != null && artifacts.Count > ArtifactInventory.Capacity) throw new InvalidOperationException("Artifact roster exceeds inventory capacity.");
            if (artifacts != null)
                foreach (var artifact in artifacts)
                    if (artifact == null || !artifact.TryValidate(out error)) throw new InvalidOperationException(error ?? "Artifact roster contains an empty entry.");
            artifactChests ??= new ArtifactChestSettings();
            if (!artifactChests.TryValidate(artifacts, out error)) throw new InvalidOperationException(error);
            var weapons = GetWeapons(startingWeapon);
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var catalog = ref builder.ConstructRoot<RewardCatalog>();
            catalog.ChoicesPerLevel = ChoicesPerLevel;
            catalog.ArtifactChoicesPerChest = artifactChests.ChoicesPerChest;
            catalog.ArtifactRarityWeights = artifactChests.RarityWeights;
            catalog.MaxWeapons = MaxWeapons;
            catalog.NewWeaponChance = NewWeaponChance;
            catalog.ExperiencePerLevel = (uint)ExperiencePerLevel;
            catalog.UseFixedSeed = (byte)(UseFixedSeed ? 1 : 0);
            catalog.RandomSeed = UseFixedSeed ? RandomSeed : (uint)UnityEngine.Random.Range(1, int.MaxValue);
            catalog.CharacterWeight = CharacterTargetWeight;
            catalog.WeaponWeight = WeaponTargetWeight;
            catalog.CharacterStats = CharacterStats & character.UpgradableStats & CharacterUpgradeStats.All;
            if (character.MagnetRadius <= 0) catalog.CharacterStats &= ~CharacterUpgradeStats.PickupRadius;
            var tiers = builder.Allocate(ref catalog.Rarities, Rarities.Length);
            for (int i = 0; i < Rarities.Length; i++)
            {
                tiers[i].Name.CopyFromTruncated(Rarities[i].Name);
                tiers[i].Weight = Rarities[i].Weight;
                tiers[i].Bonus = Rarities[i].BonusPercent / 100;
                tiers[i].Color = ToColor(Rarities[i].Color);
                catalog.RarityWeightTotal += tiers[i].Weight;
            }
            var entries = builder.Allocate(ref catalog.Weapons, weapons.Count);
            for (int i = 0; i < weapons.Count; i++)
            {
                entries[i].Config = weapons[i].ToConfig(materialIndex?.Invoke(weapons[i]) ?? 0);
                entries[i].Stats = weapons[i].UpgradableStats & WeaponUpgradeStats.All;
                entries[i].Name.CopyFromTruncated(weapons[i].name);
            }
            var artifactEntries = builder.Allocate(ref catalog.Artifacts, artifacts?.Count ?? 0);
            for (int i = 0; i < artifactEntries.Length; i++) artifactEntries[i] = artifacts[i].ToConfig();
            return builder.CreateBlobAssetReference<RewardCatalog>(Allocator.Persistent);
        }
        private static float4 ToColor(Color color) => new float4(color.r, color.g, color.b, color.a);
    }
}
