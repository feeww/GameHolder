using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileBroadphaseSystem))]
    public partial struct DamageResolutionSystem : ISystem
    {
        private ComponentLookup<CurrentHealth> m_CurrentHealthLookup;
        private ComponentLookup<EnemyActiveTag> m_EnemyActiveLookup;
        private ComponentLookup<DisableRendering> m_DisableRenderingLookup;
        private ComponentLookup<LocalTransform> m_LocalTransformLookup;
        private ComponentLookup<TypeId> m_TypeIdLookup;

        private ComponentLookup<ProjectileActiveTag> m_ProjectileActiveLookup;
        private ComponentLookup<PlayerProjectileTag> m_PlayerProjLookup;
        private ComponentLookup<EnemyProjectileTag> m_EnemyProjLookup;
        private ComponentLookup<MaterialMeshInfo> m_MaterialMeshInfoLookup;

        private UnsafeList<DamageEvent> m_DamageEventsList;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DamageEventQueueSingleton>();
            state.RequireForUpdate<PlayerDamageEventQueueSingleton>();
            state.RequireForUpdate<ProjectileDeactivationQueueSingleton>();
            state.RequireForUpdate<EnemyPoolSingleton>();
            state.RequireForUpdate<PlayerProjectilePoolSingleton>();
            state.RequireForUpdate<EnemyProjectilePoolSingleton>();
            state.RequireForUpdate<GemSpawnQueueSingleton>();

            m_CurrentHealthLookup = state.GetComponentLookup<CurrentHealth>(false);
            m_EnemyActiveLookup = state.GetComponentLookup<EnemyActiveTag>(false);
            m_DisableRenderingLookup = state.GetComponentLookup<DisableRendering>(false);
            m_LocalTransformLookup = state.GetComponentLookup<LocalTransform>(true);
            m_TypeIdLookup = state.GetComponentLookup<TypeId>(true);

            m_ProjectileActiveLookup = state.GetComponentLookup<ProjectileActiveTag>(false);
            m_PlayerProjLookup = state.GetComponentLookup<PlayerProjectileTag>(true);
            m_EnemyProjLookup = state.GetComponentLookup<EnemyProjectileTag>(true);
            m_MaterialMeshInfoLookup = state.GetComponentLookup<MaterialMeshInfo>(false);

            m_DamageEventsList = new UnsafeList<DamageEvent>(2048, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            m_CurrentHealthLookup.Update(ref state);
            m_EnemyActiveLookup.Update(ref state);
            m_DisableRenderingLookup.Update(ref state);
            m_LocalTransformLookup.Update(ref state);
            m_TypeIdLookup.Update(ref state);

            m_ProjectileActiveLookup.Update(ref state);
            m_PlayerProjLookup.Update(ref state);
            m_EnemyProjLookup.Update(ref state);
            m_MaterialMeshInfoLookup.Update(ref state);

            var damageEventQueue = SystemAPI.GetSingleton<DamageEventQueueSingleton>().DamageQueue;
            var playerDamageQueue = SystemAPI.GetSingleton<PlayerDamageEventQueueSingleton>().PlayerDamageQueue;
            var deactivationQueue = SystemAPI.GetSingleton<ProjectileDeactivationQueueSingleton>().StagedDeactivations;

            ref var enemyPool = ref SystemAPI.GetSingletonRW<EnemyPoolSingleton>().ValueRW;
            ref var playerProjPool = ref SystemAPI.GetSingletonRW<PlayerProjectilePoolSingleton>().ValueRW;
            ref var enemyProjPool = ref SystemAPI.GetSingletonRW<EnemyProjectilePoolSingleton>().ValueRW;
            var gemSpawnQueue = SystemAPI.GetSingleton<GemSpawnQueueSingleton>().SpawnQueue;

            SimulationBridgeQueuesSingleton bridgeQueues = default;
            bool hasBridge = SystemAPI.HasSingleton<SimulationBridgeQueuesSingleton>();
            if (hasBridge)
            {
                bridgeQueues = SystemAPI.GetSingleton<SimulationBridgeQueuesSingleton>();
            }

            // ---------------------------------------------------------------
            // 1. Enemy Damage: Drain, Deterministic Sort, Run-Length Consolidate
            // ---------------------------------------------------------------
            m_DamageEventsList.Clear();
            while (damageEventQueue.TryDequeue(out DamageEvent ev))
            {
                m_DamageEventsList.Add(ev);
            }

            if (m_DamageEventsList.Length > 0)
            {
                m_DamageEventsList.Sort(new DamageEventComparator());

                ulong currentKey = m_DamageEventsList[0].TargetKey;
                Entity currentTarget = m_DamageEventsList[0].TargetEntity;
                float accumulatedDamage = m_DamageEventsList[0].Damage;

                for (int i = 1; i < m_DamageEventsList.Length; i++)
                {
                    var ev = m_DamageEventsList[i];
                    if (ev.TargetKey != currentKey)
                    {
                        ApplyConsolidatedDamage(
                            currentTarget,
                            accumulatedDamage,
                            ref m_CurrentHealthLookup,
                            ref m_EnemyActiveLookup,
                            ref m_DisableRenderingLookup,
                            ref m_MaterialMeshInfoLookup,
                            ref m_LocalTransformLookup,
                            ref m_TypeIdLookup,
                            ref enemyPool,
                            ref gemSpawnQueue,
                            ref bridgeQueues,
                            hasBridge);

                        currentKey = ev.TargetKey;
                        currentTarget = ev.TargetEntity;
                        accumulatedDamage = ev.Damage;
                    }
                    else
                    {
                        accumulatedDamage += ev.Damage;
                    }
                }

                // Trailing run flush
                ApplyConsolidatedDamage(
                    currentTarget,
                    accumulatedDamage,
                    ref m_CurrentHealthLookup,
                    ref m_EnemyActiveLookup,
                    ref m_DisableRenderingLookup,
                    ref m_MaterialMeshInfoLookup,
                    ref m_LocalTransformLookup,
                    ref m_TypeIdLookup,
                    ref enemyPool,
                    ref gemSpawnQueue,
                    ref bridgeQueues,
                    hasBridge);
            }

            // ---------------------------------------------------------------
            // 2. Projectile Pool Reclamation (Centralized & Idempotent)
            // ---------------------------------------------------------------
            while (deactivationQueue.TryDequeue(out Entity projEntity))
            {
                if (!m_ProjectileActiveLookup.HasComponent(projEntity)) continue;
                if (!m_ProjectileActiveLookup.IsComponentEnabled(projEntity))
                {
                    // Already disabled, discard to eliminate multi-hit double-free
                    continue;
                }

                m_ProjectileActiveLookup.SetComponentEnabled(projEntity, false);
                m_DisableRenderingLookup.SetComponentEnabled(projEntity, true);
                m_MaterialMeshInfoLookup.SetComponentEnabled(projEntity, false);

                if (m_PlayerProjLookup.HasComponent(projEntity))
                {
                    playerProjPool.InactiveProjectiles.Enqueue(projEntity);
                }
                else if (m_EnemyProjLookup.HasComponent(projEntity))
                {
                    enemyProjPool.InactiveProjectiles.Enqueue(projEntity);
                }
            }

            // ---------------------------------------------------------------
            // 3. Player Damage: Single-hit absorption / run-length consolidation
            // ---------------------------------------------------------------
            if (!playerDamageQueue.IsEmpty())
            {
                float primaryDamage = 0.0f;
                float2 primaryHitDir = float2.zero;
                bool hasHit = false;

                while (playerDamageQueue.TryDequeue(out PlayerDamageEvent damageEvent))
                {
                    if (!hasHit)
                    {
                        primaryDamage = damageEvent.Damage;
                        primaryHitDir = damageEvent.HitDirection;
                        hasHit = true;
                    }
                    // Redundant simultaneous hits in same frame discarded to prevent shotgun deaths
                }

                if (hasHit)
                {
                    foreach (var (stats, invuln, transform) in SystemAPI.Query<RefRW<PlayerStats>, RefRW<PlayerInvulnerability>, RefRO<LocalTransform>>().WithAll<PlayerTag>())
                    {
                        if (stats.ValueRO.IsDead != 0) break;

                        float newHealth = math.max(0.0f, stats.ValueRO.CurrentHealth - primaryDamage);
                        stats.ValueRW.CurrentHealth = newHealth;
                        invuln.ValueRW.Timer = invuln.ValueRO.InvulnerabilityDuration;

                        if (newHealth <= 0.0f)
                        {
                            stats.ValueRW.IsDead = 1;
                            if (hasBridge && bridgeQueues.DeathEventQueue.IsCreated)
                            {
                                bridgeQueues.DeathEventQueue.Enqueue(new DeathEvent
                                {
                                    Position = transform.ValueRO.Position.xy,
                                    TypeId = SimulationConstants.PlayerTypeId
                                });
                            }
                        }
                        else
                        {
                            if (hasBridge && bridgeQueues.HitReactionEventQueue.IsCreated)
                            {
                                bridgeQueues.HitReactionEventQueue.Enqueue(new PlayerHitReactionEvent
                                {
                                    Damage = primaryDamage,
                                    HitDirection = primaryHitDir
                                });
                            }
                        }
                        break;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyConsolidatedDamage(
            Entity target,
            float damage,
            ref ComponentLookup<CurrentHealth> healthLookup,
            ref ComponentLookup<EnemyActiveTag> activeLookup,
            ref ComponentLookup<DisableRendering> disableRenderLookup,
            ref ComponentLookup<MaterialMeshInfo> materialMeshInfoLookup,
            ref ComponentLookup<LocalTransform> transformLookup,
            ref ComponentLookup<TypeId> typeIdLookup,
            ref EnemyPoolSingleton enemyPool,
            ref UnsafeQueue<GemSpawnRequest> gemQueue,
            ref SimulationBridgeQueuesSingleton bridgeQueues,
            bool hasBridge)
        {
            if (!activeLookup.HasComponent(target)) return;
            if (!activeLookup.IsComponentEnabled(target)) return; // Already inactive

            float newHealth = healthLookup[target].Value - damage;
            if (newHealth <= 0.0f)
            {
                healthLookup[target] = new CurrentHealth { Value = 0.0f };
                activeLookup.SetComponentEnabled(target, false);
                disableRenderLookup.SetComponentEnabled(target, true);
                materialMeshInfoLookup.SetComponentEnabled(target, false);

                // Return handle to inactive pool
                enemyPool.InactiveEnemies.Enqueue(target);

                float2 pos = transformLookup[target].Position.xy;
                uint typeId = typeIdLookup[target].Value;

                // Stage gem spawn request
                gemQueue.Enqueue(new GemSpawnRequest
                {
                    Position = pos,
                    ExperienceValue = SimulationConstants.DefaultEnemyExpDrop
                });

                // Write death event to presentation bridge queue
                if (hasBridge && bridgeQueues.DeathEventQueue.IsCreated)
                {
                    bridgeQueues.DeathEventQueue.Enqueue(new DeathEvent
                    {
                        Position = pos,
                        TypeId = typeId
                    });
                }
            }
            else
            {
                healthLookup[target] = new CurrentHealth { Value = newHealth };
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (m_DamageEventsList.IsCreated)
            {
                m_DamageEventsList.Dispose();
            }
        }
    }
}
