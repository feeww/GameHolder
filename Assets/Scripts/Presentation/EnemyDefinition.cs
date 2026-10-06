using System;
using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    [CreateAssetMenu(menuName = "Pure DOTS/Enemy", fileName = "NewEnemy")]
    public class EnemyDefinition : ScriptableObject
    {
        [Min(AuthoringLimits.MinimumHealth)] public float MaxHealth;
        [Range(0, AuthoringLimits.MaximumMoveSpeed)] public float MoveSpeed;
        [Range(AuthoringLimits.MinimumBodyRadius, AuthoringLimits.MaximumBodyRadius)] public float CollisionRadius;
        [Tooltip("Crowd push priority is Mass x Move Speed. Higher mass also makes the player slower when pushing through this enemy.")]
        [Min(AuthoringLimits.MinimumMass)] public float Mass = 1;
        [Min(0)] public float ContactDamage;
        [Range(AuthoringLimits.MinimumAttackInterval, AuthoringLimits.MaximumAttackInterval)] public float ContactAttackInterval;
        [Tooltip("Zero uses body contact distance. A larger value stops melee movement earlier.")]
        [Range(0, AuthoringLimits.MaximumRange)] public float MeleeStoppingDistance;
        [Min(0)] public int ExperienceValue;
        [Tooltip("Chance per death to drop a chest containing one random artifact. 0.01 = 1%.")]
        [Range(0, 1)] public float ChestDropChance = .01f;
        [Tooltip("Relative spawn frequency in the roster; 2 has twice the spawn chance of 1.")]
        [Range(AuthoringLimits.MinimumSpawnWeight, AuthoringLimits.MaximumSpawnWeight)] public float SpawnWeight = 1;

        [Header("Ranged attack")]
        public bool Ranged;
        public EnemyWeaponDefinition Weapon;
        [Tooltip("Preferred firing distance, limited by the assigned weapon's maximum range.")]
        [Range(0, AuthoringLimits.MaximumRange)] public float AttackRange;
        [Tooltip("Zero holds position; a positive value makes this enemy retreat inside that distance.")]
        [Range(0, AuthoringLimits.MaximumRange)] public float RetreatRange;

        [Header("Appearance")]
        public Texture2D Texture;
        public Color Tint = Color.white;

        public EnemyConfigData ToConfig(float previousThreshold, float playerRadius)
        {
            if (Ranged && Weapon == null) throw new InvalidOperationException($"Ranged enemy '{name}' requires an enemy weapon asset.");
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
                RetreatRange = Ranged ? Mathf.Clamp(RetreatRange, 0, range) : 0,
                Weapon = weapon, Tint = new float4(Tint.r, Tint.g, Tint.b, Tint.a)
            };
        }
    }
}
