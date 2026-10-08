using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct PredictiveWaveSpawnJob : IJob
    {
        public SimulationAccess A;
        public float Dt;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (run.Paused) return;
            if (A.Stats[run.Player].IsDead != 0) return;
            var wave = A.Waves[A.Wave];
            wave.ElapsedSeconds += Dt;
            wave.Timer += Dt;
            int count = run.ExtraSpawns;
            run.ExtraSpawns = 0;
            if (wave.SpawnInterval > 0 && wave.Timer >= wave.SpawnInterval)
            {
                int batches = (int)math.min(A.EnemyPool.AllEnemies.Length, math.floor((double)wave.Timer / wave.SpawnInterval));
                wave.Timer %= wave.SpawnInterval;
                float scale = wave.SpawnRateScalingInterval > 0
                    ? math.pow(math.max(1, wave.SpawnRateMultiplier), (float)math.floor(wave.ElapsedSeconds / wave.SpawnRateScalingInterval)) : 1;
                int batchSize = (int)math.min(A.EnemyPool.AllEnemies.Length, math.ceil((double)wave.BatchSize * scale));
                count = (int)math.min(A.EnemyPool.AllEnemies.Length, count + (double)batches * batchSize);
            }
            if (wave.LargeSpawnInterval > 0)
            {
                wave.LargeSpawnTimer += Dt;
                int batches = (int)math.min(A.EnemyPool.AllEnemies.Length, math.floor((double)wave.LargeSpawnTimer / wave.LargeSpawnInterval));
                wave.LargeSpawnTimer %= wave.LargeSpawnInterval;
                count = (int)math.min(A.EnemyPool.AllEnemies.Length, count + (double)batches * wave.LargeSpawnCount);
            }
            if (count <= 0) { A.Waves[A.Wave] = wave; A.Run[A.State] = run; return; }
            ref var configs = ref A.Catalog.Value.Configs;
            float totalWeight = 0;
            int eligibleTypes = 0;
            for (int i = 0; i < configs.Length; i++)
                if (wave.ElapsedSeconds >= configs[i].AvailableAfterSeconds)
                {
                    totalWeight += configs[i].SpawnThreshold - (i > 0 ? configs[i - 1].SpawnThreshold : 0);
                    eligibleTypes++;
                }
            if (eligibleTypes == 0) { A.Waves[A.Wave] = wave; A.Run[A.State] = run; return; }
            if (eligibleTypes == configs.Length) totalWeight = configs[configs.Length - 1].SpawnThreshold;
            float3 statScale = wave.StatScalingInterval > 0
                ? math.min(float.MaxValue, math.pow(math.max(1, wave.StatMultipliers), (float)math.floor(wave.ElapsedSeconds / wave.StatScalingInterval))) : new float3(1);
            var random = new Random(math.max(1u, wave.RandomSeed));
            bool moving = math.lengthsq(run.PlayerVelocity) >= RunDefaults.MovingSpawnVelocitySq;
            float baseAngle = math.atan2(run.PlayerVelocity.y, run.PlayerVelocity.x);
            float minRadius = math.max(RunDefaults.MinimumSpawnRadius, wave.MinRadius), maxRadius = math.max(minRadius + RunDefaults.MinimumSpawnAnnulusWidth, wave.MaxRadius);
            for (int i = 0; i < count && A.EnemyPool.InactiveEnemies.TryDequeue(out var enemy); i++)
            {
                float angle = random.NextFloat(0, 2 * math.PI);
                if (moving)
                {
                    float roll = random.NextFloat();
                    float offset = roll < RunDefaults.ForwardSpawnThreshold ? random.NextFloat(-RunDefaults.ForwardConeAngle, RunDefaults.ForwardConeAngle) : roll < RunDefaults.FlankSpawnThreshold
                        ? (random.NextBool() ? random.NextFloat(RunDefaults.ForwardConeAngle, RunDefaults.FlankConeAngle) : random.NextFloat(-RunDefaults.FlankConeAngle, -RunDefaults.ForwardConeAngle))
                        : random.NextFloat(RunDefaults.FlankConeAngle, RunDefaults.RearConeAngle);
                    angle = baseAngle + offset;
                }
                float distance = math.sqrt(random.NextFloat(minRadius * minRadius, maxRadius * maxRadius));
                float2 position = run.PlayerPosition + new float2(math.cos(angle), math.sin(angle)) * distance;
                float choice = random.NextFloat(0, totalWeight);
                int low = 0, high = configs.Length - 1;
                if (eligibleTypes == configs.Length)
                {
                    while (low < high)
                    {
                        int middle = (low + high) / 2;
                        if (choice < configs[middle].SpawnThreshold) high = middle; else low = middle + 1;
                    }
                }
                else
                    for (int j = 0; j < configs.Length; j++)
                    {
                        if (wave.ElapsedSeconds < configs[j].AvailableAfterSeconds) continue;
                        low = j;
                        choice -= configs[j].SpawnThreshold - (j > 0 ? configs[j - 1].SpawnThreshold : 0);
                        if (choice < 0) break;
                    }
                var type = new TypeId { Value = (uint)low, StatMultipliers = statScale };
                if (A.Catalog.Value.Elites.SpawnProbability > 0)
                    type.IsElite = (byte)(random.NextFloat() < A.Catalog.Value.Elites.SpawnProbability ? 1 : 0);
                var config = A.Catalog.Value.GetConfig(type);
                var transform = LocalTransform.FromPosition(new float3(position, 0));
                transform.Scale *= type.IsElite != 0 ? A.Catalog.Value.Elites.SizeMultiplier : 1;
                A.Transforms[enemy] = transform;
                A.Previous[enemy] = new PreviousPosition { Value = position };
                A.Health[enemy] = new CurrentHealth { Value = config.MaxHealth };
                A.Types[enemy] = type; A.Velocities[enemy] = default;
                A.Separation[enemy] = default; A.MeleeCooldown[enemy] = default;
                A.RangedCooldown[enemy] = new EnemyRangedCooldown { CooldownTimer = random.NextFloat(CombatConstants.InitialCooldownMinScale, 1) * config.Weapon.Interval };
                A.Enemies.SetComponentEnabled(enemy, true); A.Ranged.SetComponentEnabled(enemy, config.Weapon.Interval > 0);
                run.ActiveEnemies++;
            }
            wave.RandomSeed = random.state;
            A.Waves[A.Wave] = wave; A.Run[A.State] = run;
        }
    }
}
