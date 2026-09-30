using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictiveWaveSpawnerSystem))]
    public partial struct MovementAndCameraRelativeZSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationCameraBounds>();
            state.RequireForUpdate<EnemyConfigCatalogSingleton>();
            state.RequireForUpdate<ProjectileDeactivationQueueSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0.0f) return;

            var cameraBounds = SystemAPI.GetSingleton<SimulationCameraBounds>();
            var catalogRef = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            if (!catalogRef.IsCreated) return;

            var deactivationSingleton = SystemAPI.GetSingleton<ProjectileDeactivationQueueSingleton>();
            var deactivationQueueWriter = deactivationSingleton.StagedDeactivations.AsParallelWriter();

            float2 playerPos = float2.zero;
            foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
            {
                playerPos = transform.ValueRO.Position.xy;
                break;
            }

            // Constants for Tier 1 / Tier 2 LOD
            const float tier1RadiusSq = SimulationConstants.Tier1RadiusSq;
            const float tier2MaxRadiusSq = SimulationConstants.Tier2MaxRadiusSq;
            const float invBufferRangeSq = 1.0f / (tier2MaxRadiusSq - tier1RadiusSq);
            const float maxCatchUpMultiplier = SimulationConstants.MaxCatchUpMultiplier;

            // 1. Update Enemies
            var updateEnemiesJob = new UpdateEnemiesJob
            {
                Dt = dt,
                PlayerPos = playerPos,
                Catalog = catalogRef,
                CameraBounds = cameraBounds,
                Tier1RadiusSq = tier1RadiusSq,
                InvBufferRangeSq = invBufferRangeSq,
                MaxCatchUpMultiplier = maxCatchUpMultiplier
            };
            state.Dependency = updateEnemiesJob.ScheduleParallel(state.Dependency);

            // 2. Update Projectiles
            var updateProjectilesJob = new UpdateProjectilesJob
            {
                Dt = dt,
                CameraBounds = cameraBounds,
                DeactivationQueue = deactivationQueueWriter
            };
            state.Dependency = updateProjectilesJob.ScheduleParallel(state.Dependency);

            // 3. Update Player Z
            var updatePlayerZJob = new UpdatePlayerZJob
            {
                CameraBounds = cameraBounds
            };
            state.Dependency = updatePlayerZJob.Schedule(state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [WithAll(typeof(EnemyActiveTag))]
    public partial struct UpdateEnemiesJob : IJobEntity
    {
        public float Dt;
        public float2 PlayerPos;
        [ReadOnly] public BlobAssetReference<EnemyConfigCatalog> Catalog;
        public SimulationCameraBounds CameraBounds;
        public float Tier1RadiusSq;
        public float InvBufferRangeSq;
        public float MaxCatchUpMultiplier;

        public void Execute(
            Entity entity,
            ref LocalTransform transform,
            ref MovementVelocity velocity,
            ref SeparationCache separationCache,
            in TypeId typeId)
        {
            float3 pos = transform.Position;
            float2 toPlayer = PlayerPos - pos.xy;
            float distSq = math.lengthsq(toPlayer);

            int configIdx = (int)typeId.Value;
            ref var configs = ref Catalog.Value.Configs;
            float baseSpeed = (configIdx >= 0 && configIdx < configs.Length) ? configs[configIdx].MoveSpeed : 3.0f;

            float dist = math.sqrt(distSq);
            float2 dirToPlayer = dist > 0.0001f ? (toPlayer / dist) : float2.zero;

            if (distSq > Tier1RadiusSq)
            {
                // Tier 2 (Off-screen):
                // Zero separation cache to prevent lateral drift during catch-up
                separationCache.Direction = float2.zero;
                separationCache.Weight = 0.0f;

                // Off-screen catch-up boost:
                // Smooth linear attenuation approaching Tier 1 boundary
                float t = math.saturate((distSq - Tier1RadiusSq) * InvBufferRangeSq);
                float speedMultiplier = math.lerp(1.0f, MaxCatchUpMultiplier, t);

                velocity.Value = dirToPlayer * (baseSpeed * speedMultiplier);
            }
            else
            {
                // Tier 1 (On-screen + buffer):
                float2 desiredDir;
                float moveSpeed = baseSpeed;

                if (typeId.Value == SimulationConstants.EnemyRangedSkirmisherTypeId)
                {
                    // Type 1: Ranged Skirmisher (moves toward player until within attack range; if player gets too close, retreats to maintain attack range)
                    if (dist > SimulationConstants.RangedSkirmisherAttackRange)
                    {
                        desiredDir = dirToPlayer;
                    }
                    else if (dist < SimulationConstants.RangedSkirmisherRetreatRange)
                    {
                        desiredDir = -dirToPlayer; // Retreat away from player
                    }
                    else
                    {
                        desiredDir = float2.zero; // Maintain attack range
                        moveSpeed = 0.0f;
                    }
                }
                else if (typeId.Value == SimulationConstants.EnemyRangedSniperTypeId)
                {
                    // Type 2: Long-range Sniper (longer attack range than type 1, does NOT retreat when player approaches)
                    if (dist > SimulationConstants.RangedSniperAttackRange)
                    {
                        desiredDir = dirToPlayer;
                    }
                    else
                    {
                        desiredDir = float2.zero; // Holds ground within attack range, does not retreat
                        moveSpeed = 0.0f;
                    }
                }
                else
                {
                    // Melee crowd units (Tank, Runner)
                    desiredDir = dirToPlayer;
                }

                // Apply separation blending
                if (moveSpeed > 0.0f)
                {
                    if (separationCache.Weight > 0.001f && math.lengthsq(separationCache.Direction) > 0.001f)
                    {
                        float2 blendedDir = math.normalizesafe(math.lerp(desiredDir, separationCache.Direction, separationCache.Weight * 0.7f));
                        velocity.Value = blendedDir * moveSpeed;
                    }
                    else
                    {
                        velocity.Value = desiredDir * moveSpeed;
                    }
                }
                else
                {
                    // While holding ground, allow gentle separation drift to prevent perfect stacking
                    if (separationCache.Weight > 0.001f && math.lengthsq(separationCache.Direction) > 0.001f)
                    {
                        velocity.Value = separationCache.Direction * (baseSpeed * separationCache.Weight * 0.35f);
                    }
                    else
                    {
                        velocity.Value = float2.zero;
                    }
                }
            }

            // Integrate position
            pos.xy += velocity.Value * Dt;

            // Camera-relative Z depth calculation: faster enemies rendered above slower ones
            float entitySpeed = math.max(baseSpeed, math.length(velocity.Value));
            pos.z = CameraBounds.CalculateDepth(pos.y, entitySpeed);

            transform.Position = pos;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [WithAll(typeof(ProjectileActiveTag))]
    public partial struct UpdateProjectilesJob : IJobEntity
    {
        public float Dt;
        public SimulationCameraBounds CameraBounds;
        public UnsafeQueue<Entity>.ParallelWriter DeactivationQueue;

        public void Execute(
            Entity entity,
            ref LocalTransform transform,
            ref ProjectileData projectileData,
            in MovementVelocity velocity)
        {
            float3 pos = transform.Position;
            pos.xy += velocity.Value * Dt;

            // Camera-relative Z depth calculation: faster projectiles rendered above slower ones
            float speed = math.length(velocity.Value);
            pos.z = CameraBounds.CalculateDepth(pos.y, speed);
            transform.Position = pos;

            // Decrement remaining lifetime
            projectileData.RemainingLifetime -= Dt;
            if (projectileData.RemainingLifetime <= 0.0f)
            {
                DeactivationQueue.Enqueue(entity);
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [WithAll(typeof(PlayerTag))]
    public partial struct UpdatePlayerZJob : IJobEntity
    {
        public SimulationCameraBounds CameraBounds;

        public void Execute(ref LocalTransform transform)
        {
            float3 pos = transform.Position;
            pos.z = CameraBounds.CalculateDepth(pos.y, SimulationConstants.PlayerDefaultMoveSpeed);
            transform.Position = pos;
        }
    }
}
