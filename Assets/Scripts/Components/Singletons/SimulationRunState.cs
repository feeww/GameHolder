using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct PreviousPosition : IComponentData { public float2 Value; }
    public struct EnemyRangedTag : IComponentData, IEnableableComponent { }
    public struct SimulationInput : IComponentData
    {
        public float2 Movement, ViewHalfSize;
        public double2 ViewCenterWorld;
        public byte ViewValid;
    }
    public enum SimulationCommandKind : byte { SpawnExtra, KillAll, ForceRebase, GodMode, AutoAttack, Restart, SelectReward, Inventory, BlockArtifact, RerollUpgrades, AcknowledgeZoneReward }
    public struct SimulationCommand { public SimulationCommandKind Kind; public int Value; public uint PromptId, Generation; }
    public struct SimulationCommandQueue : IComponentData { public UnsafeQueue<SimulationCommand> Commands; }
    // The only hand-off to managed consumers. Jobs never access this component.
    public struct SimulationJobFence : IComponentData { public JobHandle Handle; }
    public struct SimulationRunState : IComponentData
    {
        public Entity Player;
        public float2 PlayerPosition, PreviousPlayerPosition, PlayerVelocity, RebaseDelta;
        public PlayerLoadout Loadout;
        public RewardSelection Rewards;
        public TemporaryZoneState Zone;
        public ArtifactInventory Inventory;
        public uint ArtifactRandomState, PendingChests;
        public byte InventoryOpen;
        public bool Paused => Rewards.Active != 0 || InventoryOpen != 0 || Zone.ReceiptActive != 0;
        public double2 WorldOrigin;
        public uint Tick, Generation, Kills, AngleCounter;
        public ulong TotalExperience;
        public int ExtraSpawns, ActiveEnemies, PlayerProjectiles, EnemyProjectiles, ActiveGems;
        public float AttackTimer, MaxEnemyStep, MaxEnemyRadius, PlayerCollisionRadius;
        public byte GodMode, AutoAttack, ForceRebase, KillAllPending;
    }
    public struct SimulationSnapshot : IComponentData
    {
        public PlayerStats Player;
        public PlayerWeapon FirstWeapon;
        public PlayerLoadout Loadout;
        public RewardSelection Rewards;
        public TemporaryZoneState Zone;
        public ArtifactInventory Inventory;
        public uint PendingChests;
        public byte InventoryOpen;
        public int WeaponCount, WeaponCapacity;
        public int MaxArtifactBlocks, MaxUpgradeRerolls;
        public float2 PlayerPosition;
        public double2 WorldOrigin;
        public uint Generation, Kills;
        public ulong TotalExperience;
        public int ActiveEnemies, PlayerProjectiles, EnemyProjectiles, ActiveGems;
        public byte GodMode, AutoAttack;
    }
}
