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
        [HideInInspector] public int Id = -1;
        public string Name;
        [Tooltip("Relative drop chance for upgrades and artifacts. Zero disables this rarity.")]
        [Min(0)] public float Weight;
        [HideInInspector] public UpgradeBonuses BonusPercents;
        [HideInInspector] public Color Color = Color.gray;
        [SerializeField, HideInInspector] private float BonusPercent = 5;
        [SerializeField, HideInInspector] private bool m_StatBonusesInitialized;
        public RewardTierSettings(string name, float weight, float bonus, Color color)
        { Name = name; Weight = weight; BonusPercent = bonus; Color = color; InitializeStatBonuses(); }

        public void InitializeStatBonuses()
        {
            if (m_StatBonusesInitialized) return;
            for (int i = 0; i <= (int)UpgradeStat.Lifetime; i++) BonusPercents.Add((UpgradeStat)i, BonusPercent);
            m_StatBonusesInitialized = true;
        }
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
        [Header("Upgrade Targets")]
        [Min(0)] public float CharacterTargetWeight = 1;
        [Min(0)] public float WeaponTargetWeight = 1;
        [Tooltip("Global filter, combined with Upgradable Stats on the selected character asset.")]
        [HideInInspector] public CharacterUpgradeStats CharacterStats = CharacterUpgradeStats.All;
        [HideInInspector] public WeaponUpgradeStats WeaponStats = WeaponUpgradeStats.All;
        [Tooltip("Shared upgrade and artifact rarities. Add, remove or reorder tiers here; artifact rarity choices update automatically.")]
        [HideInInspector] public RewardTierSettings[] Rarities = DefaultRarities();
        private static RewardTierSettings[] DefaultRarities() => new[] {
            new RewardTierSettings("Common", 70, 5, new Color(.8f, .8f, .8f)) { Id = 0 },
            new RewardTierSettings("Rare", 25, 10, new Color(.25f, .6f, 1)) { Id = 1 },
            new RewardTierSettings("Epic", 5, 15, new Color(.8f, .35f, 1)) { Id = 2 } };

        // Preserve the original three serialized tiers when opening existing scenes.
        [SerializeField, HideInInspector] private int m_RarityVersion;
        [SerializeField, HideInInspector] private int m_NextRarityId = 4;
        [SerializeField, HideInInspector] private RewardTierSettings Common, Rare, Epic;
        public void OnBeforeSerialize() => EnsureRarityIds();
        public void OnAfterDeserialize()
        {
            if (m_RarityVersion < 3) WeaponStats = WeaponUpgradeStats.All;
            if (m_RarityVersion == 0 && (Common != null || Rare != null || Epic != null))
            {
                var defaults = DefaultRarities();
                Rarities = new[] { Common ?? defaults[0], Rare ?? defaults[1], Epic ?? defaults[2] };
                for (int i = 0; i < Rarities.Length; i++)
                    if (string.IsNullOrWhiteSpace(Rarities[i].Name)) Rarities[i].Name = defaults[i].Name;
            }
            if (m_RarityVersion < 2 && Rarities != null && Rarities.Length > 0)
            {
                var legacy = DefaultRarities();
                Array.Resize(ref legacy, 4);
                legacy[3] = new RewardTierSettings("Legendary", 1, 25, new Color(1, .75f, .2f)) { Id = 3 };
                foreach (var tier in legacy)
                    if (!Array.Exists(Rarities, current => string.Equals(current?.Name?.Trim(), tier.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Array.Resize(ref Rarities, Rarities.Length + 1);
                        Rarities[Rarities.Length - 1] = tier;
                    }
            }
            Common = Rare = Epic = null;
            EnsureRarityIds();
        }

        public void EnsureRarityIds()
        {
            if (Rarities == null) return;
            m_NextRarityId = Math.Max(4, m_NextRarityId);
            if (m_RarityVersion < 2)
                foreach (var tier in Rarities)
                    if (tier != null)
                        tier.Id = tier.Name?.Trim().ToUpperInvariant() switch { "COMMON" => 0, "RARE" => 1, "EPIC" => 2, "LEGENDARY" => 3, _ => -1 };
            foreach (var tier in Rarities)
                if (tier != null) m_NextRarityId = Math.Max(m_NextRarityId, tier.Id + 1);
            var ids = new HashSet<int>();
            foreach (var tier in Rarities)
                if (tier != null)
                {
                    if (tier.Id < 0 || !ids.Add(tier.Id)) { tier.Id = m_NextRarityId++; ids.Add(tier.Id); }
                    tier.InitializeStatBonuses();
                }
            m_RarityVersion = 3;
        }

        public int FindRarityIndex(int id) => Rarities == null ? -1 : Array.FindIndex(Rarities, tier => tier != null && tier.Id == id);

        public bool TryValidate(CharacterDefinition character, CharacterWeaponDefinition startingWeapon, out string error)
        {
            EnsureRarityIds();
            if (ChoicesPerLevel < 1 || ChoicesPerLevel > RewardSelection.MaxChoices ||
                MaxWeapons < 1 || MaxWeapons > PlayerLoadout.Capacity || ExperiencePerLevel < 1 || UseFixedSeed && RandomSeed == 0 ||
                !FiniteNonnegative(NewWeaponChance) || NewWeaponChance > 1 || !FiniteNonnegative(CharacterTargetWeight) ||
                !FiniteNonnegative(WeaponTargetWeight) || !math.isfinite(CharacterTargetWeight + PlayerLoadout.Capacity * WeaponTargetWeight))
            { error = $"Rewards require 1-{RewardSelection.MaxChoices} choices, capacity 1-2, positive XP/seed and finite nonnegative weights."; return false; }
            float totalWeight = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Rarities != null)
                foreach (var tier in Rarities)
                {
                    if (!ValidTier(tier) || !names.Add(tier.Name.Trim()))
                    { error = "Each rarity requires a unique name, a finite nonnegative chance and finite color. Enabled character/weapon upgrades require positive, finite percentages."; return false; }
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
                startingWeapon.UpgradableStats & WeaponStats & WeaponUpgradeStats.All, startingWeapon.ToConfig(), 1)) : 0;
            if (characterCount + weaponCount == 0)
            { error = "Enable at least one usable upgrade stat on the character or starting weapon, with a positive target weight. Zero pickup radius/damage and inapplicable weapon stats are excluded."; return false; }
            error = null;
            return true;
        }
        private static bool FiniteNonnegative(float value) => math.isfinite(value) && value >= 0;
        private bool ValidTier(RewardTierSettings tier)
        {
            if (tier == null || string.IsNullOrWhiteSpace(tier.Name) || !FiniteNonnegative(tier.Weight) || !math.all(math.isfinite(ToColor(tier.Color)))) return false;
            int enabled = (int)CharacterStats | ((int)WeaponStats << 2);
            for (int i = 0; i <= (int)UpgradeStat.Lifetime; i++)
            {
                float percent = tier.BonusPercents.Get((UpgradeStat)i);
                if (!FiniteNonnegative(percent) || (enabled & (1 << i)) != 0 && percent <= 0) return false;
            }
            return true;
        }

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
                    if (artifact == null || !artifact.TryValidate(out error, this)) throw new InvalidOperationException(error ?? "Artifact roster contains an empty entry.");
            artifactChests ??= new ArtifactChestSettings();
            if (!artifactChests.TryValidate(artifacts, this, out error)) throw new InvalidOperationException(error);
            var weapons = GetWeapons(startingWeapon);
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var catalog = ref builder.ConstructRoot<RewardCatalog>();
            catalog.ChoicesPerLevel = ChoicesPerLevel;
            catalog.ArtifactChoicesPerChest = artifactChests.ChoicesPerChest;
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
                for (int stat = 0; stat <= (int)UpgradeStat.Lifetime; stat++)
                    tiers[i].Bonuses.Add((UpgradeStat)stat, Rarities[i].BonusPercents.Get((UpgradeStat)stat) / 100);
                tiers[i].Color = ToColor(Rarities[i].Color);
                catalog.RarityWeightTotal += tiers[i].Weight;
            }
            var entries = builder.Allocate(ref catalog.Weapons, weapons.Count);
            for (int i = 0; i < weapons.Count; i++)
            {
                entries[i].Config = weapons[i].ToConfig(materialIndex?.Invoke(weapons[i]) ?? 0);
                entries[i].Stats = weapons[i].UpgradableStats & WeaponStats & WeaponUpgradeStats.All;
                entries[i].Name.CopyFromTruncated(weapons[i].name);
            }
            var artifactEntries = builder.Allocate(ref catalog.Artifacts, artifacts?.Count ?? 0);
            for (int i = 0; i < artifactEntries.Length; i++)
            {
                artifactEntries[i] = artifacts[i].ToConfig();
                artifactEntries[i].Rarity = FindRarityIndex(artifacts[i].Rarity);
            }
            return builder.CreateBlobAssetReference<RewardCatalog>(Allocator.Persistent);
        }
        private static float4 ToColor(Color color) => new float4(color.r, color.g, color.b, color.a);
    }
}
