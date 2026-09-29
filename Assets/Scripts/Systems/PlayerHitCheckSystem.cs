using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(StochasticSeparationSystem))]
    public partial struct PlayerHitCheckSystem : ISystem
    {
        private ComponentLookup<ProjectileData> m_ProjectileDataLookup;
        private ComponentLookup<TypeId> m_TypeIdLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemySpatialGridSingleton>();
            state.RequireForUpdate<EnemyProjectileGridSingleton>();
            state.RequireForUpdate<PlayerDamageEventQueueSingleton>();
            state.RequireForUpdate<ProjectileDeactivationQueueSingleton>();
            state.RequireForUpdate<EnemyConfigCatalogSingleton>();

            m_ProjectileDataLookup = state.GetComponentLookup<ProjectileData>(true);
            m_TypeIdLookup = state.GetComponentLookup<TypeId>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float playerInvulnTimer = 0.0f;
            float2 playerPos = float2.zero;
            bool hasPlayer = false;

            foreach (var (transform, invuln) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PlayerInvulnerability>>().WithAll<PlayerTag>())
            {
                playerInvulnTimer = invuln.ValueRO.Timer;
                playerPos = transform.ValueRO.Position.xy;
                hasPlayer = true;
                break;
            }

            if (!hasPlayer) return;

            // Early exit if player is invulnerable: saves CPU cycles by skipping all grid queries
            if (playerInvulnTimer > 0.0f) return;

            state.CompleteDependency();

            m_ProjectileDataLookup.Update(ref state);
            m_TypeIdLookup.Update(ref state);

            var catalogRef = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            bool hasCatalog = catalogRef.IsCreated;

            var enemyGrid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;
            var enemyProjGrid = SystemAPI.GetSingleton<EnemyProjectileGridSingleton>().Grid;
            var playerDamageQueue = SystemAPI.GetSingleton<PlayerDamageEventQueueSingleton>().PlayerDamageQueue;
            var projectileDeactivationQueue = SystemAPI.GetSingleton<ProjectileDeactivationQueueSingleton>().StagedDeactivations;

            const float invCellSize = SimulationConstants.SpatialInvCellSize;
            const float playerRadius = SimulationConstants.PlayerCollisionRadius;
            const float enemyRadius = SimulationConstants.EnemyCollisionRadius;
            const float contactRadius = playerRadius + enemyRadius;
            const float contactRadiusSq = contactRadius * contactRadius;

            int2 playerCell = SpatialHashUtils.QuantizeToCell(playerPos, invCellSize);

            // 1. Check contact with enemy crowd units (9-cell neighborhood)
            bool meleeHit = false;
            for (int dy = -1; dy <= 1 && !meleeHit; ++dy)
            {
                for (int dx = -1; dx <= 1 && !meleeHit; ++dx)
                {
                    int2 targetCell = playerCell + new int2(dx, dy);
                    uint hash = SpatialHashUtils.ComputeHash(targetCell);

                    if (enemyGrid.TryGetFirstValue(hash, out GridEntry entry, out NativeParallelMultiHashMapIterator<uint> it))
                    {
                        do
                        {
                            if (math.all(entry.CellCoord == targetCell))
                            {
                                float2 diff = playerPos - entry.Position;
                                float distSq = math.lengthsq(diff);
                                if (distSq <= contactRadiusSq)
                                {
                                    float dist = math.sqrt(distSq);
                                    float2 hitDir = dist > 0.0001f ? (diff / dist) : new float2(0.0f, 1.0f);

                                    float damage = SimulationConstants.DefaultMeleeDamage;
                                    if (hasCatalog && m_TypeIdLookup.HasComponent(entry.Entity))
                                    {
                                        int typeIdx = (int)m_TypeIdLookup[entry.Entity].Value;
                                        ref var configs = ref catalogRef.Value.Configs;
                                        if (typeIdx >= 0 && typeIdx < configs.Length)
                                        {
                                            damage = configs[typeIdx].BaseDamage;
                                        }
                                    }

                                    playerDamageQueue.Enqueue(new PlayerDamageEvent
                                    {
                                        Damage = damage,
                                        HitDirection = hitDir,
                                        SourceEntity = entry.Entity
                                    });
                                    // Single melee contact is sufficient per frame due to i-frames
                                    meleeHit = true;
                                    break;
                                }
                            }
                        } while (enemyGrid.TryGetNextValue(out entry, ref it));
                    }
                }
            }

            // 2. Check contact with enemy projectiles (9-cell neighborhood)
            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int2 targetCell = playerCell + new int2(dx, dy);
                    uint hash = SpatialHashUtils.ComputeHash(targetCell);

                    if (enemyProjGrid.TryGetFirstValue(hash, out GridEntry entry, out NativeParallelMultiHashMapIterator<uint> it))
                    {
                        do
                        {
                            if (math.all(entry.CellCoord == targetCell))
                            {
                                float2 diff = playerPos - entry.Position;
                                float projRadius = SimulationConstants.EnemyProjectileRadius;
                                if (m_ProjectileDataLookup.HasComponent(entry.Entity))
                                {
                                    projRadius = m_ProjectileDataLookup[entry.Entity].Radius;
                                }
                                float combinedRadius = playerRadius + projRadius;
                                float distSq = math.lengthsq(diff);

                                if (distSq <= (combinedRadius * combinedRadius))
                                {
                                    float dist = math.sqrt(distSq);
                                    float2 hitDir = dist > 0.0001f ? (diff / dist) : new float2(0.0f, 1.0f);
                                    float damage = SimulationConstants.EnemyProjectileDamage;
                                    if (m_ProjectileDataLookup.HasComponent(entry.Entity))
                                    {
                                        damage = m_ProjectileDataLookup[entry.Entity].Damage;
                                    }

                                    playerDamageQueue.Enqueue(new PlayerDamageEvent
                                    {
                                        Damage = damage,
                                        HitDirection = hitDir,
                                        SourceEntity = entry.Entity
                                    });

                                    // Stage consumed projectile for centralized reclamation in Step 9
                                    projectileDeactivationQueue.Enqueue(entry.Entity);
                                }
                            }
                        } while (enemyProjGrid.TryGetNextValue(out entry, ref it));
                    }
                }
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
