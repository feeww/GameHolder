using System;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots.Tests
{
    public partial class SimulationRegressionTests
    {
        [Test]
        public void LargeExperienceGainsContinueBeyondUIntLevelThresholds()
        {
            var spawns = m_Em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
            for (int i = 0; i < 3; i++) spawns.Enqueue(new GemSpawnRequest { ExperienceValue = int.MaxValue });
            Tick(0);
            Assert.That(Snapshot.Player.Level, Is.EqualTo(3));
            Assert.That(Snapshot.Player.Experience, Is.Zero);
            Assert.That(Snapshot.TotalExperience, Is.EqualTo(3UL * int.MaxValue));
            Assert.That(Snapshot.Rewards.Pending, Is.EqualTo(2));
        }

        [TestCase(10f)] [TestCase(40f)]
        public void GemOverflowConservesWideExperienceAndMeasuresTargetBurst(float distance)
        {
            var pool = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>();
            var spawns = m_Em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
            var milliseconds = new double[3];
            for (int sample = 0; sample < milliseconds.Length; sample++)
            {
                Reset();
                for (int i = 0; i < 10000; i++) spawns.Enqueue(new GemSpawnRequest { Position = new float2(distance, 0), ExperienceValue = int.MaxValue });
                var timer = Stopwatch.StartNew(); Tick(); milliseconds[sample] = timer.Elapsed.TotalMilliseconds;
                ulong total = 0;
                for (int i = 0; i < pool.AllGems.Length; i++) total += pool.AllGems[i].ExperienceValue;
                Assert.That(total, Is.EqualTo(10000UL * int.MaxValue));
                Assert.That(Snapshot.ActiveGems, Is.EqualTo(1024));
                Assert.That(pool.AllGems[0].ExperienceValue, Is.GreaterThan(uint.MaxValue));
            }
            Array.Sort(milliseconds);
            TestContext.WriteLine($"10000 gem drops at {distance}: median tick {milliseconds[1]:F3} ms (min {milliseconds[0]:F3}, max {milliseconds[2]:F3})");
            Assert.That(UnsafeUtility.SizeOf<GemSpatialRecord>(), Is.EqualTo(32));
            m_Em.SetComponentData(m_Player, LocalTransform.FromPosition(new float3(distance, 0, 0))); Tick(0);
            Assert.That(Snapshot.TotalExperience, Is.EqualTo(10000UL * int.MaxValue));
            Assert.That(Snapshot.Player.Experience, Is.GreaterThan(uint.MaxValue));
            Assert.That(Snapshot.Player.Level, Is.GreaterThan(3));
            Assert.That(pool.FreeGems.Length, Is.EqualTo(1024));
        }

        [Test]
        public void IncomingPickupDoesNotTeleportDistantXpAndDeadPlayersDoNotCollect()
        {
            var pool = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>();
            var spawns = m_Em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
            for (int i = 0; i < 1024; i++) spawns.Enqueue(new GemSpawnRequest { Position = new float2(40, 0), ExperienceValue = 1 });
            Tick(0);
            spawns.Enqueue(new GemSpawnRequest { ExperienceValue = 7 }); Tick(0);
            Assert.That(Snapshot.TotalExperience, Is.EqualTo(7));
            Assert.That(Snapshot.ActiveGems, Is.EqualTo(1024));
            Assert.That(pool.AllGems[0].Position, Is.EqualTo(new float2(40, 0)));
            var stats = Snapshot.Player; stats.IsDead = 1; m_Em.SetComponentData(m_Player, stats);
            spawns.Enqueue(new GemSpawnRequest { ExperienceValue = 3 }); Tick(0);
            Assert.That(Snapshot.TotalExperience, Is.EqualTo(7));
            Assert.That(pool.AllGems[0].Position, Is.EqualTo(float2.zero));
            Assert.That(pool.AllGems[0].ExperienceValue, Is.EqualTo(4));
        }

        [Test]
        public void OverflowReevaluatesFurthestAfterRelocationAndClearsCacheOnRestart()
        {
            var pool = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>();
            var spawns = m_Em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
            for (int i = 0; i < 1024; i++) spawns.Enqueue(new GemSpawnRequest { Position = new float2(40, 0), ExperienceValue = 1 });
            for (int i = 0; i < 2; i++) spawns.Enqueue(new GemSpawnRequest { Position = new float2(20, 0), ExperienceValue = 2 });
            Tick(0);
            Assert.That(pool.AllGems[0].Position, Is.EqualTo(new float2(20, 0)));
            Assert.That(pool.AllGems[1].Position, Is.EqualTo(new float2(20, 0)));
            Assert.That(pool.AllGems[2].Position, Is.EqualTo(new float2(40, 0)));
            Reset();
            for (int i = 0; i < 1025; i++) spawns.Enqueue(new GemSpawnRequest { Position = new float2(10, 0), ExperienceValue = 1 });
            Tick(0);
            Reset();
            for (int i = 0; i < 1024; i++) spawns.Enqueue(new GemSpawnRequest { Position = new float2(i == 1 ? 10 : 20, 0), ExperienceValue = 1 });
            for (int i = 0; i < 2; i++) spawns.Enqueue(new GemSpawnRequest { Position = new float2(10, 0), ExperienceValue = 1 });
            Tick(0);
            Assert.That(pool.AllGems[0].ExperienceValue, Is.EqualTo(1));
            Assert.That(pool.AllGems[1].ExperienceValue, Is.EqualTo(3));
        }

        [Test]
        public void TargetPopulationFitsPreallocatedPools()
        {
            for (int i = 0; i < 10000; i++) Enemy(new float2(30 + (i % 100) * .5f, 30 + (i / 100) * .5f));
            for (int i = 0; i < 5000; i++)
            {
                Projectile(new float2(-100, -100), float2.zero, true);
                Projectile(new float2(-120, -100), float2.zero, false);
            }
            var timer = Stopwatch.StartNew(); Tick();
            TestContext.WriteLine($"10000 enemies + 10000 standard projectiles: one simulation tick {timer.Elapsed.TotalMilliseconds:F3} ms");
            Assert.That(Snapshot.ActiveEnemies, Is.EqualTo(10000));
            Assert.That(Snapshot.PlayerProjectiles + Snapshot.EnemyProjectiles, Is.EqualTo(10000));
            Assert.That(m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>().InactiveEnemies.Length, Is.Zero);
            Assert.That(m_Em.CreateEntityQuery(typeof(PlayerProjectilePoolSingleton)).GetSingleton<PlayerProjectilePoolSingleton>().InactiveProjectiles.Length, Is.Zero);
            Assert.That(m_Em.CreateEntityQuery(typeof(EnemyProjectilePoolSingleton)).GetSingleton<EnemyProjectilePoolSingleton>().InactiveProjectiles.Length, Is.Zero);
        }
    }
}
