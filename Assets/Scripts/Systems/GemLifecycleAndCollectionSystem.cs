using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageResolutionSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct GemLifecycleAndCollectionSystem : ISystem
    {
        private ComponentLookup<LocalTransform> m_LocalTransformLookup;
        private ComponentLookup<GemData> m_GemDataLookup;
        private ComponentLookup<SpriteUVOffset> m_SpriteUVLookup;
        private ComponentLookup<BaseColorOverride> m_BaseColorLookup;
        private ComponentLookup<GemActiveTag> m_GemActiveLookup;
        private ComponentLookup<DisableRendering> m_DisableRenderingLookup;
        private ComponentLookup<MaterialMeshInfo> m_MaterialMeshInfoLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GemPoolSingleton>();
            state.RequireForUpdate<GemSpawnQueueSingleton>();
            state.RequireForUpdate<SimulationCameraBounds>();

            m_LocalTransformLookup = state.GetComponentLookup<LocalTransform>(false);
            m_GemDataLookup = state.GetComponentLookup<GemData>(false);
            m_SpriteUVLookup = state.GetComponentLookup<SpriteUVOffset>(false);
            m_BaseColorLookup = state.GetComponentLookup<BaseColorOverride>(false);
            m_GemActiveLookup = state.GetComponentLookup<GemActiveTag>(false);
            m_DisableRenderingLookup = state.GetComponentLookup<DisableRendering>(false);
            m_MaterialMeshInfoLookup = state.GetComponentLookup<MaterialMeshInfo>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var gemPool = ref SystemAPI.GetSingletonRW<GemPoolSingleton>().ValueRW;
            var gemSpawnQueue = SystemAPI.GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;
            var cameraBounds = SystemAPI.GetSingleton<SimulationCameraBounds>();

            float2 playerPos = float2.zero;
            float magnetRadius = SimulationConstants.PlayerDefaultMagnetRadius;
            bool hasPlayer = false;

            foreach (var (transform, stats) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PlayerStats>>().WithAll<PlayerTag>())
            {
                if (stats.ValueRO.IsDead != 0) return; // Dead player doesn't spawn or collect gems
                playerPos = transform.ValueRO.Position.xy;
                magnetRadius = stats.ValueRO.MagnetRadius;
                hasPlayer = true;
                break;
            }

            if (!hasPlayer) return;

            state.CompleteDependency();

            m_LocalTransformLookup.Update(ref state);
            m_GemDataLookup.Update(ref state);
            m_SpriteUVLookup.Update(ref state);
            m_BaseColorLookup.Update(ref state);
            m_GemActiveLookup.Update(ref state);
            m_DisableRenderingLookup.Update(ref state);
            m_MaterialMeshInfoLookup.Update(ref state);

            SimulationBridgeQueuesSingleton bridgeQueues = default;
            bool hasBridge = SystemAPI.HasSingleton<SimulationBridgeQueuesSingleton>();
            if (hasBridge)
            {
                bridgeQueues = SystemAPI.GetSingleton<SimulationBridgeQueuesSingleton>();
            }

            // ---------------------------------------------------------------
            // 1. Drain GemSpawnRequests via 3-Tier Cascade
            // ---------------------------------------------------------------
            const float offScreenDistSq = SimulationConstants.OffScreenGemRecycleDistanceSq;

            while (gemSpawnQueue.TryDequeue(out GemSpawnRequest request))
            {
                float gemZ = cameraBounds.CalculateDepth(request.Position.y);

                // Tier 1: Free Pool Allocation in O(1)
                if (gemPool.FreeGems.TryDequeue(out Entity freeGem))
                {
                    uint slotIndex = m_GemDataLookup[freeGem].SlotIndex;
                    uint tier = ComputeTier(request.ExperienceValue);

                    var record = gemPool.AllGems[(int)slotIndex];
                    record.Position = request.Position;
                    record.ExperienceValue = request.ExperienceValue;
                    record.Tier = tier;
                    record.IsActive = 1;
                    gemPool.AllGems[(int)slotIndex] = record;

                    m_LocalTransformLookup[freeGem] = LocalTransform.FromPosition(new float3(request.Position.x, request.Position.y, gemZ));
                    m_GemDataLookup[freeGem] = new GemData
                    {
                        ExperienceValue = request.ExperienceValue,
                        Tier = tier,
                        SlotIndex = slotIndex
                    };
                    m_SpriteUVLookup[freeGem] = new SpriteUVOffset { Value = ComputeTierUV(tier) };
                    m_BaseColorLookup[freeGem] = new BaseColorOverride { Value = ComputeTierColor(tier) };

                    m_DisableRenderingLookup.SetComponentEnabled(freeGem, false);
                    m_GemActiveLookup.SetComponentEnabled(freeGem, true);
                    m_MaterialMeshInfoLookup[freeGem] = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
                    m_MaterialMeshInfoLookup.SetComponentEnabled(freeGem, true);
                    continue;
                }

                // If free pool empty: Scan 1024 slots for Tier 2 or Tier 3
                float maxDistSq = -1.0f;
                int furthestOffScreenIdx = -1;

                float minDistanceToReqSq = float.MaxValue;
                int nearestOnScreenIdx = -1;

                int gemCount = gemPool.AllGems.Length;
                for (int i = 0; i < gemCount; i++)
                {
                    var record = gemPool.AllGems[i];
                    if (record.IsActive == 0) continue;

                    float distToPlayerSq = math.distancesq(record.Position, playerPos);
                    if (distToPlayerSq > offScreenDistSq && distToPlayerSq > maxDistSq)
                    {
                        maxDistSq = distToPlayerSq;
                        furthestOffScreenIdx = i;
                    }

                    float distToReqSq = math.distancesq(record.Position, request.Position);
                    if (distToReqSq < minDistanceToReqSq)
                    {
                        minDistanceToReqSq = distToReqSq;
                        nearestOnScreenIdx = i;
                    }
                }

                // Tier 2: Recycle Furthest Off-screen Gem (> 35m)
                if (furthestOffScreenIdx >= 0)
                {
                    var record = gemPool.AllGems[furthestOffScreenIdx];
                    uint newExp = record.ExperienceValue + request.ExperienceValue;
                    uint tier = ComputeTier(newExp);

                    record.Position = request.Position;
                    record.ExperienceValue = newExp;
                    record.Tier = tier;
                    gemPool.AllGems[furthestOffScreenIdx] = record;

                    Entity gem = record.Entity;
                    m_LocalTransformLookup[gem] = LocalTransform.FromPosition(new float3(request.Position.x, request.Position.y, gemZ));
                    m_GemDataLookup[gem] = new GemData
                    {
                        ExperienceValue = newExp,
                        Tier = tier,
                        SlotIndex = (uint)furthestOffScreenIdx
                    };
                    m_SpriteUVLookup[gem] = new SpriteUVOffset { Value = ComputeTierUV(tier) };
                    m_BaseColorLookup[gem] = new BaseColorOverride { Value = ComputeTierColor(tier) };
                    m_DisableRenderingLookup.SetComponentEnabled(gem, false);
                    m_GemActiveLookup.SetComponentEnabled(gem, true);
                    m_MaterialMeshInfoLookup[gem] = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
                    m_MaterialMeshInfoLookup.SetComponentEnabled(gem, true);
                }
                // Tier 3: Consolidate into Nearest On-screen Gem (Flash without moving)
                else if (nearestOnScreenIdx >= 0)
                {
                    var record = gemPool.AllGems[nearestOnScreenIdx];
                    uint newExp = record.ExperienceValue + request.ExperienceValue;
                    uint tier = ComputeTier(newExp);

                    record.ExperienceValue = newExp;
                    record.Tier = tier;
                    gemPool.AllGems[nearestOnScreenIdx] = record;

                    Entity gem = record.Entity;
                    m_GemDataLookup[gem] = new GemData
                    {
                        ExperienceValue = newExp,
                        Tier = tier,
                        SlotIndex = (uint)nearestOnScreenIdx
                    };
                    m_SpriteUVLookup[gem] = new SpriteUVOffset { Value = ComputeTierUV(tier) };
                    // Visual flash effect on consolidation
                    m_BaseColorLookup[gem] = new BaseColorOverride { Value = new float4(2.0f, 2.0f, 2.0f, 1.0f) };
                }
            }

            // ---------------------------------------------------------------
            // 2. Vectorized Radial Distance Collection Query
            // ---------------------------------------------------------------
            float magnetRadiusSq = magnetRadius * magnetRadius;
            int totalGems = gemPool.AllGems.Length;
            uint totalExpGained = 0;

            for (int i = 0; i < totalGems; i++)
            {
                var record = gemPool.AllGems[i];
                if (record.IsActive == 0) continue;

                if (math.distancesq(record.Position, playerPos) <= magnetRadiusSq)
                {
                    totalExpGained += record.ExperienceValue;

                    // Collect gem
                    if (hasBridge && bridgeQueues.GemCollectEventQueue.IsCreated)
                    {
                        bridgeQueues.GemCollectEventQueue.Enqueue(new GemCollectEvent
                        {
                            Position = record.Position,
                            ExperienceValue = record.ExperienceValue
                        });
                    }

                    // Clear active record in cache
                    record.IsActive = 0;
                    gemPool.AllGems[i] = record;

                    Entity gem = record.Entity;
                    m_GemActiveLookup.SetComponentEnabled(gem, false);
                    m_DisableRenderingLookup.SetComponentEnabled(gem, true);
                    m_MaterialMeshInfoLookup.SetComponentEnabled(gem, false);

                    // Return handle to free pool in single cache-friendly pass
                    gemPool.FreeGems.Enqueue(gem);
                }
            }

            if (totalExpGained > 0)
            {
                foreach (var stats in SystemAPI.Query<RefRW<PlayerStats>>().WithAll<PlayerTag>())
                {
                    stats.ValueRW.Experience += totalExpGained;
                    uint reqExp = stats.ValueRO.Level * SimulationConstants.ExpPerLevelMultiplier;
                    while (stats.ValueRW.Experience >= reqExp)
                    {
                        stats.ValueRW.Experience -= reqExp;
                        stats.ValueRW.Level++;
                        stats.ValueRW.MaxHealth += SimulationConstants.HealthBonusPerLevel;
                        stats.ValueRW.CurrentHealth = math.min(stats.ValueRO.MaxHealth, stats.ValueRO.CurrentHealth + SimulationConstants.HealthHealPerLevel);
                        reqExp = stats.ValueRO.Level * SimulationConstants.ExpPerLevelMultiplier;
                    }
                    break;
                }
            }
        }

        private static uint ComputeTier(uint exp)
        {
            if (exp >= 16) return 3;
            if (exp >= 8) return 2;
            if (exp >= 4) return 1;
            return 0;
        }

        private static float4 ComputeTierUV(uint tier)
        {
            // Atlas 2x2 grid frames: (scale.x, scale.y, offset.x, offset.y)
            switch (tier)
            {
                case 1: return new float4(0.5f, 0.5f, 0.5f, 0.0f);
                case 2: return new float4(0.5f, 0.5f, 0.0f, 0.5f);
                case 3: return new float4(0.5f, 0.5f, 0.5f, 0.5f);
                default: return new float4(0.5f, 0.5f, 0.0f, 0.0f);
            }
        }

        private static float4 ComputeTierColor(uint tier)
        {
            switch (tier)
            {
                case 1: return new float4(0.2f, 0.8f, 1.0f, 1.0f); // Blue
                case 2: return new float4(0.9f, 0.3f, 1.0f, 1.0f); // Purple
                case 3: return new float4(1.0f, 0.84f, 0.0f, 1.0f); // Gold
                default: return new float4(0.2f, 1.0f, 0.4f, 1.0f); // Green
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
