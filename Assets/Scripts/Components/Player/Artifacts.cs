using Unity.Collections;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public enum ArtifactRarity : byte { Common, Rare, Epic, Legendary }
    public struct ArtifactConfig { public ArtifactRarity Rarity; public float MaxHealth, PickupRadius, HealthRegeneration; }
    public struct ArtifactStack { public int Index; public uint Quantity; }
    public struct ArtifactInventory
    {
        // ponytail: sixteen artifact types; expand the roster limit and native list together if needed.
        public const int Capacity = 16;
        public FixedList512Bytes<ArtifactStack> Items;
        public ArtifactConfig Bonuses;

        public void Add(int index, uint quantity, ArtifactConfig config)
        {
            int slot = 0;
            while (slot < Items.Length && Items[slot].Index != index) slot++;
            if (slot == Items.Length) Items.Add(new ArtifactStack { Index = index });
            var stack = Items[slot];
            quantity = math.min(quantity, uint.MaxValue - stack.Quantity);
            stack.Quantity += quantity; Items[slot] = stack;
            Bonuses.MaxHealth += config.MaxHealth * quantity;
            Bonuses.PickupRadius += config.PickupRadius * quantity;
            Bonuses.HealthRegeneration += config.HealthRegeneration * quantity;
        }
        public void Apply(ref PlayerStats stats, PlayerStats baseline, UpgradeBonuses upgrades)
        {
            float previousMax = stats.MaxHealth;
            stats.MaxHealth = baseline.MaxHealth * (1 + upgrades.MaxHealth) + Bonuses.MaxHealth;
            stats.CurrentHealth = math.min(stats.MaxHealth, stats.CurrentHealth + math.max(0, stats.MaxHealth - previousMax));
            stats.MagnetRadius = baseline.MagnetRadius * (1 + upgrades.PickupRadius) + Bonuses.PickupRadius;
            stats.HealthRegeneration = baseline.HealthRegeneration + Bonuses.HealthRegeneration;
        }
    }
    public static class ArtifactRoll
    {
        public static uint SeedForRun(ref RewardCatalog catalog, uint generation)
            => math.max(1u, math.hash(new uint2(RewardRoll.SeedForRun(ref catalog, generation), 0xA471FAC7u)));

        public static void Open(ref SimulationRunState run, ref RewardCatalog catalog)
        {
            if (run.Rewards.Active != 0 || run.PendingChests == 0) return;
            var random = new Unity.Mathematics.Random(math.max(1u, run.ArtifactRandomState));
            run.Rewards.Choices.Clear();
            uint offered = 0;
            for (int slot = 0; slot < catalog.ArtifactChoicesPerChest; slot++)
            {
                int4 counts = default;
                for (int i = 0; i < catalog.Artifacts.Length; i++)
                    if ((offered & (1u << i)) == 0) counts[(int)catalog.Artifacts[i].Rarity]++;
                float4 weights = math.select(float4.zero, catalog.ArtifactRarityWeights, counts > 0);
                float total = math.csum(weights);
                if (total <= 0) break;
                float roll = random.NextFloat() * total;
                int rarity = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (weights[i] <= 0) continue;
                    rarity = i;
                    if (roll < weights[i]) break;
                    roll -= weights[i];
                }
                int selected = random.NextInt(counts[rarity]);
                for (int i = 0; i < catalog.Artifacts.Length; i++)
                    if ((offered & (1u << i)) == 0 && (int)catalog.Artifacts[i].Rarity == rarity && selected-- == 0)
                    {
                        offered |= 1u << i;
                        run.Rewards.Choices.Add(new RewardChoice { Kind = RewardKind.Artifact, ArtifactIndex = i,
                            Rarity = rarity, Color = Color((ArtifactRarity)rarity) });
                        break;
                    }
            }
            run.ArtifactRandomState = random.state;
            if (run.Rewards.Choices.Length == 0) { run.PendingChests = 0; return; }
            run.Rewards.PromptId++;
            run.Rewards.Active = 1;
        }

        private static float4 Color(ArtifactRarity rarity)
        {
            switch (rarity)
            {
                case ArtifactRarity.Rare: return new float4(.25f, .6f, 1, 1);
                case ArtifactRarity.Epic: return new float4(.8f, .35f, 1, 1);
                case ArtifactRarity.Legendary: return new float4(1, .75f, .2f, 1);
                default: return new float4(.8f, .8f, .8f, 1);
            }
        }
    }
}
