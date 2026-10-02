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
    public class SimulationRegressionTests
    {
        private World m_World;
        private EntityManager m_Em;
        private SystemHandle m_Pipeline;
        private Entity m_Run, m_Player, m_Wave, m_Input;
        private double m_Time;
        private UnsafeQueue<SimulationCommand> m_Commands;

        [OneTimeSetUp]
        public void CreateWorld()
        {
            m_World = new World("DOTS regression tests");
            m_Em = m_World.EntityManager;
            Entity player = Prefab(typeof(PlayerTag), typeof(LocalTransform), typeof(PreviousPosition), typeof(MovementVelocity), typeof(PlayerStats), typeof(PlayerInvulnerability));
            m_Em.SetComponentData(player, RunDefaults.Player);
            Entity enemy = Prefab(typeof(LocalTransform), typeof(PreviousPosition), typeof(MovementVelocity), typeof(SeparationCache),
                typeof(CurrentHealth), typeof(TypeId), typeof(EnemyActiveTag), typeof(EnemyRangedTag), typeof(EnemyMeleeCooldown), typeof(EnemyRangedCooldown));
            Entity playerProjectile = Prefab(typeof(LocalTransform), typeof(PreviousPosition), typeof(MovementVelocity), typeof(ProjectileData), typeof(ProjectileActiveTag), typeof(PlayerProjectileTag));
            Entity enemyProjectile = Prefab(typeof(LocalTransform), typeof(PreviousPosition), typeof(MovementVelocity), typeof(ProjectileData), typeof(ProjectileActiveTag), typeof(EnemyProjectileTag));
            Entity gem = Prefab(typeof(LocalTransform), typeof(GemData), typeof(GemActiveTag));
            m_Em.AddComponentData(m_Em.CreateEntity(), new PureDotsPrefabsSingleton
            { PlayerPrefab = player, EnemyPrefab = enemy, PlayerProjPrefab = playerProjectile, EnemyProjPrefab = enemyProjectile, GemPrefab = gem });
            m_World.GetOrCreateSystem<SimulationBootstrapSystem>().Update(m_World.Unmanaged);
            m_Run = m_Em.CreateEntityQuery(typeof(SimulationRunState)).GetSingletonEntity();
            m_Player = m_Em.GetComponentData<SimulationRunState>(m_Run).Player;
            m_Wave = m_Em.CreateEntityQuery(typeof(WaveSpawnerConfig)).GetSingletonEntity();
            m_Input = m_Em.CreateEntityQuery(typeof(SimulationInput)).GetSingletonEntity();
            m_Commands = m_Em.CreateEntityQuery(typeof(SimulationCommandQueue)).GetSingleton<SimulationCommandQueue>().Commands;
            m_Pipeline = m_World.GetOrCreateSystem<SimulationPipelineSystem>();
        }
        private Entity Prefab(params ComponentType[] types)
        {
            Entity e = m_Em.CreateEntity(types);
            m_Em.AddComponent<Prefab>(e);
            m_Em.SetComponentData(e, LocalTransform.Identity);
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
            m_Pipeline.Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
        }
        private SimulationSnapshot Snapshot => m_Em.GetComponentData<SimulationSnapshot>(m_Run);
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
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(SimulationConstants.PlayerDefaultMaxHealth));
        }
        [TestCase(0u, 25u)] [TestCase(1u, 10u)] [TestCase(2u, 15u)] [TestCase(3u, 20u)]
        public void DeathRewardsComeFromCatalog(uint type, uint expected)
        {
            Entity e = Enemy(new float2(10, 0), type);
            var damage = m_Em.CreateEntityQuery(typeof(DamageEventQueueSingleton)).GetSingleton<DamageEventQueueSingleton>().DamageQueue;
            damage.Enqueue(new DamageEvent { TargetEntity = e, TargetKey = DamageEvent.CreateTargetKey(e), Damage = 1000 });
            Tick();
            var gems = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>().AllGems;
            uint value = 0;
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
            Assert.That(Snapshot.Player.MaxHealth, Is.EqualTo(SimulationConstants.PlayerDefaultMaxHealth));
            Assert.That(Snapshot.ActiveEnemies + Snapshot.PlayerProjectiles + Snapshot.EnemyProjectiles + Snapshot.ActiveGems, Is.Zero);
            Assert.That(Snapshot.Kills, Is.Zero); Assert.That(Snapshot.TotalExperience, Is.Zero);
            Assert.That(m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>().InactiveEnemies.Count, Is.EqualTo(SimulationConstants.MaxEnemies));
            Assert.That(m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>().FreeGems.Count, Is.EqualTo(1024));
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
            Assert.That(Snapshot.Kills, Is.EqualTo(SimulationConstants.MaxEnemies)); Assert.That(Snapshot.ActiveEnemies, Is.Zero);
            var bridge = m_Em.CreateEntityQuery(typeof(SimulationBridgeQueuesSingleton)).GetSingleton<SimulationBridgeQueuesSingleton>();
            Assert.That(bridge.DeathEventQueue.Count, Is.LessThanOrEqualTo(RunDefaults.CosmeticQueueCapacity));
            Assert.That(bridge.GemCollectEventQueue.Count, Is.LessThanOrEqualTo(RunDefaults.CosmeticQueueCapacity));
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
        [Test]
        public void SchedulingAllocatesNoManagedMemoryAfterWarmup()
        {
            for (int i = 0; i < 4; i++) Tick();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) Tick();
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
            var world = new World("Render ownership regression");
            var em = world.EntityManager;
            world.GetOrCreateSystemManaged<PureDotsRenderBootstrapSystem>().Update();
            var prefabs = em.CreateEntityQuery(typeof(PureDotsPrefabsSingleton)).GetSingleton<PureDotsPrefabsSingleton>();
            var render = em.GetSharedComponent<RenderMeshArray>(prefabs.PlayerPrefab);
            var indices = em.GetComponentData<MaterialMeshInfo>(prefabs.PlayerPrefab);
            Material material = render.GetMaterial(indices);
            Mesh mesh = render.GetMesh(indices);
            Texture texture = material.mainTexture;
            Assert.That(material != null && mesh != null && texture != null);
            world.Dispose();
            Assert.That(material == null && mesh == null && texture == null);
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
                        spatial.CrowdCells[new int2(x, y)] = new CrowdCell { Count = SimulationConstants.MaxCrowdEntries + 1, Density = 100,
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
            for (int i = 0; i < SimulationConstants.MaxCrowdEntries; i++)
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
            for (int i = 0; i < 64; i++) Enemy(new float2(3.5f, 5), SimulationConstants.EnemyTankTypeId);
            float2 start = new float2(3.5f + gap, 5);
            Entity runner = Enemy(start);
            if (withPeer) Enemy(start + new float2(0, .4f));
            m_World.GetOrCreateSystem<GridTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.PlayerPosition = run.PreviousPlayerPosition = new float2(0, 5); m_Em.SetComponentData(m_Run, run);
            var cells = m_Em.CreateEntityQuery(typeof(EnemySpatialGridSingleton)).GetSingleton<EnemySpatialGridSingleton>().CrowdCells;
            Assert.That(CrowdContact.SampleDensity(cells, start, out _, out _), Is.GreaterThan(SimulationConstants.CrowdTargetDensity));
            m_World.GetOrCreateSystem<ContactTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            float2 position = m_Em.GetComponentData<LocalTransform>(runner).Position.xy;
            if (withPeer) Assert.That(math.abs(position.x - start.x), Is.LessThan(1e-5f));
            else Assert.That(math.distance(position, start), Is.LessThan(1e-5f));
            Assert.That(m_Em.GetComponentData<SeparationCache>(runner).Density, Is.Zero);
            Tick();
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            float requestedStep = catalog.Value.Configs[(int)SimulationConstants.EnemyRunnerTypeId].MoveSpeed / 60;
            Assert.That(start.x - m_Em.GetComponentData<LocalTransform>(runner).Position.x, Is.GreaterThan(requestedStep * .7f),
                "An empty gap must allow approach instead of stalling in the density field.");
        }
        [Test]
        public void FastRunnerManeuversAroundSlowerTankOnApproach()
        {
            Command(SimulationCommandKind.GodMode, 1);
            Entity tank = Enemy(new float2(5, 0), SimulationConstants.EnemyTankTypeId);
            Entity runner = Enemy(new float2(6.2f, 0), SimulationConstants.EnemyRunnerTypeId);
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
            foreach (uint type in new[] { SimulationConstants.EnemyRunnerTypeId, SimulationConstants.EnemyTankTypeId })
            {
                Reset(); Command(SimulationCommandKind.GodMode, 1);
                var config = catalog.Value.Configs[(int)type];
                float clearance = SimulationConstants.PlayerCollisionRadius + config.CollisionRadius + SimulationConstants.PlayerContactSkin;
                Entity e = Enemy(new float2(clearance, 0), type);
                m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
                for (int i = 0; i < 60; i++) Tick();
                float displacement = m_Em.GetComponentData<LocalTransform>(e).Position.x - clearance;
                float pushLimit = math.max(SimulationConstants.PlayerCrowdPushSpeed / math.max(1, config.Mass),
                    SimulationConstants.PlayerDefaultMoveSpeed * SimulationConstants.PlayerCrowdMinimumSpeed);
                Assert.That(displacement, Is.GreaterThan(0).And.LessThanOrEqualTo(pushLimit + 1e-4f));
                Assert.That(Snapshot.PlayerPosition.x, Is.EqualTo(displacement).Within(1e-4f));
                Assert.That(Snapshot.PlayerPosition.y, Is.Zero);
                if (type == SimulationConstants.EnemyRunnerTypeId) lightDisplacement = displacement;
                else Assert.That(displacement, Is.LessThan(lightDisplacement * .3f));
            }
        }
        [Test]
        public void MeleeEnemyStillDealsDamageAtItsSurroundingDistance()
        {
            Enemy(new float2(.8f, 0)); Tick();
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(SimulationConstants.PlayerDefaultMaxHealth - 10));
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
                Assert.That(step, Is.GreaterThanOrEqualTo(.1f * SimulationConstants.PlayerCrowdMinimumSpeed - 1e-6f));
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
                    mixed && (i & 1) == 0 ? SimulationConstants.EnemyTankTypeId : SimulationConstants.EnemyRunnerTypeId);
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
        public void OnCreate(ref SystemState state) => m_Access.Initialize(ref state);
        public void OnUpdate(ref SystemState state)
        {
            m_Access.Update(ref state);
            m_Access.State = SystemAPI.GetSingletonEntity<SimulationRunState>();
            m_Access.Catalog = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            m_Access.EnemyPool = SystemAPI.GetSingleton<EnemyPoolSingleton>();
            var spatial = SystemAPI.GetSingleton<EnemySpatialGridSingleton>();
            m_Access.Grid = spatial.Grid; m_Access.CrowdCells = spatial.CrowdCells;
            state.Dependency = new RebuildSpatialGridJob { A = m_Access }.Schedule(state.Dependency);
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
