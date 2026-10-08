using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace GameHolder.PureDots
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(PureDotsRenderBootstrapSystem))]
    [UpdateBefore(typeof(SimulationBootstrapSystem))]
    public partial class GameConfigurationBootstrapSystem : SystemBase
    {
        public GamePresentationBootstrap Settings { get; private set; }
        private readonly List<Texture2D> m_ProjectileTextures = new List<Texture2D>();
        private readonly List<EnemyDefinition> m_Enemies = new List<EnemyDefinition>();
        public IReadOnlyList<Texture2D> ProjectileTextures => m_ProjectileTextures;
        public IReadOnlyList<EnemyDefinition> Enemies => m_Enemies;

        protected override void OnUpdate()
        {
            Enabled = false;
            if (SystemAPI.HasSingleton<StartingPlayerConfig>()) return;
            Settings = Object.FindAnyObjectByType<GamePresentationBootstrap>();
            if (Settings == null)
            { Debug.LogError("Pure DOTS requires a GamePresentationBootstrap with character, weapon, and enemy assets assigned."); return; }
            if (!Settings.TryValidateConfiguration(out string error))
            { Debug.LogError($"Pure DOTS configuration: {error}", Settings); return; }

            var indices = new Dictionary<Texture2D, int>();
            int MaterialIndex(WeaponDefinition weapon)
            {
                if (weapon.ProjectileTexture == null) return 0;
                if (indices.TryGetValue(weapon.ProjectileTexture, out int index)) return index;
                // Slots 0 and 1 belong to the default projectile and beam materials.
                index = m_ProjectileTextures.Count + 2;
                indices.Add(weapon.ProjectileTexture, index);
                m_ProjectileTextures.Add(weapon.ProjectileTexture);
                return index;
            }

            var em = EntityManager;
            var character = Settings.StartingCharacter;
            var startingWeapon = Settings.StartingWeaponAsset != null ? Settings.StartingWeaponAsset : character.Weapon;
            var player = character.ToConfig(startingWeapon);
            player.Weapon = startingWeapon.ToConfig(MaterialIndex(startingWeapon));
            em.AddComponentData(em.CreateEntity(), player);
            em.AddComponentData(em.CreateEntity(), Settings.EnemySpawning.ToConfig());
            em.AddComponentData(em.CreateEntity(), Settings.TemporaryZones.ToConfig());
            em.AddComponentData(em.CreateEntity(), new RewardCatalogSingleton
            { Catalog = Settings.Rewards.BuildCatalog(character, startingWeapon, MaterialIndex, Settings.GetArtifacts(), Settings.ArtifactChests) });

            foreach (var enemy in Settings.EnemyTypes) if (enemy != null) m_Enemies.Add(enemy);
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var catalog = ref builder.ConstructRoot<EnemyConfigCatalog>();
            catalog.Elites = Settings.Elites;
            var configs = builder.Allocate(ref catalog.Configs, m_Enemies.Count);
            float threshold = 0;
            for (int i = 0; i < configs.Length; i++)
            {
                configs[i] = m_Enemies[i].ToConfig(threshold, player.Stats.CollisionRadius);
                if (m_Enemies[i].Ranged) configs[i].Weapon.MaterialIndex = MaterialIndex(m_Enemies[i].Weapon);
                threshold = configs[i].SpawnThreshold;
            }
            em.AddComponentData(em.CreateEntity(), new EnemyConfigCatalogSingleton
            { Catalog = builder.CreateBlobAssetReference<EnemyConfigCatalog>(Allocator.Persistent) });
            if (!SystemAPI.HasSingleton<PureDotsPrefabsSingleton>())
                em.AddComponentData(em.CreateEntity(), SimulationBootstrapSystem.CreatePrefabs(em, player));
        }
    }
}
