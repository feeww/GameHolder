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
                Generation = run.Generation, Kills = run.Kills, TotalExperience = run.TotalExperience,
                ActiveEnemies = run.ActiveEnemies, PlayerProjectiles = run.PlayerProjectiles,
                EnemyProjectiles = run.EnemyProjectiles, ActiveGems = run.ActiveGems, GodMode = run.GodMode, AutoAttack = run.AutoAttack
            };
        }
    }
}