using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace GameHolder.PureDots.Tests
{
    public partial class SimulationRegressionTests
    {
        [Test]
        public void EarlierProjectileHitWinsOverEndOfTickMeleeContact()
        {
            Enemy(new float2(.9f, 0));
            var projectile = Projectile(new float2(-.42f, 0), new float2(100, 0), false, .01f);
            Tick();
            Assert.That(Snapshot.Player.CurrentHealth, Is.EqualTo(DefaultPlayer.Stats.MaxHealth - 25));
            Assert.That(m_Em.IsComponentEnabled<ProjectileActiveTag>(projectile), Is.False);
        }

        [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)] [TestCase(true, true)]
        public void SharedAutoAimKeepsEachWeaponsRangeAndStableTargetTie(bool poolSearch, bool firstDue)
        {
            var inactive = Enemy(new float2(0, .5f));
            m_Em.SetComponentEnabled<EnemyActiveTag>(inactive, false);
            var positive = Enemy(new float2(0, 4));
            var negative = Enemy(new float2(0, -4));
            var first = TestWeapon(WeaponType.Standard);
            first.Count = 1; first.Interval = firstDue ? .05f : .2f; first.Range = 1; first.Damage = 10;
            var second = first;
            second.Interval = .05f; second.Range = poolSearch ? 256 : 6; second.Damage = 20;
            m_Em.SetComponentData(m_Player, first);
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.ActiveEnemies--; run.Loadout.Count = 2; run.Loadout.SecondWeapon = second;
            m_Em.SetComponentData(m_Run, run);
            Command(SimulationCommandKind.AutoAttack, 1);
            Tick(.05f);
            using var projectiles = m_Em.CreateEntityQuery(typeof(PlayerProjectileTag), typeof(ProjectileActiveTag)).ToEntityArray(Allocator.Temp);
            Assert.That(projectiles.Length, Is.EqualTo(firstDue ? 2 : 1));
            float sign = DamageEvent.CreateTargetKey(positive) < DamageEvent.CreateTargetKey(negative) ? 1 : -1;
            foreach (var projectile in projectiles)
            {
                bool isFirst = m_Em.GetComponentData<ProjectileData>(projectile).Damage == 10;
                float2 expected = isFirst ? new float2(1, 0) : new float2(0, sign);
                Assert.That(math.distance(m_Em.GetComponentData<MovementVelocity>(projectile).Value, expected * first.Speed), Is.LessThan(1e-4f));
            }
            run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            Assert.That(run.AngleCounter, Is.EqualTo(firstDue ? 2 : 1));
            Tick(.01f);
            run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            Assert.That(run.AttackTimer, Is.EqualTo(firstDue ? .01f : .06f).Within(1e-6f));
            Assert.That(run.Loadout.SecondAttackTimer, Is.EqualTo(.01f).Within(1e-6f));
            Assert.That(Snapshot.PlayerProjectiles, Is.EqualTo(firstDue ? 2 : 1));
        }

        [Test]
        public void EmptyFieldPreservesIndependentFallbackAngles()
        {
            var first = TestWeapon(WeaponType.Standard);
            first.Count = 1; first.Interval = .05f; first.Range = 128; first.Damage = 10;
            var second = first;
            second.Range = 256; second.Damage = 20;
            m_Em.SetComponentData(m_Player, first);
            var run = m_Em.GetComponentData<SimulationRunState>(m_Run);
            run.Loadout.Count = 2; run.Loadout.SecondWeapon = second;
            m_Em.SetComponentData(m_Run, run);
            Command(SimulationCommandKind.AutoAttack, 1);
            Tick(.05f);
            using var projectiles = m_Em.CreateEntityQuery(typeof(PlayerProjectileTag), typeof(ProjectileActiveTag)).ToEntityArray(Allocator.Temp);
            Assert.That(projectiles.Length, Is.EqualTo(2));
            foreach (var projectile in projectiles)
            {
                float angle = m_Em.GetComponentData<ProjectileData>(projectile).Damage == 10 ? 0 : CombatConstants.AutoAimAngleStep;
                float2 expected = new float2(math.cos(angle), math.sin(angle)) * first.Speed;
                Assert.That(math.distance(m_Em.GetComponentData<MovementVelocity>(projectile).Value, expected), Is.LessThan(1e-4f));
            }
        }
    }
}
