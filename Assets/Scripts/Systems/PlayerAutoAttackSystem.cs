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
            if (run.Paused || run.AutoAttack == 0 || A.Stats[run.Player].IsDead != 0) return;
            var weapon = A.Weapons[run.Player];
            run.AttackTimer += Dt;
            bool firstDue = run.AttackTimer >= weapon.Interval;
            if (run.Loadout.Count == PlayerLoadout.Capacity)
                run.Loadout.SecondAttackTimer += Dt;
            bool secondDue = run.Loadout.Count == PlayerLoadout.Capacity && run.Loadout.SecondAttackTimer >= run.Loadout.SecondWeapon.Interval;
            if (firstDue || secondDue)
            {
                float range = math.max(firstDue ? weapon.Range : 0, secondDue ? run.Loadout.SecondWeapon.Range : 0);
                bool found = FindTarget(run, range, out float2 target, out float distance);
                if (firstDue) Attack(ref run, weapon, ref run.AttackTimer, found, target, distance);
                if (secondDue) Attack(ref run, run.Loadout.SecondWeapon, ref run.Loadout.SecondAttackTimer, found, target, distance);
            }
            A.Run[A.State] = run;
        }
        private bool FindTarget(in SimulationRunState run, float range, out float2 target, out float closest)
        {
            target = default; closest = range * range; ulong bestKey = ulong.MaxValue;
            if (run.ActiveEnemies == 0) return false;
            int2 min = SpatialHashUtils.QuantizeToCell(run.PlayerPosition - range), max = SpatialHashUtils.QuantizeToCell(run.PlayerPosition + range);
            double cells = ((double)max.x - min.x + 1) * ((double)max.y - min.y + 1);
            // A pool scan visits allocated slots, so use that cost rather than the active count.
            if (cells > A.EnemyPool.AllEnemies.Length)
            {
                for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
                {
                    Entity e = A.EnemyPool.AllEnemies[i];
                    if (A.Enemies.IsComponentEnabled(e))
                        ConsiderTarget(e, A.Transforms[e].Position.xy, run.PlayerPosition, ref closest, ref bestKey, ref target);
                }
            }
            else for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!A.Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(x, y), out var entry, out var iterator)) continue;
                    do
                    {
                        if (math.any(entry.CellCoord != new int2(x, y)) || !A.Enemies.IsComponentEnabled(entry.Entity)) continue;
                        ConsiderTarget(entry.Entity, entry.Position, run.PlayerPosition, ref closest, ref bestKey, ref target);
                    } while (A.Grid.TryGetNextValue(out entry, ref iterator));
                }
            return bestKey != ulong.MaxValue;
        }
        private static void ConsiderTarget(Entity entity, float2 position, float2 playerPosition, ref float closest, ref ulong bestKey, ref float2 target)
        {
            float distance = math.distancesq(position, playerPosition); ulong key = DamageEvent.CreateTargetKey(entity);
            if (distance < closest || distance == closest && key < bestKey)
            { closest = distance; bestKey = key; target = position; }
        }
        private void Attack(ref SimulationRunState run, PlayerWeapon weapon, ref float timer, bool found, float2 target, float distance)
        {
            timer -= weapon.Interval;
            if (!found || distance > weapon.Range * weapon.Range)
                target = run.PlayerPosition + new float2(math.cos(run.AngleCounter * CombatConstants.AutoAimAngleStep), math.sin(run.AngleCounter * CombatConstants.AutoAimAngleStep));
            float2 aim = math.normalizesafe(target - run.PlayerPosition, new float2(1, 0));
            WeaponFire.Fire(A, ref run, aim, weapon);
            run.AngleCounter++;
        }
    }

    public static class WeaponFire
    {
        public static void Fire(SimulationAccess a, ref SimulationRunState run, float2 aim, PlayerWeapon weapon)
        {
            int count = weapon.Type == WeaponType.Standard ? weapon.Count : 1;
            for (int i = 0; i < count; i++)
            {
                float2 direction = weapon.Type == WeaponType.Standard ? Direction(aim, i, weapon) : aim;
                Entity projectile = Projectile(a, ref run, direction, weapon);
                if (projectile == Entity.Null) break;
                if (weapon.Type == WeaponType.Explosive)
                {
                    a.Explosives[projectile] = new ExplosiveProjectile { BlastRadius = weapon.BlastRadius };
                    a.Explosives.SetComponentEnabled(projectile, true);
                }
                else if (weapon.Type == WeaponType.Laser)
                {
                    a.Velocities[projectile] = default;
                    a.Lasers[projectile] = new LaserBeam { Direction = aim, Length = weapon.Range, PendingHit = 1 };
                    a.Lasers.SetComponentEnabled(projectile, true);
                }
            }
        }
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
                Radius = weapon.Radius, RemainingLifetime = weapon.Lifetime, Color = weapon.Color,
                MaterialIndex = weapon.MaterialIndex, TextureScale = weapon.TextureScale, ActiveStepFraction = 1 };
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
