using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [WithAll(typeof(PlayerProjectileTag), typeof(ProjectileActiveTag))]
    public partial struct ProjectileBroadphaseJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<SimulationRunState> RunState;
        [ReadOnly] public ComponentLookup<ExplosiveProjectile> Explosives;
        [ReadOnly] public ComponentLookup<LaserBeam> Lasers;
        [ReadOnly] public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;
        public UnsafeQueue<DamageEvent>.ParallelWriter Damage;
        public UnsafeQueue<Entity>.ParallelWriter Deactivations;
        public Entity State;
        public void Execute(Entity projectile, in LocalTransform transform, in PreviousPosition previous, in ProjectileData data)
        {
            var run = RunState[State];
            if (run.Paused || Explosives.IsComponentEnabled(projectile) || Lasers.IsComponentEnabled(projectile)) return;
            if (data.ActiveStepFraction <= 0 || !ProjectileCollision.FirstHit(Grid, previous.Value, transform.Position.xy, data.Radius,
                data.ActiveStepFraction, run, out Entity target, out _)) return;
            Damage.Enqueue(new DamageEvent { TargetEntity = target, TargetKey = DamageEvent.CreateTargetKey(target), Damage = data.Damage });
            Deactivations.Enqueue(projectile);
        }
    }

    public static class ProjectileCollision
    {
        public static bool FirstHit(UnsafeParallelMultiHashMap<uint, GridEntry> grid, float2 start, float2 end, float radius, float stepFraction, SimulationRunState run,
            out Entity target, out float bestTime)
        {
            float padding = radius + run.MaxEnemyRadius + run.MaxEnemyStep;
            int2 min = SpatialHashUtils.QuantizeToCell(math.min(start, end) - padding);
            int2 max = SpatialHashUtils.QuantizeToCell(math.max(start, end) + padding);
            target = Entity.Null; bestTime = float.MaxValue; ulong bestKey = ulong.MaxValue;
            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                    do
                    {
                        if (math.any(entry.CellCoord != new int2(x, y))) continue;
                        float2 targetEnd = math.lerp(entry.PreviousPosition, entry.Position, stepFraction);
                        if (!SweptCollision.TryHit(start - entry.PreviousPosition, end - targetEnd, radius + entry.Radius, out float time)) continue;
                        ulong key = DamageEvent.CreateTargetKey(entry.Entity);
                        if (time < bestTime || time == bestTime && key < bestKey)
                        { target = entry.Entity; bestTime = time; bestKey = key; }
                    } while (grid.TryGetNextValue(out entry, ref iterator));
                }
            return target != Entity.Null;
        }
    }
}
