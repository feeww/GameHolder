using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(PureDotsRenderBootstrapSystem))]
    public partial struct SimulationBootstrapSystem : ISystem
    {
        private bool m_Initialized;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_Initialized = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (m_Initialized) return;
            if (!SystemAPI.HasSingleton<PureDotsPrefabsSingleton>() || !SystemAPI.HasSingleton<StartingPlayerConfig>() ||
                !SystemAPI.HasSingleton<EnemyConfigCatalogSingleton>() || !SystemAPI.HasSingleton<RewardCatalogSingleton>()) return;
            m_Initialized = true;

            var prefabs = SystemAPI.GetSingleton<PureDotsPrefabsSingleton>();
            var em = state.EntityManager;

            // 2. Floating origin config singleton
            var originEntity = em.CreateEntity();
            em.AddComponentData(originEntity, new FloatingOriginConfig
            {
                ThresholdSq = SimulationConstants.FloatingOriginThresholdSq
            });

            // 3. Wave spawner config singleton
            var spawnerEntity = em.CreateEntity();
            em.AddComponentData(spawnerEntity, RunDefaults.Wave);

            // 5. Spatial Hash Grids (2x over-provisioning for load factor <= 0.5)
            const int maxEnemies = EnemyPoolSingleton.Capacity;
            const int maxProjectiles = PlayerProjectilePoolSingleton.Capacity;

            var enemyGridEntity = em.CreateEntity();
            em.AddComponentData(enemyGridEntity, new EnemySpatialGridSingleton
            {
                Grid = new UnsafeParallelMultiHashMap<uint, GridEntry>(SimulationConstants.EnemyGridCapacity, Allocator.Persistent),
                CrowdCells = new UnsafeParallelHashMap<int2, CrowdCell>(SimulationConstants.CrowdGridCapacity, Allocator.Persistent)
            });

            // 6. Combat queues
            var damageQueueEntity = em.CreateEntity();
            em.AddComponentData(damageQueueEntity, new DamageEventQueueSingleton
            {
                DamageQueue = new UnsafeQueue<DamageEvent>(Allocator.Persistent)
            });

            var playerDamageQueueEntity = em.CreateEntity();
            em.AddComponentData(playerDamageQueueEntity, new PlayerDamageEventQueueSingleton
            {
                PlayerDamageQueue = new UnsafeQueue<PlayerDamageEvent>(Allocator.Persistent)
            });

            var projDeactEntity = em.CreateEntity();
            em.AddComponentData(projDeactEntity, new ProjectileDeactivationQueueSingleton
            {
                StagedDeactivations = new UnsafeQueue<Entity>(Allocator.Persistent)
            });

            var gemSpawnQueueEntity = em.CreateEntity();
            em.AddComponentData(gemSpawnQueueEntity, new GemSpawnQueueSingleton
            {
                SpawnQueue = new UnsafeQueue<GemSpawnRequest>(Allocator.Persistent)
            });

            // 7. Presentation bridge queues
            var bridgeEntity = em.CreateEntity();
            em.AddComponentData(bridgeEntity, new SimulationBridgeQueuesSingleton
            {
                DeathEventQueue = new UnsafeQueue<DeathEvent>(Allocator.Persistent),
                RebaseEventQueue = new UnsafeQueue<OriginRebaseEvent>(Allocator.Persistent),
                HitReactionEventQueue = new UnsafeQueue<PlayerHitReactionEvent>(Allocator.Persistent),
                GemCollectEventQueue = new UnsafeQueue<GemCollectEvent>(Allocator.Persistent)
            });

            // 8. Experience Gem Pool (Preallocated to EXACTLY 1024 entities - Single Source of Truth)
            var gemPoolSingleton = new GemPoolSingleton
            {
                FreeGems = new UnsafeQueue<Entity>(Allocator.Persistent),
                AllGems = new UnsafeList<GemSpatialRecord>(GemPoolSingleton.Capacity, Allocator.Persistent),
                FreeChests = new UnsafeQueue<Entity>(Allocator.Persistent),
                AllChests = new UnsafeList<ArtifactChest>(GemPoolSingleton.ChestCapacity, Allocator.Persistent)
            };

            var preallocatedGems = CollectionHelper.CreateNativeArray<Entity>(GemPoolSingleton.Capacity, Allocator.Temp);
            em.Instantiate(prefabs.GemPrefab, preallocatedGems);

            for (int i = 0; i < GemPoolSingleton.Capacity; i++)
            {
                Entity gem = preallocatedGems[i];
                em.SetComponentData(gem, new GemData
                {
                    ExperienceValue = 0,
                    Tier = 0,
                    SlotIndex = (uint)i
                });

                em.SetComponentEnabled<GemActiveTag>(gem, false);

                gemPoolSingleton.AllGems.Add(new GemSpatialRecord
                {
                    Entity = gem,
                    Position = float2.zero,
                    ExperienceValue = 0,
                    Tier = 0,
                    IsActive = 0
                });

                gemPoolSingleton.FreeGems.Enqueue(gem);
            }
            preallocatedGems.Dispose();
            var preallocatedChests = CollectionHelper.CreateNativeArray<Entity>(GemPoolSingleton.ChestCapacity, Allocator.Temp);
            em.Instantiate(prefabs.GemPrefab, preallocatedChests);
            for (int i = 0; i < preallocatedChests.Length; i++)
            {
                Entity chest = preallocatedChests[i];
                em.SetComponentData(chest, new GemData { SlotIndex = (uint)i, IsChest = 1 });
                em.SetComponentEnabled<GemActiveTag>(chest, false);
                gemPoolSingleton.AllChests.Add(new ArtifactChest { Entity = chest });
                gemPoolSingleton.FreeChests.Enqueue(chest);
            }
            preallocatedChests.Dispose();

            var gemPoolEntity = em.CreateEntity();
            em.AddComponentData(gemPoolEntity, gemPoolSingleton);

            // 9. Enemy Pool Preallocation
            var enemyPoolSingleton = new EnemyPoolSingleton
            {
                InactiveEnemies = new UnsafeQueue<Entity>(Allocator.Persistent),
                AllEnemies = new UnsafeList<Entity>(maxEnemies, Allocator.Persistent)
            };

            var preallocatedEnemies = CollectionHelper.CreateNativeArray<Entity>(maxEnemies, Allocator.Temp);
            em.Instantiate(prefabs.EnemyPrefab, preallocatedEnemies);

            for (int i = 0; i < maxEnemies; i++)
            {
                Entity enemy = preallocatedEnemies[i];
                em.SetComponentEnabled<EnemyActiveTag>(enemy, false);
                enemyPoolSingleton.InactiveEnemies.Enqueue(enemy);
                enemyPoolSingleton.AllEnemies.Add(enemy);
                em.SetComponentEnabled<EnemyRangedTag>(enemy, false);
            }
            preallocatedEnemies.Dispose();

            var enemyPoolEntity = em.CreateEntity();
            em.AddComponentData(enemyPoolEntity, enemyPoolSingleton);

            // 10. Projectile Pools Preallocation (Player & Enemy)
            var playerProjPoolSingleton = new PlayerProjectilePoolSingleton
            {
                InactiveProjectiles = new UnsafeQueue<Entity>(Allocator.Persistent),
                AllProjectiles = new UnsafeList<Entity>(maxProjectiles, Allocator.Persistent)
            };

            var preallocatedPlayerProj = CollectionHelper.CreateNativeArray<Entity>(maxProjectiles, Allocator.Temp);
            em.Instantiate(prefabs.PlayerProjPrefab, preallocatedPlayerProj);
            for (int i = 0; i < maxProjectiles; i++)
            {
                Entity proj = preallocatedPlayerProj[i];
                em.SetComponentEnabled<ProjectileActiveTag>(proj, false);
                playerProjPoolSingleton.InactiveProjectiles.Enqueue(proj);
                playerProjPoolSingleton.AllProjectiles.Add(proj);
            }
            preallocatedPlayerProj.Dispose();

            var playerProjPoolEntity = em.CreateEntity();
            em.AddComponentData(playerProjPoolEntity, playerProjPoolSingleton);

            var enemyProjPoolSingleton = new EnemyProjectilePoolSingleton
            {
                InactiveProjectiles = new UnsafeQueue<Entity>(Allocator.Persistent),
                AllProjectiles = new UnsafeList<Entity>(maxProjectiles, Allocator.Persistent)
            };

            var preallocatedEnemyProj = CollectionHelper.CreateNativeArray<Entity>(maxProjectiles, Allocator.Temp);
            em.Instantiate(prefabs.EnemyProjPrefab, preallocatedEnemyProj);
            for (int i = 0; i < maxProjectiles; i++)
            {
                Entity proj = preallocatedEnemyProj[i];
                em.SetComponentEnabled<ProjectileActiveTag>(proj, false);
                enemyProjPoolSingleton.InactiveProjectiles.Enqueue(proj);
                enemyProjPoolSingleton.AllProjectiles.Add(proj);
            }
            preallocatedEnemyProj.Dispose();

            var enemyProjPoolEntity = em.CreateEntity();
            em.AddComponentData(enemyProjPoolEntity, enemyProjPoolSingleton);

            // 11. Instantiate Player Entity (Prefab already contains default transform, velocity, stats, and invulnerability)
            var startingPlayer = SystemAPI.GetSingleton<StartingPlayerConfig>();
            var rewards = SystemAPI.GetSingleton<RewardCatalogSingleton>().Catalog;
            var player = em.Instantiate(prefabs.PlayerPrefab);
            em.SetComponentData(player, startingPlayer.Stats);
            em.SetComponentData(player, startingPlayer.Weapon);
            em.SetComponentData(player, new PlayerInvulnerability { InvulnerabilityDuration = startingPlayer.InvulnerabilityDuration });
            var runEntity = em.CreateEntity();
            em.AddComponentData(runEntity, new SimulationRunState { Player = player, AutoAttack = 1, Generation = 1,
                Loadout = RewardRoll.StartingLoadout(), Rewards = new RewardSelection { RandomState = RewardRoll.SeedForRun(ref rewards.Value, 1) },
                ArtifactRandomState = ArtifactRoll.SeedForRun(ref rewards.Value, 1),
                Zone = new TemporaryZoneState { RandomState = TemporaryZone.SeedForRun(ref rewards.Value, 1) },
                PlayerCollisionRadius = startingPlayer.Stats.CollisionRadius });
            em.AddComponentData(runEntity, new SimulationSnapshot { Player = startingPlayer.Stats, FirstWeapon = startingPlayer.Weapon,
                Loadout = RewardRoll.StartingLoadout(), AutoAttack = 1, Generation = 1,
                WeaponCount = 1, WeaponCapacity = SystemAPI.GetSingleton<RewardCatalogSingleton>().Catalog.Value.MaxWeapons });
            em.AddComponentData(runEntity, new SimulationJobFence());
            em.AddComponentData(em.CreateEntity(), new SimulationInput());
            em.AddComponentData(em.CreateEntity(), new SimulationCommandQueue { Commands = new UnsafeQueue<SimulationCommand>(Allocator.Persistent) });
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            state.EntityManager.CompleteAllTrackedJobs();
            if (SystemAPI.HasSingleton<RewardCatalogSingleton>())
            {
                var rewards = SystemAPI.GetSingleton<RewardCatalogSingleton>().Catalog;
                if (rewards.IsCreated) rewards.Dispose();
            }
            if (SystemAPI.HasSingleton<SimulationCommandQueue>()) SystemAPI.GetSingleton<SimulationCommandQueue>().Commands.Dispose();
            if (SystemAPI.HasSingleton<EnemyConfigCatalogSingleton>())
            {
                var catalogSingleton = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>();
                if (catalogSingleton.Catalog.IsCreated)
                {
                    catalogSingleton.Catalog.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<EnemySpatialGridSingleton>())
            {
                var gridSingleton = SystemAPI.GetSingleton<EnemySpatialGridSingleton>();
                if (gridSingleton.Grid.IsCreated)
                {
                    gridSingleton.Grid.Dispose();
                    gridSingleton.CrowdCells.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<EnemyProjectileGridSingleton>())
            {
                var gridSingleton = SystemAPI.GetSingleton<EnemyProjectileGridSingleton>();
                if (gridSingleton.Grid.IsCreated)
                {
                    gridSingleton.Grid.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<EnemyPoolSingleton>())
            {
                var pool = SystemAPI.GetSingleton<EnemyPoolSingleton>();
                if (pool.InactiveEnemies.IsCreated)
                {
                    pool.InactiveEnemies.Dispose();
                    pool.AllEnemies.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<PlayerProjectilePoolSingleton>())
            {
                var pool = SystemAPI.GetSingleton<PlayerProjectilePoolSingleton>();
                if (pool.InactiveProjectiles.IsCreated)
                {
                    pool.InactiveProjectiles.Dispose();
                    pool.AllProjectiles.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<EnemyProjectilePoolSingleton>())
            {
                var pool = SystemAPI.GetSingleton<EnemyProjectilePoolSingleton>();
                if (pool.InactiveProjectiles.IsCreated)
                {
                    pool.InactiveProjectiles.Dispose();
                    pool.AllProjectiles.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<ProjectileDeactivationQueueSingleton>())
            {
                var q = SystemAPI.GetSingleton<ProjectileDeactivationQueueSingleton>();
                if (q.StagedDeactivations.IsCreated)
                {
                    q.StagedDeactivations.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<DamageEventQueueSingleton>())
            {
                var q = SystemAPI.GetSingleton<DamageEventQueueSingleton>();
                if (q.DamageQueue.IsCreated)
                {
                    q.DamageQueue.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<PlayerDamageEventQueueSingleton>())
            {
                var q = SystemAPI.GetSingleton<PlayerDamageEventQueueSingleton>();
                if (q.PlayerDamageQueue.IsCreated)
                {
                    q.PlayerDamageQueue.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<GemSpawnQueueSingleton>())
            {
                var q = SystemAPI.GetSingleton<GemSpawnQueueSingleton>();
                if (q.SpawnQueue.IsCreated)
                {
                    q.SpawnQueue.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<GemPoolSingleton>())
            {
                var pool = SystemAPI.GetSingleton<GemPoolSingleton>();
                if (pool.FreeGems.IsCreated)
                {
                    pool.FreeGems.Dispose();
                }
                if (pool.AllGems.IsCreated)
                {
                    pool.AllGems.Dispose();
                }
                if (pool.FreeChests.IsCreated) pool.FreeChests.Dispose();
                if (pool.AllChests.IsCreated) pool.AllChests.Dispose();
            }

            if (SystemAPI.HasSingleton<SimulationBridgeQueuesSingleton>())
            {
                var bridge = SystemAPI.GetSingleton<SimulationBridgeQueuesSingleton>();
                if (bridge.DeathEventQueue.IsCreated) bridge.DeathEventQueue.Dispose();
                if (bridge.RebaseEventQueue.IsCreated) bridge.RebaseEventQueue.Dispose();
                if (bridge.HitReactionEventQueue.IsCreated) bridge.HitReactionEventQueue.Dispose();
                if (bridge.GemCollectEventQueue.IsCreated) bridge.GemCollectEventQueue.Dispose();
            }
        }
    }
}
