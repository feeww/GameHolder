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
        public float Padding;   // 4 bytes (Total 16 bytes, 4 entities per 64-byte cache line)
    }

    public struct CurrentHealth : IComponentData
    {
        public float Value;
    }

    [Unity.Rendering.MaterialProperty("_SpriteUV")]
    public struct SpriteUVOffset : IComponentData
    {
        public float4 Value; // xy: frame scale, zw: atlas offset
    }

    [Unity.Rendering.MaterialProperty("_BaseColor")]
    public struct BaseColorOverride : IComponentData
    {
        public float4 Value; // RGBA tint/flash
    }

    public struct EnemyActiveTag : IComponentData, IEnableableComponent
    {
    }

    public struct DisableRendering : IComponentData, IEnableableComponent
    {
    }
}
