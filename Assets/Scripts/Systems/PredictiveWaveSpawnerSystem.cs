using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FloatingOriginSystem))]
    public partial struct PredictiveWaveSpawnerSystem : ISystem
    {
        private ComponentLookup<LocalTransform> m_LocalTransformLookup;
        private ComponentLookup<CurrentHealth> m_CurrentHealthLookup;
        private ComponentLookup<TypeId> m_TypeIdLookup;
        private ComponentLookup<SpriteUVOffset> m_SpriteUVLookup;
        private ComponentLookup<BaseColorOverride> m_BaseColorLookup;
        private ComponentLookup<MovementVelocity> m_MovementVelocityLookup;
        private ComponentLookup<SeparationCache> m_SeparationCacheLookup;
        private ComponentLookup<DisableRendering> m_DisableRenderingLookup;
        private ComponentLookup<EnemyActiveTag> m_EnemyActiveLookup;
        private ComponentLookup<MaterialMeshInfo> m_MaterialMeshInfoLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemyPoolSingleton>();
            state.RequireForUpdate<EnemyConfigCatalogSingleton>();
            state.RequireForUpdate<SimulationCameraBounds>();

            m_LocalTransformLookup = state.GetComponentLookup<LocalTransform>(false);
            m_CurrentHealthLookup = state.GetComponentLookup<CurrentHealth>(false);
            m_TypeIdLookup = state.GetComponentLookup<TypeId>(false);
            m_SpriteUVLookup = state.GetComponentLookup<SpriteUVOffset>(false);
            m_BaseColorLookup = state.GetComponentLookup<BaseColorOverride>(false);
            m_MovementVelocityLookup = state.GetComponentLookup<MovementVelocity>(false);
            m_SeparationCacheLookup = state.GetComponentLookup<SeparationCache>(false);
            m_DisableRenderingLookup = state.GetComponentLookup<DisableRendering>(false);
            m_EnemyActiveLookup = state.GetComponentLookup<EnemyActiveTag>(false);
            m_MaterialMeshInfoLookup = state.GetComponentLookup<MaterialMeshInfo>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0.0f) return;

            if (!SystemAPI.HasSingleton<WaveSpawnerConfig>()) return;

            ref var spawnerConfig = ref SystemAPI.GetSingletonRW<WaveSpawnerConfig>().ValueRW;
            spawnerConfig.Timer += dt;
            if (spawnerConfig.Timer < spawnerConfig.SpawnInterval) return;
            spawnerConfig.Timer = 0.0f;

            ref var enemyPool = ref SystemAPI.GetSingletonRW<EnemyPoolSingleton>().ValueRW;
            var catalogRef = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            if (!catalogRef.IsCreated) return;
            ref var catalog = ref catalogRef.Value;

            var cameraBounds = SystemAPI.GetSingleton<SimulationCameraBounds>();

            float2 playerPos = float2.zero;
            float2 playerVel = float2.zero;
            bool foundPlayer = false;

            foreach (var (transform, velocity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<MovementVelocity>>().WithAll<PlayerTag>())
            {
                playerPos = transform.ValueRO.Position.xy;
                playerVel = velocity.ValueRO.Value;
                foundPlayer = true;
                break;
            }

            if (!foundPlayer) return;

            state.CompleteDependency();

            m_LocalTransformLookup.Update(ref state);
            m_CurrentHealthLookup.Update(ref state);
            m_TypeIdLookup.Update(ref state);
            m_SpriteUVLookup.Update(ref state);
            m_BaseColorLookup.Update(ref state);
            m_MovementVelocityLookup.Update(ref state);
            m_SeparationCacheLookup.Update(ref state);
            m_DisableRenderingLookup.Update(ref state);
            m_EnemyActiveLookup.Update(ref state);
            m_MaterialMeshInfoLookup.Update(ref state);

            var random = new Unity.Mathematics.Random(math.max(1u, spawnerConfig.RandomSeed));

            bool hasVelocity = math.lengthsq(playerVel) >= 0.01f;
            float2 fwd = hasVelocity ? math.normalize(playerVel) : new float2(0.0f, 1.0f);
            float baseAngle = math.atan2(fwd.y, fwd.x);

            float minRadius = math.max(1.0f, spawnerConfig.MinRadius);
            float maxRadius = math.max(minRadius + 1.0f, spawnerConfig.MaxRadius);
            float minRadiusSq = minRadius * minRadius;
            float maxRadiusSq = maxRadius * maxRadius;

            int batchSize = spawnerConfig.BatchSize;
            int numConfigs = catalog.Configs.Length;
            if (numConfigs == 0) return;

            for (int i = 0; i < batchSize; i++)
            {
                float2 spawnDir;
                if (hasVelocity)
                {
                    float roll = random.NextFloat();
                    float angleOffset;
                    if (roll < 0.60f)
                    {
                        // 60% in 90-degree frontal cone [-45, +45 deg]
                        angleOffset = random.NextFloat(-math.PI * 0.25f, math.PI * 0.25f);
                    }
                    else if (roll < 0.90f)
                    {
                        // 30% on flanks [45..135 deg or -135..-45 deg]
                        bool left = random.NextBool();
                        angleOffset = left ? random.NextFloat(math.PI * 0.25f, math.PI * 0.75f)
                                           : random.NextFloat(-math.PI * 0.75f, -math.PI * 0.25f);
                    }
                    else
                    {
                        // 10% in rear [135..225 deg]
                        angleOffset = random.NextFloat(math.PI * 0.75f, math.PI * 1.25f);
                    }
                    float angle = baseAngle + angleOffset;
                    spawnDir = new float2(math.cos(angle), math.sin(angle));
                }
                else
                {
                    // Fallback to uniform 360-degree radial distribution
                    float angle = random.NextFloat(0.0f, math.PI * 2.0f);
                    spawnDir = new float2(math.cos(angle), math.sin(angle));
                }

                // Uniform annulus area distribution
                float r = math.sqrt(math.lerp(minRadiusSq, maxRadiusSq, random.NextFloat()));
                float2 spawnPos = playerPos + spawnDir * r;

                // O(1) TryDequeue from preallocated inactive pool
                if (!enemyPool.InactiveEnemies.TryDequeue(out Entity entity))
                {
                    // Pool gracefully exhausted, stop batch without stalls
                    break;
                }

                // Archetype speed stratification:
                // Type 0 = Tank ("Anvil"), Type 1 = Runner ("Hammer") if 2+ configs exist
                uint requestedTypeId = (numConfigs > 1 && random.NextFloat() < 0.20f) ? SimulationConstants.EnemyTankTypeId : (uint)math.min(SimulationConstants.EnemyRunnerTypeId, (uint)(numConfigs - 1));
                ref var config = ref catalog.Configs[(int)requestedTypeId];

                // Initial camera-relative Z mapping
                float initialZ = cameraBounds.CalculateDepth(spawnPos.y);

                m_LocalTransformLookup[entity] = LocalTransform.FromPosition(new float3(spawnPos.x, spawnPos.y, initialZ));
                m_CurrentHealthLookup[entity] = new CurrentHealth { Value = config.MaxHealth };
                m_TypeIdLookup[entity] = new TypeId { Value = requestedTypeId };
                m_SpriteUVLookup[entity] = new SpriteUVOffset { Value = config.InitialUV };
                m_BaseColorLookup[entity] = new BaseColorOverride { Value = new float4(1.0f, 1.0f, 1.0f, 1.0f) };
                m_MovementVelocityLookup[entity] = default;
                m_SeparationCacheLookup[entity] = default;

                m_DisableRenderingLookup.SetComponentEnabled(entity, false);
                m_EnemyActiveLookup.SetComponentEnabled(entity, true);
                m_MaterialMeshInfoLookup[entity] = MaterialMeshInfo.FromRenderMeshArrayIndices((int)requestedTypeId, 0);
                m_MaterialMeshInfoLookup.SetComponentEnabled(entity, true);
            }

            spawnerConfig.RandomSeed = random.NextUInt(1, uint.MaxValue);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
