using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [WriteGroup(typeof(LocalToWorld))]
    public struct PresentationTransformOwner : IComponentData { }
    [MaterialProperty("_SpriteUV")]
    public struct SpriteUVOffset : IComponentData { public float4 Value; }
    [MaterialProperty("_BaseColor")]
    public struct BaseColorOverride : IComponentData { public float4 Value; }
    public struct GemVisualState : IComponentData
    {
        public uint Experience, Generation;
        public float FlashTimer;
        public byte WasActive;
    }
    public static class PresentationDepth
    {
        public static float Calculate(float y, float cameraY, float speed = 0) =>
            PresentationConstants.CameraZMinOffset +
            (math.clamp(y - cameraY, -PresentationConstants.CameraViewportExtentY, PresentationConstants.CameraViewportExtentY)
                + PresentationConstants.CameraViewportExtentY) * PresentationConstants.CameraDepthScale - speed * PresentationConstants.SpeedDepthScale;
        public static float4 TierColor(uint tier) => tier == 3 ? PresentationConstants.GemTier3Color : tier == 2
            ? PresentationConstants.GemTier2Color : tier == 1 ? PresentationConstants.GemTier1Color : PresentationConstants.GemTier0Color;
        public static float4 TierUV(uint tier) => new float4(PresentationConstants.GemAtlasUVScale, PresentationConstants.GemAtlasUVScale,
            (tier % PresentationConstants.GemAtlasGridSize) * PresentationConstants.GemAtlasUVScale,
            (tier / PresentationConstants.GemAtlasGridSize) * PresentationConstants.GemAtlasUVScale);
        public static float2 RebaseFloorPhase(float2 phase, float2 delta, float tileScale)
        {
            float scale = math.max(PresentationConstants.MinimumFloorTileScale, tileScale);
            return math.fmod(math.fmod(phase + delta, scale) + scale, scale);
        }
    }
}
