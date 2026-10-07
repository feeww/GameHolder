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
        public float BaseDamage;
        public float ContactAttackInterval;
        public uint ExperienceValue;
        public float ChestDropChance;
        public float SpawnThreshold;
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
            if (type.IsElite == 0) return config;
            config.MaxHealth *= Elites.HealthMultiplier;
            config.MoveSpeed *= Elites.SpeedMultiplier;
            config.CollisionRadius *= Elites.SizeMultiplier;
            config.Mass *= Elites.MassMultiplier;
            config.BaseDamage *= Elites.DamageMultiplier;
            config.Weapon.Damage *= Elites.DamageMultiplier;
            config.ExperienceValue = (uint)math.min(uint.MaxValue, math.round((double)config.ExperienceValue * Elites.ExperienceMultiplier));
            config.ChestDropChance = math.saturate(config.ChestDropChance * Elites.ChestDropChanceMultiplier);
            config.Tint.xyz = math.lerp(config.Tint.xyz, new float3(1, .65f, .1f), .75f);
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
    }
}
