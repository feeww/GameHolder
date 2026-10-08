using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace GameHolder.PureDots
{
    [Flags]
    public enum CharacterUpgradeStats : byte { None = 0, MaxHealth = 1, PickupRadius = 2, All = 3 }
    [Flags]
    public enum WeaponUpgradeStats : byte { None = 0, Damage = 1, AttackRate = 2, Range = 4, Size = 8, BlastRadius = 16, Lifetime = 32, All = 63 }
    public enum UpgradeStat : byte { MaxHealth, PickupRadius, Damage, AttackRate, Range, Size, BlastRadius, Lifetime }
    public enum RewardKind : byte { StatUpgrade, NewWeapon, Artifact }
    public struct RewardWeapon
    {
        public PlayerWeapon Config;
        public WeaponUpgradeStats Stats;
        public FixedString64Bytes Name;
    }
    public struct RewardTier
    {
        public FixedString64Bytes Name;
        public float Weight;
        public UpgradeBonuses Bonuses;
        public float4 Color;
    }
    public struct RewardCatalog
    {
        public int MaxWeapons, ChoicesPerLevel, MaxUpgradeRerolls, MaxArtifactBlocks;
        public uint ExperiencePerLevel, RandomSeed;
        public byte UseFixedSeed;
        public float NewWeaponChance, CharacterWeight, WeaponWeight;
        public CharacterUpgradeStats CharacterStats;
        public float RarityWeightTotal;
        public BlobArray<RewardTier> Rarities;
        public BlobArray<RewardWeapon> Weapons;
        public BlobArray<ArtifactConfig> Artifacts;
        public int ArtifactChoicesPerChest;
    }
    public struct RewardCatalogSingleton : IComponentData { public BlobAssetReference<RewardCatalog> Catalog; }
    public struct RewardChoice
    {
        public RewardKind Kind;
        // Character = 0, first weapon = 1, second weapon = 2.
        public byte Target;
        public UpgradeStat Stat;
        public int Rarity;
        public float Bonus;
        public float4 Color;
        public int WeaponIndex;
        public int ArtifactIndex;
        // Weapon name or shared rarity name.
        public FixedString64Bytes Name;
    }
    public struct RewardSelection
    {
        // ponytail: eight preallocated cards; expand the native list and HUD layout if more are needed.
        public const int MaxChoices = 8;
        public FixedList4096Bytes<RewardChoice> Choices;
        public uint Pending, PromptId, RandomState;
        public int RerollsUsed;
        public byte Active;
    }
    [Serializable]
    public struct UpgradeBonuses
    {
        public float MaxHealth, PickupRadius, Damage, AttackRate, Range, Size, BlastRadius, Lifetime;
        public float Get(UpgradeStat stat) => stat switch {
            UpgradeStat.MaxHealth => MaxHealth, UpgradeStat.PickupRadius => PickupRadius,
            UpgradeStat.Damage => Damage, UpgradeStat.AttackRate => AttackRate,
            UpgradeStat.Range => Range, UpgradeStat.Size => Size,
            UpgradeStat.BlastRadius => BlastRadius, UpgradeStat.Lifetime => Lifetime, _ => 0 };
        public void Add(UpgradeStat stat, float bonus)
        {
            switch (stat)
            {
                case UpgradeStat.MaxHealth: MaxHealth += bonus; break;
                case UpgradeStat.PickupRadius: PickupRadius += bonus; break;
                case UpgradeStat.Damage: Damage += bonus; break;
                case UpgradeStat.AttackRate: AttackRate += bonus; break;
                case UpgradeStat.Range: Range += bonus; break;
                case UpgradeStat.Size: Size += bonus; break;
                case UpgradeStat.BlastRadius: BlastRadius += bonus; break;
                case UpgradeStat.Lifetime: Lifetime += bonus; break;
            }
        }
        public PlayerWeapon Apply(PlayerWeapon baseline)
        {
            baseline.Damage *= 1 + Damage;
            baseline.Interval /= 1 + AttackRate;
            baseline.Range *= 1 + Range;
            baseline.Radius *= 1 + Size;
            baseline.BlastRadius *= 1 + BlastRadius;
            baseline.Lifetime *= 1 + Lifetime;
            return baseline;
        }
    }
    public struct PlayerLoadout
    {
        public const int Capacity = 2;
        public int Count, FirstIndex, SecondIndex;
        public uint FirstLevel, SecondLevel;
        public PlayerWeapon SecondBase, SecondWeapon;
        public UpgradeBonuses CharacterBonuses, FirstBonuses, SecondBonuses;
        public float SecondAttackTimer;
    }

    public static class RewardRoll
    {
        public static PlayerLoadout StartingLoadout() => new PlayerLoadout { Count = 1, FirstIndex = 0, SecondIndex = -1, FirstLevel = 1 };

        public static uint SeedForRun(ref RewardCatalog catalog, uint generation)
            => catalog.UseFixedSeed != 0 ? catalog.RandomSeed : math.max(1u, math.hash(new uint2(catalog.RandomSeed, generation)));

        public static int StatMask(CharacterUpgradeStats characterStats, WeaponUpgradeStats weaponStats, PlayerWeapon weapon, int target)
        {
            if (target == 0) return (int)characterStats;
            int mask = (int)weaponStats << 2;
            if (weapon.Type != WeaponType.Explosive) mask &= ~(1 << (int)UpgradeStat.BlastRadius);
            if (weapon.Type == WeaponType.Laser) mask &= ~(1 << (int)UpgradeStat.Lifetime);
            if (weapon.Damage <= 0) mask &= ~(1 << (int)UpgradeStat.Damage);
            return mask;
        }

        public static int RollRarity(ref Random random, ref RewardCatalog catalog)
        {
            float roll = random.NextFloat() * catalog.RarityWeightTotal;
            int last = 0;
            for (int i = 0; i < catalog.Rarities.Length; i++)
            {
                if (catalog.Rarities[i].Weight <= 0) continue;
                last = i;
                if (roll < catalog.Rarities[i].Weight) return i;
                roll -= catalog.Rarities[i].Weight;
            }
            return last;
        }

        public static void Open(ref RewardSelection rewards, PlayerLoadout loadout, ref RewardCatalog catalog)
        {
            if (rewards.Active != 0 || rewards.Pending == 0) return;
            var random = new Random(rewards.RandomState == 0 ? catalog.RandomSeed : rewards.RandomState);
            rewards.Choices.Clear();
            for (int i = 0; i < catalog.ChoicesPerLevel; i++)
                rewards.Choices.Add(Choice(ref random, loadout, ref catalog, ref rewards.Choices));
            rewards.RandomState = random.state;
            rewards.PromptId++;
            rewards.Active = 1;
        }

        private static RewardChoice Choice(ref Random random, PlayerLoadout loadout, ref RewardCatalog catalog, ref FixedList4096Bytes<RewardChoice> previous)
        {
            int availableWeapons = 0, unusedWeapons = 0;
            if (loadout.Count < math.min(PlayerLoadout.Capacity, catalog.MaxWeapons))
                for (int i = 0; i < catalog.Weapons.Length; i++)
                    if (i != loadout.FirstIndex && i != loadout.SecondIndex)
                    {
                        availableWeapons++;
                        if (!OfferedWeapon(ref previous, i)) unusedWeapons++;
                    }
            // Each slot rolls its type independently against the current inventory.
            if (random.NextFloat() < catalog.NewWeaponChance && availableWeapons > 0)
            {
                int selected = random.NextInt(unusedWeapons > 0 ? unusedWeapons : availableWeapons);
                for (int i = 0; i < catalog.Weapons.Length; i++)
                    if (i != loadout.FirstIndex && i != loadout.SecondIndex &&
                        (unusedWeapons == 0 || !OfferedWeapon(ref previous, i)) && selected-- == 0)
                        return new RewardChoice { Kind = RewardKind.NewWeapon, WeaponIndex = i, Name = catalog.Weapons[i].Name };
            }
            return StatChoice(ref random, loadout, ref catalog, ref previous);
        }

        public static RewardChoice StatChoice(ref Random random, PlayerLoadout loadout, ref RewardCatalog catalog,
            ref FixedList4096Bytes<RewardChoice> previous, CharacterUpgradeStats characterStats = CharacterUpgradeStats.All,
            WeaponUpgradeStats weaponStats = WeaponUpgradeStats.All)
        {
            int3 masks = default, eligibleMasks = default;
            float3 weights = default;
            for (int target = 0; target <= loadout.Count; target++)
            {
                ref var weapon = ref catalog.Weapons[target == 2 ? loadout.SecondIndex : loadout.FirstIndex];
                int mask = StatMask(catalog.CharacterStats & characterStats, weapon.Stats & weaponStats, weapon.Config, target);
                eligibleMasks[target] = mask;
                for (int i = 0; i < previous.Length; i++)
                    if (previous[i].Kind == RewardKind.StatUpgrade && previous[i].Target == target)
                        mask &= ~(1 << (int)previous[i].Stat);
                masks[target] = mask;
                weights[target] = mask == 0 ? 0 : target == 0 ? catalog.CharacterWeight : catalog.WeaponWeight;
            }
            // Repeat eligible pairs only after all distinct pairs have been offered.
            if (math.csum(weights) == 0)
            {
                masks = eligibleMasks;
                for (int target = 0; target <= loadout.Count; target++)
                    weights[target] = masks[target] == 0 ? 0 : target == 0 ? catalog.CharacterWeight : catalog.WeaponWeight;
            }
            float targetRoll = random.NextFloat() * math.csum(weights);
            int chosenTarget = 0;
            for (int i = 0; i <= loadout.Count; i++)
            {
                if (weights[i] <= 0) continue;
                chosenTarget = i;
                if (targetRoll < weights[i]) break;
                targetRoll -= weights[i];
            }
            if (masks[chosenTarget] == 0) return default;
            int chosenStat = random.NextInt(math.countbits((uint)masks[chosenTarget]));
            UpgradeStat stat = default;
            for (int i = 0; i <= (int)UpgradeStat.Lifetime; i++)
                if ((masks[chosenTarget] & (1 << i)) != 0 && chosenStat-- == 0) { stat = (UpgradeStat)i; break; }
            int rarity = RollRarity(ref random, ref catalog);
            ref var tier = ref catalog.Rarities[rarity];
            return new RewardChoice { Kind = RewardKind.StatUpgrade, Target = (byte)chosenTarget, Stat = stat,
                Rarity = rarity, Bonus = tier.Bonuses.Get(stat), Color = tier.Color, Name = tier.Name };
        }

        private static bool OfferedWeapon(ref FixedList4096Bytes<RewardChoice> choices, int index)
        {
            for (int i = 0; i < choices.Length; i++)
                if (choices[i].Kind == RewardKind.NewWeapon && choices[i].WeaponIndex == index) return true;
            return false;
        }

        public static bool MatchesPrompt(SimulationRunState run, PlayerStats stats, SimulationCommand command)
            => run.Zone.ReceiptActive == 0 && run.Rewards.Active != 0 && run.Rewards.Choices.Length > 0 && stats.IsDead == 0 &&
                command.PromptId == run.Rewards.PromptId && command.Generation == run.Generation;

        public static bool Reroll(ref SimulationRunState run, PlayerStats stats, ref RewardCatalog catalog, SimulationCommand command)
        {
            if (command.Kind != SimulationCommandKind.RerollUpgrades || !MatchesPrompt(run, stats, command) ||
                run.InventoryOpen != 0 || run.Rewards.Pending == 0 || run.Rewards.Choices[0].Kind == RewardKind.Artifact ||
                run.Rewards.RerollsUsed >= catalog.MaxUpgradeRerolls) return false;
            run.Rewards.RerollsUsed++;
            run.Rewards.Active = 0;
            Open(ref run.Rewards, run.Loadout, ref catalog);
            return true;
        }

        public static bool Select(ref SimulationRunState run, ref PlayerStats stats, ref PlayerWeapon firstWeapon,
            StartingPlayerConfig baseline, ref RewardCatalog catalog, SimulationCommand command)
        {
            if (command.Kind != SimulationCommandKind.SelectReward || !MatchesPrompt(run, stats, command) ||
                command.Value < 0 || command.Value >= run.Rewards.Choices.Length) return false;
            var choice = run.Rewards.Choices[command.Value];
            if (choice.Kind == RewardKind.Artifact)
            {
                if (run.PendingChests == 0 || choice.ArtifactIndex < 0 || choice.ArtifactIndex >= catalog.Artifacts.Length ||
                    (run.Inventory.BlockedArtifacts & (1u << choice.ArtifactIndex)) != 0) return false;
                run.Inventory.Add(choice.ArtifactIndex, 1, catalog.Artifacts[choice.ArtifactIndex]);
                run.Inventory.Apply(ref stats, baseline.Stats, run.Loadout.CharacterBonuses);
            }
            else if (run.Rewards.Pending == 0) return false;
            else if (choice.Kind == RewardKind.NewWeapon)
            {
                if (run.Loadout.Count >= math.min(PlayerLoadout.Capacity, catalog.MaxWeapons) ||
                    choice.WeaponIndex < 0 || choice.WeaponIndex >= catalog.Weapons.Length ||
                    choice.WeaponIndex == run.Loadout.FirstIndex || choice.WeaponIndex == run.Loadout.SecondIndex) return false;
                run.Loadout.SecondBase = run.Loadout.SecondWeapon = catalog.Weapons[choice.WeaponIndex].Config;
                run.Loadout.SecondIndex = choice.WeaponIndex;
                run.Loadout.SecondLevel = 1;
                run.Loadout.Count++;
            }
            else if (!ApplyStat(ref run, ref stats, ref firstWeapon, baseline, choice)) return false;
            if (choice.Kind == RewardKind.Artifact) run.PendingChests--;
            else run.Rewards.Pending--;
            run.Rewards.Active = 0;
            ArtifactRoll.Open(ref run, ref catalog);
            Open(ref run.Rewards, run.Loadout, ref catalog);
            return true;
        }

        public static bool ApplyStat(ref SimulationRunState run, ref PlayerStats stats, ref PlayerWeapon firstWeapon,
            StartingPlayerConfig baseline, RewardChoice choice)
        {
            if (choice.Kind != RewardKind.StatUpgrade || !math.isfinite(choice.Bonus) || choice.Bonus <= 0) return false;
            if (choice.Target == 0)
            {
                run.Loadout.CharacterBonuses.Add(choice.Stat, choice.Bonus);
                run.Inventory.Apply(ref stats, baseline.Stats, run.Loadout.CharacterBonuses);
            }
            else if (choice.Target == 1)
            {
                run.Loadout.FirstBonuses.Add(choice.Stat, choice.Bonus);
                firstWeapon = run.Loadout.FirstBonuses.Apply(baseline.Weapon);
                run.Loadout.FirstLevel = math.max(1u, run.Loadout.FirstLevel) + 1;
            }
            else if (choice.Target == 2 && run.Loadout.Count == PlayerLoadout.Capacity)
            {
                run.Loadout.SecondBonuses.Add(choice.Stat, choice.Bonus);
                run.Loadout.SecondWeapon = run.Loadout.SecondBonuses.Apply(run.Loadout.SecondBase);
                run.Loadout.SecondLevel = math.max(1u, run.Loadout.SecondLevel) + 1;
            }
            else return false;
            return true;
        }
    }
}
