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
            SimulationConstants.CameraZMinOffset +
            (math.clamp(y - cameraY, -SimulationConstants.CameraViewportExtentY, SimulationConstants.CameraViewportExtentY)
                + SimulationConstants.CameraViewportExtentY) * SimulationConstants.CameraDepthScale - speed * SimulationConstants.SpeedDepthScale;
        public static float4 TierColor(uint tier) => tier == 3 ? new float4(1, .84f, 0, 1) : tier == 2
            ? new float4(.9f, .3f, 1, 1) : tier == 1 ? new float4(.2f, .8f, 1, 1) : new float4(.2f, 1, .4f, 1);
        public static float4 TierUV(uint tier) => new float4(.5f, .5f, (tier & 1) * .5f, (tier >> 1) * .5f);
        public static float2 RebaseFloorPhase(float2 phase, float2 delta, float tileScale)
        {
            float scale = math.max(.1f, tileScale);
            return math.fmod(math.fmod(phase + delta, scale) + scale, scale);
        }
    }
}
