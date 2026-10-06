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
            if (run.Paused) return;
            var stats = A.Stats[run.Player];
            while (A.GemSpawns.TryDequeue(out var request))
            {
                if (request.IsChest != 0) { SpawnChest(request); continue; }
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
                for (int i = 0; i < A.GemPool.AllChests.Length; i++)
                {
                    var chest = A.GemPool.AllChests[i];
                    if (chest.IsActive == 0 || math.distancesq(chest.Position, run.PlayerPosition) > stats.MagnetRadius * stats.MagnetRadius) continue;
                    run.PendingChests += math.min(chest.Quantity, uint.MaxValue - run.PendingChests);
                    chest.IsActive = 0; A.GemPool.AllChests[i] = chest;
                    A.Gems.SetComponentEnabled(chest.Entity, false); A.GemPool.FreeChests.Enqueue(chest.Entity);
                }
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
                ArtifactRoll.Open(ref run, ref A.Rewards.Value);
                RewardRoll.Open(ref run.Rewards, run.Loadout, ref A.Rewards.Value);
                A.Stats[run.Player] = stats;
            }
            A.Run[A.State] = run;
        }
        private void SpawnChest(GemSpawnRequest request)
        {
            if (A.Rewards.Value.Artifacts.Length == 0) return;
            int slot;
            if (A.GemPool.FreeChests.TryDequeue(out var free)) slot = (int)A.GemData[free].SlotIndex;
            else
            {
                // ponytail: 128 visible chests; overflow stacks chest choices at the nearest chest.
                slot = -1; float nearest = float.MaxValue;
                for (int i = 0; i < A.GemPool.AllChests.Length; i++)
                {
                    var candidate = A.GemPool.AllChests[i];
                    float distance = math.distancesq(candidate.Position, request.Position);
                    if (candidate.IsActive != 0 && distance < nearest) { nearest = distance; slot = i; }
                }
                if (slot < 0) return;
                var existing = A.GemPool.AllChests[slot];
                if (existing.Quantity < uint.MaxValue) existing.Quantity++;
                A.GemPool.AllChests[slot] = existing; return;
            }
            var chest = A.GemPool.AllChests[slot];
            chest.Position = request.Position; chest.Quantity = 1; chest.IsActive = 1;
            A.GemPool.AllChests[slot] = chest;
            A.Transforms[chest.Entity] = LocalTransform.FromPosition(new float3(chest.Position, 0));
            A.Gems.SetComponentEnabled(chest.Entity, true);
        }
        public static uint ComputeTier(uint experience) => experience >= ProgressionConstants.GemTier3Experience ? 3u : experience >= ProgressionConstants.GemTier2Experience ? 2u : experience >= ProgressionConstants.GemTier1Experience ? 1u : 0u;
    }
}
