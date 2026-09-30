using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementAndCameraRelativeZSystem))]
    [UpdateBefore(typeof(SpatialGridRebuildSystem))]
    public partial struct EnemyRangedAttackSystem : ISystem
    {
        private ComponentLookup<LocalTransform> m_LocalTransformLookup;
        private ComponentLookup<MovementVelocity> m_VelocityLookup;
        private ComponentLookup<ProjectileData> m_ProjectileDataLookup;
        private ComponentLookup<ProjectileActiveTag> m_ProjectileActiveLookup;
        private ComponentLookup<DisableRendering> m_DisableRenderingLookup;
        private ComponentLookup<MaterialMeshInfo> m_MaterialMeshInfoLookup;
        private ComponentLookup<BaseColorOverride> m_BaseColorLookup;

        private Random m_Random;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemyProjectilePoolSingleton>();
            state.RequireForUpdate<SimulationCameraBounds>();

            m_LocalTransformLookup = state.GetComponentLookup<LocalTransform>(false);
            m_VelocityLookup = state.GetComponentLookup<MovementVelocity>(false);
            m_ProjectileDataLookup = state.GetComponentLookup<ProjectileData>(false);
            m_ProjectileActiveLookup = state.GetComponentLookup<ProjectileActiveTag>(false);
            m_DisableRenderingLookup = state.GetComponentLookup<DisableRendering>(false);
            m_MaterialMeshInfoLookup = state.GetComponentLookup<MaterialMeshInfo>(false);
            m_BaseColorLookup = state.GetComponentLookup<BaseColorOverride>(false);

            m_Random = new Random(888123u);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0.0f) return;

            float2 playerPos = float2.zero;
            bool hasPlayer = false;
            foreach (var (transform, stats) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PlayerStats>>().WithAll<PlayerTag>())
            {
                if (stats.ValueRO.IsDead != 0) return;
                playerPos = transform.ValueRO.Position.xy;
                hasPlayer = true;
                break;
            }

            if (!hasPlayer) return;

            state.CompleteDependency();

            m_LocalTransformLookup.Update(ref state);
            m_VelocityLookup.Update(ref state);
            m_ProjectileDataLookup.Update(ref state);
            m_ProjectileActiveLookup.Update(ref state);
            m_DisableRenderingLookup.Update(ref state);
            m_MaterialMeshInfoLookup.Update(ref state);
            m_BaseColorLookup.Update(ref state);

            ref var enemyProjPool = ref SystemAPI.GetSingletonRW<EnemyProjectilePoolSingleton>().ValueRW;
            var cameraBounds = SystemAPI.GetSingleton<SimulationCameraBounds>();

            foreach (var (transform, cooldown, typeId) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<EnemyRangedCooldown>, RefRO<TypeId>>().WithAll<EnemyActiveTag>())
            {
                uint type = typeId.ValueRO.Value;
                if (type != SimulationConstants.EnemyRangedSkirmisherTypeId && type != SimulationConstants.EnemyRangedSniperTypeId)
                {
                    continue;
                }

                cooldown.ValueRW.CooldownTimer -= dt;
                if (cooldown.ValueRO.CooldownTimer > 0.0f)
                {
                    continue;
                }

                float2 enemyPos = transform.ValueRO.Position.xy;
                float2 toPlayer = playerPos - enemyPos;
                float distSq = math.lengthsq(toPlayer);

                if (type == SimulationConstants.EnemyRangedSkirmisherTypeId)
                {
                    if (distSq <= SimulationConstants.RangedSkirmisherAttackRangeSq)
                    {
                        // Reset cooldown with slight random jitter
                        cooldown.ValueRW.CooldownTimer = SimulationConstants.RangedSkirmisherAttackInterval + m_Random.NextFloat(-0.15f, 0.15f);

                        FireProjectile(
                            enemyPos,
                            toPlayer,
                            SimulationConstants.RangedSkirmisherProjectileSpeed,
                            SimulationConstants.RangedSkirmisherProjectileDamage,
                            SimulationConstants.RangedSkirmisherProjectileRadius,
                            SimulationConstants.RangedSkirmisherProjectileLifetime,
                            new float4(0.9f, 0.3f, 1.0f, 1.0f),
                            ref enemyProjPool,
                            ref cameraBounds);
                    }
                }
                else if (type == SimulationConstants.EnemyRangedSniperTypeId)
                {
                    if (distSq <= SimulationConstants.RangedSniperAttackRangeSq)
                    {
                        // Reset cooldown with slight random jitter
                        cooldown.ValueRW.CooldownTimer = SimulationConstants.RangedSniperAttackInterval + m_Random.NextFloat(-0.25f, 0.25f);

                        FireProjectile(
                            enemyPos,
                            toPlayer,
                            SimulationConstants.RangedSniperProjectileSpeed,
                            SimulationConstants.RangedSniperProjectileDamage,
                            SimulationConstants.RangedSniperProjectileRadius,
                            SimulationConstants.RangedSniperProjectileLifetime,
                            new float4(1.0f, 0.45f, 0.1f, 1.0f),
                            ref enemyProjPool,
                            ref cameraBounds);
                    }
                }
            }
        }

        private void FireProjectile(
            float2 origin,
            float2 toPlayer,
            float speed,
            float damage,
            float radius,
            float lifetime,
            float4 colorTint,
            ref EnemyProjectilePoolSingleton enemyProjPool,
            ref SimulationCameraBounds cameraBounds)
        {
            if (!enemyProjPool.InactiveProjectiles.TryDequeue(out Entity proj))
            {
                return;
            }

            float dist = math.length(toPlayer);
            float2 dir = dist > 0.0001f ? (toPlayer / dist) : new float2(0.0f, -1.0f);

            // Faster projectiles rendered above slower ones
            float projZ = cameraBounds.CalculateDepth(origin.y, speed);

            m_LocalTransformLookup[proj] = LocalTransform.FromPosition(new float3(origin.x, origin.y, projZ));
            m_VelocityLookup[proj] = new MovementVelocity { Value = dir * speed };
            m_ProjectileDataLookup[proj] = new ProjectileData
            {
                Damage = damage,
                Radius = radius,
                RemainingLifetime = lifetime
            };
            m_BaseColorLookup[proj] = new BaseColorOverride { Value = colorTint };

            m_DisableRenderingLookup.SetComponentEnabled(proj, false);
            m_ProjectileActiveLookup.SetComponentEnabled(proj, true);
            m_MaterialMeshInfoLookup[proj] = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
            m_MaterialMeshInfoLookup.SetComponentEnabled(proj, true);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
