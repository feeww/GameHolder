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
    }

    public struct ProjectileActiveTag : IComponentData, IEnableableComponent
    {
    }

    public struct PlayerProjectileTag : IComponentData
    {
    }

    public struct EnemyProjectileTag : IComponentData
    {
    }

    public struct EnemyProjectilePoolSingleton : IComponentData
    {
        public UnsafeQueue<Entity> InactiveProjectiles;
    }

    public struct PlayerProjectilePoolSingleton : IComponentData
    {
        public UnsafeQueue<Entity> InactiveProjectiles;
    }

    public struct ProjectileDeactivationQueueSingleton : IComponentData
    {
        public UnsafeQueue<Entity> StagedDeactivations;
    }
}
