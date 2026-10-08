using System;
using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [Serializable]
    public class TemporaryZoneSettings
    {
        [Tooltip("Enable capture zones. Disabled zones do not spawn or offer rewards.")]
        public bool Enabled = true;
        [Tooltip("Probability of a zone spawn at each interval, from 0 to 1.")]
        [Range(0, 1)] public float SpawnChance = .1f;
        [Tooltip("Active run seconds between zone spawn attempts.")]
        [Min(.01f)] public float SpawnInterval = 10;
        [Tooltip("Maximum simultaneous zones, limited by the preallocated zone capacity.")]
        [Range(1, TemporaryZoneState.Capacity)] public int MaxActiveZones = 1;
        [Tooltip("Active run seconds before an uncaptured zone expires.")]
        [Min(.01f)] public float Lifetime = 60;
        [Tooltip("Seconds the player must stay inside to capture the zone. Leaving resets progress.")]
        [Min(.01f)] public float HoldDuration = 5;
        [Tooltip("Capture-circle radius in world units.")]
        [Min(.01f)] public float Radius = 3;
        [Tooltip("Random distance beyond the camera edge, in world units. The structure and capture circle are kept outside the view.")]
        [Min(0)] public float MinEdgeDistance = 2;
        [Tooltip("Maximum distance beyond the camera edge in world units; at least Min Edge Distance.")]
        [Min(0)] public float MaxEdgeDistance = 8;
        [Tooltip("Extra clearance in world units between zone bounds and the camera edge.")]
        [Min(0)] public float ViewPadding = 1;
        [Header("Rewards (use shared rarity chances and stat percentages)")]
        [Tooltip("Eligible character rewards, combined with shared and character asset filters.")]
        public CharacterUpgradeStats CharacterStats = CharacterUpgradeStats.All;
        [Tooltip("Eligible weapon rewards, combined with shared and weapon asset filters.")]
        public WeaponUpgradeStats WeaponStats = WeaponUpgradeStats.All;
        [Tooltip("Multiplies shared rarity bonus percentages for captured-zone rewards.")]
        [Min(.01f)] public float BonusMultiplier = 1;
        [Header("Appearance")]
        [Tooltip("Structure artwork. Empty renders a tinted white quad.")]
        public Texture2D StructureTexture;
        [Tooltip("Normalized structure texture crop; x/y are the lower-left corner.")]
        public Rect StructureUV = new Rect(0, 0, 1, 1);
        [Tooltip("Structure width and height in world units.")]
        public Vector2 StructureSize = new Vector2(4, 4);
        [Tooltip("Color multiplied with structure artwork.")]
        public Color StructureTint = Color.white;
        [Tooltip("Capture circle and progress color.")]
        public Color ZoneColor = new Color(.25f, .8f, 1, 1);
        [Tooltip("Capture-ring thickness in world units.")]
        [Min(.01f)] public float RingWidth = .08f;
        [Tooltip("Display a HUD arrow when a zone is outside the camera view.")]
        public bool ShowOffscreenArrow = true;
        [Tooltip("Right-facing arrow texture. Transparent pixels are preserved.")]
        public Texture2D ArrowTexture;
        [Tooltip("Color multiplied with arrow artwork.")]
        public Color ArrowColor = new Color(.25f, .8f, 1, 1);
        [Tooltip("Inset from the camera viewport edge, in screen pixels.")]
        [Min(0)] public float IndicatorMargin = 90;
        [Tooltip("Arrow width in screen pixels; height preserves the artwork's aspect ratio.")]
        [Min(8)] public float ArrowSize = 30;
        [Tooltip("Zone label on the offscreen indicator.")]
        public string ZoneName = "TEMPORARY ZONE";
        [Tooltip("Title displayed when receiving a zone reward.")]
        public string RewardTitle = "ZONE REWARD RECEIVED";

        public TemporaryZoneConfig ToConfig() => new TemporaryZoneConfig {
            Enabled = (byte)(Enabled ? 1 : 0), MaxActiveZones = MaxActiveZones,
            SpawnChance = SpawnChance, SpawnInterval = SpawnInterval, Lifetime = Lifetime,
            HoldDuration = HoldDuration, Radius = Radius, MinEdgeDistance = MinEdgeDistance,
            MaxEdgeDistance = MaxEdgeDistance, ViewPadding = ViewPadding, BonusMultiplier = BonusMultiplier,
            StructureSize = StructureSize, CharacterStats = CharacterStats, WeaponStats = WeaponStats };

        public bool TryValidate(RewardSettings rewards, CharacterDefinition character, CharacterWeaponDefinition weapon, out string error)
        {
            error = null;
            if (!Enabled) return true;
            if (!math.isfinite(SpawnChance) || SpawnChance < 0 || SpawnChance > 1 ||
                MaxActiveZones < 1 || MaxActiveZones > TemporaryZoneState.Capacity ||
                !Positive(SpawnInterval) || !Positive(Lifetime) || !Positive(HoldDuration) || HoldDuration > Lifetime || !Positive(Radius) ||
                !Nonnegative(MinEdgeDistance) || !Nonnegative(MaxEdgeDistance) || MaxEdgeDistance < MinEdgeDistance ||
                !Nonnegative(ViewPadding) || !Positive(BonusMultiplier))
            { error = "Temporary zones require a 0-1 chance, 1-8 active limit, finite positive timers/radius/bonus, hold <= lifetime, and ordered nonnegative spawn distances."; return false; }
            if (!Positive(StructureSize.x) || !Positive(StructureSize.y) || !Positive(RingWidth) ||
                !Nonnegative(IndicatorMargin) || !Positive(ArrowSize) || !Finite(StructureTint) || !Finite(ZoneColor) || !Finite(ArrowColor) ||
                !math.all(math.isfinite(new float4(StructureUV.x, StructureUV.y, StructureUV.width, StructureUV.height))) ||
                StructureUV.width <= 0 || StructureUV.height <= 0 || string.IsNullOrWhiteSpace(ZoneName) || string.IsNullOrWhiteSpace(RewardTitle))
            { error = "Temporary zone appearance requires finite colors/UVs, positive sizes, a nonnegative indicator margin, and nonempty labels."; return false; }
            if (!weapon.TryValidate(out error)) return false;
            var characterStats = CharacterStats & rewards.CharacterStats & character.UpgradableStats & CharacterUpgradeStats.All;
            if (character.MagnetRadius <= 0) characterStats &= ~CharacterUpgradeStats.PickupRadius;
            int weaponMask = RewardRoll.StatMask(CharacterUpgradeStats.None,
                WeaponStats & rewards.WeaponStats & weapon.UpgradableStats & WeaponUpgradeStats.All, weapon.ToConfig(), 1);
            if ((rewards.CharacterTargetWeight <= 0 || characterStats == 0) && (rewards.WeaponTargetWeight <= 0 || weaponMask == 0))
            { error = "Temporary zones need at least one usable character or starting weapon stat with a positive shared target weight."; return false; }
            foreach (var rarity in rewards.Rarities)
                for (int i = 0; i <= (int)UpgradeStat.Lifetime; i++)
                    if (!math.isfinite(rarity.BonusPercents.Get((UpgradeStat)i) * BonusMultiplier))
                    { error = "Temporary zone bonus multiplier overflows rarity bonuses."; return false; }
            return true;
        }
        private static bool Positive(float value) => math.isfinite(value) && value > 0;
        private static bool Nonnegative(float value) => math.isfinite(value) && value >= 0;
        private static bool Finite(Color color) => math.all(math.isfinite(new float4(color.r, color.g, color.b, color.a)));
    }
}
