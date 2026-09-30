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
    [UpdateBefore(typeof(MovementAndCameraRelativeZSystem))]
    [UpdateAfter(typeof(PredictiveWaveSpawnerSystem))]
    public partial struct PlayerAutoAttackSystem : ISystem
    {
        private ComponentLookup<LocalTransform> m_LocalTransformLookup;
        private ComponentLookup<MovementVelocity> m_VelocityLookup;
        private ComponentLookup<ProjectileData> m_ProjectileDataLookup;
        private ComponentLookup<ProjectileActiveTag> m_ProjectileActiveLookup;
        private ComponentLookup<DisableRendering> m_DisableRenderingLookup;
        private ComponentLookup<MaterialMeshInfo> m_MaterialMeshInfoLookup;
        private ComponentLookup<EnemyActiveTag> m_EnemyActiveLookup;

        private float m_AttackTimer;
        private uint m_AngleCounter;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerProjectilePoolSingleton>();
            state.RequireForUpdate<SimulationCameraBounds>();
            state.RequireForUpdate<EnemySpatialGridSingleton>();

            m_LocalTransformLookup = state.GetComponentLookup<LocalTransform>(false);
            m_VelocityLookup = state.GetComponentLookup<MovementVelocity>(false);
            m_ProjectileDataLookup = state.GetComponentLookup<ProjectileData>(false);
            m_ProjectileActiveLookup = state.GetComponentLookup<ProjectileActiveTag>(false);
            m_DisableRenderingLookup = state.GetComponentLookup<DisableRendering>(false);
            m_MaterialMeshInfoLookup = state.GetComponentLookup<MaterialMeshInfo>(false);
            m_EnemyActiveLookup = state.GetComponentLookup<EnemyActiveTag>(true);

            m_AttackTimer = 0.0f;
            m_AngleCounter = 0;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            m_AttackTimer += dt;
            if (m_AttackTimer < SimulationConstants.AttackInterval) return;
            m_AttackTimer = 0.0f;

            float2 playerPos = float2.zero;
            float2 playerVel = float2.zero;
            bool foundPlayer = false;

            foreach (var (transform, vel, stats) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<MovementVelocity>, RefRO<PlayerStats>>().WithAll<PlayerTag>())
            {
                if (stats.ValueRO.IsDead != 0) return; // Player is dead, do not attack!
                playerPos = transform.ValueRO.Position.xy;
                playerVel = vel.ValueRO.Value;
                foundPlayer = true;
                break;
            }

            if (!foundPlayer) return;

            ref var pool = ref SystemAPI.GetSingletonRW<PlayerProjectilePoolSingleton>().ValueRW;
            var cameraBounds = SystemAPI.GetSingleton<SimulationCameraBounds>();
            var enemyGrid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;

            state.CompleteDependency();

            m_LocalTransformLookup.Update(ref state);
            m_VelocityLookup.Update(ref state);
            m_ProjectileDataLookup.Update(ref state);
            m_ProjectileActiveLookup.Update(ref state);
            m_DisableRenderingLookup.Update(ref state);
            m_MaterialMeshInfoLookup.Update(ref state);
            m_EnemyActiveLookup.Update(ref state);

            // Find nearest active enemy within search radius via concentric Chebyshev rings
            const float cellSize = SimulationConstants.SpatialCellSize;
            const float invCellSize = SimulationConstants.SpatialInvCellSize;
            const float maxSearchRadius = SimulationConstants.AttackSearchRadius;
            float minDistanceSq = SimulationConstants.AttackSearchRadiusSq;
            float2 targetPos = float2.zero;
            bool hasTarget = false;

            int2 playerCell = SpatialHashUtils.QuantizeToCell(playerPos, invCellSize);
            int maxRings = (int)math.ceil(maxSearchRadius * invCellSize);

            for (int ring = 0; ring <= maxRings; ++ring)
            {
                if (ring > 1)
                {
                    // Conservative minimum Euclidean distance from player to any cell in this ring
                    float minRingDist = (ring - 1) * cellSize;
                    if (minRingDist * minRingDist >= minDistanceSq)
                    {
                        // Any remaining ring is strictly farther than our best candidate; terminate search
                        break;
                    }
                }

                if (ring == 0)
                {
                    ProbeCell(playerCell, playerPos, in enemyGrid, ref m_EnemyActiveLookup, ref minDistanceSq, ref targetPos, ref hasTarget);
                }
                else
                {
                    // Top and bottom horizontal edges
                    for (int dx = -ring; dx <= ring; ++dx)
                    {
                        ProbeCell(playerCell + new int2(dx, ring), playerPos, in enemyGrid, ref m_EnemyActiveLookup, ref minDistanceSq, ref targetPos, ref hasTarget);
                        ProbeCell(playerCell + new int2(dx, -ring), playerPos, in enemyGrid, ref m_EnemyActiveLookup, ref minDistanceSq, ref targetPos, ref hasTarget);
                    }
                    // Left and right vertical edges (excluding corners already probed)
                    for (int dy = -ring + 1; dy <= ring - 1; ++dy)
                    {
                        ProbeCell(playerCell + new int2(-ring, dy), playerPos, in enemyGrid, ref m_EnemyActiveLookup, ref minDistanceSq, ref targetPos, ref hasTarget);
                        ProbeCell(playerCell + new int2(ring, dy), playerPos, in enemyGrid, ref m_EnemyActiveLookup, ref minDistanceSq, ref targetPos, ref hasTarget);
                    }
                }
            }

            float baseAngle;
            if (hasTarget)
            {
                float2 aimDir = math.normalize(targetPos - playerPos);
                baseAngle = math.atan2(aimDir.y, aimDir.x);
            }
            else if (math.lengthsq(playerVel) > 0.01f)
            {
                float2 fwd = math.normalize(playerVel);
                baseAngle = math.atan2(fwd.y, fwd.x);
            }
            else
            {
                baseAngle = (m_AngleCounter * 0.35f) % (2.0f * math.PI);
                m_AngleCounter++;
            }

            // Shoot spread pattern
            for (int i = 0; i < SimulationConstants.ProjectileSpreadCount; i++)
            {
                if (!pool.InactiveProjectiles.TryDequeue(out Entity proj))
                {
                    break;
                }

                float angleOffset = (i - 1) * SimulationConstants.ProjectileSpreadAngle;
                float angle = baseAngle + angleOffset;
                float2 dir = new float2(math.cos(angle), math.sin(angle));
                float speed = SimulationConstants.ProjectileSpeed;

                float projZ = cameraBounds.CalculateDepth(playerPos.y, speed);

                m_LocalTransformLookup[proj] = LocalTransform.FromPosition(new float3(playerPos.x, playerPos.y, projZ));
                m_VelocityLookup[proj] = new MovementVelocity { Value = dir * speed };
                m_ProjectileDataLookup[proj] = new ProjectileData
                {
                    Damage = SimulationConstants.PlayerProjectileDamage,
                    Radius = SimulationConstants.PlayerProjectileRadius,
                    RemainingLifetime = SimulationConstants.PlayerProjectileLifetime
                };

                m_DisableRenderingLookup.SetComponentEnabled(proj, false);
                m_ProjectileActiveLookup.SetComponentEnabled(proj, true);
                m_MaterialMeshInfoLookup[proj] = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
                m_MaterialMeshInfoLookup.SetComponentEnabled(proj, true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ProbeCell(
            int2 targetCell,
            float2 playerPos,
            in UnsafeParallelMultiHashMap<uint, GridEntry> enemyGrid,
            ref ComponentLookup<EnemyActiveTag> enemyActiveLookup,
            ref float minDistanceSq,
            ref float2 targetPos,
            ref bool hasTarget)
        {
            uint hash = SpatialHashUtils.ComputeHash(targetCell);
            if (enemyGrid.TryGetFirstValue(hash, out GridEntry entry, out NativeParallelMultiHashMapIterator<uint> it))
            {
                do
                {
                    if (math.all(entry.CellCoord == targetCell) &&
                        enemyActiveLookup.HasComponent(entry.Entity) &&
                        enemyActiveLookup.IsComponentEnabled(entry.Entity))
                    {
                        float distSq = math.distancesq(playerPos, entry.Position);
                        if (distSq < minDistanceSq)
                        {
                            minDistanceSq = distSq;
                            targetPos = entry.Position;
                            hasTarget = true;
                        }
                    }
                } while (enemyGrid.TryGetNextValue(out entry, ref it));
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
