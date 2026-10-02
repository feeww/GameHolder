using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct PreviousPosition : IComponentData { public float2 Value; }
    public struct EnemyRangedTag : IComponentData, IEnableableComponent { }
    public struct SimulationInput : IComponentData { public float2 Movement; }
    public enum SimulationCommandKind : byte { SpawnExtra, KillAll, ForceRebase, GodMode, AutoAttack, Restart }
    public struct SimulationCommand { public SimulationCommandKind Kind; public int Value; }
    public struct SimulationCommandQueue : IComponentData { public UnsafeQueue<SimulationCommand> Commands; }
    // The only hand-off to managed consumers. Jobs never access this component.
    public struct SimulationJobFence : IComponentData { public JobHandle Handle; }
    public struct SimulationRunState : IComponentData
    {
        public Entity Player;
        public float2 PlayerPosition, PreviousPlayerPosition, PlayerVelocity, RebaseDelta;
        public double2 WorldOrigin;
        public uint Tick, Generation, Kills, TotalExperience, AngleCounter;
        public int ExtraSpawns, ActiveEnemies, PlayerProjectiles, EnemyProjectiles, ActiveGems;
        public float AttackTimer, MaxEnemyStep, MaxEnemyRadius;
        public byte GodMode, AutoAttack, ForceRebase;
    }
    public struct SimulationSnapshot : IComponentData
    {
        public PlayerStats Player;
        public float2 PlayerPosition;
        public double2 WorldOrigin;
        public uint Generation, Kills, TotalExperience;
        public int ActiveEnemies, PlayerProjectiles, EnemyProjectiles, ActiveGems;
        public byte GodMode, AutoAttack;
    }
    public static class RunDefaults
    {
        public static PlayerStats Player => new PlayerStats
        {
            MoveSpeed = SimulationConstants.PlayerDefaultMoveSpeed,
            MagnetRadius = SimulationConstants.PlayerDefaultMagnetRadius,
            CurrentHealth = SimulationConstants.PlayerDefaultMaxHealth,
            MaxHealth = SimulationConstants.PlayerDefaultMaxHealth, Level = 1
        };
        public static WaveSpawnerConfig Wave => new WaveSpawnerConfig
        {
            SpawnInterval = .5f, BatchSize = 35, MinRadius = 18, MaxRadius = 40, RandomSeed = 777123u
        };
        public const int CosmeticQueueCapacity = 128;
    }
}
