using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct ProjectileBroadphaseJob : IJob
    {
        public SimulationAccess A;
        public void Execute()
        {
            var run = A.Run[A.State];
            for (int i = 0; i < A.PlayerPool.AllProjectiles.Length; i++)
            {
                Entity projectile = A.PlayerPool.AllProjectiles[i];
                if (!A.Projectiles.IsComponentEnabled(projectile)) continue;
                float2 start = A.Previous[projectile].Value, end = A.Transforms[projectile].Position.xy;
                var data = A.ProjectileData[projectile];
                float padding = data.Radius + run.MaxEnemyRadius + run.MaxEnemyStep;
                int2 min = SpatialHashUtils.QuantizeToCell(math.min(start, end) - padding);
                int2 max = SpatialHashUtils.QuantizeToCell(math.max(start, end) + padding);
                Entity target = Entity.Null; float bestTime = float.MaxValue; ulong bestKey = ulong.MaxValue;
                for (int y = min.y; y <= max.y; y++)
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (!A.Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                        do
                        {
                            if (math.any(entry.CellCoord != new int2(x, y))) continue;
                            if (!SweptCollision.TryHit(start - entry.PreviousPosition, end - entry.Position, data.Radius + entry.Radius, out float time)) continue;
                            ulong key = DamageEvent.CreateTargetKey(entry.Entity);
                            if (time < bestTime || time == bestTime && key < bestKey)
                            { target = entry.Entity; bestTime = time; bestKey = key; }
                        } while (A.Grid.TryGetNextValue(out entry, ref iterator));
                    }
                if (target == Entity.Null) continue;
                A.Damage.Enqueue(new DamageEvent { TargetEntity = target, TargetKey = bestKey, Damage = data.Damage });
                A.Deactivations.Enqueue(projectile);
            }
        }
    }
}