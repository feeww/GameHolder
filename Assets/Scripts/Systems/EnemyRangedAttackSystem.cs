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
            if (run.Rewards.Active != 0) return;
            if (A.Stats[run.Player].IsDead != 0) return;
            for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
            {
                Entity enemy = A.EnemyPool.AllEnemies[i];
                if (!A.Ranged.IsComponentEnabled(enemy)) continue;
                var cooldown = A.RangedCooldown[enemy]; cooldown.CooldownTimer -= Dt;
                uint type = A.Types[enemy].Value;
                var config = A.Catalog.Value.Configs[(int)type];
                var weapon = config.Weapon;
                float2 position = A.Transforms[enemy].Position.xy;
                float2 delta = run.PlayerPosition - position;
                float range = config.AttackRange;
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
    }
}
