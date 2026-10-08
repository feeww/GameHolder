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
        [ReadOnly] public ComponentLookup<EnemyRangedCooldown> RangedCooldown;
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
            bool laser = config.Weapon.Type == WeaponType.Laser;
            float stoppingDistance = laser ? config.MeleeStoppingDistance : config.AttackRange;
            float laserRange = math.min(config.AttackRange, CombatConstants.EnemyLaserMaximumRange);
            if (laser && RangedCooldown[e].ChargeBeam != Entity.Null &&
                distanceSq > CombatConstants.EnemyLaserMinimumRange * CombatConstants.EnemyLaserMinimumRange &&
                distanceSq <= laserRange * laserRange)
            { Velocities[e] = default; return; }
            float baseSpeed = config.MoveSpeed;
            if (laser && distanceSq <= CombatConstants.EnemyLaserMinimumRange * CombatConstants.EnemyLaserMinimumRange)
                baseSpeed *= CombatConstants.EnemyLaserMeleeSpeedMultiplier;
            var separation = Separation[e];
            float speed = baseSpeed;
            if (distanceSq > CrowdConstants.Tier1RadiusSq)
            {
                separation = default;
                float t = math.saturate((distanceSq - CrowdConstants.Tier1RadiusSq) /
                    (CrowdConstants.Tier2MaxRadiusSq - CrowdConstants.Tier1RadiusSq));
                speed *= math.lerp(1, CrowdConstants.MaxCatchUpMultiplier, t);
            }
            if (!laser && config.RetreatRange > 0 && distanceSq < config.RetreatRange * config.RetreatRange)
            {
                direction = -direction;
                speed = math.min(speed, (config.RetreatRange - distance) / math.max(Dt, NumericalConstants.MinimumDivisor));
            }
            else speed = math.min(speed, math.max(0, distance - stoppingDistance) / math.max(Dt, NumericalConstants.MinimumDivisor));
            speed *= math.min(1, CrowdConstants.CrowdTargetDensity / math.max(CrowdConstants.CrowdTargetDensity, separation.Density));
            // Blocked approaches split around the player instead of continually driving into the centre.
            float2 tangent = new float2(-direction.y, direction.x);
            float side = math.dot(separation.Direction, tangent);
            if (math.abs(side) < CrowdConstants.SideSelectionThreshold) side = (index & 1) == 0 ? 1 : -1;
            float routing = math.smoothstep(0, CrowdConstants.CrowdPackingRange, math.max(0, distance - stoppingDistance));
            routing *= CrowdConstants.CrowdTargetDensity / (CrowdConstants.CrowdTargetDensity + separation.Density);
            float2 velocity = direction * speed + tangent * (side * baseSpeed * separation.Weight * CrowdConstants.LateralSteeringScale * routing);
            float length = math.length(velocity);
            velocity *= math.min(1, math.max(speed, baseSpeed) / math.max(length, NumericalConstants.MinimumDivisor));
            transform.Position.xy += velocity * Dt; transform.Position.z = 0;
            Transforms[e] = transform; Velocities[e] = new MovementVelocity { Value = velocity }; Separation[e] = separation;
        }
    }
    [BurstCompile]
    [WithAll(typeof(ProjectileActiveTag))]
    public partial struct MoveProjectilesJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<SimulationRunState> RunState;
        [ReadOnly] public ComponentLookup<LaserBeam> Lasers;
        [ReadOnly] public ComponentLookup<ExplosiveProjectile> Explosives;
        public UnsafeQueue<Entity>.ParallelWriter Deactivations;
        public Entity State;
        public float Dt;
        public void Execute(Entity e, ref LocalTransform transform, ref PreviousPosition previous,
            ref ProjectileData projectile, in MovementVelocity velocity)
        {
            if (RunState[State].Paused || Lasers.IsComponentEnabled(e) && Lasers[e].Charging != 0) return;
            previous.Value = transform.Position.xy;
            projectile.ActiveStepFraction = Dt > 0 ? math.saturate(projectile.RemainingLifetime / Dt) : 1;
            transform.Position.xy += velocity.Value * (Dt * projectile.ActiveStepFraction); transform.Position.z = 0;
            projectile.RemainingLifetime -= Dt;
            // Undetonated explosives resolve their expiry in the combat stage.
            if (projectile.RemainingLifetime <= 0 && !(Explosives.HasComponent(e) &&
                Explosives.IsComponentEnabled(e) && Explosives[e].Detonated == 0)) Deactivations.Enqueue(e);
        }
    }
}
