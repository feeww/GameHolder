using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public static class RunDefaults
    {
        public static WaveSpawnerConfig Wave => new WaveSpawnerConfig
        {
            SpawnInterval = 1, BatchSize = 4, MinRadius = 18, MaxRadius = 26, RandomSeed = 777123u,
            SpawnRateScalingInterval = 60, SpawnRateMultiplier = 1.09f,
            StatScalingInterval = 60, StatMultipliers = new float3(1.07f, 1.01f, 1.035f),
            LargeSpawnInterval = 60, LargeSpawnCount = 40
        };
        public const float MovingSpawnVelocitySq = .01f;
        public const float MinimumSpawnRadius = 1;
        public const float MinimumSpawnAnnulusWidth = 1;
        public const float ForwardSpawnThreshold = .6f;
        public const float FlankSpawnThreshold = .9f;
        public const float ForwardConeAngle = math.PI * .25f;
        public const float FlankConeAngle = math.PI * .75f;
        public const float RearConeAngle = math.PI * 1.25f;
    }
}
