using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = Unity.Mathematics.Random;

namespace GameHolder.PureDots.Tests
{
    public class RewardSelectionTests
    {
        private CharacterDefinition m_Character;
        private RewardSettings m_Settings;
        private BlobAssetReference<RewardCatalog> m_Catalog;
        [SetUp]
        public void Setup()
        {
            m_Character = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/GameData/Characters/DefaultCharacter.asset");
            m_Settings = new RewardSettings { UseFixedSeed = true, Weapons = new[] {
                UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterWeaponDefinition>("Assets/GameData/Weapons/Player/Explosive.asset"),
                UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterWeaponDefinition>("Assets/GameData/Weapons/Player/Laser.asset") } };
            m_Catalog = m_Settings.BuildCatalog(m_Character, m_Character.Weapon);
        }
        [TearDown]
        public void Teardown() { if (m_Catalog.IsCreated) m_Catalog.Dispose(); }

        [Test]
        public void EveryAvailableWeaponCanAppearOnTheFirstRollAndWeaponCardsDiffer()
        {
            m_Catalog.Dispose();
            m_Settings.Weapons = new[] { m_Settings.Weapons[0], m_Settings.Weapons[1],
                UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterWeaponDefinition>("Assets/GameData/Weapons/Player/Weapon1.asset") };
            m_Catalog = m_Settings.BuildCatalog(m_Character, m_Character.Weapon);
            m_Catalog.Value.NewWeaponChance = 1;
            m_Catalog.Value.UseFixedSeed = 0;
            var loadout = RewardRoll.StartingLoadout();
            var counts = new int[m_Catalog.Value.Weapons.Length];
            for (uint generation = 1; generation <= 1000; generation++)
            {
                var rewards = new RewardSelection { Pending = 1, RandomState = RewardRoll.SeedForRun(ref m_Catalog.Value, generation) };
                RewardRoll.Open(ref rewards, loadout, ref m_Catalog.Value);
                Assert.That(rewards.Choices[0].Kind, Is.EqualTo(RewardKind.NewWeapon));
                Assert.That(rewards.Choices[1].Kind, Is.EqualTo(RewardKind.NewWeapon));
                Assert.That(rewards.Choices[0].WeaponIndex, Is.Not.EqualTo(rewards.Choices[1].WeaponIndex));
                counts[rewards.Choices[0].WeaponIndex]++;
            }
            for (int i = 1; i < counts.Length; i++) Assert.That(counts[i], Is.InRange(200, 450));
            Assert.That(RewardRoll.SeedForRun(ref m_Catalog.Value, 1), Is.Not.EqualTo(RewardRoll.SeedForRun(ref m_Catalog.Value, 2)));
            m_Catalog.Value.UseFixedSeed = 1;
            Assert.That(RewardRoll.SeedForRun(ref m_Catalog.Value, 1), Is.EqualTo(RewardRoll.SeedForRun(ref m_Catalog.Value, 2)));
            var singleWeapon = new RewardSelection { Pending = 1 };
            // A catalog with only one eligible weapon still fills both independent weapon slots.
            var singleSettings = new RewardSettings { UseFixedSeed = true, NewWeaponChance = 1, Weapons = new[] { m_Settings.Weapons[1] } };
            using var singleCatalog = singleSettings.BuildCatalog(m_Character, m_Character.Weapon);
            RewardRoll.Open(ref singleWeapon, loadout, ref singleCatalog.Value);
            Assert.That(singleWeapon.Choices[0].WeaponIndex, Is.EqualTo(1));
            Assert.That(singleWeapon.Choices[1].WeaponIndex, Is.EqualTo(1));
        }

        [Test]
        public void IndependentSlotsCoverAllCombinationsAndFullInventoryOffersUniqueStatTargets()
        {
            var loadout = RewardRoll.StartingLoadout();
            var rewards = new RewardSelection { Pending = 1 };
            var combinations = new int[4];
            for (int i = 0; i < 20000; i++)
            {
                rewards.Active = 0;
                RewardRoll.Open(ref rewards, loadout, ref m_Catalog.Value);
                combinations[(int)rewards.Choices[0].Kind * 2 + (int)rewards.Choices[1].Kind]++;
                AssertChoices(rewards, loadout);
            }
            foreach (int count in combinations) Assert.That(count, Is.InRange(4500, 5500));
            loadout.Count = 2; loadout.SecondIndex = 1; loadout.SecondBase = m_Catalog.Value.Weapons[1].Config;
            int targets = 0;
            for (int i = 0; i < 2000; i++)
            {
                rewards.Active = 0;
                RewardRoll.Open(ref rewards, loadout, ref m_Catalog.Value);
                Assert.That(rewards.Choices[0].Kind, Is.EqualTo(RewardKind.StatUpgrade));
                Assert.That(rewards.Choices[1].Kind, Is.EqualTo(RewardKind.StatUpgrade));
                targets |= 1 << rewards.Choices[0].Target;
                AssertChoices(rewards, loadout);
            }
            Assert.That(targets, Is.EqualTo(7));
        }
        private void AssertChoices(RewardSelection rewards, PlayerLoadout loadout)
        {
            Assert.That(rewards.Active, Is.EqualTo(1));
            Assert.That(rewards.Choices.Length, Is.EqualTo(m_Catalog.Value.ChoicesPerLevel));
            foreach (var choice in rewards.Choices)
            {
                if (choice.Kind == RewardKind.NewWeapon)
                {
                    Assert.That(choice.WeaponIndex, Is.InRange(1, m_Catalog.Value.Weapons.Length - 1));
                    Assert.That(choice.Color, Is.EqualTo(float4.zero));
                }
                else
                {
                    Assert.That(choice.Target, Is.InRange(0, loadout.Count));
                    ref var entry = ref m_Catalog.Value.Weapons[choice.Target == 2 ? loadout.SecondIndex : loadout.FirstIndex];
                    Assert.That(RewardRoll.StatMask(m_Catalog.Value.CharacterStats, entry.Stats, entry.Config, choice.Target) & (1 << (int)choice.Stat), Is.Not.Zero);
                    Assert.That(choice.Bonus, Is.EqualTo(m_Catalog.Value.Rarities[choice.Rarity].Bonus));
                }
            }
            for (int i = 0; i < rewards.Choices.Length; i++)
                for (int j = 0; j < i; j++)
                    if (rewards.Choices[i].Kind == RewardKind.StatUpgrade && rewards.Choices[j].Kind == RewardKind.StatUpgrade)
                        Assert.That(rewards.Choices[i].Target != rewards.Choices[j].Target || rewards.Choices[i].Stat != rewards.Choices[j].Stat, Is.True);
        }
        [Test]
        public void RarityWeightsProduceSeventyTwentyFiveFivePercent()
        {
            var random = new Random(91234);
            var counts = new int[3];
            for (int i = 0; i < 100000; i++) counts[(int)RewardRoll.RollRarity(ref random, ref m_Catalog.Value)]++;
            Assert.That(counts[0] / 100000f, Is.EqualTo(.70f).Within(.006f));
            Assert.That(counts[1] / 100000f, Is.EqualTo(.25f).Within(.006f));
            Assert.That(counts[2] / 100000f, Is.EqualTo(.05f).Within(.003f));
            m_Catalog.Value.Rarities[0].Weight = m_Catalog.Value.Rarities[1].Weight = 0;
            m_Catalog.Value.RarityWeightTotal = m_Catalog.Value.Rarities[2].Weight;
            Assert.That(RewardRoll.RollRarity(ref random, ref m_Catalog.Value), Is.EqualTo(2));
        }
        [Test]
        public void ConfigurableChoiceCountsUseCustomTiersAndRemainDistinctThroughStatPoolExhaustion()
        {
            var starting = Object.Instantiate(m_Character.Weapon);
            try
            {
                starting.Type = WeaponType.Explosive;
                starting.UpgradableStats = WeaponUpgradeStats.All;
                m_Settings.MaxWeapons = 1;
                m_Settings.Weapons = null;
                m_Settings.Rarities = new[] {
                    new RewardTierSettings("Disabled", 0, 5, Color.gray),
                    new RewardTierSettings("Rare", 0, 10, Color.blue),
                    new RewardTierSettings("Epic", 0, 15, Color.magenta),
                    new RewardTierSettings("Legendary", 100, 25, new Color(1, 1, 0)) };
                for (int count = 1; count <= RewardSelection.MaxChoices; count++)
                {
                    m_Catalog.Dispose();
                    m_Settings.ChoicesPerLevel = count;
                    m_Catalog = m_Settings.BuildCatalog(m_Character, starting);
                    var rewards = new RewardSelection { Pending = 1 };
                    for (int roll = 0; roll < 100; roll++)
                    {
                        rewards.Active = 0;
                        RewardRoll.Open(ref rewards, RewardRoll.StartingLoadout(), ref m_Catalog.Value);
                        AssertChoices(rewards, RewardRoll.StartingLoadout());
                        foreach (var choice in rewards.Choices)
                        {
                            Assert.That(choice.Rarity, Is.EqualTo(3));
                            Assert.That(choice.Name.ToString(), Is.EqualTo("Legendary"));
                            Assert.That(choice.Bonus, Is.EqualTo(.25f));
                            Assert.That(choice.Color, Is.EqualTo(new float4(1, 1, 0, 1)));
                        }
                    }
                    var baseline = m_Character.ToConfig(starting); var stats = baseline.Stats; var weapon = baseline.Weapon;
                    var run = new SimulationRunState { Generation = 1, Loadout = RewardRoll.StartingLoadout(), Rewards = rewards };
                    Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value,
                        new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = count - 1,
                            PromptId = rewards.PromptId, Generation = 1 }), Is.True);
                    Assert.That(run.Rewards.Pending, Is.Zero);
                }
                m_Settings.ChoicesPerLevel = RewardSelection.MaxChoices + 1;
                Assert.That(m_Settings.TryValidate(m_Character, starting, out _), Is.False);
                m_Settings.ChoicesPerLevel = 0;
                Assert.That(m_Settings.TryValidate(m_Character, starting, out _), Is.False);
                m_Settings.ChoicesPerLevel = 3; m_Settings.CharacterStats = CharacterUpgradeStats.None;
                starting.UpgradableStats = WeaponUpgradeStats.None;
                Assert.That(m_Settings.TryValidate(m_Character, starting, out _), Is.False);
            }
            finally { Object.DestroyImmediate(starting); }
        }

        [Test]
        public void EachEquippedWeaponUsesItsOwnStatFilter()
        {
            var character = Object.Instantiate(m_Character);
            var first = Object.Instantiate(m_Character.Weapon);
            var second = Object.Instantiate(m_Settings.Weapons[1]);
            try
            {
                character.UpgradableStats = CharacterUpgradeStats.MaxHealth;
                first.UpgradableStats = WeaponUpgradeStats.Range | WeaponUpgradeStats.AttackRate | WeaponUpgradeStats.Lifetime;
                second.UpgradableStats = WeaponUpgradeStats.Damage | WeaponUpgradeStats.Size;
                m_Settings.ChoicesPerLevel = 3; m_Settings.Weapons = new[] { second };
                m_Catalog.Dispose(); m_Catalog = m_Settings.BuildCatalog(character, first);
                var loadout = RewardRoll.StartingLoadout(); loadout.Count = 2; loadout.SecondIndex = 1;
                loadout.SecondBase = m_Catalog.Value.Weapons[1].Config;
                var rewards = new RewardSelection { Pending = 1 };
                int targets = 0;
                for (int roll = 0; roll < 100; roll++)
                {
                    rewards.Active = 0;
                    RewardRoll.Open(ref rewards, loadout, ref m_Catalog.Value);
                    AssertChoices(rewards, loadout);
                    foreach (var choice in rewards.Choices)
                    {
                        targets |= 1 << choice.Target;
                        if (choice.Target == 0) Assert.That(choice.Stat, Is.EqualTo(UpgradeStat.MaxHealth));
                        else if (choice.Target == 1) Assert.That(choice.Stat, Is.EqualTo(UpgradeStat.Range).Or.EqualTo(UpgradeStat.AttackRate).Or.EqualTo(UpgradeStat.Lifetime));
                        else Assert.That(choice.Stat, Is.EqualTo(UpgradeStat.Damage).Or.EqualTo(UpgradeStat.Size));
                    }
                }
                Assert.That(targets, Is.EqualTo(7));
                second.UpgradableStats = WeaponUpgradeStats.None;
                Assert.That(m_Settings.TryValidate(character, first, out _), Is.True);
                character.UpgradableStats = CharacterUpgradeStats.None;
                first.UpgradableStats = WeaponUpgradeStats.None;
                Assert.That(m_Settings.TryValidate(character, first, out _), Is.False);
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [TestCase(1, 4)] [TestCase(2, 4)] [TestCase(2, 8)]
        public void SmallWeaponStatPoolsFillConfiguredPromptsWithoutAllocations(int statCount, int choices)
        {
            var starting = Object.Instantiate(m_Character.Weapon);
            try
            {
                starting.UpgradableStats = statCount == 1 ? WeaponUpgradeStats.Damage : WeaponUpgradeStats.Damage | WeaponUpgradeStats.AttackRate;
                m_Settings.CharacterStats = CharacterUpgradeStats.None;
                m_Settings.ChoicesPerLevel = choices; m_Settings.MaxWeapons = 1;
                Assert.That(m_Settings.TryValidate(m_Character, starting, out _), Is.True);
                m_Catalog.Dispose(); m_Catalog = m_Settings.BuildCatalog(m_Character, starting);
                var rewards = new RewardSelection { Pending = 1 };
                RewardRoll.Open(ref rewards, RewardRoll.StartingLoadout(), ref m_Catalog.Value);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) { rewards.Active = 0; RewardRoll.Open(ref rewards, RewardRoll.StartingLoadout(), ref m_Catalog.Value); }
                Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
                Assert.That(rewards.Choices.Length, Is.EqualTo(choices));
                if (statCount == 2) Assert.That(rewards.Choices[0].Stat, Is.Not.EqualTo(rewards.Choices[1].Stat));
                foreach (var choice in rewards.Choices)
                {
                    Assert.That(choice.Kind, Is.EqualTo(RewardKind.StatUpgrade));
                    Assert.That(choice.Target, Is.EqualTo(1));
                    Assert.That(choice.Stat, statCount == 1 ? Is.EqualTo(UpgradeStat.Damage) : Is.EqualTo(UpgradeStat.Damage).Or.EqualTo(UpgradeStat.AttackRate));
                }
                var baseline = m_Character.ToConfig(starting); var stats = baseline.Stats; var weapon = baseline.Weapon;
                var run = new SimulationRunState { Generation = 1, Loadout = RewardRoll.StartingLoadout(), Rewards = rewards };
                Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value,
                    new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = choices - 1,
                        PromptId = rewards.PromptId, Generation = run.Generation }), Is.True);
                Assert.That(run.Rewards.Pending, Is.Zero);
            }
            finally { Object.DestroyImmediate(starting); }
        }

        [Test]
        public void LegacyRaritiesMigrateOnceAndCustomRaritiesSurviveSerialization()
        {
            var settings = new RewardSettings();
            JsonUtility.FromJsonOverwrite("{\"Common\":{\"Weight\":40,\"BonusPercent\":7},\"Rare\":{\"Weight\":35,\"BonusPercent\":12},\"Epic\":{\"Weight\":25,\"BonusPercent\":19}}", settings);
            Assert.That(settings.Rarities.Length, Is.EqualTo(3));
            Assert.That(settings.Rarities[0].Name, Is.EqualTo("Common"));
            Assert.That(settings.Rarities[0].Weight, Is.EqualTo(40));
            Assert.That(settings.Rarities[0].BonusPercent, Is.EqualTo(7));
            Assert.That(settings.Rarities[2].BonusPercent, Is.EqualTo(19));
            settings.Rarities = new[] { new RewardTierSettings("Legendary", 100, 30, Color.yellow) };
            var copy = JsonUtility.FromJson<RewardSettings>(JsonUtility.ToJson(settings));
            copy.OnAfterDeserialize();
            Assert.That(copy.Rarities.Length, Is.EqualTo(1));
            Assert.That(copy.Rarities[0].Name, Is.EqualTo("Legendary"));
            Assert.That(copy.Rarities[0].BonusPercent, Is.EqualTo(30));
            foreach (var tiers in new[] { null, new RewardTierSettings[0], new[] { new RewardTierSettings("", 1, 5, Color.white) },
                new[] { new RewardTierSettings("Broken", 1, float.NaN, Color.white) } })
            {
                settings.Rarities = tiers;
                Assert.That(settings.TryValidate(m_Character, m_Character.Weapon, out _), Is.False);
            }
        }

        [Test]
        public void RepeatedBonusesUseOriginalStatsAndNeverChangeEitherSpeed()
        {
            var baseline = m_Character.ToConfig();
            var stats = baseline.Stats; var weapon = baseline.Weapon;
            var run = new SimulationRunState { Generation = 1, Loadout = RewardRoll.StartingLoadout() };
            run.Loadout.Count = 2; run.Loadout.SecondIndex = 1;
            run.Loadout.SecondBase = run.Loadout.SecondWeapon = m_Catalog.Value.Weapons[1].Config;
            foreach (float bonus in new[] { .05f, .10f, .15f })
            {
                Apply(ref run, ref stats, ref weapon, baseline, 0, UpgradeStat.MaxHealth, bonus);
                Apply(ref run, ref stats, ref weapon, baseline, 1, UpgradeStat.Damage, bonus);
                Apply(ref run, ref stats, ref weapon, baseline, 1, UpgradeStat.AttackRate, bonus);
                Apply(ref run, ref stats, ref weapon, baseline, 2, UpgradeStat.Damage, bonus);
            }
            Assert.That(stats.MaxHealth, Is.EqualTo(baseline.Stats.MaxHealth * 1.3f).Within(.0001f));
            Assert.That(weapon.Damage, Is.EqualTo(baseline.Weapon.Damage * 1.3f).Within(.0001f));
            Assert.That(weapon.Interval, Is.EqualTo(baseline.Weapon.Interval / 1.3f).Within(.0001f));
            Assert.That(run.Loadout.SecondWeapon.Damage, Is.EqualTo(run.Loadout.SecondBase.Damage * 1.3f).Within(.0001f));
            Assert.That(stats.MoveSpeed, Is.EqualTo(baseline.Stats.MoveSpeed));
            Assert.That(weapon.Speed, Is.EqualTo(baseline.Weapon.Speed));
            Assert.That(run.Loadout.SecondWeapon.Speed, Is.EqualTo(run.Loadout.SecondBase.Speed));
        }
        private void Apply(ref SimulationRunState run, ref PlayerStats stats, ref PlayerWeapon weapon, StartingPlayerConfig baseline, byte target, UpgradeStat stat, float bonus)
        {
            run.Rewards = new RewardSelection { Active = 1, Pending = 1, PromptId = run.Rewards.PromptId + 1,
                Choices = new FixedList4096Bytes<RewardChoice> { new RewardChoice { Target = target, Stat = stat, Bonus = bonus } } };
            Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value,
                new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = 0, PromptId = run.Rewards.PromptId, Generation = run.Generation }), Is.True);
        }
        [Test]
        public void SelectionRejectsStaleInvalidAndRepeatedCommandsAndEnforcesCapacity()
        {
            m_Catalog.Value.NewWeaponChance = 1;
            var baseline = m_Character.ToConfig(); var stats = baseline.Stats; var weapon = baseline.Weapon;
            var run = new SimulationRunState { Generation = 3, Loadout = RewardRoll.StartingLoadout(), Rewards = new RewardSelection { Pending = 2 } };
            RewardRoll.Open(ref run.Rewards, run.Loadout, ref m_Catalog.Value);
            var command = new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = -1, PromptId = run.Rewards.PromptId, Generation = 3 };
            Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value, command), Is.False);
            command.Value = 2;
            Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value, command), Is.False);
            command.Value = 0; command.Generation = 2;
            Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value, command), Is.False);
            command.Generation = 3;
            Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value, command), Is.True);
            Assert.That(run.Loadout.Count, Is.EqualTo(2));
            Assert.That(run.Rewards.Pending, Is.EqualTo(1));
            Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value, command), Is.False);
            Assert.That(run.Rewards.Choices[0].Kind, Is.EqualTo(RewardKind.StatUpgrade));
            run.Rewards.Choices[0] = new RewardChoice { Kind = RewardKind.NewWeapon, WeaponIndex = 2 };
            command.PromptId = run.Rewards.PromptId;
            Assert.That(RewardRoll.Select(ref run, ref stats, ref weapon, baseline, ref m_Catalog.Value, command), Is.False);
            Assert.That(run.Loadout.Count, Is.EqualTo(2));
        }
        [Test]
        public void SmallestValidStatPoolStillProducesTwoDistinctChoicesWithoutAllocations()
        {
            m_Catalog.Value.CharacterStats = CharacterUpgradeStats.All; m_Catalog.Value.WeaponWeight = 0;
            m_Catalog.Value.MaxWeapons = 1;
            var loadout = RewardRoll.StartingLoadout(); var rewards = new RewardSelection { Pending = 1 };
            RewardRoll.Open(ref rewards, loadout, ref m_Catalog.Value);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { rewards.Active = 0; RewardRoll.Open(ref rewards, loadout, ref m_Catalog.Value); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(rewards.Choices[0].Target, Is.Zero); Assert.That(rewards.Choices[1].Target, Is.Zero);
            Assert.That(rewards.Choices[0].Stat, Is.Not.EqualTo(rewards.Choices[1].Stat));
        }
        [Test]
        public void InspectorValidationRejectsImpossibleRollsAndCatalogDeduplicatesAssets()
        {
            m_Settings.CharacterStats = CharacterUpgradeStats.None; m_Settings.WeaponTargetWeight = 0;
            Assert.That(m_Settings.TryValidate(m_Character, m_Character.Weapon, out _), Is.False);
            m_Settings.CharacterStats = CharacterUpgradeStats.All;
            Assert.That(m_Settings.TryValidate(m_Character, m_Character.Weapon, out _), Is.True);
            m_Settings.Rarities[0].Weight = m_Settings.Rarities[1].Weight = m_Settings.Rarities[2].Weight = 0;
            Assert.That(m_Settings.TryValidate(m_Character, m_Character.Weapon, out _), Is.False);
            m_Settings.Rarities[0].Weight = 1; m_Settings.MaxWeapons = 3;
            Assert.That(m_Settings.TryValidate(m_Character, m_Character.Weapon, out _), Is.False);
            m_Settings.MaxWeapons = 2; m_Settings.NewWeaponChance = float.NaN;
            Assert.That(m_Settings.TryValidate(m_Character, m_Character.Weapon, out _), Is.False);
            m_Settings.NewWeaponChance = 1;
            m_Settings.Weapons = new[] { m_Character.Weapon, null, m_Character.Weapon };
            using var catalog = m_Settings.BuildCatalog(m_Character, m_Character.Weapon);
            Assert.That(catalog.Value.Weapons.Length, Is.EqualTo(1));
            var rewards = new RewardSelection { Pending = 1 };
            RewardRoll.Open(ref rewards, RewardRoll.StartingLoadout(), ref catalog.Value);
            Assert.That(rewards.Choices[0].Kind, Is.EqualTo(RewardKind.StatUpgrade));
            Assert.That(rewards.Choices[1].Kind, Is.EqualTo(RewardKind.StatUpgrade));
        }
        [Test]
        public void HudRetainsRewardSelectionUntilCommandQueueHasSpace()
        {
            var go = new GameObject("Reward queue test"); var commands = new UnsafeQueue<SimulationCommand>(Allocator.Temp);
            try
            {
                var hud = go.AddComponent<PureDotsHUD>();
                if (PureDotsHUD.Instance != hud)
                    typeof(PureDotsHUD).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                var snapshot = new SimulationSnapshot { Generation = 4, Rewards = new RewardSelection { Active = 1, Pending = 1, PromptId = 7 } };
                snapshot.Rewards.Choices.Add(new RewardChoice { Target = 0, Stat = UpgradeStat.MaxHealth });
                hud.ApplySnapshot(snapshot, commands);
                var button = go.transform.Find("HUD canvas/Reward panel/Choice 1").GetComponent<Button>();
                for (int i = 0; i < SimulationConstants.CommandQueueCapacity; i++)
                    commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.AutoAttack, Value = i });
                button.onClick.Invoke();
                hud.ApplySnapshot(snapshot, commands); hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.Count, Is.EqualTo(SimulationConstants.CommandQueueCapacity));
                Assert.That(button.interactable, Is.False);
                for (int i = 0; i < SimulationConstants.CommandQueueCapacity - 1; i++) commands.TryDequeue(out _);
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.Count, Is.EqualTo(2));
                commands.TryDequeue(out var previous);
                Assert.That(previous.Value, Is.EqualTo(SimulationConstants.CommandQueueCapacity - 1));
                Assert.That(commands.TryDequeue(out var selection), Is.True);
                Assert.That(selection.Kind, Is.EqualTo(SimulationCommandKind.SelectReward));
                Assert.That(selection.PromptId, Is.EqualTo(7)); Assert.That(selection.Generation, Is.EqualTo(4));
                button.onClick.Invoke(); hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.Count, Is.Zero);
            }
            finally { Object.DestroyImmediate(go); commands.Dispose(); }
        }

        [TestCase(1)] [TestCase(2)] [TestCase(5)] [TestCase(8)]
        public void HudShowsConfiguredCardsAndCustomRarityAndQueuesOneSelection(int count)
        {
            var go = new GameObject("Reward HUD test"); var commands = new UnsafeQueue<SimulationCommand>(Allocator.Temp);
            try
            {
                var hud = go.AddComponent<PureDotsHUD>();
                if (PureDotsHUD.Instance != hud)
                    typeof(PureDotsHUD).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                var snapshot = new SimulationSnapshot { Generation = 4, Rewards = new RewardSelection { Active = 1, Pending = 1, PromptId = 7 } };
                snapshot.Rewards.Choices.Add(new RewardChoice { Kind = RewardKind.NewWeapon, Name = new FixedString64Bytes("Laser") });
                for (int i = 1; i < count; i++)
                    snapshot.Rewards.Choices.Add(new RewardChoice { Kind = RewardKind.StatUpgrade, Stat = UpgradeStat.MaxHealth,
                        Rarity = 3, Name = new FixedString64Bytes("Legendary"), Bonus = .25f, Color = new float4(1, .8f, .1f, 1) });
                hud.ApplySnapshot(snapshot, commands);
                var panel = go.transform.Find("HUD canvas/Reward panel");
                Assert.That(panel.gameObject.activeSelf, Is.True);
                Assert.That(panel.GetComponentsInChildren<Button>().Length, Is.EqualTo(count));
                Assert.That(panel.Find("Choice 1/Label").GetComponent<BatchedHudText>().color, Is.EqualTo(Color.white));
                if (count > 1)
                {
                    var label = panel.Find("Choice 2/Label").GetComponent<BatchedHudText>();
                    Assert.That(label.color, Is.EqualTo(new Color(1, .8f, .1f, 1)));
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var text = (char[])typeof(BatchedHudText).GetField("m_Text", flags).GetValue(label);
                    int length = (int)typeof(BatchedHudText).GetField("m_Length", flags).GetValue(label);
                    Assert.That(new string(text, 0, length), Does.Contain("Legendary").And.Contain("+25.0%"));
                }
                panel.Find("Choice " + count).GetComponent<Button>().onClick.Invoke();
                panel.Find("Choice 1").GetComponent<Button>().onClick.Invoke();
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.Count, Is.EqualTo(1));
                Assert.That(commands.TryDequeue(out var command), Is.True);
                Assert.That(command.Kind, Is.EqualTo(SimulationCommandKind.SelectReward));
                Assert.That(command.Value, Is.EqualTo(count - 1)); Assert.That(command.PromptId, Is.EqualTo(7)); Assert.That(command.Generation, Is.EqualTo(4));
                snapshot.Rewards.PromptId++;
                snapshot.Rewards.Choices.Length = 1;
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(panel.GetComponentsInChildren<Button>().Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(go); commands.Dispose(); }
        }
    }
}
