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
    public struct PlayerHitCheckJob : IJob
    {
        public SimulationAccess A;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (run.Paused) return;
            if (A.Stats[run.Player].IsDead != 0) return;
            bool vulnerable = run.GodMode == 0 && A.Invulnerability[run.Player].Timer <= 0;
            if (vulnerable)
            {
                Entity nearest = Entity.Null; float bestDistance = float.MaxValue; ulong bestKey = ulong.MaxValue;
                float range = run.PlayerCollisionRadius + run.MaxEnemyRadius + CombatConstants.MeleeContactReach;
                int2 min = SpatialHashUtils.QuantizeToCell(run.PlayerPosition - range), max = SpatialHashUtils.QuantizeToCell(run.PlayerPosition + range);
                for (int y = min.y; y <= max.y; y++)
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (!A.Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                        do
                        {
                            if (math.any(entry.CellCoord != new int2(x, y)) || A.MeleeCooldown[entry.Entity].CooldownTimer > 0) continue;
                            if (!(A.Catalog.Value.GetConfig(A.Types[entry.Entity]).BaseDamage > 0)) continue;
                            float distance = math.distancesq(run.PlayerPosition, entry.Position);
                            float radius = run.PlayerCollisionRadius + entry.Radius + CombatConstants.MeleeContactReach;
                            ulong key = DamageEvent.CreateTargetKey(entry.Entity);
                            if (distance <= radius * radius && (distance < bestDistance || distance == bestDistance && key < bestKey))
                            { nearest = entry.Entity; bestDistance = distance; bestKey = key; }
                        } while (A.Grid.TryGetNextValue(out entry, ref iterator));
                    }
                if (nearest != Entity.Null)
                {
                    var config = A.Catalog.Value.GetConfig(A.Types[nearest]);
                    A.MeleeCooldown[nearest] = new EnemyMeleeCooldown { CooldownTimer = config.ContactAttackInterval };
                    // Contact was sampled at the tick endpoint, after any earlier swept projectile hits.
                    A.PlayerDamage.Enqueue(new PlayerDamageEvent { SourceEntity = nearest,
                        Damage = config.BaseDamage,
                        HitDirection = math.normalizesafe(run.PlayerPosition - A.Transforms[nearest].Position.xy), HitTime = 1 });
                }
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(EnemyProjectileTag), typeof(ProjectileActiveTag))]
    public partial struct EnemyProjectileHitJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<SimulationRunState> RunState;
        [ReadOnly] public ComponentLookup<PlayerStats> Stats;
        [ReadOnly] public ComponentLookup<PlayerInvulnerability> Invulnerability;
        [ReadOnly] public ComponentLookup<ExplosiveProjectile> Explosives;
        [ReadOnly] public ComponentLookup<LaserBeam> Lasers;
        public UnsafeQueue<PlayerDamageEvent>.ParallelWriter Damage;
        public UnsafeQueue<Entity>.ParallelWriter Deactivations;
        public Entity State;
        public void Execute(Entity projectile, in LocalTransform transform, in PreviousPosition previous, in ProjectileData data)
        {
            var run = RunState[State];
            if (run.Paused || Stats[run.Player].IsDead != 0 || Explosives.IsComponentEnabled(projectile) ||
                Lasers.IsComponentEnabled(projectile) || data.ActiveStepFraction <= 0) return;
            float2 start = previous.Value - run.PreviousPlayerPosition;
            float2 end = transform.Position.xy - math.lerp(run.PreviousPlayerPosition, run.PlayerPosition, data.ActiveStepFraction);
            float radius = data.Radius + run.PlayerCollisionRadius;
            // This AABB includes the complete relative sweep, even during a long frame.
            if (math.any(math.min(start, end) > radius) || math.any(math.max(start, end) < -radius)) return;
            if (!SweptCollision.TryHit(start, end, radius, out float time)) return;
            Deactivations.Enqueue(projectile);
            if (run.GodMode == 0 && Invulnerability[run.Player].Timer <= 0)
                Damage.Enqueue(new PlayerDamageEvent { SourceEntity = projectile, Damage = data.Damage,
                    HitDirection = math.normalizesafe(-end), HitTime = time * data.ActiveStepFraction });
        }
    }
}
