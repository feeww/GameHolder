using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

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
            if (A.Stats[run.Player].IsDead != 0) return;
            for (int i = 0; i < A.EnemyPool.AllEnemies.Length; i++)
            {
                Entity enemy = A.EnemyPool.AllEnemies[i];
                if (!A.Ranged.IsComponentEnabled(enemy)) continue;
                var cooldown = A.RangedCooldown[enemy]; cooldown.CooldownTimer -= Dt;
                uint type = A.Types[enemy].Value;
                bool sniper = type == SimulationConstants.EnemyRangedSniperTypeId;
                float2 position = A.Transforms[enemy].Position.xy;
                float2 delta = run.PlayerPosition - position;
                float range = A.Catalog.Value.Configs[(int)type].AttackRange;
                if (cooldown.CooldownTimer <= 0 && math.lengthsq(delta) <= range * range && A.EnemyProjectilePool.InactiveProjectiles.TryDequeue(out Entity projectile))
                {
                    // Seed from stable pool slot and tick, independent of job traversal or worker timing.
                    var random = Random.CreateFromIndex(math.hash(new uint2((uint)i, run.Tick)));
                    cooldown.CooldownTimer = (sniper ? SimulationConstants.RangedSniperAttackInterval : SimulationConstants.RangedSkirmisherAttackInterval) * random.NextFloat(.9f, 1.1f);
                    A.Transforms[projectile] = LocalTransform.FromPosition(new float3(position, 0));
                    A.Previous[projectile] = new PreviousPosition { Value = position };
                    A.Velocities[projectile] = new MovementVelocity { Value = math.normalizesafe(delta, new float2(1, 0)) *
                        (sniper ? SimulationConstants.RangedSniperProjectileSpeed : SimulationConstants.RangedSkirmisherProjectileSpeed) };
                    A.ProjectileData[projectile] = new ProjectileData
                    {
                        Damage = sniper ? SimulationConstants.RangedSniperProjectileDamage : SimulationConstants.RangedSkirmisherProjectileDamage,
                        Radius = sniper ? SimulationConstants.RangedSniperProjectileRadius : SimulationConstants.RangedSkirmisherProjectileRadius,
                        RemainingLifetime = sniper ? SimulationConstants.RangedSniperProjectileLifetime : SimulationConstants.RangedSkirmisherProjectileLifetime
                    };
                    A.Projectiles.SetComponentEnabled(projectile, true); run.EnemyProjectiles++;
                }
                A.RangedCooldown[enemy] = cooldown;
            }
            A.Run[A.State] = run;
        }
    }
}
