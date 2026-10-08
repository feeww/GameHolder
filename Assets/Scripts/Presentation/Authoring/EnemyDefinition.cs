using System;
using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [CreateAssetMenu(menuName = "Pure DOTS/Enemy", fileName = "NewEnemy")]
    public class EnemyDefinition : ScriptableObject
    {
        [Tooltip("Base health points before elite and run-time scaling.")]
        [Min(AuthoringLimits.MinimumHealth)] public float MaxHealth;
        [Tooltip("Base speed in world units per second, before elite and run-time scaling.")]
        [Range(0, AuthoringLimits.MaximumMoveSpeed)] public float MoveSpeed;
        [Tooltip("Body radius in world units, before elite size scaling.")]
        [Range(AuthoringLimits.MinimumBodyRadius, AuthoringLimits.MaximumBodyRadius)] public float CollisionRadius;
        [Tooltip("Crowd push priority is Mass x Move Speed. Higher mass also makes the player slower when pushing through this enemy.")]
        [Min(AuthoringLimits.MinimumMass)] public float Mass = 1;
        [Tooltip("Health points dealt per contact attack, before elite and run-time scaling.")]
        [Min(0)] public float ContactDamage;
        [Tooltip("Seconds between this enemy's contact attacks.")]
        [Range(AuthoringLimits.MinimumAttackInterval, AuthoringLimits.MaximumAttackInterval)] public float ContactAttackInterval;
        [Tooltip("Zero uses body contact distance. A larger value stops melee movement earlier.")]
        [Range(0, AuthoringLimits.MaximumRange)] public float MeleeStoppingDistance;
        [Tooltip("Whole XP points awarded on death, before elite scaling.")]
        [Min(0)] public int ExperienceValue;
        [Tooltip("Chance per death to drop a chest containing one random artifact. 0.01 = 1%.")]
        [Range(0, 1)] public float ChestDropChance = .01f;
        [Tooltip("Relative spawn frequency in the roster; 2 has twice the spawn chance of 1.")]
        [Range(AuthoringLimits.MinimumSpawnWeight, AuthoringLimits.MaximumSpawnWeight)] public float SpawnWeight = 1;
        [Tooltip("Earliest active run time, in seconds, when this enemy can spawn. Zero allows it immediately.")]
        [Min(0)] public float AvailableAfterSeconds;

        [Header("Ranged attack")]
        [Tooltip("Enable ranged attacks and movement rules; requires an enemy weapon.")]
        public bool Ranged;
        [Tooltip("Ranged attack weapon. Ignored by melee enemies.")]
        public EnemyWeaponDefinition Weapon;
        [Tooltip("Preferred firing distance, limited by the assigned weapon's maximum range.")]
        [Range(0, AuthoringLimits.MaximumRange)] public float AttackRange;
        [Tooltip("Zero holds position; a positive value makes this enemy retreat inside that distance.")]
        [Range(0, AuthoringLimits.MaximumRange)] public float RetreatRange;

        [Header("Appearance")]
        [Tooltip("Enemy artwork. Empty renders a white quad.")]
        public Texture2D Texture;
        [Tooltip("Color multiplied with enemy artwork. Elites also receive a gold tint.")]
        public Color Tint = Color.white;

        public bool TryValidate(out string error)
        {
            if (!math.all(math.isfinite(new float4(MaxHealth, MoveSpeed, CollisionRadius, Mass))) ||
                !math.all(math.isfinite(new float4(ContactDamage, ContactAttackInterval, MeleeStoppingDistance, SpawnWeight))) ||
                !math.all(math.isfinite(new float4(ChestDropChance, AvailableAfterSeconds, AttackRange, RetreatRange))) ||
                !math.all(math.isfinite(new float4(Tint.r, Tint.g, Tint.b, Tint.a))))
            { error = $"Enemy '{name}' stats and tint must be finite."; return false; }
            if (Ranged)
            {
                if (Weapon == null) { error = $"Ranged enemy '{name}' requires an enemy weapon asset."; return false; }
                return Weapon.TryValidate(out error);
            }
            error = null; return true;
        }

        public EnemyConfigData ToConfig(float previousThreshold, float playerRadius)
        {
            if (!TryValidate(out string error)) throw new InvalidOperationException(error);
            float radius = Mathf.Clamp(CollisionRadius, AuthoringLimits.MinimumBodyRadius, AuthoringLimits.MaximumBodyRadius);
            var weapon = Ranged ? Weapon.ToConfig() : default;
            float range = Ranged ? Mathf.Min(Mathf.Clamp(AttackRange, 0, AuthoringLimits.MaximumRange), weapon.Range)
                : Mathf.Max(Mathf.Clamp(MeleeStoppingDistance, 0, AuthoringLimits.MaximumRange), playerRadius + radius + CrowdConstants.PlayerContactSkin);
            return new EnemyConfigData
            {
                MaxHealth = Mathf.Max(AuthoringLimits.MinimumHealth, MaxHealth),
                MoveSpeed = Mathf.Clamp(MoveSpeed, 0, AuthoringLimits.MaximumMoveSpeed),
                CollisionRadius = radius, Mass = Mathf.Max(AuthoringLimits.MinimumMass, Mass),
                AttackRange = range, BaseDamage = Mathf.Max(0, ContactDamage),
                ContactAttackInterval = Mathf.Clamp(ContactAttackInterval, AuthoringLimits.MinimumAttackInterval, AuthoringLimits.MaximumAttackInterval),
                ExperienceValue = (uint)Mathf.Max(0, ExperienceValue),
                ChestDropChance = Mathf.Clamp01(ChestDropChance),
                SpawnThreshold = previousThreshold + Mathf.Clamp(SpawnWeight, AuthoringLimits.MinimumSpawnWeight, AuthoringLimits.MaximumSpawnWeight),
                AvailableAfterSeconds = Mathf.Max(0, AvailableAfterSeconds),
                RetreatRange = Ranged ? Mathf.Clamp(RetreatRange, 0, range) : 0,
                Weapon = weapon, Tint = new float4(Tint.r, Tint.g, Tint.b, Tint.a)
            };
        }
    }
}
