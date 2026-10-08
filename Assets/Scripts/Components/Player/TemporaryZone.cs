using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct TemporaryZoneConfig : IComponentData
    {
        public byte Enabled;
        public int MaxActiveZones;
        public float SpawnChance, SpawnInterval, Lifetime, HoldDuration, Radius;
        public float MinEdgeDistance, MaxEdgeDistance, ViewPadding, BonusMultiplier;
        public float2 StructureSize;
        public CharacterUpgradeStats CharacterStats;
        public WeaponUpgradeStats WeaponStats;
    }

    public struct TemporaryZoneSlot
    {
        public float2 Position;
        public float Remaining, HoldTime;
    }

    public struct TemporaryZoneState
    {
        // ponytail: eight preallocated zones; expand the native list and presentation pool together if needed.
        public const int Capacity = 8;
        public FixedList512Bytes<TemporaryZoneSlot> Active;
        public float SpawnTimer;
        public uint RandomState, ReceiptId;
        public byte ReceiptActive;
        public RewardChoice Reward;
    }

    public static class TemporaryZone
    {
        public static uint SeedForRun(ref RewardCatalog catalog, uint generation)
            => math.max(1u, math.hash(new uint2(RewardRoll.SeedForRun(ref catalog, generation), 0x20AEu)));

        public static float2 SpawnPosition(ref Random random, float2 viewCenter, float2 viewHalfSize, TemporaryZoneConfig config)
        {
            float angle = random.NextFloat(0, 2 * math.PI);
            float2 direction = new float2(math.cos(angle), math.sin(angle));
            // Expand the viewport by the whole structure and capture circle before placing it outside.
            float2 bounds = viewHalfSize + math.max(new float2(config.Radius), config.StructureSize) + config.ViewPadding;
            float2 distances = bounds / math.max(math.abs(direction), new float2(0.00001f));
            return viewCenter + direction * (math.cmin(distances) + random.NextFloat(config.MinEdgeDistance, config.MaxEdgeDistance));
        }

        public static bool Update(ref TemporaryZoneState zone, TemporaryZoneConfig config, SimulationInput input,
            double2 origin, float2 previousPosition, float2 playerPosition, bool paused, bool dead, float dt)
        {
            if (dead || config.Enabled == 0) { zone.Active.Clear(); zone.ReceiptActive = 0; return false; }
            if (paused || zone.ReceiptActive != 0 || dt <= 0) return false;
            zone.SpawnTimer += dt;
            bool spawnTick = zone.SpawnTimer >= config.SpawnInterval;
            // ponytail: one trial per update; discard missed ticks to bound work after a stall.
            if (spawnTick) zone.SpawnTimer %= config.SpawnInterval;
            for (int i = zone.Active.Length - 1; i >= 0; i--)
            {
                var slot = zone.Active[i];
                float elapsed = math.min(dt, slot.Remaining);
                // Both endpoints must be inside: a fast pass through the zone cannot count as a hold.
                bool inside = math.distancesq(previousPosition, slot.Position) <= config.Radius * config.Radius &&
                    math.distancesq(playerPosition, slot.Position) <= config.Radius * config.Radius;
                slot.HoldTime = inside ? slot.HoldTime + elapsed : 0;
                slot.Remaining = math.max(0, slot.Remaining - dt);
                if (slot.HoldTime >= config.HoldDuration)
                {
                    zone.Active.RemoveAt(i);
                    return true;
                }
                if (slot.Remaining <= 0) zone.Active.RemoveAt(i);
                else zone.Active[i] = slot;
            }
            if (!spawnTick) return false;
            if (zone.Active.Length >= config.MaxActiveZones || input.ViewValid == 0) return false;
            var random = new Random(math.max(1u, zone.RandomState));
            if (random.NextFloat() < config.SpawnChance)
            {
                zone.Active.Add(new TemporaryZoneSlot { Position = SpawnPosition(ref random,
                    (float2)(input.ViewCenterWorld - origin), input.ViewHalfSize, config), Remaining = config.Lifetime });
            }
            zone.RandomState = random.state;
            return false;
        }

        public static bool Acknowledge(ref SimulationRunState run, SimulationCommand command)
        {
            if (command.Kind != SimulationCommandKind.AcknowledgeZoneReward || run.Zone.ReceiptActive == 0 ||
                command.Generation != run.Generation || command.PromptId != run.Zone.ReceiptId) return false;
            run.Zone.ReceiptActive = 0;
            return true;
        }
    }
}
