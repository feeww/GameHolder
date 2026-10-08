using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace GameHolder.PureDots
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(GameConfigurationBootstrapSystem))]
    [UpdateBefore(typeof(SimulationBootstrapSystem))]
    public partial class PureDotsRenderBootstrapSystem : SystemBase
    {
        private UnityEngine.Object[] m_OwnedAssets;

        protected override void OnCreate() => RequireForUpdate<PureDotsPrefabsSingleton>();

        protected override void OnUpdate()
        {
            var configuration = World.GetExistingSystemManaged<GameConfigurationBootstrapSystem>();
            if (configuration?.Settings == null) return;
            Enabled = false;
            var settings = configuration.Settings;
            var character = settings.StartingCharacter;
            var em = EntityManager;
            var prefabs = SystemAPI.GetSingleton<PureDotsPrefabsSingleton>();

            var quadMesh = PureDotsAssetFactory.CreateBottomCenterQuadMesh();
            var projTex = PureDotsAssetFactory.GenerateProjectileTexture();
            var gemAtlasTex = PureDotsAssetFactory.GenerateGemAtlasTexture();
            var playerMat = PureDotsAssetFactory.CreateSpriteMaterial(character.Texture != null ? character.Texture : Texture2D.whiteTexture);
            var projMat = PureDotsAssetFactory.CreateSpriteMaterial(projTex);
            var beamMat = PureDotsAssetFactory.CreateSpriteMaterial(Texture2D.whiteTexture);
            var gemMat = PureDotsAssetFactory.CreateSpriteMaterial(gemAtlasTex);
            var chestTex = settings.ArtifactChests.Texture != null ? settings.ArtifactChests.Texture : PureDotsAssetFactory.GenerateChestTexture();
            var chestMat = PureDotsAssetFactory.CreateSpriteMaterial(chestTex);
            var owned = new List<UnityEngine.Object> { quadMesh, projTex, gemAtlasTex, playerMat, projMat, beamMat, gemMat, chestMat };
            if (settings.ArtifactChests.Texture == null) owned.Add(chestTex);
            var projectileMaterials = new List<Material> { projMat, beamMat };
            foreach (var texture in configuration.ProjectileTextures)
            {
                var material = PureDotsAssetFactory.CreateSpriteMaterial(texture);
                projectileMaterials.Add(material); owned.Add(material);
            }
            var enemyMaterials = new Material[configuration.Enemies.Count];
            for (int i = 0; i < enemyMaterials.Length; i++)
            {
                var texture = configuration.Enemies[i].Texture;
                enemyMaterials[i] = PureDotsAssetFactory.CreateSpriteMaterial(texture != null ? texture : Texture2D.whiteTexture);
                owned.Add(enemyMaterials[i]);
            }
            m_OwnedAssets = owned.ToArray();
            var renderDesc = new RenderMeshDescription(UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows: false);

            AddRendering(prefabs.PlayerPrefab, "PlayerPrefab", new RenderMeshArray(new[] { playerMat }, new[] { quadMesh }), true);
            Color tint = character.Tint;
            em.SetComponentData(prefabs.PlayerPrefab, new BaseColorOverride { Value = new float4(tint.r, tint.g, tint.b, tint.a) });
            AddRendering(prefabs.EnemyPrefab, "EnemyPrefab", new RenderMeshArray(enemyMaterials, new[] { quadMesh }), false);
            var projectileArray = new RenderMeshArray(projectileMaterials.ToArray(), new[] { quadMesh });
            AddRendering(prefabs.PlayerProjPrefab, "PlayerProjPrefab", projectileArray, false);
            AddRendering(prefabs.EnemyProjPrefab, "EnemyProjPrefab", projectileArray, false);
            AddRendering(prefabs.GemPrefab, "GemPrefab", new RenderMeshArray(new[] { gemMat, chestMat }, new[] { quadMesh }), false);
            em.AddComponent<GemVisualState>(prefabs.GemPrefab);
            em.SetComponentData(prefabs.GemPrefab, new SpriteUVOffset { Value = PresentationDepth.TierUV(0) });

            var startingWeapon = settings.StartingWeaponAsset != null ? settings.StartingWeaponAsset : character.Weapon;
            if (PureDotsHUD.Instance != null)
            {
                PureDotsHUD.Instance.BindWeapons(settings.Rewards.GetWeapons(startingWeapon));
                PureDotsHUD.Instance.BindArtifacts(settings.GetArtifacts(), settings.InventoryTexture, settings.InventoryUV);
            }

            void AddRendering(Entity prefab, string name, RenderMeshArray array, bool visible)
            {
                em.SetName(prefab, name);
                RenderMeshUtility.AddComponents(prefab, em, renderDesc, array, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                em.AddComponent<PresentationTransformOwner>(prefab);
                em.AddComponentData(prefab, new SpriteUVOffset { Value = new float4(1, 1, 0, 0) });
                em.AddComponentData(prefab, new BaseColorOverride { Value = new float4(1) });
                em.SetComponentEnabled<MaterialMeshInfo>(prefab, visible);
            }
        }

        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            if (m_OwnedAssets == null) return;
            foreach (var asset in m_OwnedAssets) GamePresentationBootstrap.DestroyOwned(asset);
            m_OwnedAssets = null;
        }
    }
}
