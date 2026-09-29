using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
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
            if (!SystemAPI.HasSingleton<PureDotsPrefabsSingleton>()) return;
            m_Initialized = true;

            var prefabs = SystemAPI.GetSingleton<PureDotsPrefabsSingleton>();
            var em = state.EntityManager;

            // 1. Viewport & Camera bounds singleton
            var cameraBoundsEntity = em.CreateEntity();
            em.AddComponentData(cameraBoundsEntity, new SimulationCameraBounds
            {
                CameraPosition = float2.zero,
                ViewportExtentY = 15.0f,
                DepthScale = 0.25f,
                ZMinOffset = -5.0f,
                ZMaxOffset = 5.0f
            });

            // 2. Floating origin config singleton
            var originEntity = em.CreateEntity();
            em.AddComponentData(originEntity, new FloatingOriginConfig
            {
                ThresholdSq = 4000000.0f // 2000m threshold
            });

            // 3. Wave spawner config singleton
            var spawnerEntity = em.CreateEntity();
            em.AddComponentData(spawnerEntity, new WaveSpawnerConfig
            {
                SpawnInterval = 0.5f,
                Timer = 0.0f,
                BatchSize = 35,
                MinRadius = 18.0f,
                MaxRadius = 40.0f,
                RandomSeed = 777123u
            });

            // 4. Enemy Config Catalog BlobAsset
            var builder = new BlobBuilder(Allocator.Temp);
            ref var catalogRoot = ref builder.ConstructRoot<EnemyConfigCatalog>();
            var configsArray = builder.Allocate(ref catalogRoot.Configs, 2);

            // Type 0: Tank ("Anvil")
            configsArray[0] = new EnemyConfigData
            {
                MaxHealth = 80.0f,
                MoveSpeed = 2.4f,
                CollisionRadius = 0.5f,
                VisualRadius = 0.6f,
                BaseDamage = 20.0f,
                InitialUV = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                ExperienceValue = 25,
                SpeedVariation = 0.1f
            };

            // Type 1: Runner ("Hammer") - 40% faster than player
            configsArray[1] = new EnemyConfigData
            {
                MaxHealth = 25.0f,
                MoveSpeed = 5.2f,
                CollisionRadius = 0.35f,
                VisualRadius = 0.4f,
                BaseDamage = 10.0f,
                InitialUV = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                ExperienceValue = 10,
                SpeedVariation = 0.2f
            };

            var catalogRef = builder.CreateBlobAssetReference<EnemyConfigCatalog>(Allocator.Persistent);
            builder.Dispose();

            var catalogEntity = em.CreateEntity();
            em.AddComponentData(catalogEntity, new EnemyConfigCatalogSingleton { Catalog = catalogRef });

            // 5. Spatial Hash Grids (2x over-provisioning for load factor <= 0.5)
            const int maxEnemies = 10000;
            const int maxProjectiles = 2000;

            var enemyGridEntity = em.CreateEntity();
            em.AddComponentData(enemyGridEntity, new EnemySpatialGridSingleton
            {
                Grid = new UnsafeParallelMultiHashMap<uint, GridEntry>(maxEnemies * 2, Allocator.Persistent)
            });

            var enemyProjGridEntity = em.CreateEntity();
            em.AddComponentData(enemyProjGridEntity, new EnemyProjectileGridSingleton
            {
                Grid = new UnsafeParallelMultiHashMap<uint, GridEntry>(maxProjectiles * 2, Allocator.Persistent)
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
                AllGems = new UnsafeList<GemSpatialRecord>(GemPoolSingleton.Capacity, Allocator.Persistent)
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
                em.SetComponentData(gem, LocalTransform.FromPosition(new float3(0, 0, 100)));
                em.SetComponentData(gem, new SpriteUVOffset { Value = new float4(0.5f, 0.5f, 0, 0) });
                em.SetComponentData(gem, new BaseColorOverride { Value = new float4(1, 1, 1, 1) });

                em.SetComponentEnabled<GemActiveTag>(gem, false);
                em.SetComponentEnabled<DisableRendering>(gem, true);
                em.SetComponentEnabled<MaterialMeshInfo>(gem, false);

                gemPoolSingleton.AllGems.Add(new GemSpatialRecord
                {
                    Entity = gem,
                    Position = float2.zero,
                    ExperienceValue = 0,
                    Tier = 0,
                    IsActive = 0,
                    Padding = 0.0f
                });

                gemPoolSingleton.FreeGems.Enqueue(gem);
            }
            preallocatedGems.Dispose();

            var gemPoolEntity = em.CreateEntity();
            em.AddComponentData(gemPoolEntity, gemPoolSingleton);

            // 9. Enemy Pool Preallocation
            var enemyPoolSingleton = new EnemyPoolSingleton
            {
                InactiveEnemies = new UnsafeQueue<Entity>(Allocator.Persistent)
            };

            var preallocatedEnemies = CollectionHelper.CreateNativeArray<Entity>(maxEnemies, Allocator.Temp);
            em.Instantiate(prefabs.EnemyPrefab, preallocatedEnemies);

            for (int i = 0; i < maxEnemies; i++)
            {
                Entity enemy = preallocatedEnemies[i];
                em.SetComponentData(enemy, LocalTransform.FromPosition(new float3(0, 0, 100)));
                em.SetComponentEnabled<EnemyActiveTag>(enemy, false);
                em.SetComponentEnabled<DisableRendering>(enemy, true);
                em.SetComponentEnabled<MaterialMeshInfo>(enemy, false);
                enemyPoolSingleton.InactiveEnemies.Enqueue(enemy);
            }
            preallocatedEnemies.Dispose();

            var enemyPoolEntity = em.CreateEntity();
            em.AddComponentData(enemyPoolEntity, enemyPoolSingleton);

            // 10. Projectile Pools Preallocation (Player & Enemy)
            var playerProjPoolSingleton = new PlayerProjectilePoolSingleton
            {
                InactiveProjectiles = new UnsafeQueue<Entity>(Allocator.Persistent)
            };

            var preallocatedPlayerProj = CollectionHelper.CreateNativeArray<Entity>(maxProjectiles, Allocator.Temp);
            em.Instantiate(prefabs.PlayerProjPrefab, preallocatedPlayerProj);
            for (int i = 0; i < maxProjectiles; i++)
            {
                Entity proj = preallocatedPlayerProj[i];
                em.SetComponentData(proj, LocalTransform.FromPosition(new float3(0, 0, 100)));
                em.SetComponentEnabled<ProjectileActiveTag>(proj, false);
                em.SetComponentEnabled<DisableRendering>(proj, true);
                em.SetComponentEnabled<MaterialMeshInfo>(proj, false);
                playerProjPoolSingleton.InactiveProjectiles.Enqueue(proj);
            }
            preallocatedPlayerProj.Dispose();

            var playerProjPoolEntity = em.CreateEntity();
            em.AddComponentData(playerProjPoolEntity, playerProjPoolSingleton);

            var enemyProjPoolSingleton = new EnemyProjectilePoolSingleton
            {
                InactiveProjectiles = new UnsafeQueue<Entity>(Allocator.Persistent)
            };

            var preallocatedEnemyProj = CollectionHelper.CreateNativeArray<Entity>(maxProjectiles, Allocator.Temp);
            em.Instantiate(prefabs.EnemyProjPrefab, preallocatedEnemyProj);
            for (int i = 0; i < maxProjectiles; i++)
            {
                Entity proj = preallocatedEnemyProj[i];
                em.SetComponentData(proj, LocalTransform.FromPosition(new float3(0, 0, 100)));
                em.SetComponentEnabled<ProjectileActiveTag>(proj, false);
                em.SetComponentEnabled<DisableRendering>(proj, true);
                em.SetComponentEnabled<MaterialMeshInfo>(proj, false);
                enemyProjPoolSingleton.InactiveProjectiles.Enqueue(proj);
            }
            preallocatedEnemyProj.Dispose();

            var enemyProjPoolEntity = em.CreateEntity();
            em.AddComponentData(enemyProjPoolEntity, enemyProjPoolSingleton);

            // 11. Instantiate Player Entity
            var player = em.Instantiate(prefabs.PlayerPrefab);
            em.SetComponentData(player, LocalTransform.FromPosition(float3.zero));
            em.SetComponentData(player, new MovementVelocity { Value = float2.zero });
            em.SetComponentData(player, new PlayerInputData { MoveInput = float2.zero });
            em.SetComponentData(player, new PlayerInvulnerability
            {
                Timer = 0.0f,
                InvulnerabilityDuration = 0.5f // 0.5s i-frame duration
            });
            em.SetComponentData(player, new PlayerStats
            {
                MoveSpeed = 6.0f,
                MagnetRadius = 4.0f,
                CurrentHealth = 100.0f,
                MaxHealth = 100.0f,
                Experience = 0,
                Level = 1,
                IsDead = 0
            });
            em.SetComponentEnabled<MaterialMeshInfo>(player, true);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
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
                }
            }

            if (SystemAPI.HasSingleton<PlayerProjectilePoolSingleton>())
            {
                var pool = SystemAPI.GetSingleton<PlayerProjectilePoolSingleton>();
                if (pool.InactiveProjectiles.IsCreated)
                {
                    pool.InactiveProjectiles.Dispose();
                }
            }

            if (SystemAPI.HasSingleton<EnemyProjectilePoolSingleton>())
            {
                var pool = SystemAPI.GetSingleton<EnemyProjectilePoolSingleton>();
                if (pool.InactiveProjectiles.IsCreated)
                {
                    pool.InactiveProjectiles.Dispose();
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
