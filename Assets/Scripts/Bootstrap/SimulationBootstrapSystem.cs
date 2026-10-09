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
    public partial struct SimulationBootstrapSystem : ISystem
    {
        private bool m_Initialized;
        private AllocatorHelper<EventQueueAllocator> m_Damage, m_PlayerDamage, m_Deactivations, m_GemSpawns,
            m_Deaths, m_Rebases, m_Reactions, m_Collections, m_Commands;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_Initialized = false;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (m_Initialized) return;
            if (!SystemAPI.HasSingleton<StartingPlayerConfig>() ||
                !SystemAPI.HasSingleton<EnemyConfigCatalogSingleton>() || !SystemAPI.HasSingleton<RewardCatalogSingleton>()) return;
            var limits = SystemAPI.HasSingleton<PoolLimits>() ? SystemAPI.GetSingleton<PoolLimits>() : PoolLimits.Defaults;
            if (!limits.IsValid) throw new System.ArgumentException("Pool limits must be positive and fit simulation buffer capacities.");
            if (!SystemAPI.HasSingleton<PureDotsPrefabsSingleton>())
                state.EntityManager.AddComponentData(state.EntityManager.CreateEntity(), CreatePrefabs(state.EntityManager, SystemAPI.GetSingleton<StartingPlayerConfig>()));
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
            if (!SystemAPI.HasSingleton<WaveSpawnerConfig>())
                em.AddComponentData(em.CreateEntity(), RunDefaults.Wave);

            // Each enemy writes one grid entry and at most five crowd-cell keys.
            int maxEnemies = limits.MaxEnemies;

            var enemyGridEntity = em.CreateEntity();
            em.AddComponentData(enemyGridEntity, new EnemySpatialGridSingleton
            {
                Grid = new UnsafeParallelMultiHashMap<uint, GridEntry>(maxEnemies, Allocator.Persistent),
                CrowdCells = new UnsafeParallelHashMap<int2, CrowdCell>(maxEnemies * 5, Allocator.Persistent)
            });

            // 6. Combat queues
            var damageQueueEntity = em.CreateEntity();
            em.AddComponentData(damageQueueEntity, new DamageEventQueueSingleton
            {
                DamageQueue = EventQueueAllocator.CreateQueue<DamageEvent>(limits.MaxPlayerProjectiles + maxEnemies, true, out m_Damage)
            });

            var playerDamageQueueEntity = em.CreateEntity();
            em.AddComponentData(playerDamageQueueEntity, new PlayerDamageEventQueueSingleton
            {
                PlayerDamageQueue = EventQueueAllocator.CreateQueue<PlayerDamageEvent>(limits.MaxEnemyProjectiles + 1, true, out m_PlayerDamage)
            });

            var projDeactEntity = em.CreateEntity();
            em.AddComponentData(projDeactEntity, new ProjectileDeactivationQueueSingleton
            {
                StagedDeactivations = EventQueueAllocator.CreateQueue<Entity>((limits.MaxPlayerProjectiles + limits.MaxEnemyProjectiles) * 2, true, out m_Deactivations)
            });

            var gemSpawnQueueEntity = em.CreateEntity();
            em.AddComponentData(gemSpawnQueueEntity, new GemSpawnQueueSingleton
            {
                SpawnQueue = EventQueueAllocator.CreateQueue<GemSpawnRequest>(maxEnemies * 2, false, out m_GemSpawns)
            });

            // 7. Presentation bridge queues
            var bridgeEntity = em.CreateEntity();
            em.AddComponentData(bridgeEntity, new SimulationBridgeQueuesSingleton
            {
                DeathEventQueue = EventQueueAllocator.CreateQueue<DeathEvent>(SimulationConstants.CosmeticQueueCapacity, false, out m_Deaths),
                RebaseEventQueue = EventQueueAllocator.CreateQueue<OriginRebaseEvent>(SimulationConstants.CosmeticQueueCapacity, false, out m_Rebases),
                HitReactionEventQueue = EventQueueAllocator.CreateQueue<PlayerHitReactionEvent>(SimulationConstants.CosmeticQueueCapacity, false, out m_Reactions),
                GemCollectEventQueue = EventQueueAllocator.CreateQueue<GemCollectEvent>(SimulationConstants.CosmeticQueueCapacity, false, out m_Collections)
            });

            // 8. Experience gems and artifact chests
            var gemPoolSingleton = new GemPoolSingleton
            {
                FreeGems = new NativeRingQueue<Entity>(limits.MaxGems, Allocator.Persistent),
                AllGems = new UnsafeList<GemSpatialRecord>(limits.MaxGems, Allocator.Persistent),
                FreeChests = new NativeRingQueue<Entity>(GemPoolSingleton.ChestCapacity, Allocator.Persistent),
                AllChests = new UnsafeList<ArtifactChest>(GemPoolSingleton.ChestCapacity, Allocator.Persistent)
            };

            var preallocatedGems = CollectionHelper.CreateNativeArray<Entity>(limits.MaxGems, Allocator.Temp);
            em.Instantiate(prefabs.GemPrefab, preallocatedGems);

            for (int i = 0; i < limits.MaxGems; i++)
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
                InactiveEnemies = new NativeRingQueue<Entity>(maxEnemies, Allocator.Persistent),
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
                InactiveProjectiles = new NativeRingQueue<Entity>(limits.MaxPlayerProjectiles, Allocator.Persistent),
                AllProjectiles = new UnsafeList<Entity>(limits.MaxPlayerProjectiles, Allocator.Persistent)
            };

            var preallocatedPlayerProj = CollectionHelper.CreateNativeArray<Entity>(limits.MaxPlayerProjectiles, Allocator.Temp);
            em.Instantiate(prefabs.PlayerProjPrefab, preallocatedPlayerProj);
            for (int i = 0; i < limits.MaxPlayerProjectiles; i++)
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
                InactiveProjectiles = new NativeRingQueue<Entity>(limits.MaxEnemyProjectiles, Allocator.Persistent),
                AllProjectiles = new UnsafeList<Entity>(limits.MaxEnemyProjectiles, Allocator.Persistent)
            };

            var preallocatedEnemyProj = CollectionHelper.CreateNativeArray<Entity>(limits.MaxEnemyProjectiles, Allocator.Temp);
            em.Instantiate(prefabs.EnemyProjPrefab, preallocatedEnemyProj);
            for (int i = 0; i < limits.MaxEnemyProjectiles; i++)
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
            em.AddComponentData(em.CreateEntity(), new SimulationCommandQueue
            { Commands = EventQueueAllocator.CreateQueue<SimulationCommand>(SimulationConstants.CommandQueueCapacity, false, out m_Commands) });
        }

        public static PureDotsPrefabsSingleton CreatePrefabs(EntityManager em, StartingPlayerConfig startingPlayer)
        {
            var player = em.CreateEntity();
            em.AddComponent<Prefab>(player);
            em.AddComponentData(player, LocalTransform.Identity);
            em.AddComponent<PlayerTag>(player);
            em.AddComponent<MovementVelocity>(player);
            em.AddComponent<PreviousPosition>(player);
            em.AddComponent<PlayerInputData>(player);
            em.AddComponentData(player, new PlayerInvulnerability { InvulnerabilityDuration = startingPlayer.InvulnerabilityDuration });
            em.AddComponentData(player, startingPlayer.Stats);
            em.AddComponentData(player, startingPlayer.Weapon);

            var enemy = em.CreateEntity();
            em.AddComponent<Prefab>(enemy);
            em.AddComponentData(enemy, LocalTransform.Identity);
            em.AddComponent<MovementVelocity>(enemy);
            em.AddComponent<PreviousPosition>(enemy);
            em.AddComponent<SeparationCache>(enemy);
            em.AddComponent<CurrentHealth>(enemy);
            em.AddComponent<TypeId>(enemy);
            em.AddComponent<EnemyRangedCooldown>(enemy);
            em.AddComponent<EnemyMeleeCooldown>(enemy);
            em.AddComponent<EnemyActiveTag>(enemy);
            em.AddComponent<EnemyRangedTag>(enemy);

            var playerProjectile = CreateProjectilePrefab(em);
            em.AddComponent<PlayerProjectileTag>(playerProjectile);
            var enemyProjectile = CreateProjectilePrefab(em);
            em.AddComponent<EnemyProjectileTag>(enemyProjectile);

            var gem = em.CreateEntity();
            em.AddComponent<Prefab>(gem);
            em.AddComponentData(gem, LocalTransform.Identity);
            em.AddComponent<GemData>(gem);
            em.AddComponent<GemActiveTag>(gem);
            return new PureDotsPrefabsSingleton { PlayerPrefab = player, EnemyPrefab = enemy,
                PlayerProjPrefab = playerProjectile, EnemyProjPrefab = enemyProjectile, GemPrefab = gem };
        }

        private static Entity CreateProjectilePrefab(EntityManager em)
        {
            var projectile = em.CreateEntity();
            em.AddComponent<Prefab>(projectile);
            em.AddComponentData(projectile, LocalTransform.Identity);
            em.AddComponent<MovementVelocity>(projectile);
            em.AddComponent<PreviousPosition>(projectile);
            em.AddComponent<ProjectileData>(projectile);
            em.AddComponent<ProjectileActiveTag>(projectile);
            em.AddComponent<ExplosiveProjectile>(projectile);
            em.AddComponent<LaserBeam>(projectile);
            em.SetComponentEnabled<ExplosiveProjectile>(projectile, false);
            em.SetComponentEnabled<LaserBeam>(projectile, false);
            return projectile;
        }

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
            if (m_Initialized)
            {
                DisposeAllocator(m_Damage); DisposeAllocator(m_PlayerDamage); DisposeAllocator(m_Deactivations);
                DisposeAllocator(m_GemSpawns); DisposeAllocator(m_Deaths); DisposeAllocator(m_Rebases);
                DisposeAllocator(m_Reactions); DisposeAllocator(m_Collections); DisposeAllocator(m_Commands);
            }
        }

        private static void DisposeAllocator(AllocatorHelper<EventQueueAllocator> owner)
        {
            owner.Allocator.Dispose();
            owner.Dispose();
        }
    }
}
