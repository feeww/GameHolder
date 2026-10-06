using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    public static class CrowdContact
    {
        public static float PushPriority(EnemyConfigData config) =>
            math.max(CrowdConstants.MinimumPushPriority, config.Mass * config.MoveSpeed);

        public static float2 OverlapDirection(Entity self, Entity other, float2 difference, float distance)
        {
            if (distance > CrowdConstants.OverlapDirectionDistance) return difference / distance;
            uint hash = math.hash(new uint2((uint)math.min(self.Index, other.Index), (uint)math.max(self.Index, other.Index)));
            const uint directionMask = ushort.MaxValue;
            const int directionBuckets = ushort.MaxValue + 1;
            float angle = (hash & directionMask) * (2 * math.PI / directionBuckets);
            math.sincos(angle, out float sin, out float cos);
            return new float2(cos, sin) * (self.Index < other.Index ? 1 : -1);
        }

        public static float SampleDensity(UnsafeParallelHashMap<int2, CrowdCell> cells, float2 position, out float2 gradient, out float averagePriority)
        {
            float2 gridPosition = position * SpatialHashUtils.InvCellSize - .5f;
            int2 corner = (int2)math.floor(gridPosition);
            float2 fraction = gridPosition - corner;
            float density = 0;
            float prioritySum = 0;
            gradient = float2.zero;
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                int2 node = corner + new int2(x, y);
                float weight = (x == 0 ? 1 - fraction.x : fraction.x) * (y == 0 ? 1 - fraction.y : fraction.y);
                if (cells.TryGetValue(node, out var sample))
                {
                    density += sample.Density * weight * SpatialHashUtils.InvCellSize * SpatialHashUtils.InvCellSize;
                    prioritySum += sample.PrioritySum * weight * SpatialHashUtils.InvCellSize * SpatialHashUtils.InvCellSize;
                }
                // Interpolate central differences so the force, as well as the density, is continuous.
                gradient += new float2(DensityAt(cells, node + new int2(1, 0)) - DensityAt(cells, node - new int2(1, 0)),
                    DensityAt(cells, node + new int2(0, 1)) - DensityAt(cells, node - new int2(0, 1))) * (weight * .5f * SpatialHashUtils.InvCellSize);
            }
            averagePriority = prioritySum / math.max(density, NumericalConstants.MinimumDivisor);
            return density;
        }

        private static float DensityAt(UnsafeParallelHashMap<int2, CrowdCell> cells, int2 node) =>
            cells.TryGetValue(node, out var sample) ? sample.Density * SpatialHashUtils.InvCellSize * SpatialHashUtils.InvCellSize : 0;
    }

    [BurstCompile]
    public struct CrowdContactJob : IJobParallelFor
    {
        [ReadOnly] public UnsafeList<Entity> Entities;
        [ReadOnly] public UnsafeParallelMultiHashMap<uint, GridEntry> Grid;
        [ReadOnly] public UnsafeParallelHashMap<int2, CrowdCell> Cells;
        [ReadOnly] public ComponentLookup<EnemyActiveTag> Active;
        [ReadOnly] public ComponentLookup<TypeId> Types;
        [ReadOnly] public ComponentLookup<PreviousPosition> Previous;
        [ReadOnly] public ComponentLookup<SimulationRunState> Run;
        public BlobAssetReference<EnemyConfigCatalog> Catalog;
        // Each worker owns one permanent pool slot. All neighbour reads come from the immutable grid.
        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Transforms;
        [NativeDisableParallelForRestriction] public ComponentLookup<MovementVelocity> Velocities;
        [NativeDisableParallelForRestriction] public ComponentLookup<SeparationCache> Cache;
        public Entity State;
        public float Dt;

        public void Execute(int index)
        {
            if (Run[State].Paused) return;
            Entity e = Entities[index];
            if (!Active.IsComponentEnabled(e)) return;
            var run = Run[State];
            var transform = Transforms[e];
            float2 position = transform.Position.xy;
            if (math.distancesq(position, run.PlayerPosition) > CrowdConstants.Tier1RadiusSq)
            { Cache[e] = default; return; }
            var config = Catalog.Value.Configs[(int)Types[e].Value];
            float priority = CrowdContact.PushPriority(config);
            float clearance = run.PlayerCollisionRadius + config.CollisionRadius + CrowdConstants.PlayerContactSkin;
            float queryRadius = config.CollisionRadius + run.MaxEnemyRadius + CrowdConstants.CrowdSteeringMargin;
            int2 min = SpatialHashUtils.QuantizeToCell(position - queryRadius), max = SpatialHashUtils.QuantizeToCell(position + queryRadius);
            float2 correction = float2.zero, steering = float2.zero;
            float2 approach = math.normalizesafe(run.PlayerPosition - position);
            float density = CrowdContact.SampleDensity(Cells, position, out float2 gradient, out float averagePriority);
            float2 flow = -gradient * (CrowdConstants.CrowdPushSpeed / CrowdConstants.CrowdTargetDensity);
            flow *= math.min(1, CrowdConstants.CrowdPushSpeed / math.max(math.length(flow), NumericalConstants.MinimumDivisor));
            float2 contactFlow = float2.zero;
            float support = 0, obstruction = 0;
            int contacts = 0, neighbours = 0;
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                int2 cell = new int2(x, y);
                if (!Cells.TryGetValue(cell, out var crowd) || crowd.Count == 0) continue;
                if (!Grid.TryGetFirstValue(SpatialHashUtils.ComputeHash(cell), out var entry, out var iterator)) continue;
                int inspected = 0;
                float cellObstruction = 0;
                do
                {
                    if (!math.all(entry.CellCoord == cell)) continue;
                    inspected++;
                    if (entry.Entity != e)
                        Accumulate(e, entry.Entity, position - entry.Position, (config.CollisionRadius + entry.Radius) * CrowdConstants.CrowdBodyRadiusScale,
                            entry.PushPriority, priority, approach, flow, ref correction, ref steering, ref contactFlow,
                            ref support, ref cellObstruction, ref contacts, ref neighbours);
                    // ponytail: sample up to 32 bodies per cell; subdivide dense cells if individual spacing needs more detail.
                } while (inspected < CrowdConstants.MaxCrowdEntries && Grid.TryGetNextValue(out entry, ref iterator));
                obstruction += cellObstruction * crowd.Count / math.max(1, inspected);
            }
            correction /= math.max(1, contacts);
            float response = 1 - math.exp(-CrowdConstants.CrowdContactResponse * Dt);
            correction *= response;
            // Equal priorities retain the existing response; stronger bodies yield less to the local crowd.
            float mobility = averagePriority > 0 ? 2 * averagePriority / (priority + averagePriority) : 1;
            bool flat = math.lengthsq(gradient) < CrowdConstants.FlatGradientLengthSq;
            float pressure = math.saturate(density / CrowdConstants.CrowdTargetDensity - 1) * support;
            // Project pressure onto actual contact normals, so remote density cannot push across gaps.
            flow = flat ? steering * CrowdConstants.CrowdPushSpeed : contactFlow;
            flow *= math.min(1, CrowdConstants.CrowdPushSpeed / math.max(math.length(flow), NumericalConstants.MinimumDivisor));
            correction += flow * (pressure * mobility * Dt);
            var cache = Cache[e];
            float radius = (config.CollisionRadius + run.MaxEnemyRadius) * CrowdConstants.CrowdBodyRadiusScale;
            float margin = CrowdConstants.CrowdSteeringMargin;
            // Integral of the proximity kernel over the forward half-plane, weighted by approach angle.
            float obstructionArea = radius * (radius + margin) + margin * margin / 3;
            cache.Density = math.lerp(cache.Density, obstruction / math.max(obstructionArea, NumericalConstants.MinimumDivisor), response);
            if (((uint)index + run.Tick) % CrowdConstants.SteeringUpdateInterval == 0)
            {
                cache.Direction = math.normalizesafe(steering + flow * pressure);
                cache.Weight = math.max(math.saturate((float)neighbours / CrowdConstants.NeighborsForFullSteering), pressure);
            }
            Cache[e] = cache;
            correction *= math.min(1, CrowdConstants.CrowdPushSpeed * mobility * CrowdConstants.ContactCorrectionSpeedScale * Dt / math.max(math.length(correction), NumericalConstants.MinimumDivisor));
            position += correction;

            // The player owns its position. Only enemies are displaced by player contact.
            float2 fromPlayer = position - run.PlayerPosition;
            float2 start = Previous[e].Value - run.PreviousPlayerPosition;
            float2 fallback = math.normalizesafe(start, CrowdContact.OverlapDirection(e, run.Player, float2.zero, 0));
            if (SweptCollision.TryHit(start, fromPlayer, clearance, out float time))
            {
                float2 normal = math.normalizesafe(math.lerp(start, fromPlayer, time), fallback);
                position += normal * math.max(0, clearance - math.dot(fromPlayer, normal));
            }
            transform.Position = new float3(position, 0);
            Transforms[e] = transform;
            Velocities[e] = new MovementVelocity { Value = (position - Previous[e].Value) / math.max(Dt, NumericalConstants.MinimumDivisor) };
        }

        private static void Accumulate(Entity self, Entity other, float2 difference, float radius,
            float otherPriority, float priority, float2 approach, float2 flow, ref float2 correction, ref float2 steering, ref float2 contactFlow,
            ref float support, ref float obstruction, ref int contacts, ref int neighbours)
        {
            float distance = math.length(difference);
            float queryRadius = radius + CrowdConstants.CrowdSteeringMargin;
            if (distance >= queryRadius) return;
            float2 away = CrowdContact.OverlapDirection(self, other, difference, distance);
            float proximity = math.saturate((queryRadius - distance) / CrowdConstants.CrowdSteeringMargin);
            support = math.max(support, proximity);
            obstruction += proximity * math.max(0, -math.dot(away, approach)) * (otherPriority / priority);
            float share = otherPriority / (priority + otherPriority);
            contactFlow += away * (math.max(0, math.dot(away, flow)) * proximity * share);
            steering += away * (share * (1 - distance / queryRadius));
            neighbours++;
            float penetration = radius - distance - CrowdConstants.CrowdContactDeadZone;
            if (penetration <= 0) return;
            correction += away * (penetration * share);
            contacts++;
        }
    }
}
