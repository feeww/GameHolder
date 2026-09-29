using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementAndCameraRelativeZSystem))]
    public partial struct SpatialGridRebuildSystem : ISystem
    {
        public const float CellSize = SimulationConstants.SpatialCellSize; // Satisfies CellSize >= R_target + R_querier
        public const float InvCellSize = SimulationConstants.SpatialInvCellSize;
        public const float Tier1Radius = SimulationConstants.Tier1Radius;
        public const float Tier1RadiusSq = SimulationConstants.Tier1RadiusSq;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemySpatialGridSingleton>();
            state.RequireForUpdate<EnemyProjectileGridSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var enemyGrid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;
            var enemyProjGrid = SystemAPI.GetSingleton<EnemyProjectileGridSingleton>().Grid;

            float2 playerPos = float2.zero;
            foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
            {
                playerPos = transform.ValueRO.Position.xy;
                break;
            }

            // Asynchronous zero-sync clearing of both grids on worker threads
            var clearEnemyJob = new ClearGridJob { Grid = enemyGrid };
            JobHandle clearEnemyHandle = clearEnemyJob.Schedule(state.Dependency);

            var clearProjJob = new ClearGridJob { Grid = enemyProjGrid };
            JobHandle clearProjHandle = clearProjJob.Schedule(state.Dependency);

            // Chained population jobs
            var popEnemyJob = new PopulateEnemySpatialGridJob
            {
                Writer = enemyGrid.AsParallelWriter(),
                InvCellSize = InvCellSize,
                PlayerPos = playerPos,
                Tier1RadiusSq = Tier1RadiusSq
            };
            JobHandle popEnemyHandle = popEnemyJob.ScheduleParallel(clearEnemyHandle);

            var popProjJob = new PopulateProjectileSpatialGridJob
            {
                Writer = enemyProjGrid.AsParallelWriter(),
                InvCellSize = InvCellSize
            };
            JobHandle popProjHandle = popProjJob.ScheduleParallel(clearProjHandle);

            // Combine dependencies without main-thread stalls
            state.Dependency = JobHandle.CombineDependencies(popEnemyHandle, popProjHandle);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    [BurstCompile]
    public struct ClearGridJob : IJob
    {
        public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;

        public void Execute()
        {
            Grid.Clear();
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [WithAll(typeof(EnemyActiveTag))]
    public partial struct PopulateEnemySpatialGridJob : IJobEntity
    {
        public UnsafeParallelMultiHashMap<uint, GridEntry>.ParallelWriter Writer;
        public float InvCellSize;
        public float2 PlayerPos;
        public float Tier1RadiusSq;

        public void Execute(Entity entity, in LocalTransform transform)
        {
            float2 pos = transform.Position.xy;
            // Only Tier 1 entities register into the spatial grid
            if (math.distancesq(pos, PlayerPos) > Tier1RadiusSq) return;

            int2 cell = SpatialHashUtils.QuantizeToCell(pos, InvCellSize);
            uint hash = SpatialHashUtils.ComputeHash(cell);

            GridEntry entry = new GridEntry
            {
                Entity = entity,
                Position = pos,
                CellCoord = cell,
                Padding = float2.zero
            };

            Writer.Add(hash, entry);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [WithAll(typeof(ProjectileActiveTag), typeof(EnemyProjectileTag))]
    public partial struct PopulateProjectileSpatialGridJob : IJobEntity
    {
        public UnsafeParallelMultiHashMap<uint, GridEntry>.ParallelWriter Writer;
        public float InvCellSize;

        public void Execute(Entity entity, in LocalTransform transform)
        {
            float2 pos = transform.Position.xy;
            int2 cell = SpatialHashUtils.QuantizeToCell(pos, InvCellSize);
            uint hash = SpatialHashUtils.ComputeHash(cell);

            GridEntry entry = new GridEntry
            {
                Entity = entity,
                Position = pos,
                CellCoord = cell,
                Padding = float2.zero
            };

            Writer.Add(hash, entry);
        }
    }
}
