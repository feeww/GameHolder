using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [CreateAssetMenu(menuName = "Pure DOTS/Artifact", fileName = "NewArtifact")]
    public class ArtifactDefinition : ScriptableObject
    {
        [Tooltip("Artifact inventory artwork. Empty hides the artifact icon.")]
        public Texture2D Texture;
        [Tooltip("Choose a rarity from Rarities & Drop Chances on the scene's Game Presentation Bootstrap.")]
        public int Rarity;
        [Tooltip("Normalized texture area displayed by the inventory icon. Crop transparent margins without changing the source image.")]
        public Rect IconUV = new Rect(0, 0, 1, 1);
        [Header("Additive bonuses per collected copy")]
        [Tooltip("Health points added per collected copy; current health increases by the same amount.")]
        [Min(0)] public float MaxHealth;
        [Tooltip("World units added to pickup radius per collected copy.")]
        [Min(0)] public float PickupRadius;
        [Tooltip("Health recovered per second while alive and the run is unpaused.")]
        [Min(0)] public float HealthRegeneration;

        public bool TryValidate(out string error, RewardSettings rewards = null)
        {
            var values = new float3(MaxHealth, PickupRadius, HealthRegeneration);
            var uv = new float4(IconUV.x, IconUV.y, IconUV.width, IconUV.height);
            if (Rarity < 0 || rewards != null && rewards.FindRarityIndex(Rarity) < 0)
            { error = $"Artifact '{name}' uses a removed rarity. Choose a rarity from Rarities & Drop Chances."; return false; }
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
        [Tooltip("Chest artwork. Empty uses the generated default chest.")]
        public Texture2D Texture;
        [Tooltip("Choose one artifact per chest. Offers are distinct; fewer cards appear when the eligible roster is smaller.")]
        [Range(1, RewardSelection.MaxChoices)] public int ChoicesPerChest = 2;
        [Tooltip("Maximum artifact types blocked from chest drops per run. Zero disables blocking; Restart clears blocked types.")]
        [Range(0, ArtifactInventory.Capacity)] public int MaxBlocksPerRun = 2;
        public bool TryValidate(System.Collections.Generic.IReadOnlyList<ArtifactDefinition> artifacts, RewardSettings rewards, out string error)
        {
            if (ChoicesPerChest < 1 || ChoicesPerChest > RewardSelection.MaxChoices || MaxBlocksPerRun < 0 ||
                MaxBlocksPerRun > ArtifactInventory.Capacity || rewards?.Rarities == null)
            { error = $"Artifact chests require 1-{RewardSelection.MaxChoices} choices, 0-{ArtifactInventory.Capacity} blocks and shared rarities."; return false; }
            float total = 0;
            foreach (var tier in rewards.Rarities)
            {
                if (tier == null || !math.isfinite(tier.Weight) || tier.Weight < 0)
                { error = "Artifact rarity weights must be finite and nonnegative."; return false; }
                total += tier.Weight;
            }
            if (!math.isfinite(total)) { error = "Artifact rarity weight total must be finite."; return false; }
            rewards.EnsureRarityIds();
            bool eligible = artifacts == null || artifacts.Count == 0;
            if (artifacts != null)
                foreach (var artifact in artifacts)
                {
                    int index = artifact == null ? -1 : rewards.FindRarityIndex(artifact.Rarity);
                    if (artifact != null && index < 0)
                    { error = $"Artifact '{artifact.name}' uses a removed rarity."; return false; }
                    if (index >= 0 && rewards.Rarities[index].Weight > 0) eligible = true;
                }
            if (!eligible) { error = "Enable at least one rarity represented in the artifact roster."; return false; }
            error = null; return true;
        }
    }
}
