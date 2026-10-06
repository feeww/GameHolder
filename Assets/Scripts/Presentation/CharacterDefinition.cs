using System;
using UnityEngine;

namespace GameHolder.PureDots
{
    [CreateAssetMenu(menuName = "Pure DOTS/Character", fileName = "NewCharacter")]
    public class CharacterDefinition : ScriptableObject
    {
        [Min(AuthoringLimits.MinimumHealth)] public float MaxHealth;
        [Range(0, AuthoringLimits.MaximumMoveSpeed)] public float MoveSpeed;
        [Range(AuthoringLimits.MinimumBodyRadius, AuthoringLimits.MaximumBodyRadius)] public float CollisionRadius;
        [Range(0, AuthoringLimits.MaximumRange)] public float MagnetRadius;
        [Range(0, AuthoringLimits.MaximumInvulnerabilityDuration)] public float InvulnerabilityDuration;
        [Range(0, AuthoringLimits.MaximumInvulnerabilityDuration)] public float RespawnGracePeriod;
        public CharacterWeaponDefinition Weapon;
        public Texture2D Texture;
        public Color Tint = Color.white;
        [Tooltip("Stats this character can receive in level-up rewards. Also filtered by the bootstrap's reward settings.")]
        public CharacterUpgradeStats UpgradableStats = CharacterUpgradeStats.All;

        public StartingPlayerConfig ToConfig(CharacterWeaponDefinition weaponOverride = null)
        {
            var weapon = weaponOverride != null ? weaponOverride : Weapon;
            if (weapon == null) throw new InvalidOperationException($"Character '{name}' requires a character weapon asset.");
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
