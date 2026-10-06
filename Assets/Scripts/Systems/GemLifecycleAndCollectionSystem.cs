using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameHolder.PureDots
{
    [BurstCompile]
    public struct GemLifecycleJob : IJob
    {
        public SimulationAccess A;
        public void Execute()
        {
            var run = A.Run[A.State];
            if (run.Rewards.Active != 0) return;
            var stats = A.Stats[run.Player];
            while (A.GemSpawns.TryDequeue(out var request))
            {
                int slot;
                bool wasActive;
                if (A.GemPool.FreeGems.TryDequeue(out var free))
                { slot = (int)A.GemData[free].SlotIndex; wasActive = false; }
                else
                {
                    int furthest = -1, nearest = -1;
                    float maximum = ProgressionConstants.OffScreenGemRecycleDistanceSq, minimum = float.MaxValue;
                    for (int i = 0; i < A.GemPool.AllGems.Length; i++)
                    {
                        var candidate = A.GemPool.AllGems[i];
                        if (candidate.IsActive == 0) continue;
                        float playerDistance = math.distancesq(candidate.Position, run.PlayerPosition);
                        if (playerDistance > maximum) { maximum = playerDistance; furthest = i; }
                        float distance = math.distancesq(candidate.Position, request.Position);
                        if (distance < minimum) { minimum = distance; nearest = i; }
                    }
                    slot = furthest >= 0 ? furthest : nearest;
                    if (slot < 0) continue;
                    wasActive = true;
                    if (furthest < 0) request.Position = A.GemPool.AllGems[slot].Position;
                }
                var record = A.GemPool.AllGems[slot];
                record.Position = request.Position;
                record.ExperienceValue = (wasActive ? record.ExperienceValue : 0) + request.ExperienceValue;
                record.Tier = ComputeTier(record.ExperienceValue); record.IsActive = 1;
                A.GemPool.AllGems[slot] = record;
                A.GemData[record.Entity] = new GemData { SlotIndex = (uint)slot, Tier = record.Tier, ExperienceValue = record.ExperienceValue };
                A.Transforms[record.Entity] = LocalTransform.FromPosition(new float3(record.Position, 0));
                A.Gems.SetComponentEnabled(record.Entity, true);
                if (!wasActive) run.ActiveGems++;
            }
            if (stats.IsDead == 0)
            {
                uint gained = 0;
                for (int i = 0; i < A.GemPool.AllGems.Length; i++)
                {
                    var record = A.GemPool.AllGems[i];
                    if (record.IsActive == 0 || math.distancesq(record.Position, run.PlayerPosition) > stats.MagnetRadius * stats.MagnetRadius) continue;
                    gained += record.ExperienceValue;
                    if (A.Bridge.GemCollectEventQueue.Count < SimulationConstants.CosmeticQueueCapacity)
                        A.Bridge.GemCollectEventQueue.Enqueue(new GemCollectEvent { Position = record.Position, ExperienceValue = record.ExperienceValue });
                    record.IsActive = 0; A.GemPool.AllGems[i] = record;
                    A.Gems.SetComponentEnabled(record.Entity, false); A.GemPool.FreeGems.Enqueue(record.Entity); run.ActiveGems--;
                }
                run.TotalExperience += gained; stats.Experience += gained;
                ulong required = (ulong)stats.Level * A.Rewards.Value.ExperiencePerLevel;
                while (required > 0 && stats.Experience >= required)
                {
                    stats.Experience -= (uint)required; stats.Level++; run.Rewards.Pending++;
                    required = (ulong)stats.Level * A.Rewards.Value.ExperiencePerLevel;
                }
                RewardRoll.Open(ref run.Rewards, run.Loadout, ref A.Rewards.Value);
                A.Stats[run.Player] = stats;
            }
            A.Run[A.State] = run;
        }
        public static uint ComputeTier(uint experience) => experience >= ProgressionConstants.GemTier3Experience ? 3u : experience >= ProgressionConstants.GemTier2Experience ? 2u : experience >= ProgressionConstants.GemTier1Experience ? 1u : 0u;
    }
}
