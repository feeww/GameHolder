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
            run.WeaponReady = 0;
            if (run.AutoAttack == 0 || A.Stats[run.Player].IsDead != 0) { A.Run[A.State] = run; return; }
            var weapon = A.Weapons[run.Player];
            run.AttackTimer += Dt;
            if (run.AttackTimer < weapon.Interval) { A.Run[A.State] = run; return; }
            run.AttackTimer -= weapon.Interval;
            float closest = weapon.Range * weapon.Range; ulong bestKey = ulong.MaxValue;
            float2 target = run.PlayerPosition + new float2(math.cos(run.AngleCounter * CombatConstants.AutoAimAngleStep), math.sin(run.AngleCounter * CombatConstants.AutoAimAngleStep));
            float radius = weapon.Range;
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
            run.Aim = aim; run.WeaponReady = 1;
            if (weapon.Type == WeaponType.Standard)
            for (int i = 0; i < weapon.Count; i++)
            {
                float2 direction = WeaponFire.Direction(aim, i, weapon);
                if (WeaponFire.Projectile(A, ref run, direction, weapon) == Entity.Null) break;
            }
            run.AngleCounter++; A.Run[A.State] = run;
        }
    }

    public static class WeaponFire
    {
        public static Entity Projectile(SimulationAccess a, ref SimulationRunState run, float2 direction, PlayerWeapon weapon)
        {
            if (!a.PlayerPool.InactiveProjectiles.TryDequeue(out Entity projectile)) return Entity.Null;
            Activate(a, projectile, run.PlayerPosition, direction, weapon);
            run.PlayerProjectiles++;
            return projectile;
        }
        public static void Activate(SimulationAccess a, Entity projectile, float2 position, float2 direction, PlayerWeapon weapon)
        {
            a.Transforms[projectile] = LocalTransform.FromPosition(new float3(position, 0));
            a.Previous[projectile] = new PreviousPosition { Value = position };
            a.Velocities[projectile] = new MovementVelocity { Value = direction * weapon.Speed };
            a.ProjectileData[projectile] = new ProjectileData { Damage = weapon.Damage,
                Radius = weapon.Radius, RemainingLifetime = weapon.Lifetime, Color = weapon.Color, ActiveStepFraction = 1 };
            a.Explosives.SetComponentEnabled(projectile, false); a.Explosives[projectile] = default;
            a.Lasers.SetComponentEnabled(projectile, false); a.Lasers[projectile] = default;
            a.Projectiles.SetComponentEnabled(projectile, true);
        }
        public static float2 Direction(float2 aim, int index, PlayerWeapon weapon)
        {
            float angle = (index - (weapon.Count - 1) * .5f) * weapon.SpreadAngle;
            math.sincos(angle, out float sin, out float cos);
            return new float2(aim.x * cos - aim.y * sin, aim.x * sin + aim.y * cos);
        }
    }
}
