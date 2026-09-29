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
    [UpdateAfter(typeof(PlayerHitCheckSystem))]
    public partial struct ProjectileBroadphaseSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemySpatialGridSingleton>();
            state.RequireForUpdate<DamageEventQueueSingleton>();
            state.RequireForUpdate<ProjectileDeactivationQueueSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var enemyGrid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;
            var damageEventQueue = SystemAPI.GetSingleton<DamageEventQueueSingleton>().DamageQueue;
            var deactivationQueue = SystemAPI.GetSingleton<ProjectileDeactivationQueueSingleton>().StagedDeactivations;

            const float cellSize = SpatialGridRebuildSystem.CellSize;
            const float invCellSize = 1.0f / cellSize;
            const float maxTargetRadius = 0.5f;

            var job = new PlayerProjectileBroadphaseJob
            {
                EnemyGrid = enemyGrid,
                DamageQueue = damageEventQueue.AsParallelWriter(),
                DeactivationQueue = deactivationQueue.AsParallelWriter(),
                InvCellSize = invCellSize,
                MaxTargetRadius = maxTargetRadius
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    [BurstCompile]
    [WithAll(typeof(ProjectileActiveTag), typeof(PlayerProjectileTag))]
    public partial struct PlayerProjectileBroadphaseJob : IJobEntity
    {
        [ReadOnly] public UnsafeParallelMultiHashMap<uint, GridEntry> EnemyGrid;
        public UnsafeQueue<DamageEvent>.ParallelWriter DamageQueue;
        public UnsafeQueue<Entity>.ParallelWriter DeactivationQueue;
        public float InvCellSize;
        public float MaxTargetRadius;

        public void Execute(
            Entity entity,
            in LocalTransform transform,
            in ProjectileData projectileData)
        {
            float2 projPos = transform.Position.xy;
            float r = projectileData.Radius;
            float queryEnvelope = r + MaxTargetRadius;

            int2 minCell = (int2)math.floor((projPos - queryEnvelope) * InvCellSize);
            int2 maxCell = (int2)math.floor((projPos + queryEnvelope) * InvCellSize);

            bool hitAny = false;

            for (int cy = minCell.y; cy <= maxCell.y; ++cy)
            {
                for (int cx = minCell.x; cx <= maxCell.x; ++cx)
                {
                    uint hash = unchecked(((uint)cx * 73856093u) ^ ((uint)cy * 19349663u));
                    int2 targetCell = new int2(cx, cy);

                    if (EnemyGrid.TryGetFirstValue(hash, out GridEntry entry, out NativeParallelMultiHashMapIterator<uint> it))
                    {
                        do
                        {
                            if (math.all(entry.CellCoord == targetCell))
                            {
                                float2 diff = projPos - entry.Position;
                                float totalRadius = r + MaxTargetRadius;
                                if (math.lengthsq(diff) <= (totalRadius * totalRadius))
                                {
                                    // Precalculated 64-bit target key: Index in upper 32 bits, Version in lower 32 bits
                                    ulong targetKey = (((ulong)(uint)entry.Entity.Index) << 32) | (ulong)(uint)entry.Entity.Version;

                                    DamageQueue.Enqueue(new DamageEvent
                                    {
                                        TargetKey = targetKey,
                                        TargetEntity = entry.Entity,
                                        Damage = projectileData.Damage,
                                        HitFlags = 0,
                                        Padding = float2.zero
                                    });

                                    hitAny = true;
                                    // For single-target projectiles, consume on first target
                                    break;
                                }
                            }
                        } while (EnemyGrid.TryGetNextValue(out entry, ref it));
                    }

                    if (hitAny) break;
                }

                if (hitAny) break;
            }

            if (hitAny)
            {
                // Stage consumed projectile for centralized idempotent reclamation in Step 9
                DeactivationQueue.Enqueue(entity);
            }
        }
    }
}
