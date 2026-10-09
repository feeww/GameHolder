using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameHolder.PureDots.Tests
{
    public partial class SimulationRegressionTests
    {
        private World m_World;
        private EntityManager m_Em;
        private SystemHandle m_Pipeline;
        private Entity m_Run, m_Player, m_Wave, m_Input;
        private double m_Time;
        private UnsafeQueue<SimulationCommand> m_Commands;

        private const uint TankType = 0, RunnerType = 1, LaserType = 4;
        private static StartingPlayerConfig DefaultPlayer => UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
            "Assets/GameData/Characters/DefaultCharacter.asset").ToConfig();
        private static PlayerWeapon TestWeapon(WeaponType type) => UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterWeaponDefinition>(
            $"Assets/GameData/Weapons/Player/{type}.asset").ToConfig();

        [OneTimeSetUp]
        public void CreateWorld()
        {
            m_World = new World("DOTS regression tests");
            m_Em = m_World.EntityManager;
            Entity player = Prefab(typeof(PlayerTag), typeof(LocalTransform), typeof(PreviousPosition), typeof(MovementVelocity), typeof(PlayerStats), typeof(PlayerInvulnerability), typeof(PlayerWeapon));
            m_Em.SetComponentData(player, DefaultPlayer.Stats);
            Entity enemy = Prefab(typeof(LocalTransform), typeof(PreviousPosition), typeof(MovementVelocity), typeof(SeparationCache),
                typeof(CurrentHealth), typeof(TypeId), typeof(EnemyActiveTag), typeof(EnemyRangedTag), typeof(EnemyMeleeCooldown), typeof(EnemyRangedCooldown));
            Entity playerProjectile = Prefab(typeof(LocalTransform), typeof(PreviousPosition), typeof(MovementVelocity), typeof(ProjectileData), typeof(ProjectileActiveTag), typeof(PlayerProjectileTag), typeof(ExplosiveProjectile), typeof(LaserBeam));
            m_Em.SetComponentEnabled<ExplosiveProjectile>(playerProjectile, false);
            m_Em.SetComponentEnabled<LaserBeam>(playerProjectile, false);
            Entity enemyProjectile = Prefab(typeof(LocalTransform), typeof(PreviousPosition), typeof(MovementVelocity), typeof(ProjectileData), typeof(ProjectileActiveTag), typeof(EnemyProjectileTag), typeof(ExplosiveProjectile), typeof(LaserBeam));
            m_Em.SetComponentEnabled<ExplosiveProjectile>(enemyProjectile, false);
            m_Em.SetComponentEnabled<LaserBeam>(enemyProjectile, false);
            Entity gem = Prefab(typeof(LocalTransform), typeof(GemData), typeof(GemActiveTag));
            m_Em.AddComponentData(m_Em.CreateEntity(), new PureDotsPrefabsSingleton
            { PlayerPrefab = player, EnemyPrefab = enemy, PlayerProjPrefab = playerProjectile, EnemyProjPrefab = enemyProjectile, GemPrefab = gem });
            var startingPlayer = DefaultPlayer;
            m_Em.AddComponentData(m_Em.CreateEntity(), startingPlayer);
            var character = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/GameData/Characters/DefaultCharacter.asset");
            // Existing combat checks do not open reward prompts; progression checks opt into a lower threshold.
            var rewardSettings = new RewardSettings { ExperiencePerLevel = int.MaxValue };
            var artifactNames = new[] { "VitalGauntlet", "RenewalIdol", "MagnetMedallion" };
            var artifacts = System.Array.ConvertAll(artifactNames, name => UnityEditor.AssetDatabase.LoadAssetAtPath<ArtifactDefinition>($"Assets/GameData/Artifacts/{name}.asset"));
            m_Em.AddComponentData(m_Em.CreateEntity(), new RewardCatalogSingleton { Catalog = rewardSettings.BuildCatalog(character, character.Weapon,
                artifacts: artifacts, artifactChests: new ArtifactChestSettings { ChoicesPerChest = 3 }) });
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<EnemyConfigCatalog>();
            var names = new[] { "Tank", "Runner", "Skirmisher", "Sniper", "Laser" };
            var configs = builder.Allocate(ref root.Configs, names.Length);
            float threshold = 0;
            for (int i = 0; i < names.Length; i++)
            {
                var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/GameData/Enemies/{names[i]}.asset");
                configs[i] = definition.ToConfig(threshold, startingPlayer.Stats.CollisionRadius);
                configs[i].ChestDropChance = 0; // Individual drop checks opt in; combat checks keep their original stats.
                threshold = configs[i].SpawnThreshold;
            }
            m_Em.AddComponentData(m_Em.CreateEntity(), new EnemyConfigCatalogSingleton
            { Catalog = builder.CreateBlobAssetReference<EnemyConfigCatalog>(Allocator.Persistent) });
            builder.Dispose();
            m_World.GetOrCreateSystem<SimulationBootstrapSystem>().Update(m_World.Unmanaged);
            m_Run = m_Em.CreateEntityQuery(typeof(SimulationRunState)).GetSingletonEntity();
            m_Player = m_Em.GetComponentData<SimulationRunState>(m_Run).Player;
            m_Wave = m_Em.CreateEntityQuery(typeof(WaveSpawnerConfig)).GetSingletonEntity();
            m_Input = m_Em.CreateEntityQuery(typeof(SimulationInput)).GetSingletonEntity();
            m_Commands = m_Em.CreateEntityQuery(typeof(SimulationCommandQueue)).GetSingleton<SimulationCommandQueue>().Commands;
            m_Pipeline = m_World.GetOrCreateSystem<SimulationPipelineSystem>();
        }
        private Entity Prefab(params ComponentType[] types)
            => Prefab(m_Em, types);
        private static Entity Prefab(EntityManager em, params ComponentType[] types)
        {
            Entity e = em.CreateEntity(types);
            em.AddComponent<Prefab>(e);
            em.SetComponentData(e, LocalTransform.Identity);
            return e;
        }
        [SetUp]
        public void Reset()
        {
            Command(SimulationCommandKind.AutoAttack, 0);
            Command(SimulationCommandKind.GodMode, 0);
            Command(SimulationCommandKind.Restart);
            m_Em.SetComponentData(m_Input, new SimulationInput());
            Tick();
            var wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave);
            wave.SpawnInterval = 100000; m_Em.SetComponentData(m_Wave, wave);
            m_Em.SetComponentData(m_Player, new PlayerInvulnerability());
        }
        [OneTimeTearDown]
        public void DisposeWorld() => m_World?.Dispose();
        private void Command(SimulationCommandKind kind, int value = 0) => m_Commands.Enqueue(new SimulationCommand { Kind = kind, Value = value });
        private void Tick(float dt = 1f / 60)
        {
            m_Time += dt; m_World.SetTime(new TimeData(m_Time, dt));
            if (math.lengthsq(m_Em.GetComponentData<SimulationInput>(m_Input).Movement) > 0)
            {
                // Fixtures write pooled entities directly; publish those writes before querying the previous grid.
                m_World.GetOrCreateSystem<GridTestSystem>().Update(m_World.Unmanaged);
                m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            }
            m_Pipeline.Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
        }
        private SimulationSnapshot Snapshot => m_Em.GetComponentData<SimulationSnapshot>(m_Run);
        private void SelectArtifact(int index)
        {
            var snapshot = Snapshot;
            int choice = -1;
            for (int i = 0; i < snapshot.Rewards.Choices.Length; i++)
                if (snapshot.Rewards.Choices[i].Kind == RewardKind.Artifact && snapshot.Rewards.Choices[i].ArtifactIndex == index) choice = i;
            Assert.That(choice, Is.GreaterThanOrEqualTo(0));
            var command = new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = choice,
                Generation = snapshot.Generation, PromptId = snapshot.Rewards.PromptId };
            m_Commands.Enqueue(command); m_Commands.Enqueue(command); Tick(0);
        }
        [Test]
        public void ArtifactChestsRespectEnemyChanceStackWithUpgradesPauseRegenerationAndReset()
        {
            var enemies = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var catalog = m_Em.CreateEntityQuery(typeof(RewardCatalogSingleton)).GetSingleton<RewardCatalogSingleton>().Catalog;
            var pool = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>();
            var spawns = m_Em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
            try
            {
                enemies.Value.Configs[0].ChestDropChance = 1;
                Enemy(new float2(20, 0), 0); Enemy(new float2(20, 1), 1);
                Command(SimulationCommandKind.KillAll); Tick(0);
                Assert.That(Snapshot.Kills, Is.EqualTo(2));
                Assert.That(pool.FreeChests.Length, Is.EqualTo(GemPoolSingleton.ChestCapacity - 1));
                Assert.That(Snapshot.Inventory.Items.Length, Is.Zero);
                var stats = Snapshot.Player; stats.CurrentHealth = 10; m_Em.SetComponentData(m_Player, stats);
                for (int i = 0; i < 4; i++) spawns.Enqueue(new GemSpawnRequest { IsChest = 1 });
                Tick(0);
                Assert.That(Snapshot.PendingChests, Is.EqualTo(4)); Assert.That(Snapshot.Inventory.Items.Length, Is.Zero);
                Assert.That(Snapshot.Rewards.Active, Is.EqualTo(1));
                m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) }); Tick(1);
                Assert.That(Snapshot.PlayerPosition, Is.EqualTo(float2.zero)); Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(10));
                m_Em.SetComponentData(m_Input, default(SimulationInput));
                foreach (int index in new[] { 0, 0, 1, 2 }) SelectArtifact(index);
                Assert.That(Snapshot.PendingChests, Is.Zero); Assert.That(Snapshot.Rewards.Active, Is.Zero);
                Assert.That(Snapshot.Inventory.Items.Length, Is.EqualTo(3));
                Assert.That(Snapshot.Inventory.Items[0].Quantity, Is.EqualTo(2));
                Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth + 50));
                Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(60));
                Assert.That(Snapshot.Player.MagnetRadius, Is.EqualTo(DefaultPlayer.Stats.MagnetRadius + .5f));
                Assert.That(Snapshot.Player.HealthRegeneration, Is.EqualTo(1));
                var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                run.Rewards = new RewardSelection { Active = 1, Pending = 1, PromptId = 9 };
                run.Rewards.Choices.Add(new RewardChoice { Target = 0, Stat = UpgradeStat.MaxHealth, Bonus = .1f });
                m_Em.SetComponentData(m_Run, run);
                m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = 0, PromptId = 9, Generation = run.Generation }); Tick(0);
                Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth * 1.1f + 50));
                Assert.That(Snapshot.Player.MagnetRadius, Is.EqualTo(DefaultPlayer.Stats.MagnetRadius + .5f));
                Entity enemy = Enemy(new float2(20, 0)); Entity projectile = Projectile(new float2(10, 10), new float2(1, 0), true);
                var enemyPosition = m_Em.GetComponentData<LocalTransform>(enemy).Position;
                var projectilePosition = m_Em.GetComponentData<LocalTransform>(projectile).Position;
                float lifetime = m_Em.GetComponentData<ProjectileData>(projectile).RemainingLifetime;
                float waveTimer = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).Timer;
                float health = Snapshot.Player.CurrentHealth;
                m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
                m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.Inventory, Value = 1, Generation = Snapshot.Generation }); Tick(1);
                Assert.That(Snapshot.InventoryOpen, Is.EqualTo(1)); Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(health));
                Assert.That(Snapshot.PlayerPosition, Is.EqualTo(float2.zero));
                Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position, Is.EqualTo(enemyPosition));
                Assert.That(m_Em.GetComponentData<LocalTransform>(projectile).Position, Is.EqualTo(projectilePosition));
                Assert.That(m_Em.GetComponentData<ProjectileData>(projectile).RemainingLifetime, Is.EqualTo(lifetime));
                Assert.That(m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).Timer, Is.EqualTo(waveTimer));
                m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.Inventory, Generation = Snapshot.Generation - 1 }); Tick(1);
                Assert.That(Snapshot.InventoryOpen, Is.EqualTo(1));
                m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.Inventory, Generation = Snapshot.Generation }); Tick(1);
                Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(health + 1)); Assert.That(Snapshot.PlayerPosition.x, Is.GreaterThan(0));
                m_Em.SetComponentData(m_Player, new PlayerInvulnerability { Timer = 1000 });
                stats = Snapshot.Player; stats.CurrentHealth = stats.MaxHealth - .1f; m_Em.SetComponentData(m_Player, stats); Tick(1);
                Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(stats.MaxHealth));
                stats = Snapshot.Player; stats.CurrentHealth = 0; stats.IsDead = 1; m_Em.SetComponentData(m_Player, stats); Tick(1);
                Assert.That(Snapshot.Player.CurrentHealth, Is.Zero); Assert.That(Snapshot.Player.IsDead, Is.EqualTo(1));
                run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                run.InventoryOpen = 1; run.PendingChests = 2; ArtifactRoll.Open(ref run, ref catalog.Value); m_Em.SetComponentData(m_Run, run);
                m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.Inventory, Generation = Snapshot.Generation }); Tick(1);
                Assert.That(Snapshot.InventoryOpen, Is.Zero); Assert.That(Snapshot.Rewards.Active, Is.EqualTo(1));
                Command(SimulationCommandKind.Restart); Tick(0);
                Assert.That(Snapshot.Inventory.Items.Length, Is.Zero); Assert.That(Snapshot.InventoryOpen, Is.Zero);
                Assert.That(Snapshot.PendingChests, Is.Zero); Assert.That(Snapshot.Rewards.Active, Is.Zero);
                Assert.That(Snapshot.Player.HealthRegeneration, Is.Zero); Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth));
                Assert.That(pool.FreeChests.Length, Is.EqualTo(GemPoolSingleton.ChestCapacity));
            }
            finally { enemies.Value.Configs[0].ChestDropChance = 0; }
        }
        [Test]
        public void ChestPoolOverflowPreservesArtifactQuantitiesAndRebasesPositions()
        {
            var pool = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>();
            var spawns = m_Em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
            for (int i = 0; i < GemPoolSingleton.ChestCapacity + 4; i++)
                spawns.Enqueue(new GemSpawnRequest { IsChest = 1, Position = new float2(20, 0) });
            Tick(0);
            uint copies = 0;
            for (int i = 0; i < pool.AllChests.Length; i++) copies += pool.AllChests[i].Quantity;
            Assert.That(copies, Is.EqualTo(GemPoolSingleton.ChestCapacity + 4));
            var position = pool.AllChests[0].Position;
            Command(SimulationCommandKind.ForceRebase); Tick(0);
            Assert.That((double2)pool.AllChests[0].Position + Snapshot.WorldOrigin, Is.EqualTo((double2)position));
            m_Em.SetComponentData(m_Player, LocalTransform.FromPosition(new float3(pool.AllChests[0].Position, 0))); Tick(0);
            Assert.That(Snapshot.PendingChests, Is.EqualTo(GemPoolSingleton.ChestCapacity + 4));
            for (int i = 0; i < GemPoolSingleton.ChestCapacity + 4; i++) SelectArtifact(0);
            Assert.That(Snapshot.Inventory.Items.Length, Is.EqualTo(1));
            Assert.That(Snapshot.Inventory.Items[0].Quantity, Is.EqualTo(GemPoolSingleton.ChestCapacity + 4));
            Assert.That(pool.FreeChests.Length, Is.EqualTo(GemPoolSingleton.ChestCapacity));
        }
        [Test]
        public void ChestChoicesPreserveQueuedLevelUpsAndInventoryPause()
        {
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run); run.Rewards.Pending = 1;
            m_Em.SetComponentData(m_Run, run);
            var spawns = m_Em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
            spawns.Enqueue(new GemSpawnRequest { IsChest = 1 }); spawns.Enqueue(new GemSpawnRequest { IsChest = 1 }); Tick(0);
            Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(1)); Assert.That(Snapshot.PendingChests, Is.EqualTo(2));
            m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.Inventory, Value = 1, Generation = Snapshot.Generation }); Tick(0);
            SelectArtifact(0);
            Assert.That(Snapshot.PendingChests, Is.EqualTo(1)); Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(1));
            m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.Inventory, Generation = Snapshot.Generation }); Tick(0);
            Assert.That(m_Em.GetComponentData<SimulationRunState>(m_Run).Paused, Is.True);
            SelectArtifact(1);
            Assert.That(Snapshot.PendingChests, Is.Zero); Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(1));
            Assert.That(Snapshot.Rewards.Choices[0].Kind, Is.Not.EqualTo(RewardKind.Artifact));
            m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = 0,
                Generation = Snapshot.Generation, PromptId = Snapshot.Rewards.PromptId }); Tick(0);
            Assert.That(Snapshot.Rewards.Active, Is.Zero); Assert.That(Snapshot.Rewards.Pending, Is.Zero);
            Assert.That(Snapshot.Inventory.Items.Length, Is.EqualTo(2));
        }
        [TestCase(0, 0)] [TestCase(1, 1)] [TestCase(2, 5)] [TestCase(3, 7)]
        public void RewardActionsEnforcePerRunLimitsRejectStaleCommandsAndReset(int blocks, int rerolls)
        {
            var catalog = m_Em.CreateEntityQuery(typeof(RewardCatalogSingleton)).GetSingleton<RewardCatalogSingleton>().Catalog;
            int previousBlocks = catalog.Value.MaxArtifactBlocks, previousRerolls = catalog.Value.MaxUpgradeRerolls;
            try
            {
                catalog.Value.MaxArtifactBlocks = blocks; catalog.Value.MaxUpgradeRerolls = rerolls;
                var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                run.PendingChests = 2; run.Rewards.Pending = 2;
                ArtifactRoll.Open(ref run, ref catalog.Value);
                int owned = run.Rewards.Choices[0].ArtifactIndex;
                run.Inventory.Add(owned, 1, catalog.Value.Artifacts[owned]);
                m_Em.SetComponentData(m_Run, run); Tick(0);
                Assert.That(Snapshot.MaxArtifactBlocks, Is.EqualTo(blocks)); Assert.That(Snapshot.MaxUpgradeRerolls, Is.EqualTo(rerolls));
                for (int i = 0; i <= blocks; i++)
                {
                    var command = new SimulationCommand { Kind = SimulationCommandKind.BlockArtifact, Value = 0,
                        Generation = Snapshot.Generation, PromptId = Snapshot.Rewards.PromptId };
                    uint before = Snapshot.Inventory.BlockedArtifacts;
                    var stale = command; stale.Generation--; m_Commands.Enqueue(stale);
                    stale = command; stale.PromptId--; m_Commands.Enqueue(stale); Tick(0);
                    Assert.That(Snapshot.Inventory.BlockedArtifacts, Is.EqualTo(before));
                    int index = Snapshot.Rewards.Choices[0].ArtifactIndex;
                    m_Commands.Enqueue(command); m_Commands.Enqueue(command); Tick(0);
                    Assert.That(math.countbits(Snapshot.Inventory.BlockedArtifacts), Is.EqualTo(math.min(i + 1, blocks)));
                    Assert.That(Snapshot.Inventory.Items[0].Index, Is.EqualTo(owned)); Assert.That(Snapshot.Inventory.Items[0].Quantity, Is.EqualTo(1));
                    Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(2));
                    if (i < blocks)
                    {
                        Assert.That(Snapshot.Rewards.PromptId, Is.EqualTo(command.PromptId + 1));
                        Assert.That(Snapshot.Inventory.BlockedArtifacts & (1u << index), Is.Not.Zero);
                    }
                    foreach (var choice in Snapshot.Rewards.Choices)
                        if (choice.Kind == RewardKind.Artifact) Assert.That(Snapshot.Inventory.BlockedArtifacts & (1u << choice.ArtifactIndex), Is.Zero);
                }
                Assert.That(Snapshot.PendingChests, Is.EqualTo(blocks == 3 ? 0u : 2u));
                run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                run.PendingChests = 0; run.Rewards.Active = 0;
                RewardRoll.Open(ref run.Rewards, run.Loadout, ref catalog.Value); m_Em.SetComponentData(m_Run, run); Tick(0);
                for (int i = 0; i <= rerolls; i++)
                {
                    var command = new SimulationCommand { Kind = SimulationCommandKind.RerollUpgrades,
                        Generation = Snapshot.Generation, PromptId = Snapshot.Rewards.PromptId };
                    var stale = command; stale.Generation--; m_Commands.Enqueue(stale);
                    stale = command; stale.PromptId--; m_Commands.Enqueue(stale); Tick(0);
                    Assert.That(Snapshot.Rewards.RerollsUsed, Is.EqualTo(i));
                    m_Commands.Enqueue(command); m_Commands.Enqueue(command); Tick(0);
                    Assert.That(Snapshot.Rewards.RerollsUsed, Is.EqualTo(math.min(i + 1, rerolls)));
                    Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(2)); Assert.That(Snapshot.Rewards.Active, Is.EqualTo(1));
                    Assert.That(Snapshot.Rewards.PromptId, Is.EqualTo(command.PromptId + (i < rerolls ? 1u : 0u)));
                    Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth));
                }
                m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = 0,
                    Generation = Snapshot.Generation, PromptId = Snapshot.Rewards.PromptId }); Tick(0);
                Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(1)); Assert.That(Snapshot.Rewards.RerollsUsed, Is.EqualTo(rerolls));
                Command(SimulationCommandKind.Restart); Tick(0);
                Assert.That(Snapshot.Rewards.RerollsUsed, Is.Zero); Assert.That(Snapshot.Inventory.BlockedArtifacts, Is.Zero);
                run = m_Em.GetComponentData<SimulationRunState>(m_Run); run.PendingChests = 1;
                ArtifactRoll.Open(ref run, ref catalog.Value);
                Assert.That(run.Rewards.Choices.Length, Is.EqualTo(3));
            }
            finally { catalog.Value.MaxArtifactBlocks = previousBlocks; catalog.Value.MaxUpgradeRerolls = previousRerolls; }
        }

        [TestCase(false)] [TestCase(true)]
        public void RewardAndArtifactSequencesChangeOnRestartUnlessFixedSeedIsEnabled(bool fixedSeed)
        {
            var catalog = m_Em.CreateEntityQuery(typeof(RewardCatalogSingleton)).GetSingleton<RewardCatalogSingleton>().Catalog;
            byte previousMode = catalog.Value.UseFixedSeed;
            try
            {
                catalog.Value.UseFixedSeed = (byte)(fixedSeed ? 1 : 0);
                var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                run.Rewards.RandomState = RewardRoll.SeedForRun(ref catalog.Value, run.Generation);
                run.ArtifactRandomState = ArtifactRoll.SeedForRun(ref catalog.Value, run.Generation);
                uint previousSeed = run.Rewards.RandomState;
                uint previousArtifactSeed = run.ArtifactRandomState;
                m_Em.SetComponentData(m_Run, run);
                Command(SimulationCommandKind.Restart); Tick();
                run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                Assert.That(run.Rewards.RandomState, Is.EqualTo(RewardRoll.SeedForRun(ref catalog.Value, run.Generation)));
                Assert.That(run.Rewards.RandomState == previousSeed, Is.EqualTo(fixedSeed));
                Assert.That(run.ArtifactRandomState, Is.EqualTo(ArtifactRoll.SeedForRun(ref catalog.Value, run.Generation)).And.Not.Zero);
                Assert.That(run.ArtifactRandomState == previousArtifactSeed, Is.EqualTo(fixedSeed));
            }
            finally { catalog.Value.UseFixedSeed = previousMode; }
        }
        [TestCase(2)] [TestCase(5)]
        public void LevelUpsQueueChoicesPauseCombatRejectRepeatedClicksAndRestartClearsRewards(int choices)
        {
            var catalog = m_Em.CreateEntityQuery(typeof(RewardCatalogSingleton)).GetSingleton<RewardCatalogSingleton>().Catalog;
            uint previousThreshold = catalog.Value.ExperiencePerLevel;
            int previousChoices = catalog.Value.ChoicesPerLevel;
            try
            {
                catalog.Value.ExperiencePerLevel = 50;
                catalog.Value.ChoicesPerLevel = choices;
                Entity enemy = Enemy(new float2(20, 0));
                Entity projectile = Projectile(new float2(10, 10), new float2(1, 0), true);
                var stats = m_Em.GetComponentData<PlayerStats>(m_Player); stats.Experience = 150; m_Em.SetComponentData(m_Player, stats);
                Tick();
                Assert.That(Snapshot.Player.Level, Is.EqualTo(3));
                Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth));
                Assert.That(Snapshot.Rewards.Active, Is.EqualTo(1)); Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(2));
                Assert.That(Snapshot.Rewards.Choices.Length, Is.EqualTo(choices));
                var enemyPosition = m_Em.GetComponentData<LocalTransform>(enemy).Position;
                var projectilePosition = m_Em.GetComponentData<LocalTransform>(projectile).Position;
                var lifetime = m_Em.GetComponentData<ProjectileData>(projectile).RemainingLifetime;
                var waveTimer = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).Timer;
                m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
                Tick(1);
                Assert.That(Snapshot.PlayerPosition, Is.EqualTo(float2.zero));
                Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position, Is.EqualTo(enemyPosition));
                Assert.That(m_Em.GetComponentData<LocalTransform>(projectile).Position, Is.EqualTo(projectilePosition));
                Assert.That(m_Em.GetComponentData<ProjectileData>(projectile).RemainingLifetime, Is.EqualTo(lifetime));
                Assert.That(m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).Timer, Is.EqualTo(waveTimer));
                var select = new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = choices - 1,
                    Generation = Snapshot.Generation, PromptId = Snapshot.Rewards.PromptId };
                m_Commands.Enqueue(select); m_Commands.Enqueue(select); Tick();
                Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(1));
                Assert.That(Snapshot.Rewards.PromptId, Is.EqualTo(select.PromptId + 1));
                Assert.That(Snapshot.PlayerPosition, Is.EqualTo(float2.zero));
                select.PromptId = Snapshot.Rewards.PromptId; m_Commands.Enqueue(select); Tick();
                Assert.That(Snapshot.Rewards.Active, Is.Zero); Assert.That(Snapshot.Rewards.Pending, Is.Zero);
                Assert.That(Snapshot.PlayerPosition.x, Is.GreaterThan(0));
                Command(SimulationCommandKind.Restart); Tick();
                var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                Assert.That(run.Loadout.Count, Is.EqualTo(1)); Assert.That(run.Loadout.FirstBonuses.Damage, Is.Zero);
                Assert.That(Snapshot.Loadout.FirstLevel, Is.EqualTo(1)); Assert.That(Snapshot.Loadout.SecondLevel, Is.Zero);
                Assert.That(Snapshot.FirstWeapon.Damage, Is.EqualTo(DefaultPlayer.Weapon.Damage));
                Assert.That(run.Rewards.Active, Is.Zero); Assert.That(run.Rewards.Pending, Is.Zero);
                Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth));
            }
            finally { catalog.Value.ExperiencePerLevel = previousThreshold; catalog.Value.ChoicesPerLevel = previousChoices; }
        }
        [TestCase(false)] [TestCase(true)]
        public void PausedKillAllCoalescesUntilResumeAndRestartDiscardsIt(bool restart)
        {
            Enemy(new float2(20, 0)); Enemy(new float2(20, 1));
            var catalog = m_Em.CreateEntityQuery(typeof(RewardCatalogSingleton)).GetSingleton<RewardCatalogSingleton>().Catalog;
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.Rewards.Pending = 1; RewardRoll.Open(ref run.Rewards, run.Loadout, ref catalog.Value);
            m_Em.SetComponentData(m_Run, run);
            var damage = m_Em.CreateEntityQuery(typeof(DamageEventQueueSingleton)).GetSingleton<DamageEventQueueSingleton>().DamageQueue;
            for (int tick = 0; tick < 3; tick++)
            {
                for (int i = 0; i < SimulationConstants.CommandQueueCapacity; i++) Command(SimulationCommandKind.KillAll);
                Tick();
                Assert.That(damage.Count, Is.Zero);
                Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(2));
                Assert.That(m_Em.GetComponentData<SimulationRunState>(m_Run).KillAllPending, Is.EqualTo(1));
            }
            if (restart) Command(SimulationCommandKind.Restart);
            else m_Commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = 0,
                PromptId = Snapshot.Rewards.PromptId, Generation = Snapshot.Generation });
            Tick();
            Assert.That(Snapshot.ActiveEnemies, Is.Zero);
            Assert.That(Snapshot.Kills, Is.EqualTo(restart ? 0 : 2));
            Assert.That(damage.Count, Is.Zero);
            Assert.That(m_Em.GetComponentData<SimulationRunState>(m_Run).KillAllPending, Is.Zero);
            Tick(); Assert.That(Snapshot.Kills, Is.EqualTo(restart ? 0 : 2));
        }

        [TestCase(WeaponType.Standard)] [TestCase(WeaponType.Explosive)] [TestCase(WeaponType.Laser)]
        public void SecondWeaponFiresWithIndependentCooldownUsingItsOwnWeaponType(WeaponType type)
        {
            var first = TestWeapon(WeaponType.Standard); first.Interval = .2f; first.Lifetime = 10;
            m_Em.SetComponentData(m_Player, first);
            var second = TestWeapon(type); second.Interval = .5f; second.Lifetime = 10;
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.Loadout.Count = 2; run.Loadout.SecondBase = run.Loadout.SecondWeapon = second;
            m_Em.SetComponentData(m_Run, run);
            Command(SimulationCommandKind.AutoAttack, 1); Tick(.25f);
            Assert.That(Snapshot.PlayerProjectiles, Is.EqualTo(first.Count));
            Tick(.25f);
            Assert.That(Snapshot.PlayerProjectiles, Is.EqualTo(first.Count * 2 + (type == WeaponType.Standard ? second.Count : 1)));
            using var projectiles = m_Em.CreateEntityQuery(typeof(PlayerProjectileTag), typeof(ProjectileActiveTag)).ToEntityArray(Allocator.Temp);
            int specialCount = 0;
            foreach (var projectile in projectiles)
            {
                if (type == WeaponType.Explosive && m_Em.IsComponentEnabled<ExplosiveProjectile>(projectile)) specialCount++;
                if (type == WeaponType.Laser && m_Em.IsComponentEnabled<LaserBeam>(projectile)) specialCount++;
            }
            Assert.That(specialCount, Is.EqualTo(type == WeaponType.Standard ? 0 : 1));
        }
        private Entity Enemy(float2 position, uint type = 1)
        {
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            Assert.That(pool.InactiveEnemies.TryDequeue(out Entity e));
            m_Em.SetComponentData(e, LocalTransform.FromPosition(new float3(position, 0)));
            m_Em.SetComponentData(e, new PreviousPosition { Value = position });
            m_Em.SetComponentData(e, new TypeId { Value = type });
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            m_Em.SetComponentData(e, new CurrentHealth { Value = catalog.Value.Configs[(int)type].MaxHealth });
            m_Em.SetComponentData(e, new EnemyRangedCooldown { CooldownTimer = 100 });
            m_Em.SetComponentEnabled<EnemyActiveTag>(e, true); m_Em.SetComponentEnabled<EnemyRangedTag>(e, type >= 2);
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run); run.ActiveEnemies++; m_Em.SetComponentData(m_Run, run);
            return e;
        }
        private Entity Projectile(float2 position, float2 velocity, bool player, float radius = .35f)
        {
            Entity e;
            if (player) Assert.That(m_Em.CreateEntityQuery(typeof(PlayerProjectilePoolSingleton)).GetSingleton<PlayerProjectilePoolSingleton>().InactiveProjectiles.TryDequeue(out e));
            else Assert.That(m_Em.CreateEntityQuery(typeof(EnemyProjectilePoolSingleton)).GetSingleton<EnemyProjectilePoolSingleton>().InactiveProjectiles.TryDequeue(out e));
            m_Em.SetComponentData(e, LocalTransform.FromPosition(new float3(position, 0)));
            m_Em.SetComponentData(e, new PreviousPosition { Value = position });
            m_Em.SetComponentData(e, new MovementVelocity { Value = velocity });
            m_Em.SetComponentData(e, new ProjectileData { Damage = 25, Radius = radius, RemainingLifetime = 10 });
            m_Em.SetComponentEnabled<ProjectileActiveTag>(e, true);
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            if (player) run.PlayerProjectiles++; else run.EnemyProjectiles++;
            m_Em.SetComponentData(m_Run, run); return e;
        }
        [Test]
        public void OverlappingTargetProducesFiniteProjectiles()
        {
            Enemy(float2.zero); Command(SimulationCommandKind.AutoAttack, 1); Tick(.25f);
            using var projectiles = m_Em.CreateEntityQuery(typeof(ProjectileActiveTag), typeof(PlayerProjectileTag)).ToEntityArray(Allocator.Temp);
            Assert.That(projectiles.Length, Is.EqualTo(3));
            foreach (var e in projectiles) Assert.That(math.all(math.isfinite(m_Em.GetComponentData<MovementVelocity>(e).Value)));
        }
        [TestCase(.5f, 0, .5f)] [TestCase(1, 1, 1)] [TestCase(0, 0, 0)]
        public void MovementPreservesAnalogMagnitudeAndClampsDiagonalInput(float x, float y, float magnitude)
        {
            m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(x, y) });
            Tick(.1f);
            Assert.That(math.length(Snapshot.PlayerPosition), Is.EqualTo(DefaultPlayer.Stats.MoveSpeed * magnitude * .1f).Within(.0001f));
        }
        [TestCase(.001f, false)] [TestCase(.004f, true)]
        public void ProjectileOnlyHitsWithinItsRemainingLifetime(float lifetime, bool hits)
        {
            Entity enemy = Enemy(new float2(.65f, 10));
            Entity projectile = Projectile(new float2(0, 10), new float2(100, 0), true, .01f);
            var data = m_Em.GetComponentData<ProjectileData>(projectile);
            data.RemainingLifetime = lifetime; m_Em.SetComponentData(projectile, data);
            Tick();
            Assert.That(m_Em.IsComponentEnabled<EnemyActiveTag>(enemy), Is.EqualTo(!hits));
            Assert.That(m_Em.IsComponentEnabled<ProjectileActiveTag>(projectile), Is.False);
        }
        [TestCase(WeaponType.Standard)] [TestCase(WeaponType.Explosive)] [TestCase(WeaponType.Laser)]
        public void EnemyEnteringAProjectileAfterExpiryTakesNoDamage(WeaponType type)
        {
            Entity enemy = Enemy(new float2(0, 3));
            Entity projectile = Projectile(type == WeaponType.Laser ? new float2(-2, 2) : new float2(0, 2), float2.zero, true, .01f);
            SetExpiringWeapon(projectile, type);
            Tick(.25f);
            Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position.y, Is.LessThan(2));
            Assert.That(m_Em.GetComponentData<CurrentHealth>(enemy).Value, Is.EqualTo(25));
        }
        [TestCase(WeaponType.Standard)] [TestCase(WeaponType.Explosive)] [TestCase(WeaponType.Laser)]
        public void PlayerEnteringAProjectileAfterExpiryTakesNoDamage(WeaponType type)
        {
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.PlayerPosition = new float2(0, -1); m_Em.SetComponentData(m_Run, run);
            m_Em.SetComponentData(m_Player, LocalTransform.FromPosition(new float3(0, -1, 0)));
            m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(0, 1) });
            Entity projectile = Projectile(type == WeaponType.Laser ? new float2(-2, 0) : float2.zero, float2.zero, false, .01f);
            SetExpiringWeapon(projectile, type);
            Tick(.2f);
            Assert.That(Snapshot.PlayerPosition.y, Is.GreaterThan(0));
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(Snapshot.Player.MaxHealth));
        }
        private void SetExpiringWeapon(Entity projectile, WeaponType type)
        {
            var data = m_Em.GetComponentData<ProjectileData>(projectile);
            data.RemainingLifetime = .001f; m_Em.SetComponentData(projectile, data);
            if (type == WeaponType.Explosive)
            {
                m_Em.SetComponentData(projectile, new ExplosiveProjectile { BlastRadius = .1f });
                m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, true);
            }
            if (type == WeaponType.Laser)
            {
                m_Em.SetComponentData(projectile, new LaserBeam { Direction = new float2(1, 0), Length = 4, PendingHit = 1 });
                m_Em.SetComponentEnabled<LaserBeam>(projectile, true);
            }
        }
        [TestCase(true)] [TestCase(false)]
        public void HarmlessContactDoesNotSuppressDamagingSources(bool projectile)
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var original = catalog.Value.Configs[1];
            try
            {
                catalog.Value.Configs[1].BaseDamage = 0;
                Enemy(new float2(.75f, 0));
                if (projectile) Projectile(new float2(-1, 0), new float2(100, 0), false, .01f);
                else Enemy(new float2(-.9f, 0), 0);
                Tick();
                Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(Snapshot.Player.MaxHealth - (projectile ? 25 : 20)));
            }
            finally { catalog.Value.Configs[1] = original; }
        }
        [Test]
        public void ExplosiveExpiryDetonatesAtTheLastLivePosition()
        {
            Entity projectile = Projectile(new float2(0, 10), new float2(100, 0), true, .01f);
            SetExpiringWeapon(projectile, WeaponType.Explosive);
            Tick(.1f);
            Assert.That(m_Em.GetComponentData<LocalTransform>(projectile).Position.x, Is.EqualTo(.1f).Within(.0001f));
            Assert.That(m_Em.GetComponentData<ExplosiveProjectile>(projectile).Detonated, Is.EqualTo(1));
        }
        [Test]
        public void RangedEnemyBeyondCrowdTierHoldsItsAuthoredDistance()
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var original = catalog.Value.Configs[2];
            try
            {
                catalog.Value.Configs[2].AttackRange = 30;
                catalog.Value.Configs[2].Weapon.Range = 30;
                Entity enemy = Enemy(new float2(25, 0), 2);
                Tick();
                Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position.x, Is.EqualTo(25).Within(.0001f));
            }
            finally { catalog.Value.Configs[2] = original; }
        }
        [Test]
        public void StandardProjectileVisualMatchesCollisionRadiusAndCenter()
        {
            Entity projectile = m_Em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(MovementVelocity),
                typeof(ProjectileData), typeof(ProjectileActiveTag), typeof(PlayerProjectileTag), typeof(ExplosiveProjectile),
                typeof(LaserBeam), typeof(MaterialMeshInfo), typeof(BaseColorOverride));
            try
            {
                m_Em.SetComponentData(projectile, LocalTransform.FromPosition(new float3(2, 10, 0)));
                m_Em.SetComponentData(projectile, new ProjectileData { Radius = 2 });
                m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, false);
                m_Em.SetComponentEnabled<LaserBeam>(projectile, false);
                m_World.GetOrCreateSystemManaged<SimulationRenderStateSystem>().Update();
                m_Em.CompleteAllTrackedJobs();
                var matrix = m_Em.GetComponentData<LocalToWorld>(projectile).Value;
                Assert.That(math.length(matrix.c0.xyz), Is.EqualTo(4).Within(.0001f));
                Assert.That(matrix.c3.y + math.length(matrix.c1.xyz) * .5f, Is.EqualTo(10).Within(.0001f));
            }
            finally { m_Em.DestroyEntity(projectile); }
        }
        [TestCase(WeaponType.Standard, 3)] [TestCase(WeaponType.Explosive, 1)] [TestCase(WeaponType.Laser, 1)]
        public void SelectedWeaponFiresOnlyItsOwnPooledProjectiles(WeaponType type, int count)
        {
            var weapon = TestWeapon(type);
            weapon.MaterialIndex = 3; weapon.TextureScale = new float2(1, .5f);
            m_Em.SetComponentData(m_Player, weapon);
            Command(SimulationCommandKind.AutoAttack, 1); Tick(weapon.Interval);
            using var projectiles = m_Em.CreateEntityQuery(typeof(ProjectileActiveTag), typeof(PlayerProjectileTag)).ToEntityArray(Allocator.Temp);
            Assert.That(projectiles.Length, Is.EqualTo(count));
            foreach (var e in projectiles)
            {
                Assert.That(m_Em.IsComponentEnabled<ExplosiveProjectile>(e), Is.EqualTo(type == WeaponType.Explosive));
                Assert.That(m_Em.IsComponentEnabled<LaserBeam>(e), Is.EqualTo(type == WeaponType.Laser));
                Assert.That(m_Em.GetComponentData<ProjectileData>(e).MaterialIndex, Is.EqualTo(3));
                Assert.That(m_Em.GetComponentData<ProjectileData>(e).TextureScale, Is.EqualTo(weapon.TextureScale));
                Assert.That(math.all(math.isfinite(m_Em.GetComponentData<MovementVelocity>(e).Value)));
            }
        }
        [Test]
        public void ExplosionDamagesNearbyEnemiesOnceAtFirstSweptImpact()
        {
            Entity first = Enemy(new float2(0, 10)), nearby = Enemy(new float2(0, 11.5f)), outside = Enemy(new float2(0, 15));
            foreach (var e in new[] { first, nearby, outside }) m_Em.SetComponentData(e, new CurrentHealth { Value = 200 });
            Entity projectile = Projectile(new float2(-2, 10), new float2(16, 0), true);
            m_Em.SetComponentData(projectile, new ExplosiveProjectile { BlastRadius = 2 });
            m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, true);
            Tick(.25f);
            Assert.That(m_Em.GetComponentData<CurrentHealth>(first).Value, Is.EqualTo(175));
            Assert.That(m_Em.GetComponentData<CurrentHealth>(nearby).Value, Is.EqualTo(175));
            Assert.That(m_Em.GetComponentData<CurrentHealth>(outside).Value, Is.EqualTo(200));
            Tick(.01f);
            Assert.That(m_Em.GetComponentData<CurrentHealth>(first).Value, Is.EqualTo(175));
            Tick(.15f);
            Assert.That(m_Em.IsComponentEnabled<ProjectileActiveTag>(projectile), Is.False);
        }
        [Test]
        public void ExplosiveExpiryDetonatesAndReturnsToPool()
        {
            Entity target = Enemy(new float2(-10, 11));
            Entity projectile = Projectile(new float2(-10, 10), float2.zero, true, .01f);
            m_Em.SetComponentData(projectile, new ProjectileData { Damage = 25, Radius = .01f, RemainingLifetime = .001f });
            m_Em.SetComponentData(projectile, new ExplosiveProjectile { BlastRadius = 2 });
            m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, true);
            Tick(.01f);
            Assert.That(m_Em.IsComponentEnabled<EnemyActiveTag>(target), Is.False);
            Tick(.15f);
            Assert.That(Snapshot.PlayerProjectiles, Is.Zero);
            Assert.That(m_Em.CreateEntityQuery(typeof(PlayerProjectilePoolSingleton)).GetSingleton<PlayerProjectilePoolSingleton>().InactiveProjectiles.Length,
                Is.EqualTo(SimulationConstants.DefaultMaxProjectiles));
        }
        [Test]
        public void LaserPiercesTargetsWithinRangeAndWidthOncePerPulse()
        {
            Entity first = Enemy(new float2(0, 10)), second = Enemy(new float2(2, 10));
            Entity outside = Enemy(new float2(4, 10)), offAxis = Enemy(new float2(1, 12));
            foreach (var e in new[] { first, second, outside, offAxis }) m_Em.SetComponentData(e, new CurrentHealth { Value = 200 });
            Entity projectile = Projectile(new float2(-2, 10), float2.zero, true, .1f);
            m_Em.SetComponentData(projectile, new LaserBeam { Direction = new float2(1, 0), Length = 5, PendingHit = 1 });
            m_Em.SetComponentEnabled<LaserBeam>(projectile, true);
            Tick(.01f); Tick(.01f);
            Assert.That(m_Em.GetComponentData<CurrentHealth>(first).Value, Is.EqualTo(175));
            Assert.That(m_Em.GetComponentData<CurrentHealth>(second).Value, Is.EqualTo(175));
            Assert.That(m_Em.GetComponentData<CurrentHealth>(outside).Value, Is.EqualTo(200));
            Assert.That(m_Em.GetComponentData<CurrentHealth>(offAxis).Value, Is.EqualTo(200));
            Command(SimulationCommandKind.Restart); Tick();
            Assert.That(m_Em.IsComponentEnabled<LaserBeam>(projectile), Is.False);
        }
        [Test]
        public void OverlappingExplosionsResolveTheFullEnemyPoolWithoutGrowingPerHitEvents()
        {
            for (int i = 0; i < SimulationConstants.DefaultMaxEnemies; i++)
                m_Em.SetComponentData(Enemy(new float2(0, 10)), new CurrentHealth { Value = 1000000 });
            for (int i = 0; i < 120; i++)
            {
                Entity projectile = Projectile(new float2(0, 10), float2.zero, true);
                m_Em.SetComponentData(projectile, new ExplosiveProjectile { BlastRadius = 4 });
                m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, true);
            }
            Tick(.001f);
            var enemies = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>().AllEnemies;
            for (int i = 0; i < enemies.Length; i++)
                Assert.That(m_Em.GetComponentData<CurrentHealth>(enemies[i]).Value, Is.EqualTo(1000000 - 120 * 25));
            Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(SimulationConstants.DefaultMaxEnemies));
        }
        [Test]
        public void LaserRenderingUsesBeamDirectionLengthAndResetsMaterialOnPoolReuse()
        {
            Entity e = m_Em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(MovementVelocity), typeof(ProjectileData),
                typeof(ProjectileActiveTag), typeof(ExplosiveProjectile), typeof(LaserBeam), typeof(MaterialMeshInfo), typeof(BaseColorOverride));
            try
            {
                m_Em.SetComponentData(e, LocalTransform.FromPosition(new float3(2, 3, 0)));
                m_Em.SetComponentData(e, new ProjectileData { Radius = .15f, RemainingLifetime = 1 });
                m_Em.SetComponentData(e, new LaserBeam { Direction = new float2(1, 0), Length = 18 });
                m_Em.SetComponentEnabled<ExplosiveProjectile>(e, false);
                var renderer = m_World.GetOrCreateSystemManaged<SimulationRenderStateSystem>();
                renderer.Update(); m_Em.CompleteAllTrackedJobs();
                var matrix = m_Em.GetComponentData<LocalToWorld>(e).Value;
                Assert.That(math.distance(matrix.c1.xy, new float2(18, 0)), Is.LessThan(.001f));
                Assert.That(math.length(matrix.c0.xyz), Is.EqualTo(.3f).Within(.001f));
                Assert.That(m_Em.GetComponentData<MaterialMeshInfo>(e).Material, Is.EqualTo(MaterialMeshInfo.FromRenderMeshArrayIndices(1, 0).Material));
                m_Em.SetComponentData(e, new LaserBeam { Direction = new float2(1, 0), Length = 18, Charging = 1 });
                m_Em.SetComponentData(e, new ProjectileData { Radius = .15f, Color = new float4(1) });
                renderer.Update(); m_Em.CompleteAllTrackedJobs();
                matrix = m_Em.GetComponentData<LocalToWorld>(e).Value;
                Assert.That(math.distance(matrix.c1.xy, new float2(18, 0)), Is.LessThan(.001f));
                Assert.That(math.length(matrix.c0.xyz), Is.EqualTo(.075f).Within(.001f));
                Assert.That(m_Em.GetComponentData<BaseColorOverride>(e).Value.w, Is.EqualTo(.5f));
                m_Em.SetComponentEnabled<LaserBeam>(e, false);
                renderer.Update(); m_Em.CompleteAllTrackedJobs();
                Assert.That(m_Em.GetComponentData<MaterialMeshInfo>(e).Material, Is.EqualTo(MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0).Material));
            }
            finally { m_Em.DestroyEntity(e); }
        }
        [Test]
        public void WeaponTextureSurvivesRewardConversionRenderingAndPoolReuse()
        {
            var texture = new Texture2D(8, 4);
            var weapon = ScriptableObject.CreateInstance<CharacterWeaponDefinition>();
            Entity projectile = m_Em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(MovementVelocity), typeof(ProjectileData),
                typeof(ProjectileActiveTag), typeof(ExplosiveProjectile), typeof(LaserBeam), typeof(MaterialMeshInfo), typeof(BaseColorOverride));
            try
            {
                weapon.ProjectileTexture = texture;
                var character = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/GameData/Characters/DefaultCharacter.asset");
                using var catalog = new RewardSettings { Weapons = new[] { weapon } }.BuildCatalog(character, character.Weapon, w => w == weapon ? 3 : 0);
                var config = catalog.Value.Weapons[1].Config;
                Assert.That(config.MaterialIndex, Is.EqualTo(3));
                Assert.That(config.TextureScale, Is.EqualTo(new float2(1, .5f)));
                m_Em.SetComponentData(projectile, LocalTransform.FromPosition(new float3(2, 10, 0)));
                m_Em.SetComponentData(projectile, new ProjectileData { Radius = 2, MaterialIndex = config.MaterialIndex, TextureScale = config.TextureScale });
                m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, false);
                m_Em.SetComponentEnabled<LaserBeam>(projectile, false);
                var renderer = m_World.GetOrCreateSystemManaged<SimulationRenderStateSystem>();
                renderer.Update(); m_Em.CompleteAllTrackedJobs();
                var matrix = m_Em.GetComponentData<LocalToWorld>(projectile).Value;
                Assert.That(math.length(matrix.c0.xyz), Is.EqualTo(4).Within(.001f));
                Assert.That(math.length(matrix.c1.xyz), Is.EqualTo(2).Within(.001f));
                Assert.That(matrix.c3.y + 1, Is.EqualTo(10).Within(.001f));
                Assert.That(m_Em.GetComponentData<MaterialMeshInfo>(projectile).Material, Is.EqualTo(MaterialMeshInfo.FromRenderMeshArrayIndices(3, 0).Material));
                m_Em.SetComponentData(projectile, new ExplosiveProjectile { Detonated = 1, BlastRadius = 3 });
                m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, true);
                renderer.Update(); m_Em.CompleteAllTrackedJobs();
                Assert.That(m_Em.GetComponentData<MaterialMeshInfo>(projectile).Material, Is.EqualTo(MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0).Material));
                Assert.That(math.length(m_Em.GetComponentData<LocalToWorld>(projectile).Value.c1.xyz), Is.EqualTo(6).Within(.001f));
                m_Em.SetComponentData(projectile, new ProjectileData { Radius = 2 });
                m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, false);
                m_Em.SetComponentEnabled<LaserBeam>(projectile, true);
                renderer.Update(); m_Em.CompleteAllTrackedJobs();
                Assert.That(m_Em.GetComponentData<MaterialMeshInfo>(projectile).Material, Is.EqualTo(MaterialMeshInfo.FromRenderMeshArrayIndices(1, 0).Material));
            }
            finally { m_Em.DestroyEntity(projectile); Object.DestroyImmediate(weapon); Object.DestroyImmediate(texture); }
        }
        [Test]
        public void AuthoringAssetsProduceClampedNativeCharacterAndEnemyData()
        {
            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            var characterWeapon = ScriptableObject.CreateInstance<CharacterWeaponDefinition>();
            var enemyWeapon = ScriptableObject.CreateInstance<EnemyWeaponDefinition>();
            try
            {
                character.MaxHealth = 250; character.MoveSpeed = 8; character.InvulnerabilityDuration = .5f;
                characterWeapon.Type = WeaponType.Laser; character.Weapon = characterWeapon;
                var player = character.ToConfig();
                Assert.That(player.Stats.CurrentHealth, Is.EqualTo(250));
                Assert.That(player.Stats.MoveSpeed, Is.EqualTo(8)); Assert.That(player.Weapon.Type, Is.EqualTo(WeaponType.Laser));
                enemy.Weapon = enemyWeapon; enemyWeapon.AttackRange = 6;
                enemy.Ranged = true; enemy.RetreatRange = 8; enemy.AttackRange = 6; enemy.CollisionRadius = 20; enemy.SpawnWeight = 2;
                var config = enemy.ToConfig(3, DefaultPlayer.Stats.CollisionRadius);
                Assert.That(config.SpawnThreshold, Is.EqualTo(5)); Assert.That(config.RetreatRange, Is.EqualTo(6));
                Assert.That(config.CollisionRadius, Is.EqualTo(5)); Assert.That(config.Weapon.Interval, Is.GreaterThan(0));
                enemy.Ranged = false; Assert.That(enemy.ToConfig(0, DefaultPlayer.Stats.CollisionRadius).Weapon.Interval, Is.Zero);
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(enemy); Object.DestroyImmediate(characterWeapon); Object.DestroyImmediate(enemyWeapon); }
        }
        [Test]
        public void MissingAssetsCannotActivateCodePresets()
        {
            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            using var world = new World("Missing authoring assets");
            try
            {
                Assert.Throws<InvalidOperationException>(() => character.ToConfig());
                enemy.Ranged = true;
                Assert.Throws<InvalidOperationException>(() => enemy.ToConfig(0, .4f));
                world.EntityManager.AddComponentData(world.EntityManager.CreateEntity(), new PureDotsPrefabsSingleton());
                world.GetOrCreateSystem<SimulationBootstrapSystem>().Update(world.Unmanaged);
                Assert.That(world.EntityManager.CreateEntityQuery(typeof(SimulationRunState)).IsEmpty, Is.True);
                Assert.That(world.EntityManager.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).IsEmpty, Is.True);
                Assert.That(world.EntityManager.CreateEntityQuery(typeof(EnemyPoolSingleton)).IsEmpty, Is.True);
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(enemy); }
        }
        [Test]
        public void SampleSceneReferencesTheEditableCharacterRosterAndWeapons()
        {
            var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
                var settings = Object.FindAnyObjectByType<GamePresentationBootstrap>();
                Assert.That(settings, Is.Not.Null);
                Assert.That(settings.TryValidateConfiguration(out string error), Is.True, error);
                Assert.That(settings.EnemyTypes, Is.Not.Empty);
                Assert.That(settings.StartingCharacter.Weapon, Is.Not.Null);
                Assert.That(settings.StartingCharacter.Texture, Is.Not.Null);
                foreach (var enemy in settings.EnemyTypes)
                {
                    Assert.That(enemy.Texture, Is.Not.Null);
                    if (enemy.Ranged) Assert.That(enemy.Weapon, Is.Not.Null);
                }
                foreach (WeaponType type in Enum.GetValues(typeof(WeaponType)))
                    Assert.That(TestWeapon(type).Type, Is.EqualTo(type));
            }
            finally { RestoreScenes(setup); }
        }
        private static void RestoreScenes(UnityEditor.SceneManagement.SceneSetup[] setup)
        {
            foreach (var scene in setup)
                if (scene.isLoaded && scene.isActive)
                { UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup); return; }
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        }
        [TestCase(.4f, false)] [TestCase(.8f, true)]
        public void CharacterBodyRadiusControlsProjectileCollision(float radius, bool hits)
        {
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.PlayerCollisionRadius = radius; m_Em.SetComponentData(m_Run, run);
            Projectile(new float2(-2, .7f), new float2(4, 0), false, .01f);
            Tick(1);
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(Snapshot.Player.MaxHealth - (hits ? 25 : 0)));
        }
        [Test]
        public void EnemyContactCooldownUsesTheAuthoredInterval()
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            var original = catalog.Value.Configs[(int)RunnerType];
            try
            {
                catalog.Value.Configs[(int)RunnerType].ContactAttackInterval = .2f;
                Entity enemy = Enemy(new float2(.75f, 0));
                Tick(.001f);
                Assert.That(m_Em.GetComponentData<EnemyMeleeCooldown>(enemy).CooldownTimer, Is.EqualTo(.2f));
                Tick(.1f);
                Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(Snapshot.Player.MaxHealth - 10));
                Tick(.11f);
                Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(Snapshot.Player.MaxHealth - 20));
            }
            finally { catalog.Value.Configs[(int)RunnerType] = original; }
        }
        [TestCase(WeaponType.Standard)] [TestCase(WeaponType.Explosive)] [TestCase(WeaponType.Laser)]
        public void SceneAuthoringControlsBootstrapSpawningAndRestart(WeaponType enemyWeaponType)
        {
            var settings = Object.FindAnyObjectByType<GamePresentationBootstrap>();
            GameObject ownedSettings = null;
            if (settings == null)
            { ownedSettings = new GameObject("Authoring test settings"); settings = ownedSettings.AddComponent<GamePresentationBootstrap>(); }
            var oldCharacter = settings.StartingCharacter;
            var oldWeaponAsset = settings.StartingWeaponAsset;
            var oldEnemies = (EnemyDefinition[])settings.EnemyTypes?.Clone();
            string oldSpawning = JsonUtility.ToJson(settings.EnemySpawning);
            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            var characterWeapon = ScriptableObject.CreateInstance<CharacterWeaponDefinition>();
            var overrideWeapon = ScriptableObject.CreateInstance<CharacterWeaponDefinition>();
            var enemyWeapon = ScriptableObject.CreateInstance<EnemyWeaponDefinition>();
            var serialized = new UnityEditor.SerializedObject(settings);
            World world = null;
            try
            {
                var spawning = serialized.FindProperty("m_EnemySpawning");
                spawning.FindPropertyRelative("SpawnInterval").floatValue = .25f;
                spawning.FindPropertyRelative("BatchSize").intValue = 3;
                spawning.FindPropertyRelative("SpawnRateScalingInterval").floatValue = 10;
                spawning.FindPropertyRelative("SpawnRateMultiplier").floatValue = 1.5f;
                spawning.FindPropertyRelative("StatScalingInterval").floatValue = 5;
                spawning.FindPropertyRelative("HealthMultiplier").floatValue = 2;
                spawning.FindPropertyRelative("EnableLargeSpawns").boolValue = true;
                spawning.FindPropertyRelative("LargeSpawnInterval").floatValue = 1;
                spawning.FindPropertyRelative("LargeSpawnCount").intValue = 4;
                if (enemyWeaponType == WeaponType.Laser)
                {
                    spawning.FindPropertyRelative("MinRadius").floatValue = 5;
                    spawning.FindPropertyRelative("MaxRadius").floatValue = 6;
                }
                character.MaxHealth = 250; character.MoveSpeed = 8; character.InvulnerabilityDuration = .5f;
                character.CollisionRadius = .65f; character.RespawnGracePeriod = 2.75f;
                character.Texture = Texture2D.whiteTexture;
                characterWeapon.Type = WeaponType.Laser; characterWeapon.Damage = 47; characterWeapon.AttackInterval = .9f;
                character.Weapon = characterWeapon;
                enemy.ContactAttackInterval = 1.7f; enemy.MaxHealth = 300; enemy.MoveSpeed = 0; enemy.Ranged = true;
                enemy.AvailableAfterSeconds = .05f;
                enemyWeapon.Type = enemyWeaponType; enemyWeapon.AttackRange = 64; enemyWeapon.AttackInterval = .05f;
                enemyWeapon.Tint = new Color(.2f, .3f, .7f, .8f); enemyWeapon.ProjectileCount = 1; enemyWeapon.Damage = 9; enemy.Weapon = enemyWeapon;
                enemy.AttackRange = 64;
                enemy.Texture = Texture2D.whiteTexture;
                serialized.FindProperty("m_StartingCharacter").objectReferenceValue = character;
                serialized.FindProperty("m_StartingWeaponAsset").objectReferenceValue = null;
                var roster = serialized.FindProperty("m_EnemyTypes"); roster.arraySize = 2;
                roster.GetArrayElementAtIndex(0).objectReferenceValue = null;
                roster.GetArrayElementAtIndex(1).objectReferenceValue = enemy;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                world = new World("Scene authoring integration"); var em = world.EntityManager;
                world.GetOrCreateSystemManaged<GameConfigurationBootstrapSystem>().Update();
                world.GetOrCreateSystemManaged<PureDotsRenderBootstrapSystem>().Update();
                world.GetOrCreateSystem<SimulationBootstrapSystem>().Update(world.Unmanaged);
                var runEntity = em.CreateEntityQuery(typeof(SimulationRunState)).GetSingletonEntity();
                var player = em.GetComponentData<SimulationRunState>(runEntity).Player;
                Assert.That(em.GetComponentData<PlayerStats>(player).MaxHealth, Is.EqualTo(250));
                Assert.That(em.GetComponentData<PlayerWeapon>(player).Type, Is.EqualTo(WeaponType.Laser));
                Assert.That(em.GetComponentData<PlayerWeapon>(player).Damage, Is.EqualTo(47));
                var catalog = em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
                Assert.That(catalog.Value.Configs.Length, Is.EqualTo(1));
                Assert.That(catalog.Value.Configs[0].ContactAttackInterval, Is.EqualTo(1.7f));
                Assert.That(catalog.Value.Configs[0].AvailableAfterSeconds, Is.EqualTo(.05f));
                Assert.That(em.GetComponentData<SimulationRunState>(runEntity).PlayerCollisionRadius, Is.EqualTo(.65f));
                var commands = em.CreateEntityQuery(typeof(SimulationCommandQueue)).GetSingleton<SimulationCommandQueue>().Commands;
                commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.AutoAttack, Value = 0 });
                commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.SpawnExtra, Value = 10 });
                var pipeline = world.GetOrCreateSystem<SimulationPipelineSystem>();
                world.SetTime(new TimeData(.1, .1f)); pipeline.Update(world.Unmanaged);
                em.GetComponentData<SimulationJobFence>(runEntity).Handle.Complete();
                var snapshot = em.GetComponentData<SimulationSnapshot>(runEntity);
                Assert.That(snapshot.ActiveEnemies, Is.EqualTo(10)); Assert.That(snapshot.EnemyProjectiles, Is.EqualTo(10));
                var pools = em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
                for (int i = 0; i < 10; i++)
                {
                    Assert.That(em.GetComponentData<TypeId>(pools.AllEnemies[i]).Value, Is.Zero);
                    Assert.That(em.IsComponentEnabled<EnemyRangedTag>(pools.AllEnemies[i]), Is.True);
                }
                using (var projectiles = em.CreateEntityQuery(typeof(ProjectileActiveTag), typeof(EnemyProjectileTag)).ToEntityArray(Allocator.Temp))
                    foreach (var projectile in projectiles)
                    {
                        Assert.That(em.GetComponentData<ProjectileData>(projectile).Damage, Is.EqualTo(enemyWeaponType == WeaponType.Laser ? 0 : 9));
                        Assert.That(em.GetComponentData<ProjectileData>(projectile).Color, Is.EqualTo(new float4(.2f, .3f, .7f, .8f)));
                        Assert.That(em.IsComponentEnabled<ExplosiveProjectile>(projectile), Is.EqualTo(enemyWeaponType == WeaponType.Explosive));
                        Assert.That(em.IsComponentEnabled<LaserBeam>(projectile), Is.EqualTo(enemyWeaponType == WeaponType.Laser));
                        if (enemyWeaponType == WeaponType.Laser)
                            Assert.That(em.GetComponentData<LaserBeam>(projectile).Charging, Is.EqualTo(1));
                    }
                var stats = em.GetComponentData<PlayerStats>(player); stats.MaxHealth = 999; em.SetComponentData(player, stats);
                commands.Enqueue(new SimulationCommand { Kind = SimulationCommandKind.Restart });
                world.SetTime(new TimeData(.11, .01f)); pipeline.Update(world.Unmanaged);
                em.GetComponentData<SimulationJobFence>(runEntity).Handle.Complete();
                Assert.That(em.GetComponentData<PlayerStats>(player).MaxHealth, Is.EqualTo(250));
                Assert.That(em.GetComponentData<PlayerWeapon>(player).Type, Is.EqualTo(WeaponType.Laser));
                Assert.That(em.GetComponentData<PlayerInvulnerability>(player).InvulnerabilityDuration, Is.EqualTo(.5f));
                Assert.That(em.GetComponentData<PlayerInvulnerability>(player).Timer, Is.EqualTo(2.74f).Within(.0001f));
                Assert.That(em.GetComponentData<PlayerStats>(player).CollisionRadius, Is.EqualTo(.65f));
                var wave = em.CreateEntityQuery(typeof(WaveSpawnerConfig)).GetSingleton<WaveSpawnerConfig>();
                Assert.That(wave.SpawnInterval, Is.EqualTo(.25f)); Assert.That(wave.BatchSize, Is.EqualTo(3));
                Assert.That(wave.SpawnRateScalingInterval, Is.EqualTo(10)); Assert.That(wave.SpawnRateMultiplier, Is.EqualTo(1.5f));
                Assert.That(wave.StatScalingInterval, Is.EqualTo(5)); Assert.That(wave.StatMultipliers.x, Is.EqualTo(2));
                Assert.That(wave.LargeSpawnInterval, Is.EqualTo(1)); Assert.That(wave.LargeSpawnCount, Is.EqualTo(4));
                Assert.That(wave.ElapsedSeconds, Is.EqualTo(.01f).Within(.0001));
                Assert.That(wave.LargeSpawnTimer, Is.EqualTo(.01f).Within(.0001));
                world.Dispose(); world = null;
                overrideWeapon.Type = WeaponType.Explosive; overrideWeapon.Damage = 123;
                serialized.Update(); serialized.FindProperty("m_StartingWeaponAsset").objectReferenceValue = overrideWeapon;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                world = new World("Weapon override integration");
                world.GetOrCreateSystemManaged<GameConfigurationBootstrapSystem>().Update();
                world.GetOrCreateSystemManaged<PureDotsRenderBootstrapSystem>().Update();
                world.GetOrCreateSystem<SimulationBootstrapSystem>().Update(world.Unmanaged);
                var startingPlayer = world.EntityManager.CreateEntityQuery(typeof(StartingPlayerConfig)).GetSingleton<StartingPlayerConfig>();
                Assert.That(startingPlayer.Weapon.Type, Is.EqualTo(WeaponType.Explosive));
                Assert.That(startingPlayer.Weapon.Damage, Is.EqualTo(123));
            }
            finally
            {
                world?.Dispose();
                JsonUtility.FromJsonOverwrite(oldSpawning, settings.EnemySpawning);
                serialized.Update(); serialized.FindProperty("m_StartingCharacter").objectReferenceValue = oldCharacter;
                serialized.FindProperty("m_StartingWeaponAsset").objectReferenceValue = oldWeaponAsset;
                var roster = serialized.FindProperty("m_EnemyTypes"); roster.arraySize = oldEnemies?.Length ?? 0;
                for (int i = 0; i < roster.arraySize; i++) roster.GetArrayElementAtIndex(i).objectReferenceValue = oldEnemies[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (ownedSettings != null) Object.DestroyImmediate(ownedSettings);
                Object.DestroyImmediate(character); Object.DestroyImmediate(enemy);
                Object.DestroyImmediate(characterWeapon); Object.DestroyImmediate(enemyWeapon);
                Object.DestroyImmediate(overrideWeapon);
            }
            Assert.That(Texture2D.whiteTexture != null, "Authored textures must remain owned by their assets.");
        }
        [TestCase(false)] [TestCase(true)]
        public void WeaponAssetsClampSettingsAndMeleeEnemiesIgnoreDisabledRangedSettings(bool forEnemies)
        {
            WeaponDefinition weapon = forEnemies ? (WeaponDefinition)ScriptableObject.CreateInstance<EnemyWeaponDefinition>()
                : ScriptableObject.CreateInstance<CharacterWeaponDefinition>();
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            try
            {
                weapon.Type = WeaponType.Laser; weapon.AttackInterval = 0; weapon.AttackRange = 6;
                weapon.ProjectileRadius = 0; weapon.ProjectileCount = 20; weapon.Damage = 73;
                var config = weapon.ToConfig();
                Assert.That(config.Interval, Is.EqualTo(.05f)); Assert.That(config.Radius, Is.EqualTo(.01f));
                Assert.That(config.Count, Is.EqualTo(12)); Assert.That(config.Damage, Is.EqualTo(73));
                enemy.CollisionRadius = .35f; enemy.Weapon = weapon as EnemyWeaponDefinition; enemy.AttackRange = 64; enemy.RetreatRange = 30;
                var melee = enemy.ToConfig(0, DefaultPlayer.Stats.CollisionRadius);
                Assert.That(melee.Weapon.Interval, Is.Zero); Assert.That(melee.RetreatRange, Is.Zero);
                Assert.That(melee.AttackRange, Is.EqualTo(DefaultPlayer.Stats.CollisionRadius + enemy.CollisionRadius + CrowdConstants.PlayerContactSkin));
                if (forEnemies)
                {
                    enemy.Ranged = true; var ranged = enemy.ToConfig(0, DefaultPlayer.Stats.CollisionRadius);
                    Assert.That(ranged.AttackRange, Is.EqualTo(6)); Assert.That(ranged.RetreatRange, Is.EqualTo(6));
                }
            }
            finally { Object.DestroyImmediate(weapon); Object.DestroyImmediate(enemy); }
        }
        [TestCase(WeaponType.Explosive)] [TestCase(WeaponType.Laser)]
        public void EnemyAreaWeaponsDamageThePlayerOnceAndResetTheirPoolState(WeaponType type)
        {
            Entity projectile = Projectile(new float2(-2, 0), new float2(16, 0), false);
            if (type == WeaponType.Explosive)
            {
                m_Em.SetComponentData(projectile, new ExplosiveProjectile { BlastRadius = 1 });
                m_Em.SetComponentEnabled<ExplosiveProjectile>(projectile, true);
            }
            else
            {
                m_Em.SetComponentData(projectile, new MovementVelocity());
                m_Em.SetComponentData(projectile, new LaserBeam { Direction = new float2(1, 0), Length = 5, PendingHit = 1 });
                m_Em.SetComponentEnabled<LaserBeam>(projectile, true);
            }
            Tick(.25f); Tick(.01f);
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth - 25));
            Command(SimulationCommandKind.Restart); Tick();
            Assert.That(Snapshot.EnemyProjectiles, Is.Zero);
            Assert.That(m_Em.IsComponentEnabled<ExplosiveProjectile>(projectile), Is.False);
            Assert.That(m_Em.IsComponentEnabled<LaserBeam>(projectile), Is.False);
        }
        [Test]
        public void PlayerProjectileHitsBetweenEndpoints()
        {
            Entity e = Enemy(new float2(0, 10));
            Entity p = Projectile(new float2(-1.3f, 10), new float2(16, 0), true);
            Tick(.1625f);
            Assert.That(m_Em.IsComponentEnabled<EnemyActiveTag>(e), Is.False);
            Assert.That(m_Em.IsComponentEnabled<ProjectileActiveTag>(p), Is.False);
        }
        [Test]
        public void EnemyProjectileHitsAcrossLongRelativeSweep()
        {
            Entity p = Projectile(new float2(-.9f, 0), new float2(13, 0), false, .3f);
            var stats = m_Em.GetComponentData<PlayerStats>(m_Player); stats.CurrentHealth = stats.MaxHealth = 100; m_Em.SetComponentData(m_Player, stats);
            m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(-1, 0) });
            Tick(1f / 3);
            Assert.That(m_Em.IsComponentEnabled<ProjectileActiveTag>(p), Is.False);
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(75));
        }
        [Test]
        public void RunnerRadiusDoesNotUseTankRadius()
        {
            Entity e = Enemy(new float2(.85f, 0));
            Projectile(float2.zero, float2.zero, true);
            Tick(.001f);
            Assert.That(m_Em.GetComponentData<CurrentHealth>(e).Value, Is.EqualTo(25));
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth));
        }
        [TestCase(0u, 25u)] [TestCase(1u, 10u)] [TestCase(2u, 15u)] [TestCase(3u, 20u)]
        public void DeathRewardsComeFromCatalog(uint type, uint expected)
        {
            Entity e = Enemy(new float2(10, 0), type);
            var damage = m_Em.CreateEntityQuery(typeof(DamageEventQueueSingleton)).GetSingleton<DamageEventQueueSingleton>().DamageQueue;
            damage.Enqueue(new DamageEvent { TargetEntity = e, TargetKey = DamageEvent.CreateTargetKey(e), Damage = 1000 });
            Tick();
            var gems = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>().AllGems;
            ulong value = 0;
            for (int i = 0; i < gems.Length; i++) if (gems[i].IsActive != 0) value += gems[i].ExperienceValue;
            Assert.That(value, Is.EqualTo(expected)); Assert.That(Snapshot.Kills, Is.EqualTo(1));
        }
        [Test]
        public void RestartRestoresAllPoolsProgressionAndTimers()
        {
            Enemy(new float2(10, 0), 0); Projectile(new float2(5, 0), new float2(1, 0), true);
            Projectile(new float2(10, 0), new float2(1, 0), false);
            var stats = m_Em.GetComponentData<PlayerStats>(m_Player); stats.Level = 5; stats.Experience = 20; stats.MaxHealth += 40;
            stats.IsDead = 1; stats.CurrentHealth = 0; m_Em.SetComponentData(m_Player, stats);
            Command(SimulationCommandKind.Restart); Tick();
            Assert.That(Snapshot.Player.Level, Is.EqualTo(1)); Assert.That(Snapshot.Player.Experience, Is.Zero);
            Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth));
            Assert.That(Snapshot.ActiveEnemies + Snapshot.PlayerProjectiles + Snapshot.EnemyProjectiles + Snapshot.ActiveGems, Is.Zero);
            Assert.That(Snapshot.Kills, Is.Zero); Assert.That(Snapshot.TotalExperience, Is.Zero);
            Assert.That(m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>().InactiveEnemies.Length, Is.EqualTo(SimulationConstants.DefaultMaxEnemies));
            Assert.That(m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>().FreeGems.Length, Is.EqualTo(1024));
            var wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave);
            Assert.That(wave.BatchSize, Is.EqualTo(35)); Assert.That(wave.RandomSeed, Is.EqualTo(777123));
            Assert.That(wave.Timer, Is.LessThan(.02f));
        }
        [Test]
        public void ExtraSpawnIsConsumedOnceWithoutChangingRecurringWave()
        {
            Command(SimulationCommandKind.SpawnExtra, 100); Tick();
            Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(100));
            Assert.That(m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).BatchSize, Is.EqualTo(35));
            Tick(); Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(100));
        }
        [Test]
        public void CosmeticQueuesRemainBoundedWhileGameplayProcessesEveryDeath()
        {
            Command(SimulationCommandKind.SpawnExtra, 50000); Tick();
            Command(SimulationCommandKind.KillAll); Tick();
            Assert.That(Snapshot.Kills, Is.EqualTo(SimulationConstants.DefaultMaxEnemies)); Assert.That(Snapshot.ActiveEnemies, Is.Zero);
            var bridge = m_Em.CreateEntityQuery(typeof(SimulationBridgeQueuesSingleton)).GetSingleton<SimulationBridgeQueuesSingleton>();
            Assert.That(bridge.DeathEventQueue.Count, Is.LessThanOrEqualTo(SimulationConstants.CosmeticQueueCapacity));
            Assert.That(bridge.GemCollectEventQueue.Count, Is.LessThanOrEqualTo(SimulationConstants.CosmeticQueueCapacity));
        }
        [Test]
        public void PipelineKeepsDelayedGridProducersOffMainThreadAndInDependencyChain()
        {
            Enemy(new float2(.1f, 0));
            Tick(); // Warm up reflection and Burst compilation.
            var producer = m_World.GetOrCreateSystem<DelayedRunProducerSystem>();
            producer.Update(m_World.Unmanaged);
            JobHandle delay = m_World.Unmanaged.ResolveSystemStateRef(producer).Dependency;
            m_World.SetTime(new TimeData(++m_Time, 1f / 60));
            var watch = Stopwatch.StartNew(); m_Pipeline.Update(m_World.Unmanaged); watch.Stop();
            var fence = m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle;
            Assert.That(JobHandle.CheckFenceIsDependencyOrDidSyncFence(fence, delay));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(150), "Scheduling must not wait for the delayed worker.");
            fence.Complete();
            Assert.That(m_Em.CreateEntityQuery(typeof(EnemySpatialGridSingleton)).GetSingleton<EnemySpatialGridSingleton>().Grid.Count(), Is.EqualTo(1));
        }
        [Test]
        public void ReplayingAfterRestartProducesIdenticalSimulation()
        {
            uint first = Replay(); Command(SimulationCommandKind.Restart); Tick();
            var wave = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave); wave.SpawnInterval = 100000; m_Em.SetComponentData(m_Wave, wave);
            // Align the restart tick with the SetUp tick; invulnerability does not affect this distant swarm.
            uint second = Replay(); Assert.That(second, Is.EqualTo(first));
        }
        private uint Replay()
        {
            Command(SimulationCommandKind.SpawnExtra, 1000);
            for (int i = 0; i < 15; i++) Tick();
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            uint hash = 0;
            for (int i = 0; i < pool.AllEnemies.Length; i++)
            {
                Entity e = pool.AllEnemies[i];
                if (m_Em.IsComponentEnabled<EnemyActiveTag>(e)) hash = math.hash(new uint3(hash, math.hash(m_Em.GetComponentData<LocalTransform>(e).Position), m_Em.GetComponentData<TypeId>(e).Value));
            }
            return hash;
        }
        [TestCase(WeaponType.Standard)] [TestCase(WeaponType.Explosive)] [TestCase(WeaponType.Laser)]
        public void SchedulingAllocatesNoManagedMemoryAfterWarmup(WeaponType type)
        {
            m_Em.SetComponentData(m_Player, TestWeapon(type));
            Command(SimulationCommandKind.AutoAttack, 1); Command(SimulationCommandKind.GodMode, 1);
            Enemy(new float2(5, 0), 0); Command(SimulationCommandKind.SpawnExtra, 300);
            for (int i = 0; i < 4; i++) Tick(.25f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) Tick(.25f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
        [Test]
        public void FloorPhasePreservesTextureAcrossNonAlignedRebases()
        {
            float2 point = new float2(100, -25), delta = new float2(2100.25f, -2000.75f);
            float2 phase = PresentationDepth.RebaseFloorPhase(float2.zero, delta, 2);
            Assert.That(math.distance(math.frac(point / 2), math.frac((point - delta + phase) / 2)), Is.LessThan(.001f));
            Assert.That(math.all(phase >= 0) && math.all(phase < 2));
        }
        [Test]
        public void GemPresentationDepthAndFlashRefreshEveryFrame()
        {
            Entity e = m_Em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(GemData), typeof(GemActiveTag),
                typeof(MaterialMeshInfo), typeof(SpriteUVOffset), typeof(BaseColorOverride), typeof(GemVisualState));
            m_Em.SetComponentData(e, LocalTransform.FromPosition(new float3(3, 5, 0)));
            m_Em.SetComponentData(e, new GemData { Tier = 3, ExperienceValue = 20 });
            var query = m_Em.CreateEntityQuery(new EntityQueryDesc
            { All = new[] { ComponentType.ReadOnly<GemVisualState>() }, Options = EntityQueryOptions.IgnoreComponentEnabledState });
            var renderSystem = m_World.GetOrCreateSystemManaged<SimulationRenderStateSystem>();
            var snapshot = Snapshot; snapshot.PlayerPosition = float2.zero;
            m_Em.SetComponentData(m_Run, snapshot);
            m_World.SetTime(new TimeData(m_Time, .016f));
            renderSystem.Update(); m_Em.CompleteAllTrackedJobs();
            float first = m_Em.GetComponentData<LocalToWorld>(e).Position.z;
            m_Em.SetComponentData(e, new GemData { Tier = 3, ExperienceValue = 40 });
            snapshot.PlayerPosition = new float2(0, 4); m_Em.SetComponentData(m_Run, snapshot);
            renderSystem.Update(); m_Em.CompleteAllTrackedJobs();
            Assert.That(m_Em.GetComponentData<LocalToWorld>(e).Position.z, Is.Not.EqualTo(first));
            Assert.That(m_Em.GetComponentData<GemVisualState>(e).FlashTimer, Is.GreaterThan(0));
            m_World.SetTime(new TimeData(m_Time, 1));
            renderSystem.Update(); m_Em.CompleteAllTrackedJobs();
            Assert.That(m_Em.GetComponentData<BaseColorOverride>(e).Value, Is.EqualTo(PresentationDepth.TierColor(3)));
            m_Em.DestroyEntity(e);
        }
        [Test]
        public void CustomShadersAreExplicitBuildDependencies()
        {
            var shaders = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var included = shaders.FindProperty("m_AlwaysIncludedShaders");
            bool sprite = false, floor = false;
            for (int i = 0; i < included.arraySize; i++)
            {
                var shader = included.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader == null) continue;
                sprite |= shader.name == "PureDots/SpriteDOTS"; floor |= shader.name == "PureDots/InfiniteFloor";
            }
            Assert.That(sprite && floor);
        }
        [Test]
        public void GeneratedRenderAssetsAreReleasedWhenTheirWorldIsDisposed()
        {
            var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
            World world = null;
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
                world = new World("Render ownership regression");
                var em = world.EntityManager;
                var initialization = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
                initialization.AddSystemToUpdateList(world.GetOrCreateSystem<SimulationBootstrapSystem>());
                initialization.AddSystemToUpdateList(world.GetOrCreateSystemManaged<PureDotsRenderBootstrapSystem>());
                initialization.AddSystemToUpdateList(world.GetOrCreateSystemManaged<GameConfigurationBootstrapSystem>());
                initialization.SortSystems();
                initialization.Update();
                var prefabs = em.CreateEntityQuery(typeof(PureDotsPrefabsSingleton)).GetSingleton<PureDotsPrefabsSingleton>();
                var render = em.GetSharedComponent<RenderMeshArray>(prefabs.PlayerPrefab);
                var indices = em.GetComponentData<MaterialMeshInfo>(prefabs.PlayerPrefab);
                Material material = render.GetMaterial(indices);
                Mesh mesh = render.GetMesh(indices);
                Texture authoredTexture = material.mainTexture;
                var projectileRender = em.GetSharedComponent<RenderMeshArray>(prefabs.PlayerProjPrefab);
                var projectileIndices = em.GetComponentData<MaterialMeshInfo>(prefabs.PlayerProjPrefab);
                Texture generatedTexture = projectileRender.GetMaterial(projectileIndices).mainTexture;
                Assert.That(material != null && mesh != null && authoredTexture != null && generatedTexture != null);
                world.Dispose(); world = null;
                Assert.That(material == null && mesh == null && generatedTexture == null);
                Assert.That(authoredTexture != null, "Textures referenced by authoring assets must not be destroyed by a simulation world.");
            }
            finally { world?.Dispose(); RestoreScenes(setup); }
        }
        [Test]
        public void SweptProjectileChoosesFirstImpactRatherThanHashTraversalOrder()
        {
            Entity first = Enemy(new float2(0, 10)), second = Enemy(new float2(.8f, 10));
            Projectile(new float2(-2, 10), new float2(16, 0), true);
            Tick(.25f);
            Assert.That(m_Em.IsComponentEnabled<EnemyActiveTag>(first), Is.False);
            Assert.That(m_Em.GetComponentData<CurrentHealth>(second).Value, Is.EqualTo(25));
        }
        [TestCase(4f, 1f, 5f, 5f, false)] [TestCase(4f, 1f, 5f, 5f, true)]
        [TestCase(2f, 2f, 5f, 5f, false)] [TestCase(2f, 2f, 5f, 5f, true)]
        [TestCase(2f, 1f, 2.5f, 5f, false)] [TestCase(2f, 1f, 2.5f, 5f, true)]
        [TestCase(2f, 1f, .25f, .5f, false)] [TestCase(2f, 1f, .25f, .5f, true)]
        public void EnemyPushDependsOnSpeedAndMassNotAttackRangeOrTargetDistance(float speed, float mass, float range, float weakRange, bool dense)
        {
            var original = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>();
            var catalogEntity = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingletonEntity();
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<EnemyConfigCatalog>();
            var configs = builder.Allocate(ref root.Configs, 2);
            configs[0] = new EnemyConfigData { MoveSpeed = speed, Mass = mass, AttackRange = range, CollisionRadius = .35f };
            configs[1] = new EnemyConfigData { MoveSpeed = 2, Mass = 1, AttackRange = weakRange, CollisionRadius = .35f };
            var catalog = builder.CreateBlobAssetReference<EnemyConfigCatalog>(Allocator.Persistent); builder.Dispose();
            Entity strong = Enemy(new float2(5, 5), 0), weak = Enemy(new float2(5.4f, 5), 1);
            try
            {
                m_Em.SetComponentData(catalogEntity, new EnemyConfigCatalogSingleton { Catalog = catalog });
                var spatial = m_Em.CreateEntityQuery(typeof(EnemySpatialGridSingleton)).GetSingleton<EnemySpatialGridSingleton>();
                var grid = spatial.Grid;
                grid.Clear(); spatial.CrowdCells.Clear();
                for (int i = 0; i < 2; i++)
                {
                    Entity e = i == 0 ? strong : weak;
                    float2 position = m_Em.GetComponentData<LocalTransform>(e).Position.xy;
                    int2 cell = SpatialHashUtils.QuantizeToCell(position);
                    grid.Add(SpatialHashUtils.ComputeHash(cell), new GridEntry { Entity = e, Position = position, CellCoord = cell,
                        Radius = .35f, PushPriority = CrowdContact.PushPriority(catalog.Value.Configs[i]) });
                    spatial.CrowdCells.TryGetValue(cell, out var crowd); crowd.Count++; spatial.CrowdCells[cell] = crowd;
                }
                if (dense)
                {
                    // A uniform dense field exercises pressure without imposing a direction on the pair.
                    float averagePriority = (CrowdContact.PushPriority(catalog.Value.Configs[0]) + CrowdContact.PushPriority(catalog.Value.Configs[1])) * .5f;
                    for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        spatial.CrowdCells[new int2(x, y)] = new CrowdCell { Count = CrowdConstants.MaxCrowdEntries + 1, Density = 100,
                            PrioritySum = 100 * averagePriority };
                }
                float2 farStrongStep = float2.zero, farWeakStep = float2.zero;
                float blockedWeakDensity = 0;
                foreach (float2 playerPosition in new[] { float2.zero, new float2(3.5f, 5), new float2(10, 5) })
                {
                    m_Em.SetComponentData(strong, LocalTransform.FromPosition(new float3(5, 5, 0)));
                    m_Em.SetComponentData(weak, LocalTransform.FromPosition(new float3(5.4f, 5, 0)));
                    m_Em.SetComponentData(strong, default(SeparationCache));
                    m_Em.SetComponentData(weak, default(SeparationCache));
                    var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                    run.MaxEnemyRadius = .35f; run.PlayerPosition = run.PreviousPlayerPosition = playerPosition;
                    m_Em.SetComponentData(m_Run, run);
                    m_World.SetTime(new TimeData(m_Time, .1f));
                    m_World.GetOrCreateSystem<ContactTestSystem>().Update(m_World.Unmanaged);
                    m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
                    float2 strongStep = m_Em.GetComponentData<LocalTransform>(strong).Position.xy - new float2(5, 5);
                    float2 weakStep = m_Em.GetComponentData<LocalTransform>(weak).Position.xy - new float2(5.4f, 5);
                    if (speed > 2 || mass > 1)
                        Assert.That(math.length(weakStep), Is.GreaterThan(math.length(strongStep) * (dense ? 1.3f : 1.9f)));
                    else Assert.That(math.length(weakStep), Is.EqualTo(math.length(strongStep)).Within(1e-5f), "Attack range must not change pushing strength.");
                    if (math.all(playerPosition == float2.zero))
                    {
                        farStrongStep = strongStep; farWeakStep = weakStep;
                        blockedWeakDensity = m_Em.GetComponentData<SeparationCache>(weak).Density;
                    }
                    else
                    {
                        Assert.That(math.distance(strongStep, farStrongStep), Is.LessThan(1e-5f));
                        Assert.That(math.distance(weakStep, farWeakStep), Is.LessThan(1e-5f));
                    }
                    if (playerPosition.x == 10 && (speed > 2 || mass > 1))
                        Assert.That(m_Em.GetComponentData<SeparationCache>(strong).Density, Is.LessThan(blockedWeakDensity * .5f),
                            "A stronger enemy must lose less approach speed to a weaker blocker than the reverse.");
                }
            }
            finally { m_Em.SetComponentData(catalogEntity, original); catalog.Dispose(); }
        }
        [Test]
        public void HashCollisionsDoNotConsumeCrowdSamplingBudget()
        {
            int2 cell = new int2(1, 1), unrelatedCell = new int2(-1, -1);
            Assert.That(SpatialHashUtils.ComputeHash(unrelatedCell), Is.EqualTo(SpatialHashUtils.ComputeHash(cell)));
            float2 start = new float2(1.9f, 1.6f), peerStart = new float2(1.6f, 1.6f);
            Entity enemy = Enemy(start), peer = Enemy(peerStart);
            m_World.GetOrCreateSystem<GridTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            m_World.GetOrCreateSystem<ContactTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            float2 expectedStep = m_Em.GetComponentData<LocalTransform>(enemy).Position.xy - start;
            float expectedDensity = m_Em.GetComponentData<SeparationCache>(enemy).Density;
            Assert.That(math.length(expectedStep), Is.GreaterThan(0));
            Assert.That(expectedDensity, Is.GreaterThan(0));
            m_Em.SetComponentData(enemy, LocalTransform.FromPosition(new float3(start, 0)));
            m_Em.SetComponentData(peer, LocalTransform.FromPosition(new float3(peerStart, 0)));
            m_Em.SetComponentData(enemy, default(SeparationCache));
            m_Em.SetComponentData(peer, default(SeparationCache));
            var spatial = m_Em.CreateEntityQuery(typeof(EnemySpatialGridSingleton)).GetSingleton<EnemySpatialGridSingleton>();
            for (int i = 0; i < CrowdConstants.MaxCrowdEntries; i++)
                spatial.Grid.Add(SpatialHashUtils.ComputeHash(unrelatedCell), new GridEntry
                    { CellCoord = unrelatedCell, Position = new float2(-.5f, -.5f) });
            m_World.GetOrCreateSystem<ContactTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            Assert.That(math.distance(m_Em.GetComponentData<LocalTransform>(enemy).Position.xy - start, expectedStep), Is.LessThan(1e-6f));
            Assert.That(m_Em.GetComponentData<SeparationCache>(enemy).Density, Is.EqualTo(expectedDensity).Within(1e-6f));
        }
        [Test]
        public void CrowdedEnemiesSurroundPlayerWithoutMovingThem()
        {
            Command(SimulationCommandKind.GodMode, 1);
            var enemies = new Entity[40];
            for (int i = 0; i < enemies.Length; i++) enemies[i] = Enemy(new float2(1.5f + i * .08f, (i & 1) == 0 ? .02f : -.02f));
            for (int i = 0; i < 240; i++) Tick();
            int above = 0, below = 0, behind = 0;
            foreach (var e in enemies)
            {
                float2 position = m_Em.GetComponentData<LocalTransform>(e).Position.xy;
                Assert.That(math.length(position), Is.GreaterThanOrEqualTo(.749f));
                if (position.y > .5f) above++; if (position.y < -.5f) below++; if (position.x < -.1f) behind++;
            }
            Assert.That(Snapshot.PlayerPosition, Is.EqualTo(float2.zero));
            Assert.That(above, Is.GreaterThan(3)); Assert.That(below, Is.GreaterThan(3)); Assert.That(behind, Is.GreaterThan(3));
        }
        [TestCase(1.1f, false)] [TestCase(1.5f, false)] [TestCase(1.1f, true)] [TestCase(1.5f, true)]
        public void DenseHeavyCrowdCannotPushOrSlowAnEnemyAcrossEmptySpace(float gap, bool withPeer)
        {
            m_Em.SetComponentData(m_Player, LocalTransform.FromPosition(new float3(0, 5, 0)));
            for (int i = 0; i < 64; i++) Enemy(new float2(3.5f, 5), TankType);
            float2 start = new float2(3.5f + gap, 5);
            Entity runner = Enemy(start);
            if (withPeer) Enemy(start + new float2(0, .4f));
            m_World.GetOrCreateSystem<GridTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.PlayerPosition = run.PreviousPlayerPosition = new float2(0, 5); m_Em.SetComponentData(m_Run, run);
            var cells = m_Em.CreateEntityQuery(typeof(EnemySpatialGridSingleton)).GetSingleton<EnemySpatialGridSingleton>().CrowdCells;
            Assert.That(CrowdContact.SampleDensity(cells, start, out _, out _), Is.GreaterThan(CrowdConstants.CrowdTargetDensity));
            m_World.GetOrCreateSystem<ContactTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            float2 position = m_Em.GetComponentData<LocalTransform>(runner).Position.xy;
            if (withPeer) Assert.That(math.abs(position.x - start.x), Is.LessThan(1e-5f));
            else Assert.That(math.distance(position, start), Is.LessThan(1e-5f));
            Assert.That(m_Em.GetComponentData<SeparationCache>(runner).Density, Is.Zero);
            Tick();
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            float requestedStep = catalog.Value.Configs[(int)RunnerType].MoveSpeed / 60;
            Assert.That(start.x - m_Em.GetComponentData<LocalTransform>(runner).Position.x, Is.GreaterThan(requestedStep * .7f),
                "An empty gap must allow approach instead of stalling in the density field.");
        }
        [Test]
        public void FastRunnerManeuversAroundSlowerTankOnApproach()
        {
            Command(SimulationCommandKind.GodMode, 1);
            Entity tank = Enemy(new float2(5, 0), TankType);
            Entity runner = Enemy(new float2(6.2f, 0), RunnerType);
            bool passedBesideTank = false;
            for (int i = 0; i < 180; i++)
            {
                Tick();
                float2 tankPosition = m_Em.GetComponentData<LocalTransform>(tank).Position.xy;
                float2 runnerPosition = m_Em.GetComponentData<LocalTransform>(runner).Position.xy;
                passedBesideTank |= runnerPosition.x < tankPosition.x - .1f && math.abs(runnerPosition.y - tankPosition.y) > .35f;
            }
            Assert.That(passedBesideTank, "The runner should use lateral movement to overtake the tank.");
            Assert.That(Snapshot.PlayerPosition, Is.EqualTo(float2.zero));
        }
        [Test]
        public void MovingPlayerGentlyDisplacesEnemyAndKeepsInputMotion()
        {
            Entity e = Enemy(new float2(.8f, 0));
            m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
            Tick();
            Assert.That(Snapshot.PlayerPosition.x, Is.GreaterThan(0).And.LessThan(.1f));
            Assert.That(Snapshot.PlayerPosition.y, Is.Zero);
            float displacement = m_Em.GetComponentData<LocalTransform>(e).Position.x - .8f;
            Assert.That(displacement, Is.GreaterThan(0).And.LessThan(.1f));
        }
        [Test]
        public void PlayerPushesHeavyEnemiesLessAndRemainsInControl()
        {
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            float lightDisplacement = 0;
            foreach (uint type in new[] { RunnerType, TankType })
            {
                Reset(); Command(SimulationCommandKind.GodMode, 1);
                var config = catalog.Value.Configs[(int)type];
                float clearance = DefaultPlayer.Stats.CollisionRadius + config.CollisionRadius + CrowdConstants.PlayerContactSkin;
                Entity e = Enemy(new float2(clearance, 0), type);
                m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
                for (int i = 0; i < 60; i++) Tick();
                float displacement = m_Em.GetComponentData<LocalTransform>(e).Position.x - clearance;
                float pushLimit = math.max(CrowdConstants.PlayerCrowdPushSpeed / math.max(1, config.Mass),
                    DefaultPlayer.Stats.MoveSpeed * CrowdConstants.PlayerCrowdMinimumSpeed);
                Assert.That(displacement, Is.GreaterThan(0).And.LessThanOrEqualTo(pushLimit + 1e-4f));
                Assert.That(Snapshot.PlayerPosition.x, Is.EqualTo(displacement).Within(1e-4f));
                Assert.That(Snapshot.PlayerPosition.y, Is.Zero);
                if (type == RunnerType) lightDisplacement = displacement;
                else Assert.That(displacement, Is.LessThan(lightDisplacement * .3f));
            }
        }
        [Test]
        public void MeleeEnemyStillDealsDamageAtItsSurroundingDistance()
        {
            Enemy(new float2(.8f, 0)); Tick();
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth - 10));
        }
        [Test]
        public void CrowdResistanceScalesWithDensityAndAlwaysAllowsForwardProgress()
        {
            float previousStep = float.MaxValue;
            foreach (int count in new[] { 0, 1, 64, 512, 3000 })
            {
                Reset();
                for (int i = 0; i < count; i++) Enemy(new float2(.8f, 0));
                m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
                Tick();
                float step = Snapshot.PlayerPosition.x;
                Assert.That(step, Is.LessThanOrEqualTo(previousStep));
                if (count <= 64) Assert.That(step, Is.LessThan(previousStep));
                Assert.That(step, Is.GreaterThanOrEqualTo(.1f * CrowdConstants.PlayerCrowdMinimumSpeed - 1e-6f));
                Assert.That(Snapshot.PlayerPosition.y, Is.Zero);
                previousStep = step;
                m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(-1, 0) });
                Tick();
                Assert.That(step - Snapshot.PlayerPosition.x, Is.EqualTo(.1f).Within(1e-6f), "Bodies behind the player must not resist escape.");
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void CrowdsCannotDisplaceIdleOrDeadPlayer(bool dead)
        {
            for (int i = 0; i < 256; i++) Enemy(new float2(.8f, 0));
            if (dead)
            {
                var stats = m_Em.GetComponentData<PlayerStats>(m_Player);
                stats.IsDead = 1; stats.CurrentHealth = 0; m_Em.SetComponentData(m_Player, stats);
                m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
            }
            else Command(SimulationCommandKind.GodMode, 1);
            for (int i = 0; i < 60; i++) Tick();
            Assert.That(Snapshot.PlayerPosition, Is.EqualTo(float2.zero));
            Assert.That(m_Em.GetComponentData<MovementVelocity>(m_Player).Value, Is.EqualTo(float2.zero));
        }
        [Test]
        public void LongPlayerSweepSlowsAtContactAndOnlyPushesSlightly()
        {
            Entity e = Enemy(new float2(1.5f, 0));
            m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
            Tick(.5f);
            Assert.That(Snapshot.PlayerPosition.x, Is.GreaterThan(0).And.LessThanOrEqualTo(1.74f + 1e-4f));
            float2 enemyPosition = m_Em.GetComponentData<LocalTransform>(e).Position.xy;
            Assert.That(enemyPosition.x - 1.5f, Is.GreaterThan(0).And.LessThanOrEqualTo(1f + 1e-4f));
            Assert.That(enemyPosition.x - Snapshot.PlayerPosition.x, Is.GreaterThanOrEqualTo(.759f));
            Assert.That(Snapshot.PlayerPosition.y, Is.Zero);
        }
        [TestCase(0f, false)] [TestCase(.6f, false)] [TestCase(.6f, true)]
        public void ThousandsOfMeleeEnemiesSettleIntoBroadLayeredCrowd(float gridOffset, bool mixed)
        {
            Command(SimulationCommandKind.GodMode, 1);
            float2 playerPosition = new float2(gridOffset, gridOffset);
            m_Em.SetComponentData(m_Player, LocalTransform.FromPosition(new float3(playerPosition, 0)));
            var enemies = new Entity[3000];
            var random = new Unity.Mathematics.Random(12345);
            for (int i = 0; i < enemies.Length; i++)
            {
                float angle = random.NextFloat(0, 2 * math.PI);
                enemies[i] = Enemy(playerPosition + new float2(math.cos(angle), math.sin(angle)) * random.NextFloat(.8f, 2),
                    mixed && (i & 1) == 0 ? TankType : RunnerType);
            }
            for (int i = 0; i < 600; i++) Tick();
            float maxStep = 0, totalRadius = 0, totalStep = 0;
            var positions = new float2[enemies.Length];
            var radii = new float[enemies.Length];
            for (int frame = 0; frame < 60; frame++)
            {
                Tick();
                foreach (var e in enemies)
                {
                    float2 position = m_Em.GetComponentData<LocalTransform>(e).Position.xy;
                    float step = math.distance(position, m_Em.GetComponentData<PreviousPosition>(e).Value);
                    maxStep = math.max(maxStep, step); totalStep += step;
                }
            }
            for (int i = 0; i < enemies.Length; i++)
            {
                float2 position = m_Em.GetComponentData<LocalTransform>(enemies[i]).Position.xy;
                Assert.That(math.all(math.isfinite(position)));
                Assert.That(math.distance(position, playerPosition), Is.GreaterThanOrEqualTo(.759f));
                totalRadius += math.distance(position, playerPosition);
                radii[i] = math.distance(position, playerPosition);
                positions[i] = position;
            }
            Assert.That(Snapshot.PlayerPosition, Is.EqualTo(playerPosition));
            Array.Sort(radii);
            TestContext.WriteLine($"3000 enemies: mean radius {totalRadius / enemies.Length:F3}, row depth {radii[2700] - radii[300]:F3}, mean step {totalStep / (enemies.Length * 60):F5}, max step {maxStep:F5}");
            Assert.That(totalRadius / enemies.Length, Is.GreaterThan(4).And.LessThan(18));
            Assert.That(radii[2700] - radii[300], Is.GreaterThan(2), "The horde must occupy multiple rows, not a thin ring.");
            Assert.That(radii[0], Is.LessThan(1.1f), "The front row must still reach melee contact.");
            Assert.That(maxStep, Is.LessThan(.06f), "Settled crowd must not jump between contacts.");
            Assert.That(totalStep / (enemies.Length * 60), Is.LessThan(.01f), "Settled crowd must not keep orbiting or jittering.");
            bool overlap = false;
            for (int i = 0; i < 64; i++)
                for (int j = i + 1; j < 64; j++) overlap |= math.distancesq(positions[i], positions[j]) < .7f * .7f;
            Assert.That(overlap, "Soft enemy bodies must permit overlap.");
        }
        [Test]
        public void CrowdFootprintGrowsWithEnemyCount()
        {
            float previousRadius = 0;
            foreach (int count in new[] { 64, 512, 3000 })
            {
                Reset(); Command(SimulationCommandKind.GodMode, 1);
                var enemies = new Entity[count];
                var random = new Unity.Mathematics.Random(12345);
                for (int i = 0; i < count; i++)
                {
                    float angle = random.NextFloat(0, 2 * math.PI);
                    enemies[i] = Enemy(new float2(math.cos(angle), math.sin(angle)) * random.NextFloat(.8f, 2));
                }
                for (int i = 0; i < 600; i++) Tick();
                var radii = new float[count];
                for (int i = 0; i < count; i++) radii[i] = math.length(m_Em.GetComponentData<LocalTransform>(enemies[i]).Position.xy);
                Array.Sort(radii);
                float radius = radii[count * 9 / 10];
                TestContext.WriteLine($"{count} enemies: 90th-percentile radius {radius:F3}");
                Assert.That(radius, Is.GreaterThan(previousRadius * 1.35f));
                if (count == 3000) Assert.That(radius, Is.GreaterThan(8), "Large hordes must retain a broad footprint with readable silhouettes.");
                previousRadius = radius;
            }
        }
        [Test]
        public void DensityAndPressureRemainContinuousAcrossGridBoundaries()
        {
            using var cells = new Unity.Collections.LowLevel.Unsafe.UnsafeParallelHashMap<int2, CrowdCell>(64, Allocator.Temp);
            for (int y = -3; y <= 3; y++)
                for (int x = -3; x <= 3; x++) cells.Add(new int2(x, y), new CrowdCell { Density = x + 4 });
            float2 boundary = new float2(SpatialHashUtils.CellSize * .5f, 0);
            float left = CrowdContact.SampleDensity(cells, boundary - new float2(1e-5f, 0), out float2 leftGradient, out _);
            float right = CrowdContact.SampleDensity(cells, boundary + new float2(1e-5f, 0), out float2 rightGradient, out _);
            Assert.That(right, Is.GreaterThan(left));
            Assert.That(right - left, Is.LessThan(1e-4f));
            Assert.That(leftGradient.x, Is.GreaterThan(0));
            Assert.That(math.distance(leftGradient, rightGradient), Is.LessThan(1e-4f));
            Assert.That(math.abs(leftGradient.y), Is.LessThan(1e-6f));
        }
        [Test]
        public void HudBatchesStatsReusesNumericStorageAndStagesButtonCommands()
        {
            var go = new GameObject("HUD regression");
            var commands = new UnsafeQueue<SimulationCommand>(Allocator.Temp);
            try
            {
                var hud = go.AddComponent<PureDotsHUD>();
                if (PureDotsHUD.Instance != hud)
                    typeof(PureDotsHUD).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                var snapshot = Snapshot; snapshot.ActiveEnemies = 50000;
                hud.ApplySnapshot(snapshot, commands);
                Canvas.ForceUpdateCanvases();
                var stats = go.transform.Find("HUD canvas/Stats panel/Stats").GetComponent<CanvasRenderer>();
                Assert.That(stats.materialCount, Is.EqualTo(1));
                Assert.That(stats.GetMesh().vertexCount, Is.GreaterThan(400));
                var button = go.transform.Find("HUD canvas/Controls panel/Spawn +100").GetComponent<UnityEngine.UI.Button>();
                button.onClick.Invoke();
                Assert.That(commands.Count, Is.Zero);
                hud.ApplySnapshot(snapshot, commands);
                Assert.That(commands.TryDequeue(out var command));
                Assert.That(command.Kind, Is.EqualTo(SimulationCommandKind.SpawnExtra)); Assert.That(command.Value, Is.EqualTo(100));
                for (int i = 0; i < 4; i++) { snapshot.ActiveEnemies++; hud.ApplySnapshot(snapshot, commands); Canvas.ForceUpdateCanvases(); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20; i++) { snapshot.ActiveEnemies++; hud.ApplySnapshot(snapshot, commands); Canvas.ForceUpdateCanvases(); }
                Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            }
            finally { Object.DestroyImmediate(go); commands.Dispose(); }
        }
    }

    [DisableAutoCreation]
    public partial struct GridTestSystem : ISystem
    {
        private SimulationAccess m_Access;
        public bool GridOnly;
        public void OnCreate(ref SystemState state) => m_Access.Initialize(ref state);
        public void OnUpdate(ref SystemState state)
        {
            m_Access.Update(ref state);
            m_Access.State = SystemAPI.GetSingletonEntity<SimulationRunState>();
            m_Access.Catalog = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            m_Access.EnemyPool = SystemAPI.GetSingleton<EnemyPoolSingleton>();
            m_Access.PlayerPool = SystemAPI.GetSingleton<PlayerProjectilePoolSingleton>();
            m_Access.EnemyProjectilePool = SystemAPI.GetSingleton<EnemyProjectilePoolSingleton>();
            m_Access.GemPool = SystemAPI.GetSingleton<GemPoolSingleton>();
            var spatial = SystemAPI.GetSingleton<EnemySpatialGridSingleton>();
            m_Access.Grid = spatial.Grid; m_Access.CrowdCells = spatial.CrowdCells;
            state.Dependency = new RebuildSpatialGridJob { A = m_Access, BuildCrowdCells = !GridOnly }.Schedule(state.Dependency);
            SystemAPI.SetSingleton(new SimulationJobFence { Handle = state.Dependency });
        }
    }

    [DisableAutoCreation]
    public partial struct ContactTestSystem : ISystem
    {
        private CrowdContactJob m_Job;
        public void OnCreate(ref SystemState state)
        {
            m_Job.Active = state.GetComponentLookup<EnemyActiveTag>(true);
            m_Job.Types = state.GetComponentLookup<TypeId>(true);
            m_Job.Previous = state.GetComponentLookup<PreviousPosition>(true);
            m_Job.Run = state.GetComponentLookup<SimulationRunState>(true);
            m_Job.Transforms = state.GetComponentLookup<LocalTransform>();
            m_Job.Velocities = state.GetComponentLookup<MovementVelocity>();
            m_Job.Cache = state.GetComponentLookup<SeparationCache>();
        }
        public void OnUpdate(ref SystemState state)
        {
            Entity run = SystemAPI.GetSingletonEntity<SimulationRunState>();
            var pool = SystemAPI.GetSingleton<EnemyPoolSingleton>();
            m_Job.Active.Update(ref state); m_Job.Types.Update(ref state); m_Job.Previous.Update(ref state);
            m_Job.Run.Update(ref state); m_Job.Transforms.Update(ref state); m_Job.Velocities.Update(ref state); m_Job.Cache.Update(ref state);
            m_Job.Entities = pool.AllEnemies; m_Job.Grid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;
            m_Job.Cells = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().CrowdCells;
            m_Job.Catalog = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            m_Job.State = run; m_Job.Dt = SystemAPI.Time.DeltaTime;
            state.Dependency = m_Job.Schedule(pool.AllEnemies.Length, 128, state.Dependency);
            SystemAPI.SetSingleton(new SimulationJobFence { Handle = state.Dependency });
        }
    }

    [DisableAutoCreation]
    public partial struct DelayedRunProducerSystem : ISystem
    {
        private ComponentLookup<SimulationRunState> m_Run;
        private Entity m_State;
        public void OnCreate(ref SystemState state)
        {
            m_Run = state.GetComponentLookup<SimulationRunState>();
            m_State = state.GetEntityQuery(ComponentType.ReadWrite<SimulationRunState>()).GetSingletonEntity();
        }
        public void OnUpdate(ref SystemState state)
        {
            m_Run.Update(ref state);
            state.Dependency = new DelayRunJob { Run = m_Run, State = m_State }.Schedule(state.Dependency);
        }
        public struct DelayRunJob : IJob
        {
            public ComponentLookup<SimulationRunState> Run;
            public Entity State;
            public void Execute() { Thread.Sleep(200); Run[State] = Run[State]; }
        }
    }
}
