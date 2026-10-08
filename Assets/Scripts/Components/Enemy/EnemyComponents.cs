using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct MovementVelocity : IComponentData
    {
        public float2 Value;
    }

    public struct SeparationCache : IComponentData
    {
        public float2 Direction; // 8 bytes
        public float Weight;    // 4 bytes
        public float Density;  // Bodies per square unit; keeps the cache at 16 bytes.
    }

    public struct CurrentHealth : IComponentData
    {
        public float Value;
    }

    public struct EnemyActiveTag : IComponentData, IEnableableComponent
    {
    }

    public struct EnemyRangedCooldown : IComponentData
    {
        public float CooldownTimer;
        public float ChargeTimer;
        public Entity ChargeBeam;
    }

    public struct EnemyMeleeCooldown : IComponentData
    {
        public float CooldownTimer;
    }
}
