using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace GameHolder.PureDots.Tests
{
    public partial class SimulationRegressionTests
    {
        [Test]
        public void SpatialMapsFitTheFullPoolWithoutGrowing()
        {
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            var spatial = m_Em.CreateEntityQuery(typeof(EnemySpatialGridSingleton)).GetSingleton<EnemySpatialGridSingleton>();
            int count = pool.AllEnemies.Length;
            Assert.That(spatial.Grid.Capacity, Is.EqualTo(count));
            Assert.That(spatial.CrowdCells.Capacity, Is.EqualTo(count * 5));
            for (int i = 0; i < count; i++) Enemy(new float2(-500 + (i % 100) * 5.125f, -500 + (i / 100) * 5.125f));
            Tick(0);
            Assert.That(spatial.Grid.Count(), Is.EqualTo(count));
            Assert.That(spatial.Grid.Capacity, Is.EqualTo(count));
            Assert.That(spatial.CrowdCells.Capacity, Is.EqualTo(count * 5));
        }

        [Test]
        public void GridOnlyRebuildPreservesDensityAndPublishesPostContactSweepsInPoolOrder()
        {
            for (int i = 0; i < 64; i++) Enemy(new float2(20 + i % 8 * .04f, 5 + i / 8 * .04f), (uint)(i % 2));
            var gridSystem = m_World.GetOrCreateSystem<GridTestSystem>();
            gridSystem.Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            var spatial = m_Em.CreateEntityQuery(typeof(EnemySpatialGridSingleton)).GetSingleton<EnemySpatialGridSingleton>();
            using var density = spatial.CrowdCells.GetKeyValueArrays(Allocator.Temp);
            m_World.GetOrCreateSystem<ContactTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            try
            {
                m_World.Unmanaged.GetUnsafeSystemRef<GridTestSystem>(gridSystem).GridOnly = true;
                gridSystem.Update(m_World.Unmanaged);
                m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
                Assert.That(spatial.CrowdCells.Count(), Is.EqualTo(density.Length));
                for (int i = 0; i < density.Length; i++)
                    Assert.That(spatial.CrowdCells[density.Keys[i]], Is.EqualTo(density.Values[i]));
                var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
                var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
                using var entries = spatial.Grid.GetValueArray(Allocator.Temp);
                Assert.That(entries.Length, Is.EqualTo(64));
                float step = 0, radius = 0;
                foreach (var entry in entries)
                {
                    Assert.That(entry.Entity, Is.EqualTo(pool.AllEnemies[entry.PoolIndex]));
                    Assert.That(entry.Position, Is.EqualTo(m_Em.GetComponentData<LocalTransform>(entry.Entity).Position.xy));
                    Assert.That(entry.PreviousPosition, Is.EqualTo(m_Em.GetComponentData<PreviousPosition>(entry.Entity).Value));
                    Assert.That(entry.CellCoord, Is.EqualTo(SpatialHashUtils.QuantizeToCell(entry.Position)));
                    Assert.That(entry.Radius, Is.EqualTo(catalog.Value.GetConfig(m_Em.GetComponentData<TypeId>(entry.Entity)).CollisionRadius));
                    step = math.max(step, math.distance(entry.Position, entry.PreviousPosition));
                    radius = math.max(radius, entry.Radius);
                }
                var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
                Assert.That(step, Is.GreaterThan(0), "Dense contacts must actually displace the bodies in this fixture.");
                Assert.That(run.MaxEnemyStep, Is.EqualTo(step));
                Assert.That(run.MaxEnemyRadius, Is.EqualTo(radius));
                Assert.That(ProjectileCollision.FirstHit(spatial.Grid, new float2(18, 5), new float2(22, 5), .1f, 1, run,
                    out Entity target, out float time), Is.True);
                m_World.Unmanaged.GetUnsafeSystemRef<GridTestSystem>(gridSystem).GridOnly = false;
                gridSystem.Update(m_World.Unmanaged);
                m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
                using var rebuilt = spatial.Grid.GetValueArray(Allocator.Temp);
                for (int i = 0; i < entries.Length; i++) Assert.That(rebuilt[i], Is.EqualTo(entries[i]));
                Assert.That(ProjectileCollision.FirstHit(spatial.Grid, new float2(18, 5), new float2(22, 5), .1f, 1, run,
                    out Entity rebuiltTarget, out float rebuiltTime), Is.True);
                Assert.That(rebuiltTarget, Is.EqualTo(target)); Assert.That(rebuiltTime, Is.EqualTo(time));
            }
            finally { m_World.Unmanaged.GetUnsafeSystemRef<GridTestSystem>(gridSystem).GridOnly = false; }
        }

        [TestCase(-1.25f, 1f / 60, 4)] [TestCase(-1.25f, 1f / 60, 128)] [TestCase(.625f, .5f, 128)]
        public void SpatialPlayerQueryMatchesTheFullSweepWithDenseAndInactiveEnemies(float x, float dt, int nearby)
        {
            float2 position = new float2(x, -.625f);
            m_Em.SetComponentData(m_Player, LocalTransform.FromPosition(new float3(position, 0)));
            m_Em.SetComponentData(m_Player, new PlayerInvulnerability { Timer = 1000 });
            for (int i = 0; i < nearby; i++) Enemy(position + new float2(.8f + (i % 8) * .08f, (i % 5 - 2) * .05f), (uint)(i % 2));
            for (int i = 0; i < 256; i++) Enemy(position + new float2(50 + i, 50));
            Enemy(position + new float2(-.7f, 0));
            Entity inactive = Enemy(position + new float2(.6f, 0));
            m_World.GetOrCreateSystem<GridTestSystem>().Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            m_Em.SetComponentEnabled<EnemyActiveTag>(inactive, false);
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run); run.ActiveEnemies--; m_Em.SetComponentData(m_Run, run);
            float2 step = new float2(Snapshot.Player.MoveSpeed * dt, 0);
            float expected = FullPlayerSweep(position, step, dt);
            m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
            // The published grid deliberately retains the now-inactive enemy for this tick.
            m_Time += dt; m_World.SetTime(new TimeData(m_Time, dt));
            m_Pipeline.Update(m_World.Unmanaged);
            m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            Assert.That(Snapshot.PlayerPosition.x, Is.EqualTo(position.x + step.x * expected).Within(1e-6f));
            Assert.That(Snapshot.PlayerPosition.y, Is.EqualTo(position.y));
        }

        private float FullPlayerSweep(float2 position, float2 step, float dt)
        {
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            float load = 0, pushScale = 1;
            float stepLength = math.length(step), stepSq = math.lengthsq(step);
            float2 direction = step / stepLength;
            for (int i = 0; i < pool.AllEnemies.Length; i++)
            {
                Entity e = pool.AllEnemies[i];
                if (!m_Em.IsComponentEnabled<EnemyActiveTag>(e)) continue;
                float2 offset = m_Em.GetComponentData<LocalTransform>(e).Position.xy - position;
                if (math.dot(offset, direction) < 0) continue;
                var config = catalog.Value.GetConfig(m_Em.GetComponentData<TypeId>(e));
                float clearance = DefaultPlayer.Stats.CollisionRadius + config.CollisionRadius + CrowdConstants.PlayerContactSkin;
                if (SweptCollision.TryHit(offset, offset - step, clearance, out float time))
                    pushScale = math.min(pushScale, time + CrowdConstants.PlayerCrowdPushSpeed * dt / (math.max(1, config.Mass) * stepLength));
                float radius = clearance + CrowdConstants.CrowdSteeringMargin;
                float t = math.saturate(math.dot(offset, step) / stepSq);
                float weight = math.saturate(1 - math.length(offset - step * t) / radius);
                load += weight * weight * config.Mass;
            }
            return math.max(CrowdConstants.PlayerCrowdMinimumSpeed, math.min(pushScale, 1 / (1 + load * CrowdConstants.PlayerCrowdResistance)));
        }

        [Test]
        public void ParallelProjectileHitsAndExpiryKeepPoolReuseIndependentOfWorkerCount()
        {
            int original = JobsUtility.JobWorkerCount;
            float expectedHealth = -1;
            float expectedPlayerHealth = -1;
            var expectedReuse = new Entity[2][];
            try
            {
                foreach (int workers in new[] { 1, math.min(4, JobsUtility.JobWorkerMaximumCount) })
                {
                    JobsUtility.JobWorkerCount = workers;
                    Reset();
                    var stats = m_Em.GetComponentData<PlayerStats>(m_Player);
                    stats.MaxHealth = stats.CurrentHealth = 1000000; m_Em.SetComponentData(m_Player, stats);
                    Entity target = Enemy(new float2(20, 0), TankType);
                    m_Em.SetComponentData(target, new CurrentHealth { Value = 1000000 });
                    for (int i = 0; i < 256; i++)
                    {
                        Entity hit = Projectile(new float2(19, 0), new float2(120, 0), true);
                        var data = m_Em.GetComponentData<ProjectileData>(hit);
                        data.Damage = i % 3 == 0 ? .1f : i % 3 == 1 ? 1 : 1000;
                        data.RemainingLifetime = SimulationConstants.FixedTimestep;
                        m_Em.SetComponentData(hit, data);
                        Entity enemyHit = Projectile(new float2(-1, 0), new float2(120, 0), false);
                        m_Em.SetComponentData(enemyHit, data);
                    }
                    Tick();
                    float firstHealth = m_Em.GetComponentData<CurrentHealth>(target).Value;
                    if (expectedHealth < 0) expectedHealth = firstHealth;
                    else Assert.That(firstHealth, Is.EqualTo(expectedHealth));
                    if (expectedPlayerHealth < 0) expectedPlayerHealth = Snapshot.Player.CurrentHealth;
                    else Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(expectedPlayerHealth));
                    Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(1000000 - .1f));
                    Assert.That(m_Em.CreateEntityQuery(typeof(DamageEventQueueSingleton)).GetSingleton<DamageEventQueueSingleton>().DamageQueue.Count, Is.Zero);
                    Assert.That(m_Em.CreateEntityQuery(typeof(PlayerDamageEventQueueSingleton)).GetSingleton<PlayerDamageEventQueueSingleton>().PlayerDamageQueue.Count, Is.Zero);
                    Assert.That(m_Em.CreateEntityQuery(typeof(ProjectileDeactivationQueueSingleton)).GetSingleton<ProjectileDeactivationQueueSingleton>().StagedDeactivations.Count, Is.Zero);
                    Assert.That(Snapshot.PlayerProjectiles + Snapshot.EnemyProjectiles, Is.Zero);
                    var playerPool = m_Em.CreateEntityQuery(typeof(PlayerProjectilePoolSingleton)).GetSingleton<PlayerProjectilePoolSingleton>();
                    var enemyPool = m_Em.CreateEntityQuery(typeof(EnemyProjectilePoolSingleton)).GetSingleton<EnemyProjectilePoolSingleton>();
                    Assert.That(playerPool.InactiveProjectiles.Length, Is.EqualTo(playerPool.AllProjectiles.Length));
                    Assert.That(enemyPool.InactiveProjectiles.Length, Is.EqualTo(enemyPool.AllProjectiles.Length));
                    int poolIndex = 0;
                    foreach (var free in new[] { playerPool.InactiveProjectiles, enemyPool.InactiveProjectiles })
                    {
                        bool first = expectedReuse[poolIndex] == null;
                        if (first) expectedReuse[poolIndex] = new Entity[free.Length];
                        for (int i = 0; i < expectedReuse[poolIndex].Length; i++)
                        {
                            Assert.That(free.TryDequeue(out Entity reusable), Is.True);
                            if (first) expectedReuse[poolIndex][i] = reusable;
                            else Assert.That(reusable, Is.EqualTo(expectedReuse[poolIndex][i]));
                            free.Enqueue(reusable);
                        }
                        poolIndex++;
                    }
                }
            }
            finally { JobsUtility.JobWorkerCount = original; }
        }

        [TestCase(0, 10)] [TestCase(1, 10)] [TestCase(2, 10)]
        [TestCase(0, 30)] [TestCase(1, 30)] [TestCase(2, 30)]
        public void GemFlashPausesForEveryPromptAndResumesOnPresentationTime(int reason, int fps)
        {
            Entity gem = m_Em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(GemData), typeof(GemActiveTag),
                typeof(MaterialMeshInfo), typeof(SpriteUVOffset), typeof(BaseColorOverride), typeof(GemVisualState));
            try
            {
                m_Em.SetComponentData(gem, LocalTransform.Identity);
                m_Em.SetComponentData(gem, new GemData { Tier = 3, ExperienceValue = 20 });
                m_Em.SetComponentData(gem, new GemVisualState { Generation = Snapshot.Generation, WasActive = 1,
                    Experience = 20, FlashTimer = .18f });
                var snapshot = Snapshot;
                snapshot.InventoryOpen = (byte)(reason == 0 ? 1 : 0);
                snapshot.Rewards.Active = (byte)(reason == 1 ? 1 : 0);
                snapshot.Zone.ReceiptActive = (byte)(reason == 2 ? 1 : 0);
                m_Em.SetComponentData(m_Run, snapshot);
                var render = m_World.GetOrCreateSystemManaged<SimulationRenderStateSystem>();
                uint tick = m_Em.GetComponentData<SimulationRunState>(m_Run).Tick;
                m_World.SetTime(new TimeData(m_Time, 1f / fps));
                for (int i = 0; i < fps; i++) { render.Update(); m_Em.CompleteAllTrackedJobs(); }
                Assert.That(m_Em.GetComponentData<GemVisualState>(gem).FlashTimer, Is.EqualTo(.18f));
                snapshot.InventoryOpen = snapshot.Rewards.Active = snapshot.Zone.ReceiptActive = 0;
                m_Em.SetComponentData(m_Run, snapshot);
                render.Update(); m_Em.CompleteAllTrackedJobs();
                Assert.That(m_Em.GetComponentData<GemVisualState>(gem).FlashTimer, Is.EqualTo(.18f - 1f / fps).Within(1e-6f));
                Assert.That(m_Em.GetComponentData<SimulationRunState>(m_Run).Tick, Is.EqualTo(tick));
            }
            finally { m_Em.DestroyEntity(gem); }
        }

        [Test]
        public void FixedSchedulerRunsOneTickPerSlowFrameWithoutLaterCatchUp()
        {
            m_World.GetOrCreateSystemManaged<SimulationRateBootstrapSystem>();
            var group = m_World.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>();
            group.SetRateManagerCreateAllocator(new SingleTickRateManager());
            group.AddSystemToUpdateList(m_Pipeline); group.SortSystems();
            uint tick = m_Em.GetComponentData<SimulationRunState>(m_Run).Tick;
            double start = m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).ElapsedSeconds;
            for (int i = 0; i < 10; i++)
            {
                m_Time += .1; m_World.SetTime(new TimeData(m_Time, .1f));
                group.Update(); m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
                Assert.That(m_Em.GetComponentData<SimulationRunState>(m_Run).Tick, Is.EqualTo(tick + (uint)i + 1));
                Assert.That(m_World.Time.ElapsedTime, Is.EqualTo(m_Time));
                Assert.That(m_World.Time.DeltaTime, Is.EqualTo(.1f));
            }
            Assert.That(m_Em.GetComponentData<WaveSpawnerConfig>(m_Wave).ElapsedSeconds - start,
                Is.EqualTo(10 * SimulationConstants.FixedTimestep).Within(1e-5f));
            tick = m_Em.GetComponentData<SimulationRunState>(m_Run).Tick;
            for (int i = 0; i < 120; i++)
            {
                float dt = SimulationConstants.FixedTimestep / 2;
                m_Time += dt; m_World.SetTime(new TimeData(m_Time, dt));
                group.Update(); m_Em.GetComponentData<SimulationJobFence>(m_Run).Handle.Complete();
            }
            Assert.That(m_Em.GetComponentData<SimulationRunState>(m_Run).Tick - tick, Is.EqualTo(60));
        }
    }
}
