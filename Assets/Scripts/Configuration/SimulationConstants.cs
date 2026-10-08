using Unity.Mathematics;
using Unity.Entities;

namespace GameHolder.PureDots
{
    public static class SimulationConstants
    {
        public const int DefaultMaxEnemies = 10000;
        public const int DefaultMaxProjectiles = 5000;
        public const int DefaultMaxGems = 1024;
        public const float FloatingOriginThreshold = 2000.0f;
        public const float FloatingOriginThresholdSq = FloatingOriginThreshold * FloatingOriginThreshold;

        public const int JobBatchSize = 128;
        public const int CosmeticQueueCapacity = 128;
        public const int CommandQueueCapacity = 64;
        public static float2 DebugRebaseOffset => new float2(FloatingOriginThreshold + 100.25f, FloatingOriginThreshold + 100.75f);
    }

    public struct PoolLimits : IComponentData
    {
        public int MaxEnemies, MaxGems, MaxPlayerProjectiles, MaxEnemyProjectiles;
        public static PoolLimits Defaults => new PoolLimits
        {
            MaxEnemies = SimulationConstants.DefaultMaxEnemies, MaxGems = SimulationConstants.DefaultMaxGems,
            MaxPlayerProjectiles = SimulationConstants.DefaultMaxProjectiles, MaxEnemyProjectiles = SimulationConstants.DefaultMaxProjectiles
        };
        public bool IsValid => MaxEnemies > 0 && MaxGems > 0 && MaxPlayerProjectiles > 0 && MaxEnemyProjectiles > 0 &&
            MaxEnemies <= int.MaxValue / 8 && (long)MaxPlayerProjectiles + MaxEnemies * 2L <= int.MaxValue &&
            (MaxPlayerProjectiles + (long)MaxEnemyProjectiles) * 2 <= int.MaxValue;
    }
}
