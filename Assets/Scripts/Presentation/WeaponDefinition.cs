using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    public abstract class WeaponDefinition : ScriptableObject
    {
        public WeaponType Type;
        [Range(AuthoringLimits.MinimumAttackInterval, AuthoringLimits.MaximumAttackInterval)] public float AttackInterval;
        [Min(0)] public float Damage;
        [Range(AuthoringLimits.MinimumWeaponRange, AuthoringLimits.MaximumRange)] public float AttackRange;
        [Range(AuthoringLimits.MinimumProjectileRadius, AuthoringLimits.MaximumProjectileRadius)] public float ProjectileRadius;
        [Range(AuthoringLimits.MinimumProjectileSpeed, AuthoringLimits.MaximumProjectileSpeed)] public float ProjectileSpeed;
        [Range(AuthoringLimits.MinimumProjectileLifetime, AuthoringLimits.MaximumProjectileLifetime)] public float ProjectileLifetime;
        [Range(AuthoringLimits.MinimumBlastRadius, AuthoringLimits.MaximumBlastRadius)] public float BlastRadius;
        [Range(1, AuthoringLimits.MaximumProjectileCount)] public int ProjectileCount = 1;
        [Range(0, AuthoringLimits.MaximumSpreadAngle)] public float SpreadAngle;

        public Color Tint = Color.white;

        public PlayerWeapon ToConfig() => new PlayerWeapon
        {
            Type = Type, Interval = Mathf.Clamp(AttackInterval, AuthoringLimits.MinimumAttackInterval, AuthoringLimits.MaximumAttackInterval), Damage = Mathf.Max(0, Damage),
            Range = Mathf.Clamp(AttackRange, AuthoringLimits.MinimumWeaponRange, AuthoringLimits.MaximumRange), Radius = Mathf.Clamp(ProjectileRadius, AuthoringLimits.MinimumProjectileRadius, AuthoringLimits.MaximumProjectileRadius),
            Speed = Mathf.Clamp(ProjectileSpeed, AuthoringLimits.MinimumProjectileSpeed, AuthoringLimits.MaximumProjectileSpeed), Lifetime = Mathf.Clamp(ProjectileLifetime, AuthoringLimits.MinimumProjectileLifetime, AuthoringLimits.MaximumProjectileLifetime),
            BlastRadius = Mathf.Clamp(BlastRadius, AuthoringLimits.MinimumBlastRadius, AuthoringLimits.MaximumBlastRadius), Count = Mathf.Clamp(ProjectileCount, 1, AuthoringLimits.MaximumProjectileCount),
            SpreadAngle = Mathf.Clamp(SpreadAngle, 0, AuthoringLimits.MaximumSpreadAngle),
            Color = new float4(Tint.r, Tint.g, Tint.b, Tint.a)
        };
    }
}
