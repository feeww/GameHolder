using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct PlayerHitCheckJob : IJob
    {
        public SimulationAccess A;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (A.Stats[run.Player].IsDead != 0) return;
            bool vulnerable = run.GodMode == 0 && A.Invulnerability[run.Player].Timer <= 0;
            if (vulnerable)
            {
                Entity nearest = Entity.Null; float bestDistance = float.MaxValue; ulong bestKey = ulong.MaxValue;
                float range = SimulationConstants.PlayerCollisionRadius + run.MaxEnemyRadius + SimulationConstants.MeleeContactReach;
                int2 min = SpatialHashUtils.QuantizeToCell(run.PlayerPosition - range), max = SpatialHashUtils.QuantizeToCell(run.PlayerPosition + range);
                for (int y = min.y; y <= max.y; y++)
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (!A.Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                        do
                        {
                            if (math.any(entry.CellCoord != new int2(x, y)) || A.MeleeCooldown[entry.Entity].CooldownTimer > 0) continue;
                            float distance = math.distancesq(run.PlayerPosition, entry.Position);
                            float radius = SimulationConstants.PlayerCollisionRadius + entry.Radius + SimulationConstants.MeleeContactReach;
                            ulong key = DamageEvent.CreateTargetKey(entry.Entity);
                            if (distance <= radius * radius && (distance < bestDistance || distance == bestDistance && key < bestKey))
                            { nearest = entry.Entity; bestDistance = distance; bestKey = key; }
                        } while (A.Grid.TryGetNextValue(out entry, ref iterator));
                    }
                if (nearest != Entity.Null)
                {
                    A.MeleeCooldown[nearest] = new EnemyMeleeCooldown { CooldownTimer = SimulationConstants.EnemyMeleeAttackCooldown };
                    A.PlayerDamage.Enqueue(new PlayerDamageEvent { SourceEntity = nearest,
                        Damage = A.Catalog.Value.Configs[(int)A.Types[nearest].Value].BaseDamage,
                        HitDirection = math.normalizesafe(run.PlayerPosition - A.Transforms[nearest].Position.xy), HitTime = 0 });
                }
            }
            for (int i = 0; i < A.EnemyProjectilePool.AllProjectiles.Length; i++)
            {
                Entity projectile = A.EnemyProjectilePool.AllProjectiles[i];
                if (!A.Projectiles.IsComponentEnabled(projectile)) continue;
                float2 start = A.Previous[projectile].Value - run.PreviousPlayerPosition;
                float2 end = A.Transforms[projectile].Position.xy - run.PlayerPosition;
                var data = A.ProjectileData[projectile];
                float radius = data.Radius + SimulationConstants.PlayerCollisionRadius;
                // This AABB includes the complete relative sweep, even during a long frame.
                if (math.any(math.min(start, end) > radius) || math.any(math.max(start, end) < -radius)) continue;
                if (!SweptCollision.TryHit(start, end, radius, out float time)) continue;
                A.Deactivations.Enqueue(projectile);
                if (vulnerable) A.PlayerDamage.Enqueue(new PlayerDamageEvent
                { SourceEntity = projectile, Damage = data.Damage, HitDirection = math.normalizesafe(-end), HitTime = time });
            }
        }
    }
}
