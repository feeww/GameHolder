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
            bool moving = math.lengthsq(run.PlayerVelocity) >= .01f;
            float baseAngle = math.atan2(run.PlayerVelocity.y, run.PlayerVelocity.x);
            float minRadius = math.max(1, wave.MinRadius), maxRadius = math.max(minRadius + 1, wave.MaxRadius);
            for (int i = 0; i < count && A.EnemyPool.InactiveEnemies.TryDequeue(out var enemy); i++)
            {
                float angle = random.NextFloat(0, 2 * math.PI);
                if (moving)
                {
                    float roll = random.NextFloat();
                    float offset = roll < .6f ? random.NextFloat(-math.PI * .25f, math.PI * .25f) : roll < .9f
                        ? (random.NextBool() ? random.NextFloat(math.PI * .25f, math.PI * .75f) : random.NextFloat(-math.PI * .75f, -math.PI * .25f))
                        : random.NextFloat(math.PI * .75f, math.PI * 1.25f);
                    angle = baseAngle + offset;
                }
                float distance = math.sqrt(random.NextFloat(minRadius * minRadius, maxRadius * maxRadius));
                float2 position = run.PlayerPosition + new float2(math.cos(angle), math.sin(angle)) * distance;
                float choice = random.NextFloat();
                uint type = choice < .2f ? 0u : choice < .7f ? 1u : choice < .85f ? 2u : 3u;
                A.Transforms[enemy] = LocalTransform.FromPosition(new float3(position, 0));
                A.Previous[enemy] = new PreviousPosition { Value = position };
                A.Health[enemy] = new CurrentHealth { Value = A.Catalog.Value.Configs[(int)type].MaxHealth };
                A.Types[enemy] = new TypeId { Value = type }; A.Velocities[enemy] = default;
                A.Separation[enemy] = default; A.MeleeCooldown[enemy] = default;
                A.RangedCooldown[enemy] = new EnemyRangedCooldown { CooldownTimer = type == 2
                    ? random.NextFloat(.3f, SimulationConstants.RangedSkirmisherAttackInterval)
                    : type == 3 ? random.NextFloat(.5f, SimulationConstants.RangedSniperAttackInterval) : 0 };
                A.Enemies.SetComponentEnabled(enemy, true); A.Ranged.SetComponentEnabled(enemy, type >= 2);
                run.ActiveEnemies++;
            }
            wave.RandomSeed = random.state;
            A.Waves[A.Wave] = wave; A.Run[A.State] = run;
        }
    }
}
