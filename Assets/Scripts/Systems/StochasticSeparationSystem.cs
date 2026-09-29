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
    [UpdateAfter(typeof(SpatialGridRebuildSystem))]
    public partial struct StochasticSeparationSystem : ISystem
    {
        private uint m_FrameIndex;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemySpatialGridSingleton>();
            m_FrameIndex = 0;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_FrameIndex++;

            var grid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;

            float2 playerPos = float2.zero;
            foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
            {
                playerPos = transform.ValueRO.Position.xy;
                break;
            }

            const float tier1Radius = 22.0f;
            const float tier1RadiusSq = tier1Radius * tier1Radius;
            const float separationRadius = 0.8f;
            const float cellSize = SpatialGridRebuildSystem.CellSize;
            const float invCellSize = 1.0f / cellSize;

            var job = new StochasticSeparationJob
            {
                Grid = grid,
                FrameIndex = m_FrameIndex,
                PlayerPos = playerPos,
                Tier1RadiusSq = tier1RadiusSq,
                SeparationRadius = separationRadius,
                InvCellSize = invCellSize
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    [BurstCompile]
    [WithAll(typeof(EnemyActiveTag))]
    public partial struct StochasticSeparationJob : IJobEntity
    {
        [ReadOnly] public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;
        public uint FrameIndex;
        public float2 PlayerPos;
        public float Tier1RadiusSq;
        public float SeparationRadius;
        public float InvCellSize;

        public void Execute(
            Entity entity,
            ref SeparationCache cache,
            in LocalTransform transform)
        {
            float2 currentPos = transform.Position.xy;

            // Outside Tier 1: clear separation cache
            if (math.distancesq(currentPos, PlayerPos) > Tier1RadiusSq)
            {
                cache.Direction = float2.zero;
                cache.Weight = 0.0f;
                return;
            }

            // Deterministic 4-phase interleaving: only 25% of Tier 1 agents update per frame
            if ((((uint)entity.Index + FrameIndex) & 3u) != 0u)
            {
                // The remaining 75% retain their cached separation vector
                return;
            }

            const int MaxSeparationNeighbors = 4;
            float separationRadiusSq = SeparationRadius * SeparationRadius;
            float invSeparationRadius = 1.0f / SeparationRadius;

            int2 minCell = (int2)math.floor((currentPos - SeparationRadius) * InvCellSize);
            int2 maxCell = (int2)math.floor((currentPos + SeparationRadius) * InvCellSize);
            float2 separation = float2.zero;
            int count = 0;

            // Multi-cell neighborhood iteration over bounded interaction cells
            for (int cy = minCell.y; cy <= maxCell.y && count < MaxSeparationNeighbors; ++cy)
            {
                for (int cx = minCell.x; cx <= maxCell.x && count < MaxSeparationNeighbors; ++cx)
                {
                    uint queryHash = unchecked(((uint)cx * 73856093u) ^ ((uint)cy * 19349663u));
                    int2 targetCell = new int2(cx, cy);
                    if (Grid.TryGetFirstValue(queryHash, out GridEntry entry, out NativeParallelMultiHashMapIterator<uint> it))
                    {
                        do
                        {
                            if (math.all(entry.CellCoord == targetCell) && entry.Entity != entity)
                            {
                                float2 diff = currentPos - entry.Position;
                                float dSq = math.lengthsq(diff);
                                if (dSq < separationRadiusSq && dSq > 0.0001f)
                                {
                                    float invDist = math.rsqrt(dSq);
                                    float dist = dSq * invDist;
                                    separation += diff * (invDist * (1.0f - dist * invSeparationRadius));
                                    if (++count >= MaxSeparationNeighbors) break;
                                }
                            }
                        } while (Grid.TryGetNextValue(out entry, ref it));
                    }
                }
            }

            cache.Direction = count > 0 ? math.normalizesafe(separation) : float2.zero;
            cache.Weight = math.saturate((float)count / MaxSeparationNeighbors);
        }
    }
}
