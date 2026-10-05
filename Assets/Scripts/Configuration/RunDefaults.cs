using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public static class RunDefaults
    {
        public static WaveSpawnerConfig Wave => new WaveSpawnerConfig
        {
            SpawnInterval = .5f, BatchSize = 35, MinRadius = 18, MaxRadius = 40, RandomSeed = 777123u
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
