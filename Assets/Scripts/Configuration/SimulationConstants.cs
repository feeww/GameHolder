using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public static class SimulationConstants
    {
        public const int MaxEnemies = 3000;
        public const int MaxProjectiles = 2000;
        public const int MaxGems = 1024;
        public const float FloatingOriginThreshold = 2000.0f;
        public const float FloatingOriginThresholdSq = FloatingOriginThreshold * FloatingOriginThreshold;

        public const int JobBatchSize = 128;
        public const int EnemyGridCapacity = MaxEnemies * 2;
        public const int CrowdGridCapacity = MaxEnemies * 8;
        public const int DamageBufferCapacity = MaxProjectiles + MaxEnemies * 2;
        public const int DeactivationBufferCapacity = MaxProjectiles * 4;
        public const int CosmeticQueueCapacity = 128;
        public const int CommandQueueCapacity = 64;
        public static float2 DebugRebaseOffset => new float2(FloatingOriginThreshold + 100.25f, FloatingOriginThreshold + 100.75f);
    }
}
