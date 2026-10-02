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
        public uint ExperienceValue;
        public float SpeedVariation;
    }

    public struct EnemyConfigCatalog
    {
        public BlobArray<EnemyConfigData> Configs;
    }

    public struct EnemyConfigCatalogSingleton : IComponentData
    {
        public BlobAssetReference<EnemyConfigCatalog> Catalog;
    }

    public struct TypeId : IComponentData
    {
        public uint Value;
    }
}
