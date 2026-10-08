using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct GemData : IComponentData
    {
        public ulong ExperienceValue;
        public uint Tier;            // 4 bytes
        public uint SlotIndex;       // Index in AllGems or AllChests, selected by IsChest.
        public byte IsChest;
    }

    public struct GemActiveTag : IComponentData, IEnableableComponent
    {
    }

    public struct GemSpatialRecord
    {
        public Entity Entity;        // 8 bytes
        public float2 Position;      // 8 bytes
        public ulong ExperienceValue;
        public uint Tier;            // 4 bytes
        public byte IsActive;        // 1 byte
        private byte _pad0;          // 1 byte
        private byte _pad1;          // 1 byte
        private byte _pad2;          // 1 byte
    }

    public struct GemPoolSingleton : IComponentData
    {
        public NativeRingQueue<Entity> FreeGems;
        public UnsafeList<GemSpatialRecord> AllGems;
        public const int ChestCapacity = 128;
        public NativeRingQueue<Entity> FreeChests;
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
