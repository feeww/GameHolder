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
        public void ArtifactOffersRespectRarityWeightsExcludeDisabledTiersAndStayDistinct()
        {
            var artifacts = new ArtifactDefinition[4];
            try
            {
                for (int i = 0; i < artifacts.Length; i++)
                {
                    artifacts[i] = ScriptableObject.CreateInstance<ArtifactDefinition>();
                    artifacts[i].MaxHealth = 1;
                    artifacts[i].Rarity = i < 2 ? 0 : i == 2 ? 1 : 2;
                }
                var settings = new ArtifactChestSettings { ChoicesPerChest = 1 };
                Array.Resize(ref m_Settings.Rarities, 4);
                m_Settings.Rarities[3] = new RewardTierSettings("Legendary", 999, 25, Color.yellow);
                using var catalog = m_Settings.BuildCatalog(m_Character, m_Character.Weapon, artifacts: artifacts, artifactChests: settings);
                var counts = new int[3];
                var run = new SimulationRunState { PendingChests = 1, ArtifactRandomState = 12345,
                    Rewards = new RewardSelection { RandomState = 777 } };
                for (int i = 0; i < 10000; i++)
                {
                    run.Rewards.Active = 0; ArtifactRoll.Open(ref run, ref catalog.Value);
                    Assert.That(run.Rewards.Choices.Length, Is.EqualTo(1));
                    counts[run.Rewards.Choices[0].Rarity]++;
                }
                Assert.That(counts[0], Is.InRange(6800, 7200));
                Assert.That(counts[1], Is.InRange(2300, 2700));
                Assert.That(counts[2], Is.InRange(400, 600));
                Assert.That(run.Rewards.RandomState, Is.EqualTo(777));
                catalog.Value.ArtifactChoicesPerChest = RewardSelection.MaxChoices;
                run.Rewards.Active = 0; ArtifactRoll.Open(ref run, ref catalog.Value);
                Assert.That(run.Rewards.Choices.Length, Is.EqualTo(4));
                uint offered = 0;
                for (int i = 0; i < run.Rewards.Choices.Length; i++)
                {
                    uint bit = 1u << run.Rewards.Choices[i].ArtifactIndex;
                    Assert.That(offered & bit, Is.Zero); offered |= bit;
                }
                catalog.Value.Rarities[0].Weight = 0;
                run.Rewards.Active = 0; var replay = run;
                ArtifactRoll.Open(ref run, ref catalog.Value); ArtifactRoll.Open(ref replay, ref catalog.Value);
                Assert.That(run.Rewards.Choices.Length, Is.EqualTo(2));
                for (int i = 0; i < 2; i++)
                {
                    Assert.That(run.Rewards.Choices[i].Rarity, Is.Not.Zero);
                    Assert.That(run.Rewards.Choices[i].ArtifactIndex, Is.EqualTo(replay.Rewards.Choices[i].ArtifactIndex));
                }
            }
            finally { foreach (var artifact in artifacts) if (artifact != null) Object.DestroyImmediate(artifact); }
        }

        [Test]
        public void ArtifactChestAuthoringRejectsInvalidChoicesWeightsAndUnavailableRarities()
        {
            var artifact = ScriptableObject.CreateInstance<ArtifactDefinition>();
            try
            {
                artifact.MaxHealth = 1;
                var artifacts = new[] { artifact };
                var settings = new ArtifactChestSettings();
                Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.True);
                Assert.That(settings.MaxBlocksPerRun, Is.EqualTo(2));
                settings.MaxBlocksPerRun = -1; Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.False);
                settings.MaxBlocksPerRun = ArtifactInventory.Capacity + 1; Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.False);
                settings.MaxBlocksPerRun = 0; Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.True);
                settings.ChoicesPerChest = 0; Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.False);
                settings.ChoicesPerChest = 9; Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.False);
                settings.ChoicesPerChest = 8; m_Settings.Rarities[0].Weight = float.NaN;
                Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.False);
                m_Settings.Rarities[0].Weight = -1; Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.False);
                m_Settings.Rarities[0].Weight = 0; Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.False);
                Assert.Throws<InvalidOperationException>(() => m_Settings.BuildCatalog(m_Character, m_Character.Weapon, artifacts: artifacts, artifactChests: settings));
                artifact.Rarity = 4; Assert.That(artifact.TryValidate(out _, m_Settings), Is.False);
                artifact.Rarity = 1; Assert.That(settings.TryValidate(artifacts, m_Settings, out _), Is.True);
            }
            finally { Object.DestroyImmediate(artifact); }
        }

        [Test]
        public void ChestCardShowsArtifactIconRarityAndBonusAndImageSelectsOnce()
        {
            var go = new GameObject("Chest card test"); var commands = new UnsafeQueue<SimulationCommand>(Allocator.Temp);
            try
            {
                var hud = go.AddComponent<PureDotsHUD>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                if (PureDotsHUD.Instance != hud) typeof(PureDotsHUD).GetMethod("Awake", flags).Invoke(hud, null);
                var artifact = UnityEditor.AssetDatabase.LoadAssetAtPath<ArtifactDefinition>("Assets/GameData/Artifacts/VitalGauntlet.asset");
                hud.BindArtifacts(new[] { artifact }, null);
                var snapshot = new SimulationSnapshot { Generation = 4, PendingChests = 2, Rewards = new RewardSelection { Active = 1, PromptId = 7 } };
                snapshot.Rewards.Choices.Add(new RewardChoice { Kind = RewardKind.Artifact, ArtifactIndex = 0, Rarity = 0, Name = new FixedString64Bytes("Common"),
                    Color = new float4(.8f, .8f, .8f, 1) });
                hud.ApplySnapshot(snapshot, commands);
                var canvas = go.transform.Find("HUD canvas");
                string Label(string path)
                {
                    var label = canvas.Find(path).GetComponent<BatchedHudText>();
                    return new string((char[])typeof(BatchedHudText).GetField("m_Text", flags).GetValue(label), 0,
                        (int)typeof(BatchedHudText).GetField("m_Length", flags).GetValue(label));
                }
                Assert.That(Label("Reward panel/Reward title"), Does.Contain("ARTIFACT CHEST"));
                Assert.That(Label("Reward panel/Choice 1/Label"), Does.Contain("Common").And.Contain("VitalGauntlet").And.Contain("+10.0 max HP"));
                var icon = canvas.Find("Reward panel/Choice 1/Weapon image").GetComponent<RawImage>();
                Assert.That(icon.texture, Is.SameAs(artifact.Texture)); Assert.That(icon.uvRect, Is.EqualTo(artifact.IconUV));
                var pointer = new UnityEngine.EventSystems.PointerEventData(null) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(icon.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(icon.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands.TryDequeue(out var command), Is.True);
                Assert.That(command.Kind, Is.EqualTo(SimulationCommandKind.SelectReward));
                Assert.That(command.Value, Is.Zero); Assert.That(command.PromptId, Is.EqualTo(7));
                Assert.That(canvas.Find("Weapon details panel").gameObject.activeSelf, Is.False);
                snapshot.Rewards.PromptId++; hud.ApplySnapshot(snapshot, commands);
                Assert.That(canvas.Find("Reward panel/Choice 1").GetComponent<Button>().interactable, Is.True);
            }
            finally { Object.DestroyImmediate(go); commands.Dispose(); }
        }

        [Test]
        public void EveryAvailableWeaponCanAppearOnTheFirstRollAndWeaponCardsDiffer()
        {
            var additionalWeapon = Object.Instantiate(m_Character.Weapon);
            try
            {
                m_Catalog.Dispose();
                m_Settings.Weapons = new[] { m_Settings.Weapons[0], m_Settings.Weapons[1], additionalWeapon };
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
            finally { Object.DestroyImmediate(additionalWeapon); }
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
                    Assert.That(choice.Bonus, Is.EqualTo(m_Catalog.Value.Rarities[choice.Rarity].Bonuses.Get(choice.Stat)));
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

        [Test]
        public void SharedUpgradeMenusApplyPerStatRarityPercentagesAndWeaponTypeFilters()
        {
            var first = Object.Instantiate(m_Character.Weapon);
            var explosive = Object.Instantiate(m_Settings.Weapons[0]);
            var laser = Object.Instantiate(m_Settings.Weapons[1]);
            try
            {
                first.Type = WeaponType.Standard; first.UpgradableStats = WeaponUpgradeStats.All;
                explosive.Type = WeaponType.Explosive; explosive.UpgradableStats = WeaponUpgradeStats.BlastRadius;
                laser.Type = WeaponType.Laser; laser.UpgradableStats = WeaponUpgradeStats.Damage | WeaponUpgradeStats.Lifetime;
                m_Settings.CharacterStats = CharacterUpgradeStats.MaxHealth;
                m_Settings.WeaponStats = WeaponUpgradeStats.Damage | WeaponUpgradeStats.BlastRadius | WeaponUpgradeStats.Lifetime;
                m_Settings.NewWeaponChance = 0; m_Settings.ChoicesPerLevel = 4;
                m_Settings.Weapons = new[] { explosive, laser };
                for (int i = 0; i < m_Settings.Rarities.Length; i++)
                {
                    ref var percent = ref m_Settings.Rarities[i].BonusPercents;
                    percent.MaxHealth = 7 + i * 10; percent.Damage = 9 + i * 10;
                    percent.BlastRadius = 11 + i * 10; percent.Lifetime = 13 + i * 10;
                }
                Assert.That(first.GetApplicableUpgradeStats(m_Settings.WeaponStats), Is.EqualTo(WeaponUpgradeStats.Damage | WeaponUpgradeStats.Lifetime));
                Assert.That(laser.GetApplicableUpgradeStats(m_Settings.WeaponStats), Is.EqualTo(WeaponUpgradeStats.Damage));
                Assert.That(explosive.GetApplicableUpgradeStats(m_Settings.WeaponStats) & WeaponUpgradeStats.BlastRadius, Is.Not.Zero);
                using var catalog = m_Settings.BuildCatalog(m_Character, first);
                Assert.That(catalog.Value.Weapons[1].Stats, Is.EqualTo(WeaponUpgradeStats.BlastRadius));
                int seen = 0;
                var loadout = RewardRoll.StartingLoadout(); loadout.Count = 2;
                for (int second = 1; second <= 2; second++)
                {
                    loadout.SecondIndex = second;
                    var rewards = new RewardSelection { Pending = 1 };
                    RewardRoll.Open(ref rewards, loadout, ref catalog.Value);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 1000; i++) { rewards.Active = 0; RewardRoll.Open(ref rewards, loadout, ref catalog.Value); }
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    Assert.That(allocated, Is.Zero);
                    for (int i = 0; i < 100; i++)
                    {
                        rewards.Active = 0; RewardRoll.Open(ref rewards, loadout, ref catalog.Value);
                        foreach (var choice in rewards.Choices)
                        {
                            seen |= 1 << (int)choice.Stat;
                            Assert.That(choice.Bonus, Is.EqualTo(m_Settings.Rarities[choice.Rarity].BonusPercents.Get(choice.Stat) / 100).Within(.000001f));
                            if (choice.Target == 0) Assert.That(choice.Stat, Is.EqualTo(UpgradeStat.MaxHealth));
                            if (choice.Target == 1) Assert.That(choice.Stat, Is.EqualTo(UpgradeStat.Damage).Or.EqualTo(UpgradeStat.Lifetime));
                            if (choice.Target == 2) Assert.That(choice.Stat, Is.EqualTo(second == 1 ? UpgradeStat.BlastRadius : UpgradeStat.Damage));
                        }
                    }
                }
                Assert.That(seen, Is.EqualTo((1 << (int)UpgradeStat.MaxHealth) | (1 << (int)UpgradeStat.Damage) | (1 << (int)UpgradeStat.BlastRadius) | (1 << (int)UpgradeStat.Lifetime)));
                laser.Damage = 0;
                Assert.That(laser.GetApplicableUpgradeStats(m_Settings.WeaponStats), Is.EqualTo(WeaponUpgradeStats.None));
                m_Settings.Rarities[0].BonusPercents.Damage = float.NaN;
                Assert.That(m_Settings.TryValidate(m_Character, first, out _), Is.False);
                m_Settings.Rarities[0].BonusPercents.Damage = 0;
                Assert.That(m_Settings.TryValidate(m_Character, first, out _), Is.False);
                m_Settings.WeaponStats = WeaponUpgradeStats.None;
                Assert.That(m_Settings.TryValidate(m_Character, first, out _), Is.True);
                m_Settings.CharacterStats = CharacterUpgradeStats.None;
                Assert.That(m_Settings.TryValidate(m_Character, first, out _), Is.False);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(explosive); Object.DestroyImmediate(laser); }
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
            Assert.That(settings.Rarities.Length, Is.EqualTo(4));
            Assert.That(settings.Rarities[0].Name, Is.EqualTo("Common"));
            Assert.That(settings.Rarities[0].Weight, Is.EqualTo(40));
            Assert.That(settings.Rarities[0].BonusPercents.MaxHealth, Is.EqualTo(7));
            Assert.That(settings.Rarities[2].BonusPercents.Damage, Is.EqualTo(19));
            settings.Rarities = new[] { new RewardTierSettings("Legendary", 100, 30, Color.yellow) };
            var copy = JsonUtility.FromJson<RewardSettings>(JsonUtility.ToJson(settings));
            copy.OnAfterDeserialize();
            Assert.That(copy.Rarities.Length, Is.EqualTo(1));
            Assert.That(copy.Rarities[0].Name, Is.EqualTo("Legendary"));
            Assert.That(copy.Rarities[0].BonusPercents.PickupRadius, Is.EqualTo(30));
            foreach (var tiers in new[] { null, new RewardTierSettings[0], new[] { new RewardTierSettings("", 1, 5, Color.white) },
                new[] { new RewardTierSettings("Broken", 1, float.NaN, Color.white) } })
            {
                settings.Rarities = tiers;
                Assert.That(settings.TryValidate(m_Character, m_Character.Weapon, out _), Is.False);
            }
        }

        [Test]
        public void SharedRaritiesPreserveAssignmentsAndRollCustomArtifactsWithoutAllocations()
        {
            var custom = new RewardTierSettings("Ancient", 100, 35, new Color(.85f, .75f, 1));
            Array.Resize(ref m_Settings.Rarities, 5);
            m_Settings.Rarities[3] = new RewardTierSettings("Legendary", 0, 25, Color.yellow);
            m_Settings.Rarities[4] = custom;
            m_Settings.EnsureRarityIds();
            var artifact = ScriptableObject.CreateInstance<ArtifactDefinition>();
            try
            {
                artifact.MaxHealth = 1; artifact.Rarity = custom.Id;
                var settings = new ArtifactChestSettings();
                Assert.That(m_Settings.FindRarityIndex(artifact.Rarity), Is.EqualTo(4));
                using (var catalog = m_Settings.BuildCatalog(m_Character, m_Character.Weapon, artifacts: new[] { artifact }, artifactChests: settings))
                {
                    var run = new SimulationRunState { PendingChests = 1, ArtifactRandomState = 12345 };
                    ArtifactRoll.Open(ref run, ref catalog.Value);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 10000; i++) { run.Rewards.Active = 0; ArtifactRoll.Open(ref run, ref catalog.Value); }
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    Assert.That(allocated, Is.Zero);
                    Assert.That(run.Rewards.Choices.Length, Is.EqualTo(1));
                    Assert.That(run.Rewards.Choices[0].Rarity, Is.EqualTo(4));
                    Assert.That(run.Rewards.Choices[0].Name.ToString(), Is.EqualTo("Ancient"));
                    Assert.That(run.Rewards.Choices[0].Color, Is.EqualTo(new float4(.85f, .75f, 1, 1)));
                }
                custom.Name = "Mythic";
                Array.Reverse(m_Settings.Rarities);
                var copy = JsonUtility.FromJson<RewardSettings>(JsonUtility.ToJson(m_Settings));
                Assert.That(copy.FindRarityIndex(artifact.Rarity), Is.Zero);
                Assert.That(copy.Rarities[0].Name, Is.EqualTo("Mythic"));
                using (var catalog = copy.BuildCatalog(m_Character, m_Character.Weapon, artifacts: new[] { artifact }))
                    Assert.That(catalog.Value.Artifacts[0].Rarity, Is.Zero);
                m_Settings.Rarities = Array.FindAll(m_Settings.Rarities, tier => tier != custom);
                Assert.That(m_Settings.FindRarityIndex(artifact.Rarity), Is.EqualTo(-1));
                Assert.That(artifact.TryValidate(out _, m_Settings), Is.False);
                Assert.Throws<InvalidOperationException>(() => m_Settings.BuildCatalog(m_Character, m_Character.Weapon, artifacts: new[] { artifact }));
                var replacement = new RewardTierSettings("Replacement", 1, 5, Color.white);
                Array.Resize(ref m_Settings.Rarities, 5); m_Settings.Rarities[4] = replacement;
                m_Settings.EnsureRarityIds();
                Assert.That(replacement.Id, Is.GreaterThan(custom.Id));
            }
            finally { Object.DestroyImmediate(artifact); }
        }

        [Test]
        public void LegacyBonusPercentMigratesToEveryStatOnce()
        {
            var settings = JsonUtility.FromJson<RewardSettings>("{\"m_RarityVersion\":1,\"Rarities\":[{\"Name\":\"Epic\",\"Weight\":5,\"BonusPercent\":15},{\"Name\":\"Ancient\",\"Weight\":25,\"BonusPercent\":35},{\"Name\":\"Common\",\"Weight\":70,\"BonusPercent\":5},{\"Name\":\"Rare\",\"Weight\":25,\"BonusPercent\":10},{\"Name\":\"Legendary\",\"Weight\":5,\"BonusPercent\":25}]}");
            Assert.That(settings.Rarities[0].Id, Is.EqualTo(2));
            Assert.That(settings.Rarities[0].BonusPercents.MaxHealth, Is.EqualTo(15));
            Assert.That(settings.Rarities[0].BonusPercents.Damage, Is.EqualTo(15));
            Assert.That(settings.Rarities[2].Id, Is.Zero);
            Assert.That(settings.Rarities[2].BonusPercents.MaxHealth, Is.EqualTo(5));
            Assert.That(settings.Rarities[3].BonusPercents.Range, Is.EqualTo(10));
            Assert.That(settings.Rarities[4].BonusPercents.Lifetime, Is.EqualTo(25));
            settings.Rarities[0].BonusPercents.MaxHealth = 12;
            settings.Rarities[0].BonusPercents.Damage = 21;
            settings.WeaponStats = WeaponUpgradeStats.Size;
            var copy = JsonUtility.FromJson<RewardSettings>(JsonUtility.ToJson(settings));
            copy.OnAfterDeserialize();
            Assert.That(copy.Rarities[0].BonusPercents.MaxHealth, Is.EqualTo(12));
            Assert.That(copy.Rarities[0].BonusPercents.Damage, Is.EqualTo(21));
            Assert.That(copy.WeaponStats, Is.EqualTo(WeaponUpgradeStats.Size));
        }

        [TestCase(0)] [TestCase(1)]
        public void LegacyArtifactRaritiesMigrateOnceWithoutRestoringRemovedTiers(int version)
        {
            var json = version == 0
                ? "{\"Common\":{\"Weight\":40,\"BonusPercent\":7},\"Rare\":{\"Weight\":35,\"BonusPercent\":12},\"Epic\":{\"Weight\":25,\"BonusPercent\":19}}"
                : "{\"m_RarityVersion\":1,\"Rarities\":[{\"Name\":\" Rare \",\"Weight\":35,\"BonusPercent\":12}]}";
            var settings = JsonUtility.FromJson<RewardSettings>(json);
            Assert.That(settings.Rarities.Length, Is.EqualTo(4));
            Assert.That(settings.Rarities[settings.FindRarityIndex(1)].Weight, Is.EqualTo(35));
            var artifact = ScriptableObject.CreateInstance<ArtifactDefinition>();
            try
            {
                artifact.MaxHealth = 1;
                for (int rarity = 0; rarity < 4; rarity++)
                {
                    artifact.Rarity = rarity;
                    Assert.That(artifact.TryValidate(out var error, settings), Is.True, error);
                }
                using (var catalog = settings.BuildCatalog(m_Character, m_Character.Weapon, artifacts: new[] { artifact }))
                {
                    var run = new SimulationRunState { PendingChests = 1, ArtifactRandomState = 12345 };
                    ArtifactRoll.Open(ref run, ref catalog.Value);
                    Assert.That(run.Rewards.Choices[0].Name.ToString(), Is.EqualTo("Legendary"));
                }
                var copy = JsonUtility.FromJson<RewardSettings>(JsonUtility.ToJson(settings));
                Assert.That(copy.Rarities.Length, Is.EqualTo(4));
                copy.Rarities = Array.FindAll(copy.Rarities, tier => tier.Id != 3);
                copy = JsonUtility.FromJson<RewardSettings>(JsonUtility.ToJson(copy));
                Assert.That(copy.FindRarityIndex(3), Is.EqualTo(-1));
                Assert.That(artifact.TryValidate(out _, copy), Is.False);
            }
            finally { Object.DestroyImmediate(artifact); }
        }

        [Test]
        public void ExistingSceneUsesSharedRaritiesAndRegistersArtifactInspector()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                GamePresentationBootstrap bootstrap = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((bootstrap = root.GetComponentInChildren<GamePresentationBootstrap>()) != null) break;
                Assert.That(bootstrap, Is.Not.Null);
                Assert.That(bootstrap.TryValidateConfiguration(out var error), Is.True, error);
                using (var catalog = bootstrap.Rewards.BuildCatalog(bootstrap.StartingCharacter,
                    bootstrap.StartingWeaponAsset != null ? bootstrap.StartingWeaponAsset : bootstrap.StartingCharacter.Weapon,
                    artifacts: bootstrap.GetArtifacts(), artifactChests: bootstrap.ArtifactChests))
                {
                    Assert.That(catalog.Value.Rarities.Length, Is.EqualTo(bootstrap.Rewards.Rarities.Length));
                    for (int i = 0; i < catalog.Value.Rarities.Length; i++)
                        Assert.That(catalog.Value.Rarities[i].Weight, Is.EqualTo(bootstrap.Rewards.Rarities[i].Weight));
                }
                foreach (var artifact in bootstrap.GetArtifacts())
                {
                    Assert.That(artifact.TryValidate(out error, bootstrap.Rewards), Is.True, error);
                    var inspector = UnityEditor.Editor.CreateEditor(artifact);
                    try { Assert.That(inspector.GetType().Name, Is.EqualTo("ArtifactDefinitionEditor")); }
                    finally { Object.DestroyImmediate(inspector); }
                }
                var script = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>("Assets/Scripts/Editor/RaritySettingsWindow.cs");
                Assert.That(script.GetClass().Name, Is.EqualTo("RaritySettingsWindow"));
                var menu = (UnityEditor.MenuItem)Attribute.GetCustomAttribute(script.GetClass().GetMethod("Open"), typeof(UnityEditor.MenuItem));
                Assert.That(menu.menuItem, Is.EqualTo("Pure DOTS/Rarities & Drop Chances"));
                foreach (string method in new[] { "OpenCharacterUpgrades", "OpenWeaponUpgrades" })
                    Assert.That(Attribute.GetCustomAttribute(script.GetClass().GetMethod(method), typeof(UnityEditor.MenuItem)), Is.Not.Null);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
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
            Assert.That(run.Loadout.FirstLevel, Is.EqualTo(7));
            Assert.That(run.Loadout.SecondLevel, Is.EqualTo(4));
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
            Assert.That(run.Loadout.FirstLevel, Is.EqualTo(1));
            Assert.That(run.Loadout.SecondLevel, Is.EqualTo(1));
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
            Assert.That(m_Settings.MaxRerollsPerRun, Is.EqualTo(5));
            Assert.That(m_Catalog.Value.MaxUpgradeRerolls, Is.EqualTo(5));
            Assert.That(m_Catalog.Value.MaxArtifactBlocks, Is.EqualTo(2));
            m_Settings.MaxRerollsPerRun = -1;
            Assert.That(m_Settings.TryValidate(m_Character, m_Character.Weapon, out _), Is.False);
            m_Settings.MaxRerollsPerRun = 0;
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
        public void RerollsAdvanceDeterministicRewardsWithoutSpendingLevelsOrArtifactRandomness()
        {
            var run = new SimulationRunState { Generation = 4, Loadout = RewardRoll.StartingLoadout(), ArtifactRandomState = 777,
                Rewards = new RewardSelection { Pending = 2 } };
            RewardRoll.Open(ref run.Rewards, run.Loadout, ref m_Catalog.Value);
            var replay = run;
            var command = new SimulationCommand { Kind = SimulationCommandKind.RerollUpgrades, PromptId = run.Rewards.PromptId, Generation = 4 };
            uint previous = run.Rewards.RandomState;
            Assert.That(RewardRoll.Reroll(ref run, m_Character.ToConfig().Stats, ref m_Catalog.Value, command), Is.True);
            Assert.That(RewardRoll.Reroll(ref replay, m_Character.ToConfig().Stats, ref m_Catalog.Value, command), Is.True);
            Assert.That(run.Rewards.RandomState, Is.Not.EqualTo(previous).And.EqualTo(replay.Rewards.RandomState));
            Assert.That(run.Rewards.Pending, Is.EqualTo(2)); Assert.That(run.ArtifactRandomState, Is.EqualTo(777));
            Assert.That(run.Rewards.RerollsUsed, Is.EqualTo(1)); Assert.That(run.Rewards.Active, Is.EqualTo(1));
            for (int i = 0; i < run.Rewards.Choices.Length; i++)
                Assert.That(run.Rewards.Choices[i], Is.EqualTo(replay.Rewards.Choices[i]));
        }

        [TestCase(false)] [TestCase(true)]
        public void HudRewardActionsShowRemainingUsesAndQueueOnlyOnce(bool artifact)
        {
            var go = new GameObject("Reward actions test"); var commands = new UnsafeQueue<SimulationCommand>(Allocator.Temp);
            try
            {
                var hud = go.AddComponent<PureDotsHUD>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                if (PureDotsHUD.Instance != hud) typeof(PureDotsHUD).GetMethod("Awake", flags).Invoke(hud, null);
                hud.BindArtifacts(new[] { UnityEditor.AssetDatabase.LoadAssetAtPath<ArtifactDefinition>("Assets/GameData/Artifacts/VitalGauntlet.asset") }, null);
                var snapshot = new SimulationSnapshot { Generation = 4, MaxArtifactBlocks = 2, MaxUpgradeRerolls = 5,
                    PendingChests = artifact ? 1u : 0, Rewards = new RewardSelection { Active = 1, Pending = artifact ? 0u : 1, PromptId = 7 } };
                snapshot.Rewards.Choices.Add(new RewardChoice { Kind = artifact ? RewardKind.Artifact : RewardKind.StatUpgrade });
                hud.ApplySnapshot(snapshot, commands);
                var panel = go.transform.Find("HUD canvas/Reward panel");
                var block = panel.Find("Block artifact 1").GetComponent<Button>();
                var reroll = panel.Find("Reroll upgrades").GetComponent<Button>();
                Assert.That(block.gameObject.activeSelf, Is.EqualTo(artifact));
                Assert.That(reroll.gameObject.activeSelf, Is.EqualTo(!artifact));
                string Uses()
                {
                    var label = artifact ? panel.Find("Reward title").GetComponent<BatchedHudText>() : reroll.GetComponentInChildren<BatchedHudText>();
                    return new string((char[])typeof(BatchedHudText).GetField("m_Text", flags).GetValue(label), 0,
                        (int)typeof(BatchedHudText).GetField("m_Length", flags).GetValue(label));
                }
                Assert.That(Uses(), Does.Contain(artifact ? "Blocks left: 2" : "5 left"));
                var action = artifact ? block : reroll;
                action.onClick.Invoke(); action.onClick.Invoke(); panel.Find("Choice 1").GetComponent<Button>().onClick.Invoke();
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(action.interactable, Is.False); Assert.That(commands.Count, Is.EqualTo(1));
                commands.TryDequeue(out var command);
                Assert.That(command.Kind, Is.EqualTo(artifact ? SimulationCommandKind.BlockArtifact : SimulationCommandKind.RerollUpgrades));
                Assert.That(command.PromptId, Is.EqualTo(7)); Assert.That(command.Generation, Is.EqualTo(4)); Assert.That(command.Value, Is.Zero);
                snapshot.Rewards.PromptId++;
                if (artifact) snapshot.Inventory.BlockedArtifacts = 1; else snapshot.Rewards.RerollsUsed = 1;
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(action.interactable, Is.True); Assert.That(Uses(), Does.Contain(artifact ? "Blocks left: 1" : "4 left"));
                snapshot.Rewards.PromptId++;
                if (artifact) snapshot.Inventory.BlockedArtifacts = 3; else snapshot.Rewards.RerollsUsed = 5;
                hud.ApplySnapshot(snapshot, commands); action.onClick.Invoke(); hud.ApplySnapshot(snapshot, commands);
                Assert.That(action.interactable, Is.False); Assert.That(commands.Count, Is.Zero);
                Assert.That(action.gameObject.activeSelf, Is.False);
                Assert.That(panel.GetComponent<RectTransform>().sizeDelta.y, Is.EqualTo(220));
                Assert.That(panel.Find("Choice 1").GetComponent<Button>().navigation.selectOnDown, Is.Null);
                if (artifact) Assert.That(Uses(), Does.Not.Contain("Blocks left").And.Not.Contain("Block excludes"));
                snapshot.Generation++;
                snapshot.Inventory.BlockedArtifacts = 0; snapshot.Rewards.RerollsUsed = 0;
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(action.gameObject.activeSelf, Is.True); Assert.That(action.interactable, Is.True);
            }
            finally { Object.DestroyImmediate(go); commands.Dispose(); }
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
        [TestCase(0)] [TestCase(1)]
        public void RewardImagesSelectTheirCardAndEquippedWeaponsShowCurrentStats(int choiceIndex)
        {
            var go = new GameObject("Weapon menu test"); var commands = new UnsafeQueue<SimulationCommand>(Allocator.Temp);
            try
            {
                var hud = go.AddComponent<PureDotsHUD>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                if (PureDotsHUD.Instance != hud) typeof(PureDotsHUD).GetMethod("Awake", flags).Invoke(hud, null);
                hud.BindWeapons(m_Settings.GetWeapons(m_Character.Weapon));
                var first = m_Character.Weapon.ToConfig(); first.Damage = 77;
                var snapshot = new SimulationSnapshot { Generation = 4, WeaponCount = 2, FirstWeapon = first,
                    Loadout = new PlayerLoadout { Count = 2, FirstIndex = 0, SecondIndex = 1, FirstLevel = 3, SecondLevel = 2,
                        SecondWeapon = m_Settings.Weapons[0].ToConfig() },
                    Rewards = new RewardSelection { Active = 1, Pending = 1, PromptId = 7 } };
                snapshot.Rewards.Choices.Add(new RewardChoice { Kind = RewardKind.NewWeapon, WeaponIndex = 2, Name = new FixedString64Bytes("Laser") });
                snapshot.Rewards.Choices.Add(new RewardChoice { Target = 1, Stat = UpgradeStat.Damage, Name = new FixedString64Bytes("Rare"), Bonus = .1f });
                snapshot.Rewards.Choices.Add(new RewardChoice { Target = 0, Stat = UpgradeStat.MaxHealth });
                hud.ApplySnapshot(snapshot, commands);
                var canvas = go.transform.Find("HUD canvas");
                string Label(string path)
                {
                    var label = canvas.Find(path).GetComponent<BatchedHudText>();
                    return new string((char[])typeof(BatchedHudText).GetField("m_Text", flags).GetValue(label), 0,
                        (int)typeof(BatchedHudText).GetField("m_Length", flags).GetValue(label));
                }
                Assert.That(canvas.Find("Selected weapons panel/Weapon 1/Weapon image").GetComponent<RawImage>().texture, Is.SameAs(m_Character.Weapon.WeaponTexture));
                Assert.That(Label("Selected weapons panel/Weapon 1/Label"), Does.Contain("Level 3"));
                Assert.That(Label("Selected weapons panel/Weapon 2/Label"), Does.Contain("Level 2"));
                var preview = canvas.Find("Reward panel/Choice 1/Weapon image");
                Assert.That(preview.GetComponent<RawImage>().texture, Is.SameAs(m_Settings.Weapons[1].WeaponTexture));
                Assert.That(canvas.Find("Reward panel/Choice 2/Weapon image").GetComponent<RawImage>().texture, Is.SameAs(m_Character.Weapon.WeaponTexture));
                Assert.That(Label("Reward panel/Choice 2/Label"), Does.Contain("Level 3 -> 4"));
                Assert.That(canvas.Find("Reward panel/Choice 3/Weapon image").gameObject.activeSelf, Is.False);
                var icon = canvas.Find("Reward panel/Choice " + (choiceIndex + 1) + "/Weapon image");
                var pointer = new UnityEngine.EventSystems.PointerEventData(null)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(icon.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(canvas.Find("Weapon details panel").gameObject.activeSelf, Is.False);
                Assert.That(commands.Count, Is.EqualTo(1));
                Assert.That(commands.TryDequeue(out var command), Is.True);
                Assert.That(command.Kind, Is.EqualTo(SimulationCommandKind.SelectReward));
                Assert.That(command.Value, Is.EqualTo(choiceIndex));
                Assert.That(command.PromptId, Is.EqualTo(7)); Assert.That(command.Generation, Is.EqualTo(4));
                canvas.Find("Selected weapons panel/Weapon 1").GetComponent<Button>().onClick.Invoke();
                Assert.That(Label("Weapon details panel/Weapon stats"), Does.Contain("Level 3").And.Contain("Damage: 77.0").And.Contain("Projectiles"));
                snapshot.FirstWeapon.Damage = 88; snapshot.Loadout.FirstLevel = 4;
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(Label("Weapon details panel/Weapon stats"), Does.Contain("Level 4").And.Contain("Damage: 88.0"));
                canvas.Find("Selected weapons panel/Weapon 2").GetComponent<Button>().onClick.Invoke();
                Assert.That(Label("Weapon details panel/Weapon stats"), Does.Contain("Level 2 - Explosive").And.Contain("Blast radius"));
                snapshot.Generation++; snapshot.WeaponCount = 1; snapshot.Loadout = RewardRoll.StartingLoadout(); snapshot.Rewards = default;
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(canvas.Find("Weapon details panel").gameObject.activeSelf, Is.False);
                Assert.That(Label("Selected weapons panel/Weapon 1/Label"), Does.Contain("Level 1"));
                Assert.That(Label("Selected weapons panel/Weapon 2/Label"), Is.EqualTo("Empty slot"));
                Assert.That(canvas.Find("Selected weapons panel/Weapon 2").GetComponent<Button>().interactable, Is.False);
            }
            finally { Object.DestroyImmediate(go); commands.Dispose(); }
        }
        [Test]
        public void BackpackUsesBagImageQueuesPauseAndShowsStackQuantitiesAndStats()
        {
            var go = new GameObject("Backpack test"); var commands = new UnsafeQueue<SimulationCommand>(Allocator.Temp);
            try
            {
                var hud = go.AddComponent<PureDotsHUD>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                if (PureDotsHUD.Instance != hud) typeof(PureDotsHUD).GetMethod("Awake", flags).Invoke(hud, null);
                var artifact = UnityEditor.AssetDatabase.LoadAssetAtPath<ArtifactDefinition>("Assets/GameData/Artifacts/VitalGauntlet.asset");
                var bag = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameData/Textures/UI/InventoryBag.png");
                hud.BindArtifacts(new[] { artifact }, bag);
                var snapshot = new SimulationSnapshot { Generation = 4, Player = m_Character.ToConfig().Stats, Rewards = new RewardSelection { Active = 1 } };
                hud.ApplySnapshot(snapshot, commands);
                var canvas = go.transform.Find("HUD canvas");
                Assert.That(canvas.Find("Inventory button/Bag image").GetComponent<RawImage>().texture, Is.SameAs(bag));
                canvas.Find("Inventory button").GetComponent<Button>().onClick.Invoke(); hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.TryDequeue(out var command), Is.True);
                Assert.That(command.Kind, Is.EqualTo(SimulationCommandKind.Inventory)); Assert.That(command.Value, Is.EqualTo(1)); Assert.That(command.Generation, Is.EqualTo(4));
                Assert.That(canvas.Find("Inventory overlay").gameObject.activeSelf, Is.False);
                snapshot.InventoryOpen = 1; snapshot.Inventory.Add(0, 2, artifact.ToConfig());
                snapshot.Inventory.Apply(ref snapshot.Player, m_Character.ToConfig().Stats, default); hud.ApplySnapshot(snapshot, commands);
                Assert.That(canvas.Find("Inventory overlay").gameObject.activeSelf, Is.True);
                var panel = canvas.Find("Inventory overlay/Inventory panel");
                var label = panel.Find("Artifact viewport/Artifacts/Artifact 1/Artifact stats").GetComponent<BatchedHudText>();
                var text = new string((char[])typeof(BatchedHudText).GetField("m_Text", flags).GetValue(label), 0,
                    (int)typeof(BatchedHudText).GetField("m_Length", flags).GetValue(label));
                Assert.That(text, Does.Contain("VitalGauntlet x2").And.Contain("+10.0 HP each").And.Contain("+20.0 total"));
                Assert.That(panel.Find("Artifact viewport/Artifacts/Artifact 1/Artifact image").GetComponent<RawImage>().texture, Is.SameAs(artifact.Texture));
                Assert.That(panel.Find("Artifact viewport/Artifacts/Artifact 1/Artifact image").GetComponent<RawImage>().uvRect, Is.EqualTo(artifact.IconUV));
                Assert.That(panel.Find("Artifact viewport/Artifacts/Artifact 2").gameObject.activeSelf, Is.False);
                panel.Find("Close inventory (Esc)").GetComponent<Button>().onClick.Invoke(); hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.TryDequeue(out command), Is.True); Assert.That(command.Kind, Is.EqualTo(SimulationCommandKind.Inventory)); Assert.That(command.Value, Is.Zero);
                snapshot.InventoryOpen = 0; hud.ApplySnapshot(snapshot, commands);
                Assert.That(canvas.Find("Inventory overlay").gameObject.activeSelf, Is.False);
                Assert.That(canvas.Find("Reward panel").gameObject.activeSelf, Is.True);
                snapshot.InventoryOpen = 1; snapshot.Inventory = default; hud.ApplySnapshot(snapshot, commands);
                Assert.That(panel.Find("Artifact viewport/Artifacts/Empty inventory").gameObject.activeSelf, Is.True);
                hud.Unbind(); Assert.That(canvas.Find("Inventory overlay").gameObject.activeSelf, Is.False);
            }
            finally { Object.DestroyImmediate(go); commands.Dispose(); }
        }
    }
}
