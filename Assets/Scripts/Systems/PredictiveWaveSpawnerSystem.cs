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
            if (run.Rewards.Active != 0) return;
            if (A.Stats[run.Player].IsDead != 0) return;
            var wave = A.Waves[A.Wave];
            wave.Timer += Dt;
            int count = run.ExtraSpawns;
            run.ExtraSpawns = 0;
            if (wave.SpawnInterval > 0 && wave.Timer >= wave.SpawnInterval)
            {
                int batches = (int)math.floor(wave.Timer / wave.SpawnInterval);
                wave.Timer -= batches * wave.SpawnInterval;
                count = math.min(SimulationConstants.MaxEnemies, count + math.min(batches, SimulationConstants.MaxEnemies) * wave.BatchSize);
            }
            if (count <= 0) { A.Waves[A.Wave] = wave; A.Run[A.State] = run; return; }
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
                ref var configs = ref A.Catalog.Value.Configs;
                float choice = random.NextFloat(0, configs[configs.Length - 1].SpawnThreshold);
                int low = 0, high = configs.Length - 1;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    if (choice < configs[middle].SpawnThreshold) high = middle; else low = middle + 1;
                }
                uint type = (uint)low;
                var config = configs[low];
                A.Transforms[enemy] = LocalTransform.FromPosition(new float3(position, 0));
                A.Previous[enemy] = new PreviousPosition { Value = position };
                A.Health[enemy] = new CurrentHealth { Value = A.Catalog.Value.Configs[(int)type].MaxHealth };
                A.Types[enemy] = new TypeId { Value = type }; A.Velocities[enemy] = default;
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
