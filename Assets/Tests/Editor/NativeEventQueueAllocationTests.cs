using System;
using System.Threading;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots.Tests
{
    public partial class SimulationRegressionTests
    {
        [Test]
        public void NativeEventQueuesReportAllocationsAfterWarmupWithHitsDeathsAndXpBursts()
        {
            using var damage = new EventQueueAllocationProbe<DamageEvent>();
            using var playerDamage = new EventQueueAllocationProbe<PlayerDamageEvent>();
            using var deactivations = new EventQueueAllocationProbe<Entity>();
            using var spawns = new EventQueueAllocationProbe<GemSpawnRequest>();
            using var commands = new EventQueueAllocationProbe<SimulationCommand>();
            using var deaths = new EventQueueAllocationProbe<DeathEvent>();
            using var rebases = new EventQueueAllocationProbe<OriginRebaseEvent>();
            using var reactions = new EventQueueAllocationProbe<PlayerHitReactionEvent>();
            using var collects = new EventQueueAllocationProbe<GemCollectEvent>();
            Entity damageEntity = m_Em.CreateEntityQuery(typeof(DamageEventQueueSingleton)).GetSingletonEntity();
            Entity playerDamageEntity = m_Em.CreateEntityQuery(typeof(PlayerDamageEventQueueSingleton)).GetSingletonEntity();
            Entity deactivationEntity = m_Em.CreateEntityQuery(typeof(ProjectileDeactivationQueueSingleton)).GetSingletonEntity();
            Entity spawnEntity = m_Em.CreateEntityQuery(typeof(GemSpawnQueueSingleton)).GetSingletonEntity();
            Entity commandEntity = m_Em.CreateEntityQuery(typeof(SimulationCommandQueue)).GetSingletonEntity();
            Entity bridgeEntity = m_Em.CreateEntityQuery(typeof(SimulationBridgeQueuesSingleton)).GetSingletonEntity();
            var originalDamage = m_Em.GetComponentData<DamageEventQueueSingleton>(damageEntity);
            var originalPlayerDamage = m_Em.GetComponentData<PlayerDamageEventQueueSingleton>(playerDamageEntity);
            var originalDeactivations = m_Em.GetComponentData<ProjectileDeactivationQueueSingleton>(deactivationEntity);
            var originalSpawns = m_Em.GetComponentData<GemSpawnQueueSingleton>(spawnEntity);
            var originalCommands = m_Em.GetComponentData<SimulationCommandQueue>(commandEntity);
            var originalBridge = m_Em.GetComponentData<SimulationBridgeQueuesSingleton>(bridgeEntity);
            var originalPipeline = m_Pipeline;
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            float originalChestChance = catalog.Value.Configs[(int)TankType].ChestDropChance;
            try
            {
                // Bind measured handles in a separate pipeline; retain the fixture's cached starting configuration.
                m_Em.CompleteAllTrackedJobs();
                m_Em.SetComponentData(damageEntity, new DamageEventQueueSingleton { DamageQueue = damage.Queue });
                m_Em.SetComponentData(playerDamageEntity, new PlayerDamageEventQueueSingleton { PlayerDamageQueue = playerDamage.Queue });
                m_Em.SetComponentData(deactivationEntity, new ProjectileDeactivationQueueSingleton { StagedDeactivations = deactivations.Queue });
                m_Em.SetComponentData(spawnEntity, new GemSpawnQueueSingleton { SpawnQueue = spawns.Queue });
                m_Em.SetComponentData(commandEntity, new SimulationCommandQueue { Commands = commands.Queue });
                m_Em.SetComponentData(bridgeEntity, new SimulationBridgeQueuesSingleton { DeathEventQueue = deaths.Queue,
                    RebaseEventQueue = rebases.Queue, HitReactionEventQueue = reactions.Queue, GemCollectEventQueue = collects.Queue });
                m_Commands = commands.Queue;
                m_Pipeline = m_World.CreateSystem<SimulationPipelineSystem>();
                catalog.Value.Configs[(int)TankType].ChestDropChance = 1;
                var bridge = m_World.GetOrCreateSystemManaged<PresentationBridgeSystem>();
                Reset(); RunQueueDeathAndXpBurst(); bridge.Update();
                Reset();
                for (int i = 0; i < 128; i++)
                {
                    Entity enemy = Enemy(new float2(20 + i % 8 * .05f, i / 8 * .05f), TankType);
                    m_Em.SetComponentData(enemy, new CurrentHealth { Value = 1000000 });
                }
                for (int i = 0; i < 4; i++) { RunQueueHitBurst(); bridge.Update(); }
                BeginSample();
                for (int i = 0; i < 8; i++) { RunQueueHitBurst(); bridge.Update(); }
                Report("sustained-hits-8-frames");
                BeginSample();
                RunQueueDeathAndXpBurst(); bridge.Update();
                Report("full-pool-death-expiry-xp-2-frames");
                // Restart also has to empty pending events before the measured storage is disposed.
                damage.Queue.Enqueue(default); playerDamage.Queue.Enqueue(default); deactivations.Queue.Enqueue(default);
                spawns.Queue.Enqueue(default); deaths.Queue.Enqueue(default); rebases.Queue.Enqueue(default);
                reactions.Queue.Enqueue(default); collects.Queue.Enqueue(default);
                Command(SimulationCommandKind.Restart); Tick();
                Assert.That(damage.Queue.Count + playerDamage.Queue.Count + deactivations.Queue.Count + spawns.Queue.Count +
                    commands.Queue.Count + deaths.Queue.Count + rebases.Queue.Count + reactions.Queue.Count + collects.Queue.Count, Is.Zero);
            }
            finally
            {
                m_Em.CompleteAllTrackedJobs();
                if (m_Pipeline != originalPipeline) m_World.DestroySystem(m_Pipeline);
                m_Em.SetComponentData(damageEntity, originalDamage);
                m_Em.SetComponentData(playerDamageEntity, originalPlayerDamage);
                m_Em.SetComponentData(deactivationEntity, originalDeactivations);
                m_Em.SetComponentData(spawnEntity, originalSpawns);
                m_Em.SetComponentData(commandEntity, originalCommands);
                m_Em.SetComponentData(bridgeEntity, originalBridge);
                m_Commands = originalCommands.Commands;
                catalog.Value.Configs[(int)TankType].ChestDropChance = originalChestChance;
                m_Pipeline = originalPipeline;
            }

            void BeginSample()
            {
                damage.BeginSample(); playerDamage.BeginSample(); deactivations.BeginSample(); spawns.BeginSample();
                commands.BeginSample(); deaths.BeginSample(); rebases.BeginSample(); reactions.BeginSample(); collects.BeginSample();
            }
            void Report(string phase)
            {
                damage.Report(phase); playerDamage.Report(phase); deactivations.Report(phase); spawns.Report(phase);
                commands.Report(phase); deaths.Report(phase); rebases.Report(phase); reactions.Report(phase); collects.Report(phase);
            }
        }

        private void RunQueueHitBurst()
        {
            var stats = m_Em.GetComponentData<PlayerStats>(m_Player);
            stats.MaxHealth = stats.CurrentHealth = 1000000; m_Em.SetComponentData(m_Player, stats);
            m_Em.SetComponentData(m_Player, new PlayerInvulnerability());
            Command(SimulationCommandKind.AutoAttack, 0);
            m_Em.SetComponentData(m_Input, new SimulationInput { Movement = new float2(1, 0) });
            for (int i = 0; i < 256; i++)
            {
                Entity playerShot = Projectile(new float2(19, 0), new float2(120, 0), true);
                Entity enemyShot = Projectile(Snapshot.PlayerPosition - new float2(1, 0), new float2(120, 0), false);
                var data = m_Em.GetComponentData<ProjectileData>(playerShot);
                data.RemainingLifetime = SimulationConstants.FixedTimestep;
                m_Em.SetComponentData(playerShot, data); m_Em.SetComponentData(enemyShot, data);
            }
            Tick();
            Assert.That(Snapshot.PlayerProjectiles + Snapshot.EnemyProjectiles, Is.Zero);
            var playerPool = m_Em.CreateEntityQuery(typeof(PlayerProjectilePoolSingleton)).GetSingleton<PlayerProjectilePoolSingleton>();
            var enemyPool = m_Em.CreateEntityQuery(typeof(EnemyProjectilePoolSingleton)).GetSingleton<EnemyProjectilePoolSingleton>();
            Assert.That(playerPool.InactiveProjectiles.Length, Is.EqualTo(playerPool.AllProjectiles.Length));
            Assert.That(enemyPool.InactiveProjectiles.Length, Is.EqualTo(enemyPool.AllProjectiles.Length));
        }

        private void RunQueueDeathAndXpBurst()
        {
            var pool = m_Em.CreateEntityQuery(typeof(EnemyPoolSingleton)).GetSingleton<EnemyPoolSingleton>();
            while (pool.InactiveEnemies.Length > 0) Enemy(new float2(10, 0), TankType);
            foreach (Entity enemy in pool.AllEnemies)
            {
                m_Em.SetComponentData(enemy, LocalTransform.FromPosition(10, 0, 0));
                m_Em.SetComponentData(enemy, new PreviousPosition { Value = new float2(10, 0) });
            }
            Command(SimulationCommandKind.KillAll); RunQueueHitBurst();
            Assert.That(Snapshot.ActiveEnemies, Is.Zero);
            Assert.That(pool.InactiveEnemies.Length, Is.EqualTo(pool.AllEnemies.Length));
            var catalog = m_Em.CreateEntityQuery(typeof(EnemyConfigCatalogSingleton)).GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            ulong expected = (ulong)pool.AllEnemies.Length * catalog.Value.Configs[(int)TankType].ExperienceValue;
            var gems = m_Em.CreateEntityQuery(typeof(GemPoolSingleton)).GetSingleton<GemPoolSingleton>();
            ulong stored = 0; foreach (var gem in gems.AllGems) if (gem.IsActive != 0) stored += gem.ExperienceValue;
            Assert.That(stored, Is.EqualTo(expected));
            Assert.That(Snapshot.ActiveGems, Is.EqualTo(gems.AllGems.Length));
            m_Em.SetComponentData(m_Input, default(SimulationInput));
            m_Em.SetComponentData(m_Player, LocalTransform.FromPosition(10, 0, 0)); Tick();
            Assert.That(Snapshot.TotalExperience, Is.EqualTo(expected));
            Assert.That(Snapshot.ActiveGems, Is.Zero);
            Assert.That(gems.FreeGems.Length, Is.EqualTo(gems.AllGems.Length));
            var bridge = m_Em.CreateEntityQuery(typeof(SimulationBridgeQueuesSingleton)).GetSingleton<SimulationBridgeQueuesSingleton>();
            Assert.That(bridge.DeathEventQueue.Count, Is.EqualTo(SimulationConstants.CosmeticQueueCapacity));
            Assert.That(bridge.GemCollectEventQueue.Count, Is.EqualTo(SimulationConstants.CosmeticQueueCapacity));
        }
    }

    internal sealed class EventQueueAllocationProbe<T> : IDisposable where T : unmanaged
    {
        private AllocatorHelper<EventQueueAllocationCounter> m_Helper = new AllocatorHelper<EventQueueAllocationCounter>(Allocator.Persistent);
        public UnsafeQueue<T> Queue;
        public EventQueueAllocationProbe()
        {
            Queue = new UnsafeQueue<T>(m_Helper.Allocator.Handle);
            Assert.That(m_Helper.Allocator.Allocations, Is.GreaterThan(0), "The probe must observe the queue's native header allocation.");
        }
        public void BeginSample()
        {
            Assert.That(Queue.Count, Is.Zero);
            m_Helper.Allocator.Allocations = m_Helper.Allocator.Frees = 0;
            m_Helper.Allocator.Bytes = 0;
        }
        public void Report(string phase)
        {
            Assert.That(Queue.Count, Is.Zero);
            Assert.That(m_Helper.Allocator.Frees, Is.EqualTo(m_Helper.Allocator.Allocations), "Drained queue blocks must be released.");
            TestContext.WriteLine($"NativeEventQueue phase={phase} event={typeof(T).Name} allocations={m_Helper.Allocator.Allocations} " +
                $"frees={m_Helper.Allocator.Frees} requestedBytes={m_Helper.Allocator.Bytes}");
        }
        public void Dispose() { Queue.Dispose(); m_Helper.Dispose(); }
    }

    [BurstCompile]
    internal struct EventQueueAllocationCounter : AllocatorManager.IAllocator
    {
        public AllocatorManager.AllocatorHandle Handle { get; set; }
        public Allocator ToAllocator => Handle.ToAllocator;
        public bool IsCustomAllocator => Handle.IsCustomAllocator;
        public AllocatorManager.TryFunction Function => Allocate;
        public int Allocations, Frees;
        public long Bytes;
        public int Try(ref AllocatorManager.Block block)
        {
            bool allocating = block.Range.Pointer == IntPtr.Zero;
            long bytes = block.Bytes;
            var original = block.Range.Allocator; block.Range.Allocator = Allocator.Persistent;
            int error = AllocatorManager.Try(ref block); block.Range.Allocator = original;
            if (error == 0)
            {
                if (allocating) { Interlocked.Increment(ref Allocations); Interlocked.Add(ref Bytes, bytes); }
                else Interlocked.Increment(ref Frees);
            }
            return error;
        }
        [BurstCompile(CompileSynchronously = true)]
        [AOT.MonoPInvokeCallback(typeof(AllocatorManager.TryFunction))]
        private static unsafe int Allocate(IntPtr state, ref AllocatorManager.Block block)
            => ((EventQueueAllocationCounter*)state)->Try(ref block);
        public void Dispose() => Handle.Dispose();
    }
}
