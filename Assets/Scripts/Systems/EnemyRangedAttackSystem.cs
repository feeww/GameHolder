using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct EnemyRangedAttackJob : IJob
    {
        public SimulationAccess A;
        public float Dt;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (run.Paused) return;
            bool playerDead = A.Stats[run.Player].IsDead != 0;
            for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
            {
                Entity enemy = A.EnemyPool.AllEnemies[i];
                if (!A.Ranged.IsComponentEnabled(enemy)) continue;
                var cooldown = A.RangedCooldown[enemy]; cooldown.CooldownTimer -= Dt;
                var config = A.Catalog.Value.GetConfig(A.Types[enemy]);
                var weapon = config.Weapon;
                float2 position = A.Transforms[enemy].Position.xy;
                float2 delta = run.PlayerPosition - position;
                float range = config.AttackRange;
                if (weapon.Type == WeaponType.Laser)
                {
                    ChargeLaser(ref run, ref cooldown, weapon, position, delta, range, playerDead, i);
                    A.RangedCooldown[enemy] = cooldown;
                    continue;
                }
                if (playerDead) continue;
                if (cooldown.CooldownTimer <= 0 && math.lengthsq(delta) <= range * range)
                {
                    float2 aim = math.normalizesafe(delta, new float2(1, 0));
                    int count = weapon.Type == WeaponType.Standard ? weapon.Count : 1;
                    for (int shot = 0; shot < count && A.EnemyProjectilePool.InactiveProjectiles.TryDequeue(out Entity projectile); shot++)
                    {
                        float2 direction = count > 1 ? WeaponFire.Direction(aim, shot, weapon) : aim;
                        WeaponFire.Activate(A, projectile, position, direction, weapon);
                        A.Explosives[projectile] = new ExplosiveProjectile { BlastRadius = weapon.BlastRadius };
                        A.Explosives.SetComponentEnabled(projectile, weapon.Type == WeaponType.Explosive);
                        A.Lasers[projectile] = new LaserBeam { Direction = direction, Length = weapon.Range, PendingHit = 1 };
                        A.Lasers.SetComponentEnabled(projectile, weapon.Type == WeaponType.Laser);
                        A.Velocities[projectile] = new MovementVelocity { Value = math.select(direction * weapon.Speed, float2.zero, weapon.Type == WeaponType.Laser) };
                        run.EnemyProjectiles++;
                        // Stable pool-slot seed keeps cooldown jitter independent of worker timing.
                        var random = Random.CreateFromIndex(math.hash(new uint2((uint)i, run.Tick)));
                        cooldown.CooldownTimer = weapon.Interval * random.NextFloat(CombatConstants.CooldownMinScale, CombatConstants.CooldownMaxScale);
                    }
                }
                A.RangedCooldown[enemy] = cooldown;
            }
            A.Run[A.State] = run;
        }

        private void ChargeLaser(ref SimulationRunState run, ref EnemyRangedCooldown cooldown,
            PlayerWeapon weapon, float2 position, float2 delta, float range, bool playerDead, int slot)
        {
            range = math.min(range, CombatConstants.EnemyLaserMaximumRange);
            float distanceSq = math.lengthsq(delta);
            if (playerDead || distanceSq <= CombatConstants.EnemyLaserMinimumRange * CombatConstants.EnemyLaserMinimumRange ||
                distanceSq > range * range)
            {
                if (cooldown.ChargeBeam != Entity.Null)
                {
                    A.Projectiles.SetComponentEnabled(cooldown.ChargeBeam, false);
                    A.Lasers.SetComponentEnabled(cooldown.ChargeBeam, false);
                    A.EnemyProjectilePool.InactiveProjectiles.Enqueue(cooldown.ChargeBeam);
                    run.EnemyProjectiles--;
                    cooldown.ChargeBeam = Entity.Null; cooldown.ChargeTimer = 0;
                }
                return;
            }
            if (cooldown.ChargeBeam == Entity.Null)
            {
                if (cooldown.CooldownTimer > 0 || !A.EnemyProjectilePool.InactiveProjectiles.TryDequeue(out Entity projectile)) return;
                float2 aim = math.normalizesafe(delta, new float2(1, 0));
                var telegraph = weapon; telegraph.Damage = 0;
                WeaponFire.Activate(A, projectile, position, aim, telegraph);
                A.Velocities[projectile] = default;
                A.Lasers[projectile] = new LaserBeam { Direction = aim, Length = weapon.Range, Charging = 1 };
                A.Lasers.SetComponentEnabled(projectile, true);
                cooldown.ChargeBeam = projectile; cooldown.ChargeTimer = CombatConstants.EnemyLaserChargeDuration;
                run.EnemyProjectiles++;
                return;
            }
            Entity beam = cooldown.ChargeBeam;
            var transform = A.Transforms[beam]; transform.Position.xy = position; A.Transforms[beam] = transform;
            cooldown.ChargeTimer -= Dt;
            if (cooldown.ChargeTimer > NumericalConstants.MinimumDivisor) return;
            // Fire along the advertised direction so the player can dodge during the charge.
            float2 direction = A.Lasers[beam].Direction;
            WeaponFire.Activate(A, beam, position, direction, weapon);
            A.Velocities[beam] = default;
            A.Lasers[beam] = new LaserBeam { Direction = direction, Length = weapon.Range, PendingHit = 1 };
            A.Lasers.SetComponentEnabled(beam, true);
            cooldown.ChargeBeam = Entity.Null; cooldown.ChargeTimer = 0;
            var random = Random.CreateFromIndex(math.hash(new uint2((uint)slot, run.Tick)));
            cooldown.CooldownTimer = weapon.Interval * random.NextFloat(CombatConstants.CooldownMinScale, CombatConstants.CooldownMaxScale);
        }
    }
}
