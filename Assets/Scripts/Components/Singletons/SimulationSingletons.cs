using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct FloatingOriginConfig : IComponentData
    {
        public const float DefaultThreshold = SimulationConstants.FloatingOriginThreshold;
        public float ThresholdSq; // 4,000,000f (2000m radius threshold)
    }

    public struct EnemyPoolSingleton : IComponentData
    {
        public const int Capacity = SimulationConstants.MaxEnemies;
        public UnsafeQueue<Entity> InactiveEnemies;
        public UnsafeList<Entity> AllEnemies;
    }

    public struct DamageEvent
    {
        public ulong TargetKey;     // 8 bytes: (((ulong)(uint)TargetEntity.Index) << 32) | (ulong)(uint)TargetEntity.Version
        public Entity TargetEntity; // 8 bytes
        public float Damage;        // 4 bytes
        public uint HitFlags;       // 4 bytes
        public float2 Padding;      // 8 bytes (Total: exactly 32 bytes)

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static ulong CreateTargetKey(Entity target)
        {
            return (((ulong)(uint)target.Index) << 32) | (ulong)(uint)target.Version;
        }
    }

    [BurstCompile]
    public struct DamageEventComparator : IComparer<DamageEvent>
    {
        public int Compare(DamageEvent x, DamageEvent y)
        {
            int targetOrder = x.TargetKey < y.TargetKey ? -1 : (x.TargetKey > y.TargetKey ? 1 : 0);
            return targetOrder != 0 ? targetOrder : x.Damage.CompareTo(y.Damage);
        }
    }

    public struct PlayerDamageEvent
    {
        public float Damage;
        public float2 HitDirection;
        public Entity SourceEntity;
        public float HitTime;
    }

    public struct DamageEventQueueSingleton : IComponentData
    {
        public UnsafeQueue<DamageEvent> DamageQueue;
    }

    public struct PlayerDamageEventQueueSingleton : IComponentData
    {
        public UnsafeQueue<PlayerDamageEvent> PlayerDamageQueue;
    }

    public struct DeathEvent
    {
        public float2 Position;
        public uint TypeId;
    }

    public struct OriginRebaseEvent
    {
        public float2 Delta;
    }

    public struct PlayerHitReactionEvent
    {
        public float Damage;
        public float2 HitDirection;
    }

    public struct GemCollectEvent
    {
        public float2 Position;
        public uint ExperienceValue;
    }

    public struct SimulationBridgeQueuesSingleton : IComponentData
    {
        public UnsafeQueue<DeathEvent> DeathEventQueue;
        public UnsafeQueue<OriginRebaseEvent> RebaseEventQueue;
        public UnsafeQueue<PlayerHitReactionEvent> HitReactionEventQueue;
        public UnsafeQueue<GemCollectEvent> GemCollectEventQueue;
    }

    public struct WaveSpawnerConfig : IComponentData
    {
        public float SpawnInterval;
        public float Timer;
        public int BatchSize;
        public float MinRadius;
        public float MaxRadius;
        public uint RandomSeed;
    }

    public struct PureDotsPrefabsSingleton : IComponentData
    {
        public Entity PlayerPrefab;
        public Entity EnemyPrefab;
        public Entity PlayerProjPrefab;
        public Entity EnemyProjPrefab;
        public Entity GemPrefab;
    }
}
