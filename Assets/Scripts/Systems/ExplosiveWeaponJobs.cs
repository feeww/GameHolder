using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct ExplosiveCombatJob : IJob
    {
        public SimulationAccess A;
        public NativeParallelHashMap<Entity, float> AreaDamage;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (run.Paused) return;
            for (int i = 0; i < A.PlayerPool.AllProjectiles.Length; i++)
            {
                Entity projectile = A.PlayerPool.AllProjectiles[i];
                if (!A.Projectiles.IsComponentEnabled(projectile) || !A.Explosives.IsComponentEnabled(projectile)) continue;
                var explosive = A.Explosives[projectile];
                if (explosive.Detonated != 0) continue;
                var data = A.ProjectileData[projectile];
                float2 start = A.Previous[projectile].Value, end = A.Transforms[projectile].Position.xy;
                bool hit = ProjectileCollision.FirstHit(A.Grid, start, end, data.Radius, data.ActiveStepFraction, run, out _, out float time);
                if (!hit && data.RemainingLifetime > 0) continue;
                time = hit ? time : 1;
                float2 center = math.lerp(start, end, time);
                float padding = explosive.BlastRadius + run.MaxEnemyRadius + run.MaxEnemyStep;
                int2 min = SpatialHashUtils.QuantizeToCell(center - padding), max = SpatialHashUtils.QuantizeToCell(center + padding);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!A.Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                    do
                    {
                        if (math.any(entry.CellCoord != new int2(x, y))) continue;
                        float radius = explosive.BlastRadius + entry.Radius;
                        if (math.distancesq(center, math.lerp(entry.PreviousPosition, entry.Position, time * data.ActiveStepFraction)) > radius * radius) continue;
                        AreaDamage.TryGetValue(entry.Entity, out float damage);
                        AreaDamage[entry.Entity] = damage + data.Damage;
                    } while (A.Grid.TryGetNextValue(out entry, ref iterator));
                }
                Detonate(A, projectile, center);
            }
            bool vulnerable = A.Stats[run.Player].IsDead == 0 && run.GodMode == 0 && A.Invulnerability[run.Player].Timer <= 0;
            for (int i = 0; i < A.EnemyProjectilePool.AllProjectiles.Length; i++)
            {
                Entity projectile = A.EnemyProjectilePool.AllProjectiles[i];
                if (!A.Projectiles.IsComponentEnabled(projectile) || !A.Explosives.IsComponentEnabled(projectile)) continue;
                var explosive = A.Explosives[projectile];
                if (explosive.Detonated != 0) continue;
                var data = A.ProjectileData[projectile];
                float2 start = A.Previous[projectile].Value, end = A.Transforms[projectile].Position.xy;
                float2 playerEnd = math.lerp(run.PreviousPlayerPosition, run.PlayerPosition, data.ActiveStepFraction);
                bool hit = SweptCollision.TryHit(start - run.PreviousPlayerPosition, end - playerEnd,
                    data.Radius + run.PlayerCollisionRadius, out float time);
                if (!hit && data.RemainingLifetime > 0) continue;
                time = hit ? time : 1;
                float2 center = math.lerp(start, end, time);
                float hitTime = time * data.ActiveStepFraction;
                float2 offset = math.lerp(run.PreviousPlayerPosition, run.PlayerPosition, hitTime) - center;
                float radius = explosive.BlastRadius + run.PlayerCollisionRadius;
                if (vulnerable && math.lengthsq(offset) <= radius * radius)
                    A.PlayerDamage.Enqueue(new PlayerDamageEvent { SourceEntity = projectile, Damage = data.Damage,
                        HitDirection = math.normalizesafe(offset), HitTime = hitTime });
                Detonate(A, projectile, center);
            }
        }
        private static void Detonate(SimulationAccess a, Entity projectile, float2 center)
        {
            var explosive = a.Explosives[projectile]; explosive.Detonated = 1; a.Explosives[projectile] = explosive;
            a.Transforms[projectile] = LocalTransform.FromPosition(new float3(center, 0));
            a.Velocities[projectile] = default;
            // Keep the pooled sprite briefly as the blast visual; damage is applied only once.
            var data = a.ProjectileData[projectile]; data.RemainingLifetime = CombatConstants.BlastVisualLifetime; a.ProjectileData[projectile] = data;
        }
    }
}
