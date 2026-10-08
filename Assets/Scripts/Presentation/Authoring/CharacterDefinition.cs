using System;
using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [CreateAssetMenu(menuName = "Pure DOTS/Character", fileName = "NewCharacter")]
    public class CharacterDefinition : ScriptableObject
    {
        [Tooltip("Starting maximum and current health, in health points.")]
        [Min(AuthoringLimits.MinimumHealth)] public float MaxHealth;
        [Tooltip("Movement speed in world units per second. Zero prevents movement.")]
        [Range(0, AuthoringLimits.MaximumMoveSpeed)] public float MoveSpeed;
        [Tooltip("Body collision radius in world units.")]
        [Range(AuthoringLimits.MinimumBodyRadius, AuthoringLimits.MaximumBodyRadius)] public float CollisionRadius;
        [Tooltip("XP and chest pickup radius in world units. Zero also excludes pickup-radius upgrades.")]
        [Range(0, AuthoringLimits.MaximumRange)] public float MagnetRadius;
        [Tooltip("Protection after taking damage, in seconds. Zero permits damage on the next tick.")]
        [Range(0, AuthoringLimits.MaximumInvulnerabilityDuration)] public float InvulnerabilityDuration;
        [Tooltip("Protection after restarting the run, in seconds.")]
        [Range(0, AuthoringLimits.MaximumInvulnerabilityDuration)] public float RespawnGracePeriod;
        [Tooltip("Starting character weapon; the scene's Starting Weapon Asset can override it.")]
        public CharacterWeaponDefinition Weapon;
        [Tooltip("Character artwork. Empty renders a white quad.")]
        public Texture2D Texture;
        [Tooltip("Color multiplied with the character artwork.")]
        public Color Tint = Color.white;
        [Tooltip("Stats this character can receive in level-up rewards. Also filtered by the bootstrap's reward settings.")]
        public CharacterUpgradeStats UpgradableStats = CharacterUpgradeStats.All;

        public bool TryValidate(out string error, CharacterWeaponDefinition weaponOverride = null)
        {
            if (!math.all(math.isfinite(new float4(MaxHealth, MoveSpeed, CollisionRadius, MagnetRadius))) ||
                !math.all(math.isfinite(new float2(InvulnerabilityDuration, RespawnGracePeriod))) ||
                !math.all(math.isfinite(new float4(Tint.r, Tint.g, Tint.b, Tint.a))))
            { error = $"Character '{name}' stats and tint must be finite."; return false; }
            var weapon = weaponOverride != null ? weaponOverride : Weapon;
            if (weapon == null) { error = $"Character '{name}' requires a character weapon asset."; return false; }
            return weapon.TryValidate(out error);
        }

        public StartingPlayerConfig ToConfig(CharacterWeaponDefinition weaponOverride = null)
        {
            if (!TryValidate(out string error, weaponOverride)) throw new InvalidOperationException(error);
            var weapon = weaponOverride != null ? weaponOverride : Weapon;
            float health = Mathf.Max(AuthoringLimits.MinimumHealth, MaxHealth);
            return new StartingPlayerConfig
            {
                Stats = new PlayerStats { MaxHealth = health, CurrentHealth = health,
                    MoveSpeed = Mathf.Clamp(MoveSpeed, 0, AuthoringLimits.MaximumMoveSpeed),
                    CollisionRadius = Mathf.Clamp(CollisionRadius, AuthoringLimits.MinimumBodyRadius, AuthoringLimits.MaximumBodyRadius),
                    MagnetRadius = Mathf.Clamp(MagnetRadius, 0, AuthoringLimits.MaximumRange), Level = 1 },
                InvulnerabilityDuration = Mathf.Clamp(InvulnerabilityDuration, 0, AuthoringLimits.MaximumInvulnerabilityDuration),
                RespawnGracePeriod = Mathf.Clamp(RespawnGracePeriod, 0, AuthoringLimits.MaximumInvulnerabilityDuration),
                Weapon = weapon.ToConfig()
            };
        }
    }
}
