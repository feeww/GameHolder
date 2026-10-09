using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots.Tests
{
    public partial class SimulationRegressionTests
    {
        [TestCase(3f, false)] [TestCase(3.001f, true)]
        [TestCase(7f, true)] [TestCase(7.001f, false)]
        public void LaserEnemyChargesOnlyBeyondThreeAndThroughSevenMeters(float distance, bool charges)
        {
            Entity enemy = Enemy(new float2(distance, 0), LaserType);
            m_Em.SetComponentData(enemy, default(EnemyRangedCooldown));
            Tick(0);
            Assert.That(m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeBeam != Entity.Null, Is.EqualTo(charges));
            Assert.That(Snapshot.EnemyProjectiles, Is.EqualTo(charges ? 1 : 0));
        }

        [Test]
        public void LaserEnemyPausesForOneSecondThenDamagesOnceAlongItsWarningLine()
        {
            Entity enemy = Enemy(new float2(5, 0), LaserType);
            m_Em.SetComponentData(enemy, default(EnemyRangedCooldown));
            Tick(0);
            Entity beam = m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeBeam;
            Assert.That(beam, Is.Not.EqualTo(Entity.Null));
            Assert.That(m_Em.GetComponentData<LaserBeam>(beam).Charging, Is.EqualTo(1));
            Assert.That(m_Em.GetComponentData<LaserBeam>(beam).Direction, Is.EqualTo(new float2(-1, 0)));
            Assert.That(m_Em.GetComponentData<ProjectileData>(beam).Damage, Is.Zero);
            Tick(.5f); Tick(.49f);
            Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position.xy, Is.EqualTo(new float2(5, 0)));
            Assert.That(m_Em.GetComponentData<MovementVelocity>(enemy).Value, Is.EqualTo(float2.zero));
            Assert.That(m_Em.GetComponentData<LaserBeam>(beam).Charging, Is.EqualTo(1));
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth));
            Tick(.01f);
            Assert.That(m_Em.GetComponentData<LaserBeam>(beam).Charging, Is.Zero);
            Assert.That(m_Em.GetComponentData<LaserBeam>(beam).PendingHit, Is.EqualTo(1));
            Assert.That(m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeBeam, Is.EqualTo(Entity.Null));
            Tick(.01f); Tick(.01f);
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth - 20));
            Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position.x, Is.LessThan(5));
            Tick(.2f);
            Assert.That(m_Em.IsComponentEnabled<ProjectileActiveTag>(beam), Is.False);
            Assert.That(Snapshot.EnemyProjectiles, Is.Zero);
        }

        [Test]
        public void LaserEnemyWarningDirectionStaysFixedSoThePlayerCanDodge()
        {
            Entity enemy = Enemy(new float2(5, 0), LaserType);
            m_Em.SetComponentData(enemy, default(EnemyRangedCooldown));
            Tick(0);
            Entity beam = m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeBeam;
            m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(0, 1) });
            Tick(.25f);
            m_Em.SetComponentData(m_Input, default(SimulationInput));
            Tick(.75f); Tick(.01f);
            Assert.That(m_Em.GetComponentData<LaserBeam>(beam).Direction, Is.EqualTo(new float2(-1, 0)));
            Assert.That(m_Em.GetComponentData<LaserBeam>(beam).Charging, Is.Zero);
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth));
        }

        [TestCase(3f)] [TestCase(8f)]
        public void LaserEnemyCancelsOutsideItsFiringBandAndRechargesOnReturn(float distance)
        {
            Entity enemy = Enemy(new float2(5, 0), LaserType);
            m_Em.SetComponentData(enemy, default(EnemyRangedCooldown));
            Tick(0); Tick(.5f);
            Entity beam = m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeBeam;
            m_Em.SetComponentData(enemy, LocalTransform.FromPosition(new float3(distance, 0, 0)));
            Tick(0);
            Assert.That(m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeBeam, Is.EqualTo(Entity.Null));
            Assert.That(m_Em.IsComponentEnabled<ProjectileActiveTag>(beam), Is.False);
            Assert.That(Snapshot.EnemyProjectiles, Is.Zero);
            var pool = m_Em.CreateEntityQuery(typeof(EnemyProjectilePoolSingleton)).GetSingleton<EnemyProjectilePoolSingleton>();
            Assert.That(pool.InactiveProjectiles.Length, Is.EqualTo(SimulationConstants.DefaultMaxProjectiles));
            m_Em.SetComponentData(enemy, LocalTransform.FromPosition(new float3(5, 0, 0)));
            Tick(0);
            Assert.That(m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeTimer, Is.EqualTo(1));
            Assert.That(Snapshot.EnemyProjectiles, Is.EqualTo(1));
        }

        [Test]
        public void LaserEnemyApproachesForMeleeAndSpeedBonusTracksDistanceWithoutStacking()
        {
            Entity enemy = Enemy(new float2(3, 0), LaserType);
            foreach (float distance in new[] { 3f, 6f, 3f })
            {
                m_Em.SetComponentData(enemy, LocalTransform.FromPosition(new float3(distance, 0, 0)));
                m_Em.SetComponentData(enemy, default(SeparationCache));
                Tick(.1f);
                float speed = 1.8f * (distance <= 3 ? 1.15f : 1);
                Assert.That(m_Em.GetComponentData<MovementVelocity>(enemy).Value.x, Is.EqualTo(-speed).Within(.0001f));
                Assert.That(m_Em.GetComponentData<LocalTransform>(enemy).Position.x, Is.EqualTo(distance - speed * .1f).Within(.0001f));
                Assert.That(Snapshot.EnemyProjectiles, Is.Zero);
            }
            m_Em.SetComponentData(enemy, LocalTransform.FromPosition(new float3(.8f, 0, 0)));
            Tick(.1f);
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth - 12));
        }

        [Test]
        public void LaserEnemyChargeFreezesOnPauseAndDeathAndRestartReturnTheBeamToItsPool()
        {
            Entity enemy = Enemy(new float2(5, 0), LaserType);
            m_Em.SetComponentData(enemy, default(EnemyRangedCooldown));
            Tick(0); Tick(.25f);
            Entity beam = m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeBeam;
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run); run.InventoryOpen = 1;
            m_Em.SetComponentData(m_Run, run); Tick(2);
            Assert.That(m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeTimer, Is.EqualTo(.75f));
            Assert.That(m_Em.GetComponentData<LaserBeam>(beam).Charging, Is.EqualTo(1));
            run = m_Em.GetComponentData<SimulationRunState>(m_Run); run.InventoryOpen = 0;
            m_Em.SetComponentData(m_Run, run);
            Command(SimulationCommandKind.KillAll); Tick(0);
            Assert.That(m_Em.IsComponentEnabled<ProjectileActiveTag>(beam), Is.False);
            Assert.That(Snapshot.EnemyProjectiles, Is.Zero);
            enemy = Enemy(new float2(5, 0), LaserType);
            m_Em.SetComponentData(enemy, default(EnemyRangedCooldown));
            Tick(0);
            beam = m_Em.GetComponentData<EnemyRangedCooldown>(enemy).ChargeBeam;
            Command(SimulationCommandKind.Restart); Tick(0);
            Assert.That(m_Em.IsComponentEnabled<LaserBeam>(beam), Is.False);
            Assert.That(Snapshot.EnemyProjectiles, Is.Zero);
            var pool = m_Em.CreateEntityQuery(typeof(EnemyProjectilePoolSingleton)).GetSingleton<EnemyProjectilePoolSingleton>();
            Assert.That(pool.InactiveProjectiles.Length, Is.EqualTo(SimulationConstants.DefaultMaxProjectiles));
        }
    }
}
