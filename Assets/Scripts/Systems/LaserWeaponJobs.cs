using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct LaserCombatJob : IJob
    {
        public SimulationAccess A;
        public NativeParallelHashMap<Entity, float> AreaDamage;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (run.Rewards.Active != 0) return;
            for (int i = 0; i < A.PlayerPool.AllProjectiles.Length; i++)
            {
                Entity projectile = A.PlayerPool.AllProjectiles[i];
                if (!A.Projectiles.IsComponentEnabled(projectile) || !A.Lasers.IsComponentEnabled(projectile)) continue;
                var beam = A.Lasers[projectile];
                if (beam.PendingHit == 0) continue;
                float2 start = A.Transforms[projectile].Position.xy, end = start + beam.Direction * beam.Length;
                var data = A.ProjectileData[projectile];
                if (data.ActiveStepFraction <= 0) continue;
                float padding = data.Radius + run.MaxEnemyRadius + (1 - data.ActiveStepFraction) * run.MaxEnemyStep;
                int2 min = SpatialHashUtils.QuantizeToCell(math.min(start, end) - padding);
                int2 max = SpatialHashUtils.QuantizeToCell(math.max(start, end) + padding);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!A.Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                    do
                    {
                        if (math.any(entry.CellCoord != new int2(x, y))) continue;
                        float2 targetPosition = math.lerp(entry.PreviousPosition, entry.Position, data.ActiveStepFraction);
                        if (!SweptCollision.TryHit(start - targetPosition, end - targetPosition, data.Radius + entry.Radius, out _)) continue;
                        AreaDamage.TryGetValue(entry.Entity, out float damage);
                        AreaDamage[entry.Entity] = damage + data.Damage;
                    } while (A.Grid.TryGetNextValue(out entry, ref iterator));
                }
                beam.PendingHit = 0; A.Lasers[projectile] = beam;
            }
            bool vulnerable = A.Stats[run.Player].IsDead == 0 && run.GodMode == 0 && A.Invulnerability[run.Player].Timer <= 0;
            for (int i = 0; i < A.EnemyProjectilePool.AllProjectiles.Length; i++)
            {
                Entity projectile = A.EnemyProjectilePool.AllProjectiles[i];
                if (!A.Projectiles.IsComponentEnabled(projectile) || !A.Lasers.IsComponentEnabled(projectile)) continue;
                var beam = A.Lasers[projectile];
                if (beam.PendingHit == 0) continue;
                float2 start = A.Transforms[projectile].Position.xy, end = start + beam.Direction * beam.Length;
                var data = A.ProjectileData[projectile];
                if (data.ActiveStepFraction <= 0) continue;
                float2 playerPosition = math.lerp(run.PreviousPlayerPosition, run.PlayerPosition, data.ActiveStepFraction);
                if (vulnerable && SweptCollision.TryHit(start - playerPosition, end - playerPosition,
                    data.Radius + run.PlayerCollisionRadius, out _))
                    A.PlayerDamage.Enqueue(new PlayerDamageEvent { SourceEntity = projectile, Damage = data.Damage,
                        HitDirection = math.normalizesafe(playerPosition - start), HitTime = data.ActiveStepFraction });
                beam.PendingHit = 0; A.Lasers[projectile] = beam;
            }
        }
    }
}
