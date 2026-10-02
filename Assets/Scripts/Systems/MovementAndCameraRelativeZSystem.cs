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
    public struct MoveEnemiesJob : IJobParallelFor
    {
        [ReadOnly] public UnsafeList<Entity> Entities;
        [ReadOnly] public ComponentLookup<EnemyActiveTag> Active;
        [ReadOnly] public ComponentLookup<TypeId> Types;
        [ReadOnly] public ComponentLookup<SimulationRunState> Run;
        public Entity State;
        public BlobAssetReference<EnemyConfigCatalog> Catalog;
        public float Dt;
        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
        [NativeDisableParallelForRestriction] public ComponentLookup<PreviousPosition> Previous;
        [NativeDisableParallelForRestriction] public ComponentLookup<MovementVelocity> Velocities;
        [NativeDisableParallelForRestriction] public ComponentLookup<SeparationCache> Separation;
        [NativeDisableParallelForRestriction] public ComponentLookup<EnemyMeleeCooldown> Cooldown;
        public void Execute(int index)
        {
            Entity e = Entities[index];
            if (!Active.IsComponentEnabled(e)) return;
            var transform = Transforms[e];
            Previous[e] = new PreviousPosition { Value = transform.Position.xy };
            var cooldown = Cooldown[e]; cooldown.CooldownTimer = math.max(0, cooldown.CooldownTimer - Dt); Cooldown[e] = cooldown;
            float2 toPlayer = Run[State].PlayerPosition - transform.Position.xy;
            float distanceSq = math.lengthsq(toPlayer);
            float distance = math.sqrt(distanceSq);
            float2 direction = math.normalizesafe(toPlayer);
            uint type = Types[e].Value;
            var config = Catalog.Value.Configs[(int)type];
            float baseSpeed = config.MoveSpeed;
            var separation = Separation[e];
            float speed = baseSpeed;
            if (distanceSq > SimulationConstants.Tier1RadiusSq)
            {
                separation = default;
                float t = math.saturate((distanceSq - SimulationConstants.Tier1RadiusSq) /
                    (SimulationConstants.Tier2MaxRadiusSq - SimulationConstants.Tier1RadiusSq));
                speed *= math.lerp(1, SimulationConstants.MaxCatchUpMultiplier, t);
            }
            else
            {
                if (type == SimulationConstants.EnemyRangedSkirmisherTypeId && distanceSq < SimulationConstants.RangedSkirmisherRetreatRangeSq)
                {
                    direction = -direction;
                    speed = math.min(speed, (SimulationConstants.RangedSkirmisherRetreatRange - distance) / math.max(Dt, 1e-6f));
                }
                else speed = math.min(speed, math.max(0, distance - config.AttackRange) / math.max(Dt, 1e-6f));
                speed *= math.min(1, SimulationConstants.CrowdTargetDensity / math.max(SimulationConstants.CrowdTargetDensity, separation.Density));
            }
            // Blocked approaches split around the player instead of continually driving into the centre.
            float2 tangent = new float2(-direction.y, direction.x);
            float side = math.dot(separation.Direction, tangent);
            if (math.abs(side) < .5f) side = (index & 1) == 0 ? 1 : -1;
            float routing = math.smoothstep(0, SimulationConstants.CrowdPackingRange, math.max(0, distance - config.AttackRange));
            routing *= SimulationConstants.CrowdTargetDensity / (SimulationConstants.CrowdTargetDensity + separation.Density);
            float2 velocity = direction * speed + tangent * (side * baseSpeed * separation.Weight * .65f * routing);
            float length = math.length(velocity);
            velocity *= math.min(1, math.max(speed, baseSpeed) / math.max(length, 1e-6f));
            transform.Position.xy += velocity * Dt; transform.Position.z = 0;
            Transforms[e] = transform; Velocities[e] = new MovementVelocity { Value = velocity }; Separation[e] = separation;
        }
    }
    [BurstCompile]
    public struct MoveProjectilesJob : IJob
    {
        public SimulationAccess A;
        public float Dt;
        public void Execute()
        {
            Move(A.PlayerPool.AllProjectiles); Move(A.EnemyProjectilePool.AllProjectiles);
        }
        private void Move(UnsafeList<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                if (!A.Projectiles.IsComponentEnabled(e)) continue;
                var transform = A.Transforms[e];
                A.Previous[e] = new PreviousPosition { Value = transform.Position.xy };
                transform.Position.xy += A.Velocities[e].Value * Dt; transform.Position.z = 0;
                A.Transforms[e] = transform;
                var projectile = A.ProjectileData[e]; projectile.RemainingLifetime -= Dt; A.ProjectileData[e] = projectile;
                if (projectile.RemainingLifetime <= 0) A.Deactivations.Enqueue(e);
            }
        }
    }
}
