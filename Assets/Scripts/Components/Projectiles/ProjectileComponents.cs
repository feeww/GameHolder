using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct ProjectileData : IComponentData
    {
        public float Damage;
        public float Radius;
        public float RemainingLifetime;
        public float4 Color;
        public int MaterialIndex;
        public float2 TextureScale;
        public float ActiveStepFraction; // Fraction of the current simulation tick before expiry.
    }

    public struct ProjectileActiveTag : IComponentData, IEnableableComponent
    {
    }

    public struct ExplosiveProjectile : IComponentData, IEnableableComponent
    {
        public float BlastRadius;
        public byte Detonated;
    }

    public struct LaserBeam : IComponentData, IEnableableComponent
    {
        public float2 Direction;
        public float Length;
        public byte PendingHit;
    }

    public struct PlayerProjectileTag : IComponentData
    {
    }

    public struct EnemyProjectileTag : IComponentData
    {
    }

    public struct EnemyProjectilePoolSingleton : IComponentData
    {
        public const int Capacity = SimulationConstants.MaxProjectiles;
        public UnsafeQueue<Entity> InactiveProjectiles;
        public UnsafeList<Entity> AllProjectiles;
    }

    public struct PlayerProjectilePoolSingleton : IComponentData
    {
        public const int Capacity = SimulationConstants.MaxProjectiles;
        public UnsafeQueue<Entity> InactiveProjectiles;
        public UnsafeList<Entity> AllProjectiles;
    }

    public struct ProjectileDeactivationQueueSingleton : IComponentData
    {
        public UnsafeQueue<Entity> StagedDeactivations;
    }
}
