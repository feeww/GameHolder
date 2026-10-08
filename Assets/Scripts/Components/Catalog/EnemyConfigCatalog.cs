using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace GameHolder.PureDots
{
    public struct EnemyConfigData
    {
        public float MaxHealth;
        public float MoveSpeed;
        public float CollisionRadius;
        public float Mass;
        public float AttackRange;
        public float MeleeStoppingDistance;
        public float BaseDamage;
        public float ContactAttackInterval;
        public uint ExperienceValue;
        public float ChestDropChance;
        public float SpawnThreshold;
        public float AvailableAfterSeconds;
        public float RetreatRange;
        public PlayerWeapon Weapon;
        public float4 Tint;
    }

    public struct EnemyConfigCatalog
    {
        public EliteEnemyConfig Elites;
        public BlobArray<EnemyConfigData> Configs;

        public EnemyConfigData GetConfig(TypeId type)
        {
            var config = Configs[(int)type.Value];
            if (type.IsElite != 0)
            {
                config.MaxHealth *= Elites.HealthMultiplier;
                config.MoveSpeed *= Elites.SpeedMultiplier;
                config.CollisionRadius *= Elites.SizeMultiplier;
                config.Mass *= Elites.MassMultiplier;
                config.BaseDamage *= Elites.DamageMultiplier;
                config.Weapon.Damage *= Elites.DamageMultiplier;
                config.ExperienceValue = (uint)math.min(uint.MaxValue, math.round((double)config.ExperienceValue * Elites.ExperienceMultiplier));
                config.ChestDropChance = math.saturate(config.ChestDropChance * Elites.ChestDropChanceMultiplier);
                config.Tint.xyz = math.lerp(config.Tint.xyz, new float3(1, .65f, .1f), .75f);
            }
            if (math.all(type.StatMultipliers <= 1)) return config;
            var scale = math.max(1, type.StatMultipliers);
            config.MaxHealth = (float)math.min(float.MaxValue, (double)config.MaxHealth * scale.x);
            config.MoveSpeed = (float)math.min(float.MaxValue, (double)config.MoveSpeed * scale.y);
            config.BaseDamage = (float)math.min(float.MaxValue, (double)config.BaseDamage * scale.z);
            config.Weapon.Damage = (float)math.min(float.MaxValue, (double)config.Weapon.Damage * scale.z);
            return config;
        }
    }

    public struct EliteEnemyConfig
    {
        public float SpawnProbability;
        public float HealthMultiplier, SpeedMultiplier, SizeMultiplier, MassMultiplier;
        public float DamageMultiplier, ExperienceMultiplier, ChestDropChanceMultiplier;
    }

    public struct EnemyConfigCatalogSingleton : IComponentData
    {
        public BlobAssetReference<EnemyConfigCatalog> Catalog;
    }

    public struct TypeId : IComponentData
    {
        public uint Value;
        public byte IsElite;
        public float3 StatMultipliers;
    }
}
