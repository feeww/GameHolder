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
            if (Run[State].Paused) return;
            Entity e = Entities[index];
            if (!Active.IsComponentEnabled(e)) return;
            var transform = Transforms[e];
            Previous[e] = new PreviousPosition { Value = transform.Position.xy };
            var cooldown = Cooldown[e]; cooldown.CooldownTimer = math.max(0, cooldown.CooldownTimer - Dt); Cooldown[e] = cooldown;
            float2 toPlayer = Run[State].PlayerPosition - transform.Position.xy;
            float distanceSq = math.lengthsq(toPlayer);
            float distance = math.sqrt(distanceSq);
            float2 direction = math.normalizesafe(toPlayer);
            var config = Catalog.Value.GetConfig(Types[e]);
            float baseSpeed = config.MoveSpeed;
            var separation = Separation[e];
            float speed = baseSpeed;
            if (distanceSq > CrowdConstants.Tier1RadiusSq)
            {
                separation = default;
                float t = math.saturate((distanceSq - CrowdConstants.Tier1RadiusSq) /
                    (CrowdConstants.Tier2MaxRadiusSq - CrowdConstants.Tier1RadiusSq));
                speed *= math.lerp(1, CrowdConstants.MaxCatchUpMultiplier, t);
            }
            if (config.RetreatRange > 0 && distanceSq < config.RetreatRange * config.RetreatRange)
            {
                direction = -direction;
                speed = math.min(speed, (config.RetreatRange - distance) / math.max(Dt, NumericalConstants.MinimumDivisor));
            }
            else speed = math.min(speed, math.max(0, distance - config.AttackRange) / math.max(Dt, NumericalConstants.MinimumDivisor));
            speed *= math.min(1, CrowdConstants.CrowdTargetDensity / math.max(CrowdConstants.CrowdTargetDensity, separation.Density));
            // Blocked approaches split around the player instead of continually driving into the centre.
            float2 tangent = new float2(-direction.y, direction.x);
            float side = math.dot(separation.Direction, tangent);
            if (math.abs(side) < CrowdConstants.SideSelectionThreshold) side = (index & 1) == 0 ? 1 : -1;
            float routing = math.smoothstep(0, CrowdConstants.CrowdPackingRange, math.max(0, distance - config.AttackRange));
            routing *= CrowdConstants.CrowdTargetDensity / (CrowdConstants.CrowdTargetDensity + separation.Density);
            float2 velocity = direction * speed + tangent * (side * baseSpeed * separation.Weight * CrowdConstants.LateralSteeringScale * routing);
            float length = math.length(velocity);
            velocity *= math.min(1, math.max(speed, baseSpeed) / math.max(length, NumericalConstants.MinimumDivisor));
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
            if (A.Run[A.State].Paused) return;
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
                var projectile = A.ProjectileData[e];
                projectile.ActiveStepFraction = Dt > 0 ? math.saturate(projectile.RemainingLifetime / Dt) : 1;
                transform.Position.xy += A.Velocities[e].Value * (Dt * projectile.ActiveStepFraction); transform.Position.z = 0;
                A.Transforms[e] = transform;
                projectile.RemainingLifetime -= Dt; A.ProjectileData[e] = projectile;
                // Undetonated explosives resolve their expiry in the combat stage.
                if (projectile.RemainingLifetime <= 0 && !(A.Explosives.HasComponent(e) &&
                    A.Explosives.IsComponentEnabled(e) && A.Explosives[e].Detonated == 0)) A.Deactivations.Enqueue(e);
            }
        }
    }
}
