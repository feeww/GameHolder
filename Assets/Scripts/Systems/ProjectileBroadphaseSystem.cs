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
            if (run.Rewards.Active != 0) return;
            for (int i = 0; i < A.PlayerPool.AllProjectiles.Length; i++)
            {
                Entity projectile = A.PlayerPool.AllProjectiles[i];
                if (!A.Projectiles.IsComponentEnabled(projectile) || A.Explosives.IsComponentEnabled(projectile) ||
                    A.Lasers.IsComponentEnabled(projectile)) continue;
                float2 start = A.Previous[projectile].Value, end = A.Transforms[projectile].Position.xy;
                var data = A.ProjectileData[projectile];
                if (data.ActiveStepFraction <= 0 || !ProjectileCollision.FirstHit(A, start, end, data.Radius,
                    data.ActiveStepFraction, run, out Entity target, out _)) continue;
                A.Damage.Enqueue(new DamageEvent { TargetEntity = target, TargetKey = DamageEvent.CreateTargetKey(target), Damage = data.Damage });
                A.Deactivations.Enqueue(projectile);
            }
        }
    }

    public static class ProjectileCollision
    {
        public static bool FirstHit(SimulationAccess a, float2 start, float2 end, float radius, float stepFraction, SimulationRunState run,
            out Entity target, out float bestTime)
        {
            float padding = radius + run.MaxEnemyRadius + run.MaxEnemyStep;
            int2 min = SpatialHashUtils.QuantizeToCell(math.min(start, end) - padding);
            int2 max = SpatialHashUtils.QuantizeToCell(math.max(start, end) + padding);
            target = Entity.Null; bestTime = float.MaxValue; ulong bestKey = ulong.MaxValue;
            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!a.Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                    do
                    {
                        if (math.any(entry.CellCoord != new int2(x, y))) continue;
                        float2 targetEnd = math.lerp(entry.PreviousPosition, entry.Position, stepFraction);
                        if (!SweptCollision.TryHit(start - entry.PreviousPosition, end - targetEnd, radius + entry.Radius, out float time)) continue;
                        ulong key = DamageEvent.CreateTargetKey(entry.Entity);
                        if (time < bestTime || time == bestTime && key < bestKey)
                        { target = entry.Entity; bestTime = time; bestKey = key; }
                    } while (a.Grid.TryGetNextValue(out entry, ref iterator));
                }
            return target != Entity.Null;
        }
    }
}
