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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemySpatialGridSingleton>();
            state.RequireForUpdate<EnemyProjectileGridSingleton>();
            state.RequireForUpdate<PlayerDamageEventQueueSingleton>();
            state.RequireForUpdate<ProjectileDeactivationQueueSingleton>();

            m_ProjectileDataLookup = state.GetComponentLookup<ProjectileData>(true);
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

            var enemyGrid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;
            var enemyProjGrid = SystemAPI.GetSingleton<EnemyProjectileGridSingleton>().Grid;
            var playerDamageQueue = SystemAPI.GetSingleton<PlayerDamageEventQueueSingleton>().PlayerDamageQueue;
            var projectileDeactivationQueue = SystemAPI.GetSingleton<ProjectileDeactivationQueueSingleton>().StagedDeactivations;

            const float cellSize = SpatialGridRebuildSystem.CellSize;
            const float invCellSize = 1.0f / cellSize;
            const float playerRadius = 0.4f;
            const float enemyRadius = 0.4f;
            const float contactRadius = playerRadius + enemyRadius;
            const float contactRadiusSq = contactRadius * contactRadius;

            int2 playerCell = (int2)math.floor(playerPos * invCellSize);

            // 1. Check contact with enemy crowd units (9-cell neighborhood)
            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    int2 targetCell = playerCell + new int2(dx, dy);
                    uint hash = unchecked(((uint)targetCell.x * 73856093u) ^ ((uint)targetCell.y * 19349663u));

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

                                    playerDamageQueue.Enqueue(new PlayerDamageEvent
                                    {
                                        Damage = 10.0f, // Contact melee damage
                                        HitDirection = hitDir,
                                        SourceEntity = entry.Entity
                                    });
                                    // Single melee contact is sufficient per frame due to i-frames
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
                    uint hash = unchecked(((uint)targetCell.x * 73856093u) ^ ((uint)targetCell.y * 19349663u));

                    if (enemyProjGrid.TryGetFirstValue(hash, out GridEntry entry, out NativeParallelMultiHashMapIterator<uint> it))
                    {
                        do
                        {
                            if (math.all(entry.CellCoord == targetCell))
                            {
                                float2 diff = playerPos - entry.Position;
                                float projRadius = 0.25f;
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
                                    float damage = 15.0f;
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
