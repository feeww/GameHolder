using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct TemporaryZoneJob : IJob
    {
        public SimulationAccess A;
        public float Dt;
        public void Execute()
        {
            var run = A.Run[A.State];
            var stats = A.Stats[run.Player];
            if (TemporaryZone.Update(ref run.Zone, A.Zones, A.Input[A.InputEntity], run.WorldOrigin,
                run.PreviousPlayerPosition, run.PlayerPosition, run.Paused, stats.IsDead != 0, Dt))
            {
                var random = new Random(math.max(1u, run.Zone.RandomState));
                var previous = default(Unity.Collections.FixedList4096Bytes<RewardChoice>);
                var reward = RewardRoll.StatChoice(ref random, run.Loadout, ref A.Rewards.Value, ref previous,
                    A.Zones.CharacterStats, A.Zones.WeaponStats);
                reward.Bonus *= A.Zones.BonusMultiplier;
                var weapon = A.Weapons[run.Player];
                if (RewardRoll.ApplyStat(ref run, ref stats, ref weapon, A.StartingPlayer, reward))
                {
                    run.Zone.Reward = reward;
                    run.Zone.ReceiptId++;
                    run.Zone.ReceiptActive = 1;
                    A.Stats[run.Player] = stats; A.Weapons[run.Player] = weapon;
                }
                run.Zone.RandomState = random.state;
            }
            A.Run[A.State] = run;
        }
    }
}
