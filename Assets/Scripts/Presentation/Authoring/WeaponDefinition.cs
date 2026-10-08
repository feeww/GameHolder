using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace GameHolder.PureDots
{
    public abstract class WeaponDefinition : ScriptableObject
    {
        [Tooltip("Standard fires projectiles, Explosive creates blasts, and Laser fires a beam.")]
        public WeaponType Type;
        [Tooltip("Seconds between attacks. Attack-rate upgrades reduce this interval.")]
        [Range(AuthoringLimits.MinimumAttackInterval, AuthoringLimits.MaximumAttackInterval)] public float AttackInterval;
        [Tooltip("Health points dealt per hit. Zero excludes damage upgrades.")]
        [Min(0)] public float Damage;
        [Tooltip("Maximum targeting distance in world units; also the laser beam length.")]
        [Range(AuthoringLimits.MinimumWeaponRange, AuthoringLimits.MaximumRange)] public float AttackRange;
        [Tooltip("Projectile collision radius or laser half-width, in world units.")]
        [Range(AuthoringLimits.MinimumProjectileRadius, AuthoringLimits.MaximumProjectileRadius)] public float ProjectileRadius;
        [Tooltip("Projectile speed in world units per second. Ignored by lasers.")]
        [Range(AuthoringLimits.MinimumProjectileSpeed, AuthoringLimits.MaximumProjectileSpeed)] public float ProjectileSpeed;
        [Tooltip("Projectile lifetime or laser beam duration, in seconds.")]
        [Range(AuthoringLimits.MinimumProjectileLifetime, AuthoringLimits.MaximumProjectileLifetime)] public float ProjectileLifetime;
        [Tooltip("Explosion radius in world units. Used by Explosive weapons.")]
        [Range(AuthoringLimits.MinimumBlastRadius, AuthoringLimits.MaximumBlastRadius)] public float BlastRadius;
        [Tooltip("Projectiles per Standard attack. Other types fire one.")]
        [Range(1, AuthoringLimits.MaximumProjectileCount)] public int ProjectileCount = 1;
        [Tooltip("Radians between adjacent Standard projectiles. Zero fires them in the same direction.")]
        [Range(0, AuthoringLimits.MaximumSpreadAngle)] public float SpreadAngle;

        [Header("Appearance")]
        [FormerlySerializedAs("Texture")]
        [Tooltip("Reward-card and loadout artwork. Empty hides the weapon icon.")]
        public Texture2D WeaponTexture;
        [Tooltip("Projectile / beam artwork. Empty uses the default projectile or beam.")]
        public Texture2D ProjectileTexture;
        [Tooltip("Color multiplied with projectile or beam artwork.")]
        public Color Tint = Color.white;

        public bool TryValidate(out string error)
        {
            if ((uint)Type > (uint)WeaponType.Laser ||
                !math.all(math.isfinite(new float4(AttackInterval, Damage, AttackRange, ProjectileRadius))) ||
                !math.all(math.isfinite(new float4(ProjectileSpeed, ProjectileLifetime, BlastRadius, SpreadAngle))) ||
                !math.all(math.isfinite(new float4(Tint.r, Tint.g, Tint.b, Tint.a))))
            { error = $"Weapon '{name}' requires a supported type and finite stats and tint."; return false; }
            error = null; return true;
        }

        public PlayerWeapon ToConfig(int materialIndex = 0)
        {
            if (!TryValidate(out string error)) throw new InvalidOperationException(error);
            return new PlayerWeapon
            {
                Type = Type, Interval = Mathf.Clamp(AttackInterval, AuthoringLimits.MinimumAttackInterval, AuthoringLimits.MaximumAttackInterval), Damage = Mathf.Max(0, Damage),
                Range = Mathf.Clamp(AttackRange, AuthoringLimits.MinimumWeaponRange, AuthoringLimits.MaximumRange), Radius = Mathf.Clamp(ProjectileRadius, AuthoringLimits.MinimumProjectileRadius, AuthoringLimits.MaximumProjectileRadius),
                Speed = Mathf.Clamp(ProjectileSpeed, AuthoringLimits.MinimumProjectileSpeed, AuthoringLimits.MaximumProjectileSpeed), Lifetime = Mathf.Clamp(ProjectileLifetime, AuthoringLimits.MinimumProjectileLifetime, AuthoringLimits.MaximumProjectileLifetime),
                BlastRadius = Mathf.Clamp(BlastRadius, AuthoringLimits.MinimumBlastRadius, AuthoringLimits.MaximumBlastRadius), Count = Mathf.Clamp(ProjectileCount, 1, AuthoringLimits.MaximumProjectileCount),
                SpreadAngle = Mathf.Clamp(SpreadAngle, 0, AuthoringLimits.MaximumSpreadAngle),
                Color = new float4(Tint.r, Tint.g, Tint.b, Tint.a), MaterialIndex = materialIndex,
                TextureScale = ProjectileTexture != null ? new float2(ProjectileTexture.width, ProjectileTexture.height) / Mathf.Max(ProjectileTexture.width, ProjectileTexture.height) : new float2(1)
            };
        }
    }
}
