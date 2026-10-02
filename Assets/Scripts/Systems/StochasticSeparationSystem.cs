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
            math.max(.01f, config.Mass * config.MoveSpeed);

        public static float2 OverlapDirection(Entity self, Entity other, float2 difference, float distance)
        {
            if (distance > 1e-5f) return difference / distance;
            uint hash = math.hash(new uint2((uint)math.min(self.Index, other.Index), (uint)math.max(self.Index, other.Index)));
            float angle = (hash & 65535u) * (2 * math.PI / 65536);
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
            averagePriority = prioritySum / math.max(density, 1e-6f);
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
            Entity e = Entities[index];
            if (!Active.IsComponentEnabled(e)) return;
            var run = Run[State];
            var transform = Transforms[e];
            float2 position = transform.Position.xy;
            if (math.distancesq(position, run.PlayerPosition) > SimulationConstants.Tier1RadiusSq)
            { Cache[e] = default; return; }
            var config = Catalog.Value.Configs[(int)Types[e].Value];
            float priority = CrowdContact.PushPriority(config);
            float clearance = SimulationConstants.PlayerCollisionRadius + config.CollisionRadius + SimulationConstants.PlayerContactSkin;
            float queryRadius = config.CollisionRadius + run.MaxEnemyRadius + SimulationConstants.CrowdSteeringMargin;
            int2 min = SpatialHashUtils.QuantizeToCell(position - queryRadius), max = SpatialHashUtils.QuantizeToCell(position + queryRadius);
            float2 correction = float2.zero, steering = float2.zero;
            float2 approach = math.normalizesafe(run.PlayerPosition - position);
            float density = CrowdContact.SampleDensity(Cells, position, out float2 gradient, out float averagePriority);
            float2 flow = -gradient * (SimulationConstants.CrowdPushSpeed / SimulationConstants.CrowdTargetDensity);
            flow *= math.min(1, SimulationConstants.CrowdPushSpeed / math.max(math.length(flow), 1e-6f));
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
                        Accumulate(e, entry.Entity, position - entry.Position, (config.CollisionRadius + entry.Radius) * SimulationConstants.CrowdBodyRadiusScale,
                            entry.PushPriority, priority, approach, flow, ref correction, ref steering, ref contactFlow,
                            ref support, ref cellObstruction, ref contacts, ref neighbours);
                    // ponytail: sample up to 32 bodies per cell; subdivide dense cells if individual spacing needs more detail.
                } while (inspected < SimulationConstants.MaxCrowdEntries && Grid.TryGetNextValue(out entry, ref iterator));
                obstruction += cellObstruction * crowd.Count / math.max(1, inspected);
            }
            correction /= math.max(1, contacts);
            float response = 1 - math.exp(-SimulationConstants.CrowdContactResponse * Dt);
            correction *= response;
            // Equal priorities retain the existing response; stronger bodies yield less to the local crowd.
            float mobility = averagePriority > 0 ? 2 * averagePriority / (priority + averagePriority) : 1;
            bool flat = math.lengthsq(gradient) < 1e-10f;
            float pressure = math.saturate(density / SimulationConstants.CrowdTargetDensity - 1) * support;
            // Project pressure onto actual contact normals, so remote density cannot push across gaps.
            flow = flat ? steering * SimulationConstants.CrowdPushSpeed : contactFlow;
            flow *= math.min(1, SimulationConstants.CrowdPushSpeed / math.max(math.length(flow), 1e-6f));
            correction += flow * (pressure * mobility * Dt);
            var cache = Cache[e];
            float radius = (config.CollisionRadius + run.MaxEnemyRadius) * SimulationConstants.CrowdBodyRadiusScale;
            float margin = SimulationConstants.CrowdSteeringMargin;
            // Integral of the proximity kernel over the forward half-plane, weighted by approach angle.
            float obstructionArea = radius * (radius + margin) + margin * margin / 3;
            cache.Density = math.lerp(cache.Density, obstruction / math.max(obstructionArea, 1e-6f), response);
            if (((uint)index + run.Tick & 3u) == 0)
            {
                cache.Direction = math.normalizesafe(steering + flow * pressure);
                cache.Weight = math.max(math.saturate(neighbours * .25f), pressure);
            }
            Cache[e] = cache;
            correction *= math.min(1, SimulationConstants.CrowdPushSpeed * mobility * .5f * Dt / math.max(math.length(correction), 1e-6f));
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
            Velocities[e] = new MovementVelocity { Value = (position - Previous[e].Value) / math.max(Dt, 1e-6f) };
        }

        private static void Accumulate(Entity self, Entity other, float2 difference, float radius,
            float otherPriority, float priority, float2 approach, float2 flow, ref float2 correction, ref float2 steering, ref float2 contactFlow,
            ref float support, ref float obstruction, ref int contacts, ref int neighbours)
        {
            float distance = math.length(difference);
            float queryRadius = radius + SimulationConstants.CrowdSteeringMargin;
            if (distance >= queryRadius) return;
            float2 away = CrowdContact.OverlapDirection(self, other, difference, distance);
            float proximity = math.saturate((queryRadius - distance) / SimulationConstants.CrowdSteeringMargin);
            support = math.max(support, proximity);
            obstruction += proximity * math.max(0, -math.dot(away, approach)) * (otherPriority / priority);
            float share = otherPriority / (priority + otherPriority);
            contactFlow += away * (math.max(0, math.dot(away, flow)) * proximity * share);
            steering += away * (share * (1 - distance / queryRadius));
            neighbours++;
            float penetration = radius - distance - SimulationConstants.CrowdContactDeadZone;
            if (penetration <= 0) return;
            correction += away * (penetration * share);
            contacts++;
        }
    }
}
