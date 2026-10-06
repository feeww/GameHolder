using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [CreateAssetMenu(menuName = "Pure DOTS/Artifact", fileName = "NewArtifact")]
    public class ArtifactDefinition : ScriptableObject
    {
        public Texture2D Texture;
        public ArtifactRarity Rarity;
        [Tooltip("Normalized texture area displayed by the inventory icon. Crop transparent margins without changing the source image.")]
        public Rect IconUV = new Rect(0, 0, 1, 1);
        [Header("Additive bonuses per collected copy")]
        [Min(0)] public float MaxHealth;
        [Min(0)] public float PickupRadius;
        [Tooltip("Health recovered per second while alive and the run is unpaused.")]
        [Min(0)] public float HealthRegeneration;

        public bool TryValidate(out string error)
        {
            var values = new float3(MaxHealth, PickupRadius, HealthRegeneration);
            var uv = new float4(IconUV.x, IconUV.y, IconUV.width, IconUV.height);
            if ((uint)Rarity > (uint)ArtifactRarity.Legendary)
            { error = $"Artifact '{name}' has an invalid rarity."; return false; }
            if (!math.all(math.isfinite(uv)) || IconUV.x < 0 || IconUV.y < 0 || IconUV.width <= 0 || IconUV.height <= 0 || IconUV.xMax > 1 || IconUV.yMax > 1)
            { error = $"Artifact '{name}' icon UV must be a nonempty rectangle inside 0-1."; return false; }
            if (!math.all(math.isfinite(values)) || math.any(values < 0) || math.any(values > 1000000) || !math.any(values > 0))
            { error = $"Artifact '{name}' needs at least one positive bonus; bonuses must be finite and between 0 and 1,000,000."; return false; }
            error = null; return true;
        }
        public ArtifactConfig ToConfig() => new ArtifactConfig
        { Rarity = Rarity, MaxHealth = MaxHealth, PickupRadius = PickupRadius, HealthRegeneration = HealthRegeneration };
    }

    [System.Serializable]
    public class ArtifactChestSettings
    {
        [Tooltip("Choose one artifact per chest. Offers are distinct; fewer cards appear when the eligible roster is smaller.")]
        [Range(1, RewardSelection.MaxChoices)] public int ChoicesPerChest = 2;
        [Header("Rarity weights (relative; zero disables a tier)")]
        [Min(0)] public float CommonWeight = 70, RareWeight = 25, EpicWeight = 5, LegendaryWeight = 1;
        public float4 RarityWeights => new float4(CommonWeight, RareWeight, EpicWeight, LegendaryWeight);

        public bool TryValidate(System.Collections.Generic.IReadOnlyList<ArtifactDefinition> artifacts, out string error)
        {
            var weights = RarityWeights;
            if (ChoicesPerChest < 1 || ChoicesPerChest > RewardSelection.MaxChoices || !math.all(math.isfinite(weights)) ||
                math.any(weights < 0) || !math.isfinite(math.csum(weights)))
            { error = $"Artifact chests require 1-{RewardSelection.MaxChoices} choices and finite nonnegative rarity weights."; return false; }
            bool eligible = artifacts == null || artifacts.Count == 0;
            if (artifacts != null)
                foreach (var artifact in artifacts)
                    if (artifact != null && (uint)artifact.Rarity <= (uint)ArtifactRarity.Legendary && weights[(int)artifact.Rarity] > 0) eligible = true;
            if (!eligible) { error = "Enable at least one rarity represented in the artifact roster."; return false; }
            error = null; return true;
        }
    }
}
