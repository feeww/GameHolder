using System;
using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [Serializable]
    public class EnemySpawnSettings
    {
        [Tooltip("Seconds between regular batches. Zero disables regular spawning.")]
        [Min(0)] public float SpawnInterval = RunDefaults.Wave.SpawnInterval;
        [Tooltip("Enemies per regular batch before scaling, limited by the available enemy pool.")]
        [Min(1)] public int BatchSize = RunDefaults.Wave.BatchSize;
        [Tooltip("Minimum spawn distance from the player, in world units.")]
        [Min(1)] public float MinRadius = RunDefaults.Wave.MinRadius;
        [Tooltip("Maximum spawn distance in world units; must exceed Min Radius by at least one.")]
        [Min(2)] public float MaxRadius = RunDefaults.Wave.MaxRadius;

        [Header("Spawn Rate Scaling")]
        [Tooltip("Active run seconds between increases. Zero disables scaling.")]
        [Min(0)] public float SpawnRateScalingInterval;
        [Tooltip("Multiplies enemies per regular batch each scaling interval. 1.1 adds 10%, compounded.")]
        [Min(1)] public float SpawnRateMultiplier = 1;

        [Header("Enemy Stat Scaling")]
        [Tooltip("Active run seconds between increases. Applies to newly spawned enemies; zero disables scaling.")]
        [Min(0)] public float StatScalingInterval;
        [Tooltip("Health multiplier per scaling interval for newly spawned enemies. 1 disables health scaling.")]
        [Min(1)] public float HealthMultiplier = 1;
        [Tooltip("Speed multiplier per scaling interval for newly spawned enemies. 1 disables speed scaling.")]
        [Min(1)] public float SpeedMultiplier = 1;
        [Tooltip("Scales both contact and ranged attack damage.")]
        [Min(1)] public float DamageMultiplier = 1;

        [Header("Large Spawns")]
        [Tooltip("Enable additional periodic enemy batches alongside regular spawning.")]
        public bool EnableLargeSpawns;
        [Tooltip("Active run seconds between additional large batches; pauses stop this timer.")]
        [Min(.01f)] public float LargeSpawnInterval = 30;
        [Tooltip("Additional enemies per large spawn, subject to the shared enemy pool limit.")]
        [Min(1)] public int LargeSpawnCount = 100;

        public WaveSpawnerConfig ToConfig() => new WaveSpawnerConfig
        {
            SpawnInterval = SpawnInterval, BatchSize = BatchSize, MinRadius = MinRadius, MaxRadius = MaxRadius,
            RandomSeed = RunDefaults.Wave.RandomSeed,
            SpawnRateScalingInterval = SpawnRateScalingInterval, SpawnRateMultiplier = SpawnRateMultiplier,
            StatScalingInterval = StatScalingInterval, StatMultipliers = new float3(HealthMultiplier, SpeedMultiplier, DamageMultiplier),
            LargeSpawnInterval = EnableLargeSpawns ? LargeSpawnInterval : 0, LargeSpawnCount = LargeSpawnCount
        };

        public bool TryValidate(out string error)
        {
            if (!math.all(math.isfinite(new float4(SpawnInterval, MinRadius, MaxRadius, SpawnRateScalingInterval))) ||
                SpawnInterval < 0 || MinRadius < 1 || MaxRadius < MinRadius + 1 || SpawnRateScalingInterval < 0 ||
                BatchSize < 1)
            { error = "Enemy spawning requires finite nonnegative intervals, positive batch sizes, and radii with at least 1 unit between them."; return false; }
            if (!math.isfinite(StatScalingInterval) || StatScalingInterval < 0 ||
                !math.all(math.isfinite(new float4(SpawnRateMultiplier, HealthMultiplier, SpeedMultiplier, DamageMultiplier))) ||
                SpawnRateMultiplier < 1 || HealthMultiplier < 1 || SpeedMultiplier < 1 || DamageMultiplier < 1)
            { error = "Enemy scaling requires a finite nonnegative interval and finite multipliers of at least 1."; return false; }
            if (EnableLargeSpawns && (!math.isfinite(LargeSpawnInterval) || LargeSpawnInterval <= 0 ||
                LargeSpawnCount < 1))
            { error = "Large spawns require a finite positive interval and a positive enemy count."; return false; }
            error = null; return true;
        }
    }
}
