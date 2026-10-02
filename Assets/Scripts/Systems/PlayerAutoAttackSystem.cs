using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct PlayerAutoAttackJob : IJob
    {
        public SimulationAccess A;
        public float Dt;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (run.AutoAttack == 0 || A.Stats[run.Player].IsDead != 0) return;
            run.AttackTimer += Dt;
            if (run.AttackTimer < SimulationConstants.AttackInterval) { A.Run[A.State] = run; return; }
            run.AttackTimer -= SimulationConstants.AttackInterval;
            float closest = SimulationConstants.AttackSearchRadiusSq; ulong bestKey = ulong.MaxValue;
            float2 target = run.PlayerPosition + new float2(math.cos(run.AngleCounter * .37f), math.sin(run.AngleCounter * .37f));
            float radius = SimulationConstants.AttackSearchRadius;
            int2 min = SpatialHashUtils.QuantizeToCell(run.PlayerPosition - radius), max = SpatialHashUtils.QuantizeToCell(run.PlayerPosition + radius);
            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!A.Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                    do
                    {
                        if (math.any(entry.CellCoord != new int2(x, y)) || !A.Enemies.IsComponentEnabled(entry.Entity)) continue;
                        float distance = math.distancesq(entry.Position, run.PlayerPosition); ulong key = DamageEvent.CreateTargetKey(entry.Entity);
                        if (distance < closest || distance == closest && key < bestKey)
                        { closest = distance; bestKey = key; target = entry.Position; }
                    } while (A.Grid.TryGetNextValue(out entry, ref iterator));
                }
            float2 aim = math.normalizesafe(target - run.PlayerPosition, new float2(1, 0));
            for (int i = 0; i < SimulationConstants.ProjectileSpreadCount && A.PlayerPool.InactiveProjectiles.TryDequeue(out Entity projectile); i++)
            {
                float angle = (i - 1) * SimulationConstants.ProjectileSpreadAngle;
                float2 direction = new float2(aim.x * math.cos(angle) - aim.y * math.sin(angle), aim.x * math.sin(angle) + aim.y * math.cos(angle));
                A.Transforms[projectile] = LocalTransform.FromPosition(new float3(run.PlayerPosition, 0));
                A.Previous[projectile] = new PreviousPosition { Value = run.PlayerPosition };
                A.Velocities[projectile] = new MovementVelocity { Value = direction * SimulationConstants.ProjectileSpeed };
                A.ProjectileData[projectile] = new ProjectileData { Damage = SimulationConstants.PlayerProjectileDamage,
                    Radius = SimulationConstants.PlayerProjectileRadius, RemainingLifetime = SimulationConstants.PlayerProjectileLifetime };
                A.Projectiles.SetComponentEnabled(projectile, true); run.PlayerProjectiles++;
            }
            run.AngleCounter++; A.Run[A.State] = run;
        }
    }
}