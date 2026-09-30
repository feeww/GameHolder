using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(StochasticSeparationSystem))]
    public partial struct PlayerHitCheckSystem : ISystem
    {
        private ComponentLookup<TypeId> m_TypeIdLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnemySpatialGridSingleton>();
            state.RequireForUpdate<EnemyProjectileGridSingleton>();
            state.RequireForUpdate<PlayerDamageEventQueueSingleton>();
            state.RequireForUpdate<ProjectileDeactivationQueueSingleton>();
            state.RequireForUpdate<EnemyConfigCatalogSingleton>();

            m_TypeIdLookup = state.GetComponentLookup<TypeId>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0.0f) return;

            float playerInvulnTimer = 0.0f;
            float2 playerPos = float2.zero;
            float2 playerVel = float2.zero;
            bool hasPlayer = false;

            foreach (var (transform, velocity, invuln) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<MovementVelocity>, RefRO<PlayerInvulnerability>>().WithAll<PlayerTag>())
            {
                playerInvulnTimer = invuln.ValueRO.Timer;
                playerPos = transform.ValueRO.Position.xy;
                playerVel = velocity.ValueRO.Value;
                hasPlayer = true;
                break;
            }

            if (!hasPlayer) return;

            bool isInvulnerable = playerInvulnTimer > 0.0f;

            state.CompleteDependency();

            m_TypeIdLookup.Update(ref state);

            var catalogRef = SystemAPI.GetSingleton<EnemyConfigCatalogSingleton>().Catalog;
            bool hasCatalog = catalogRef.IsCreated;

            var enemyGrid = SystemAPI.GetSingleton<EnemySpatialGridSingleton>().Grid;
            var playerDamageQueue = SystemAPI.GetSingleton<PlayerDamageEventQueueSingleton>().PlayerDamageQueue;
            var projectileDeactivationQueue = SystemAPI.GetSingleton<ProjectileDeactivationQueueSingleton>().StagedDeactivations;

            const float invCellSize = SimulationConstants.SpatialInvCellSize;
            const float playerRadius = SimulationConstants.PlayerCollisionRadius;
            const float enemyRadius = SimulationConstants.EnemyCollisionRadius;
            const float contactRadius = playerRadius + enemyRadius;
            const float contactRadiusSq = contactRadius * contactRadius;

            // 1. Check contact with enemy crowd units (9-cell neighborhood)
            // Skipped if player is invulnerable to save CPU cycles
            if (!isInvulnerable)
            {
                int2 playerCell = SpatialHashUtils.QuantizeToCell(playerPos, invCellSize);
                bool meleeHit = false;
                for (int dy = -1; dy <= 1 && !meleeHit; ++dy)
                {
                    for (int dx = -1; dx <= 1 && !meleeHit; ++dx)
                    {
                        int2 targetCell = playerCell + new int2(dx, dy);
                        uint hash = SpatialHashUtils.ComputeHash(targetCell);

                        if (enemyGrid.TryGetFirstValue(hash, out GridEntry entry, out NativeParallelMultiHashMapIterator<uint> it))
                        {
                            do
                            {
                                if (math.all(entry.CellCoord == targetCell))
                                {
                                    float2 diff = playerPos - entry.Position;
                                    float distSq = math.lengthsq(diff);
                                    if (distSq <= contactRadiusSq)
                                    {
                                        float dist = math.sqrt(distSq);
                                        float2 hitDir = dist > 0.0001f ? (diff / dist) : new float2(0.0f, 1.0f);

                                        float damage = SimulationConstants.DefaultMeleeDamage;
                                        if (hasCatalog && m_TypeIdLookup.HasComponent(entry.Entity))
                                        {
                                            int typeIdx = (int)m_TypeIdLookup[entry.Entity].Value;
                                            ref var configs = ref catalogRef.Value.Configs;
                                            if (typeIdx >= 0 && typeIdx < configs.Length)
                                            {
                                                damage = configs[typeIdx].BaseDamage;
                                            }
                                        }

                                        playerDamageQueue.Enqueue(new PlayerDamageEvent
                                        {
                                            Damage = damage,
                                            HitDirection = hitDir,
                                            SourceEntity = entry.Entity
                                        });
                                        // Single melee contact is sufficient per frame due to i-frames
                                        meleeHit = true;
                                        break;
                                    }
                                }
                            } while (enemyGrid.TryGetNextValue(out entry, ref it));
                        }
                    }
                }
            }

            // 2. Check contact with enemy projectiles using Continuous Collision Detection (CCD swept-circle)
            // Evaluates projectile trajectory relative to player velocity over dt to eliminate tunneling.
            // All projectiles entering the player hitbox are consumed and deactivated; damage is enqueued if vulnerable.
            const float broadphaseCullDist = 6.0f; // Max relative movement per frame + collision diameter

            foreach (var (transform, velocity, projData, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<MovementVelocity>, RefRO<ProjectileData>>()
                         .WithAll<ProjectileActiveTag, EnemyProjectileTag>()
                         .WithEntityAccess())
            {
                float2 currPos = transform.ValueRO.Position.xy;
                float2 diffToPlayer = currPos - playerPos;

                // Fast AABB broadphase culling before swept-segment math
                if (math.abs(diffToPlayer.x) > broadphaseCullDist || math.abs(diffToPlayer.y) > broadphaseCullDist)
                {
                    continue;
                }

                float2 projVel = velocity.ValueRO.Value;
                // Relative displacement between projectile and player during dt
                float2 relDisplacement = (projVel - playerVel) * dt;

                // Relative start position in player's current local frame
                float2 prevPos = currPos - relDisplacement;
                float2 seg = relDisplacement;
                float2 toPlayer = playerPos - prevPos;

                float segLenSq = math.lengthsq(seg);
                float t = segLenSq > 0.00001f ? math.saturate(math.dot(toPlayer, seg) / segLenSq) : 0.0f;
                float2 closestPoint = prevPos + t * seg;

                float projRadius = projData.ValueRO.Radius;
                float combinedRadius = playerRadius + projRadius;
                float distSq = math.distancesq(playerPos, closestPoint);

                if (distSq <= combinedRadius * combinedRadius)
                {
                    float dist = math.sqrt(distSq);
                    float2 hitDir = dist > 0.0001f ? ((playerPos - closestPoint) / dist) : new float2(0.0f, 1.0f);

                    if (!isInvulnerable)
                    {
                        playerDamageQueue.Enqueue(new PlayerDamageEvent
                        {
                            Damage = projData.ValueRO.Damage,
                            HitDirection = hitDir,
                            SourceEntity = entity
                        });
                    }

                    // Stage consumed projectile for centralized reclamation in Step 9
                    projectileDeactivationQueue.Enqueue(entity);
                }
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
