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
            const float tier1Radius = 22.0f;
            const float tier2MaxRadius = 45.0f;
            const float tier1RadiusSq = tier1Radius * tier1Radius; // 484.0f
            const float tier2MaxRadiusSq = tier2MaxRadius * tier2MaxRadius; // 2025.0f
            const float invBufferRangeSq = 1.0f / (tier2MaxRadiusSq - tier1RadiusSq);
            const float maxCatchUpMultiplier = 3.0f;

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

    [BurstCompile]
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
                // Apply blended separation vector
                float2 blendedDir;
                if (separationCache.Weight > 0.001f && math.lengthsq(separationCache.Direction) > 0.001f)
                {
                    blendedDir = math.normalizesafe(math.lerp(dirToPlayer, separationCache.Direction, separationCache.Weight * 0.7f));
                }
                else
                {
                    blendedDir = dirToPlayer;
                }

                velocity.Value = blendedDir * baseSpeed;
            }

            // Integrate position
            pos.xy += velocity.Value * Dt;

            // Camera-relative Z depth calculation
            float relativeY = math.clamp(pos.y - CameraBounds.CameraPosition.y, -CameraBounds.ViewportExtentY, CameraBounds.ViewportExtentY);
            pos.z = CameraBounds.ZMinOffset + (relativeY + CameraBounds.ViewportExtentY) * CameraBounds.DepthScale;

            transform.Position = pos;
        }
    }

    [BurstCompile]
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

            // Camera-relative Z depth calculation
            float relativeY = math.clamp(pos.y - CameraBounds.CameraPosition.y, -CameraBounds.ViewportExtentY, CameraBounds.ViewportExtentY);
            pos.z = CameraBounds.ZMinOffset + (relativeY + CameraBounds.ViewportExtentY) * CameraBounds.DepthScale;
            transform.Position = pos;

            // Decrement remaining lifetime
            projectileData.RemainingLifetime -= Dt;
            if (projectileData.RemainingLifetime <= 0.0f)
            {
                DeactivationQueue.Enqueue(entity);
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(PlayerTag))]
    public partial struct UpdatePlayerZJob : IJobEntity
    {
        public SimulationCameraBounds CameraBounds;

        public void Execute(ref LocalTransform transform)
        {
            float3 pos = transform.Position;
            float relativeY = math.clamp(pos.y - CameraBounds.CameraPosition.y, -CameraBounds.ViewportExtentY, CameraBounds.ViewportExtentY);
            pos.z = CameraBounds.ZMinOffset + (relativeY + CameraBounds.ViewportExtentY) * CameraBounds.DepthScale;
            transform.Position = pos;
        }
    }
}
