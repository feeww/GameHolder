using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct GemData : IComponentData
    {
        public uint ExperienceValue; // 4 bytes
        public uint Tier;            // 4 bytes
        public uint SlotIndex;       // 4 bytes (0 to 1023, links entity to AllGems in O(1))
        public byte IsChest;
    }

    public struct GemActiveTag : IComponentData, IEnableableComponent
    {
    }

    public struct GemSpatialRecord
    {
        public Entity Entity;        // 8 bytes
        public float2 Position;      // 8 bytes
        public uint ExperienceValue; // 4 bytes
        public uint Tier;            // 4 bytes
        public byte IsActive;        // 1 byte
        private byte _pad0;          // 1 byte
        private byte _pad1;          // 1 byte
        private byte _pad2;          // 1 byte
        public float Padding;        // 4 bytes (Total: exactly 32 bytes, cache aligned)
    }

    public struct GemPoolSingleton : IComponentData
    {
        public const int Capacity = SimulationConstants.MaxGems; // Single Source of Truth
        public UnsafeQueue<Entity> FreeGems;
        public UnsafeList<GemSpatialRecord> AllGems; // Exactly 1024 slots (32 KB, L1D cache)
        public const int ChestCapacity = 128;
        public UnsafeQueue<Entity> FreeChests;
        public UnsafeList<ArtifactChest> AllChests;
    }

    public struct ArtifactChest
    {
        public Entity Entity;
        public float2 Position;
        public uint Quantity;
        public byte IsActive;
    }

    public struct GemSpawnRequest
    {
        public float2 Position;
        public uint ExperienceValue;
        public byte IsChest;
    }

    public struct GemSpawnQueueSingleton : IComponentData
    {
        public UnsafeQueue<GemSpawnRequest> SpawnQueue;
    }
}
