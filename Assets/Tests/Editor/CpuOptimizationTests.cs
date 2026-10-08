using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
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
            try
            {
                foreach (int workers in new[] { 1, math.min(4, JobsUtility.JobWorkerMaximumCount) })
                {
                    JobsUtility.JobWorkerCount = workers;
                    Reset(); Command(SimulationCommandKind.GodMode, 1);
                    Entity target = Enemy(new float2(20, 0), TankType);
                    m_Em.SetComponentData(target, new CurrentHealth { Value = 1000000 });
                    for (int i = 0; i < 256; i++)
                    {
                        Entity hit = Projectile(new float2(19, 0), new float2(120, 0), true);
                        var data = m_Em.GetComponentData<ProjectileData>(hit);
                        data.Damage = i % 3 == 0 ? .1f : i % 3 == 1 ? 1 : 1000;
                        m_Em.SetComponentData(hit, data);
                        Entity expired = Projectile(new float2(-100, -100), float2.zero, false);
                        data = m_Em.GetComponentData<ProjectileData>(expired); data.RemainingLifetime = 0;
                        m_Em.SetComponentData(expired, data);
                    }
                    Tick();
                    float firstHealth = m_Em.GetComponentData<CurrentHealth>(target).Value;
                    if (expectedHealth < 0) expectedHealth = firstHealth;
                    else Assert.That(firstHealth, Is.EqualTo(expectedHealth));
                    Assert.That(Snapshot.PlayerProjectiles + Snapshot.EnemyProjectiles, Is.Zero);
                    var playerPool = m_Em.CreateEntityQuery(typeof(PlayerProjectilePoolSingleton)).GetSingleton<PlayerProjectilePoolSingleton>();
                    var enemyPool = m_Em.CreateEntityQuery(typeof(EnemyProjectilePoolSingleton)).GetSingleton<EnemyProjectilePoolSingleton>();
                    Assert.That(playerPool.InactiveProjectiles.Length, Is.EqualTo(playerPool.AllProjectiles.Length));
                    Assert.That(enemyPool.InactiveProjectiles.Length, Is.EqualTo(enemyPool.AllProjectiles.Length));
                }
            }
            finally { JobsUtility.JobWorkerCount = original; }
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
