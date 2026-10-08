using Unity.Burst;
using Unity.Jobs;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct PublishSimulationSnapshotJob : IJob
    {
        public SimulationAccess A;
        public void Execute()
        {
            var run = A.Run[A.State];
            A.Snapshots[A.State] = new SimulationSnapshot
            {
                Player = A.Stats[run.Player], PlayerPosition = run.PlayerPosition, WorldOrigin = run.WorldOrigin,
                FirstWeapon = A.Weapons[run.Player], Loadout = run.Loadout,
                Inventory = run.Inventory, InventoryOpen = run.InventoryOpen, PendingChests = run.PendingChests,
                Rewards = run.Rewards, Zone = run.Zone, WeaponCount = run.Loadout.Count, WeaponCapacity = A.Rewards.Value.MaxWeapons,
                MaxArtifactBlocks = A.Rewards.Value.MaxArtifactBlocks, MaxUpgradeRerolls = A.Rewards.Value.MaxUpgradeRerolls,
                Generation = run.Generation, Kills = run.Kills, TotalExperience = run.TotalExperience,
                ActiveEnemies = run.ActiveEnemies, PlayerProjectiles = run.PlayerProjectiles,
                EnemyProjectiles = run.EnemyProjectiles, ActiveGems = run.ActiveGems, GodMode = run.GodMode, AutoAttack = run.AutoAttack
            };
        }
    }
}
