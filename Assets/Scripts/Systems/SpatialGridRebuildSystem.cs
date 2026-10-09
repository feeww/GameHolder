using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct RebuildSpatialGridJob : IJob
    {
        public SimulationAccess A;
        public bool BuildCrowdCells;
        public void Execute()
        {
            if (A.Run[A.State].Paused) return;
            A.Grid.Clear();
            if (BuildCrowdCells) A.CrowdCells.Clear();
            var run = A.Run[A.State];
            run.MaxEnemyStep = 0; run.MaxEnemyRadius = 0;
            // A single writer inserts in permanent pool order. Hash traversal is reproducible across worker counts.
            for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
            {
                var e = A.EnemyPool.AllEnemies[i];
                if (!A.Enemies.IsComponentEnabled(e)) continue;
                float2 position = A.Transforms[e].Position.xy;
                float2 previous = A.Previous[e].Value;
                var config = A.Catalog.Value.GetConfig(A.Types[e]);
                float radius = config.CollisionRadius;
                run.MaxEnemyStep = math.max(run.MaxEnemyStep, math.distance(position, previous));
                run.MaxEnemyRadius = math.max(run.MaxEnemyRadius, radius);
                int2 cell = SpatialHashUtils.QuantizeToCell(position);
                float priority = CrowdContact.PushPriority(config);
                A.Grid.Add(SpatialHashUtils.ComputeHash(cell), new GridEntry
                { Entity = e, Position = position, PreviousPosition = previous, CellCoord = cell, Radius = radius,
                    PushPriority = priority, PoolIndex = i });
                if (!BuildCrowdCells) continue;
                A.CrowdCells.TryGetValue(cell, out var crowd);
                crowd.Count++;
                A.CrowdCells[cell] = crowd;
                // Splat onto cell centres: density changes continuously as bodies cross cell boundaries.
                float2 gridPosition = position * SpatialHashUtils.InvCellSize - .5f;
                int2 corner = (int2)math.floor(gridPosition);
                float2 fraction = gridPosition - corner;
                for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                {
                    int2 node = corner + new int2(x, y);
                    A.CrowdCells.TryGetValue(node, out var sample);
                    float weight = (x == 0 ? 1 - fraction.x : fraction.x) * (y == 0 ? 1 - fraction.y : fraction.y);
                    sample.Density += weight;
                    sample.PrioritySum += priority * weight;
                    A.CrowdCells[node] = sample;
                }
            }
            A.Run[A.State] = run;
        }
    }
}
