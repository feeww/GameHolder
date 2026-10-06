using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace GameHolder.PureDots
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class PureDotsRenderBootstrapSystem : SystemBase
    {
        private bool m_Initialized;
        private UnityEngine.Object[] m_OwnedAssets;

        protected override void OnCreate()
        {
            m_Initialized = false;
        }

        protected override void OnUpdate()
        {
            if (m_Initialized) return;
            m_Initialized = true;

            var settings = Object.FindAnyObjectByType<GamePresentationBootstrap>();
            if (settings == null)
            {
                Debug.LogError("Pure DOTS requires a GamePresentationBootstrap with character, weapon, and enemy assets assigned.");
                return;
            }
            if (!settings.TryValidateConfiguration(out string error))
            {
                Debug.LogError($"Pure DOTS configuration: {error}", settings);
                return;
            }
            var em = EntityManager;
            var character = settings.StartingCharacter;
            var startingPlayer = character.ToConfig(settings.StartingWeaponAsset);
            em.AddComponentData(em.CreateEntity(), startingPlayer);
            em.AddComponentData(em.CreateEntity(), new RewardCatalogSingleton
            { Catalog = settings.Rewards.BuildCatalog(character, settings.StartingWeaponAsset != null ? settings.StartingWeaponAsset : character.Weapon) });

            Mesh quadMesh = PureDotsAssetFactory.CreateBottomCenterQuadMesh();
            var projTex = PureDotsAssetFactory.GenerateProjectileTexture();
            var gemAtlasTex = PureDotsAssetFactory.GenerateGemAtlasTexture();
            var playerMat = PureDotsAssetFactory.CreateSpriteMaterial(character.Texture != null ? character.Texture : Texture2D.whiteTexture);
            var projMat = PureDotsAssetFactory.CreateSpriteMaterial(projTex);
            var beamMat = PureDotsAssetFactory.CreateSpriteMaterial(Texture2D.whiteTexture);
            var gemMat = PureDotsAssetFactory.CreateSpriteMaterial(gemAtlasTex);
            var owned = new List<UnityEngine.Object> { quadMesh, projTex, gemAtlasTex, playerMat, projMat, beamMat, gemMat };
            var definitions = new List<EnemyDefinition>();
            foreach (var definition in settings.EnemyTypes) if (definition != null) definitions.Add(definition);
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<EnemyConfigCatalog>();
            var configs = builder.Allocate(ref root.Configs, definitions.Count);
            var enemyMaterials = new Material[definitions.Count];
            float threshold = 0;
            for (int i = 0; i < definitions.Count; i++)
            {
                configs[i] = definitions[i].ToConfig(threshold, startingPlayer.Stats.CollisionRadius);
                threshold = configs[i].SpawnThreshold;
                enemyMaterials[i] = PureDotsAssetFactory.CreateSpriteMaterial(definitions[i].Texture != null ? definitions[i].Texture : Texture2D.whiteTexture);
                owned.Add(enemyMaterials[i]);
            }
            em.AddComponentData(em.CreateEntity(), new EnemyConfigCatalogSingleton
            { Catalog = builder.CreateBlobAssetReference<EnemyConfigCatalog>(Allocator.Persistent) });
            builder.Dispose();
            m_OwnedAssets = owned.ToArray();
            var renderDesc = new RenderMeshDescription(
                UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows: false
            );

            // 3. Create Prefab Entities with Entities Graphics and Simulation components
            // Player Prefab
            var playerPrefab = em.CreateEntity();
            em.SetName(playerPrefab, "PlayerPrefab");
            var playerRMA = new RenderMeshArray(new[] { playerMat }, new[] { quadMesh });
            RenderMeshUtility.AddComponents(playerPrefab, em, renderDesc, playerRMA, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.AddComponentData(playerPrefab, Unity.Transforms.LocalTransform.FromPosition(new Unity.Mathematics.float3(0, 0, 0)));
            em.AddComponent<PresentationTransformOwner>(playerPrefab);
            em.AddComponentData(playerPrefab, new MovementVelocity { Value = Unity.Mathematics.float2.zero });
            em.AddComponentData(playerPrefab, new PreviousPosition());
            em.AddComponentData(playerPrefab, new PlayerInputData { MoveInput = Unity.Mathematics.float2.zero });
            em.AddComponentData(playerPrefab, new PlayerInvulnerability { Timer = 0.0f, InvulnerabilityDuration = startingPlayer.InvulnerabilityDuration });
            em.AddComponentData(playerPrefab, startingPlayer.Stats);
            em.AddComponentData(playerPrefab, new PlayerTag());
            em.AddComponentData(playerPrefab, startingPlayer.Weapon);
            Color tint = character.Tint;
            em.AddComponentData(playerPrefab, new BaseColorOverride { Value = new float4(tint.r, tint.g, tint.b, tint.a) });
            em.AddComponentData(playerPrefab, new SpriteUVOffset { Value = new float4(1, 1, 0, 0) });

            em.AddComponent<Prefab>(playerPrefab);

            // Enemy material indices match the native catalog and configured roster.
            var enemyPrefab = em.CreateEntity();
            em.SetName(enemyPrefab, "EnemyPrefab");
            var enemyRMA = new RenderMeshArray(enemyMaterials, new[] { quadMesh });
            RenderMeshUtility.AddComponents(enemyPrefab, em, renderDesc, enemyRMA, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.AddComponentData(enemyPrefab, Unity.Transforms.LocalTransform.FromPosition(new Unity.Mathematics.float3(0, 0, 0)));
            em.AddComponent<PresentationTransformOwner>(enemyPrefab);
            em.AddComponentData(enemyPrefab, new MovementVelocity());
            em.AddComponentData(enemyPrefab, new PreviousPosition());
            em.AddComponentData(enemyPrefab, new SeparationCache());
            em.AddComponentData(enemyPrefab, new CurrentHealth());
            em.AddComponentData(enemyPrefab, new TypeId());
            em.AddComponentData(enemyPrefab, new SpriteUVOffset { Value = new Unity.Mathematics.float4(1, 1, 0, 0) });
            em.AddComponentData(enemyPrefab, new BaseColorOverride { Value = new Unity.Mathematics.float4(1, 1, 1, 1) });
            em.AddComponentData(enemyPrefab, new EnemyRangedCooldown { CooldownTimer = 0.0f });
            em.AddComponentData(enemyPrefab, new EnemyMeleeCooldown { CooldownTimer = 0.0f });
            em.AddComponentData(enemyPrefab, new EnemyActiveTag());
            em.AddComponentData(enemyPrefab, new EnemyRangedTag());
            em.SetComponentEnabled<MaterialMeshInfo>(enemyPrefab, false);
            em.AddComponent<Prefab>(enemyPrefab);

            // Player Projectile Prefab
            var playerProjPrefab = em.CreateEntity();
            em.SetName(playerProjPrefab, "PlayerProjPrefab");
            var projRMA = new RenderMeshArray(new[] { projMat, beamMat }, new[] { quadMesh });
            RenderMeshUtility.AddComponents(playerProjPrefab, em, renderDesc, projRMA, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.AddComponentData(playerProjPrefab, Unity.Transforms.LocalTransform.FromPosition(new Unity.Mathematics.float3(0, 0, 0)));
            em.AddComponent<PresentationTransformOwner>(playerProjPrefab);
            em.AddComponentData(playerProjPrefab, new MovementVelocity());
            em.AddComponentData(playerProjPrefab, new PreviousPosition());
            em.AddComponentData(playerProjPrefab, new ProjectileData());
            em.AddComponentData(playerProjPrefab, new SpriteUVOffset { Value = new Unity.Mathematics.float4(1, 1, 0, 0) });
            em.AddComponentData(playerProjPrefab, new BaseColorOverride { Value = new float4(1) });
            em.AddComponentData(playerProjPrefab, new PlayerProjectileTag());
            em.AddComponentData(playerProjPrefab, new ProjectileActiveTag());
            em.AddComponentData(playerProjPrefab, new ExplosiveProjectile());
            em.AddComponentData(playerProjPrefab, new LaserBeam());
            em.SetComponentEnabled<ExplosiveProjectile>(playerProjPrefab, false);
            em.SetComponentEnabled<LaserBeam>(playerProjPrefab, false);
            em.SetComponentEnabled<MaterialMeshInfo>(playerProjPrefab, false);
            em.AddComponent<Prefab>(playerProjPrefab);

            // Enemy Projectile Prefab
            var enemyProjPrefab = em.CreateEntity();
            em.SetName(enemyProjPrefab, "EnemyProjPrefab");
            RenderMeshUtility.AddComponents(enemyProjPrefab, em, renderDesc, projRMA, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.AddComponentData(enemyProjPrefab, Unity.Transforms.LocalTransform.FromPosition(new Unity.Mathematics.float3(0, 0, 0)));
            em.AddComponent<PresentationTransformOwner>(enemyProjPrefab);
            em.AddComponentData(enemyProjPrefab, new MovementVelocity());
            em.AddComponentData(enemyProjPrefab, new PreviousPosition());
            em.AddComponentData(enemyProjPrefab, new ProjectileData());
            em.AddComponentData(enemyProjPrefab, new SpriteUVOffset { Value = new Unity.Mathematics.float4(1, 1, 0, 0) });
            em.AddComponentData(enemyProjPrefab, new BaseColorOverride { Value = new Unity.Mathematics.float4(1, 1, 1, 1) });
            em.AddComponentData(enemyProjPrefab, new EnemyProjectileTag());
            em.AddComponentData(enemyProjPrefab, new ProjectileActiveTag());
            em.AddComponentData(enemyProjPrefab, new ExplosiveProjectile());
            em.AddComponentData(enemyProjPrefab, new LaserBeam());
            em.SetComponentEnabled<ExplosiveProjectile>(enemyProjPrefab, false);
            em.SetComponentEnabled<LaserBeam>(enemyProjPrefab, false);
            em.SetComponentEnabled<MaterialMeshInfo>(enemyProjPrefab, false);
            em.AddComponent<Prefab>(enemyProjPrefab);

            // Gem Prefab (Material uses GemAtlas with 2x2 frames for Tiers 0-3)
            var gemPrefab = em.CreateEntity();
            em.SetName(gemPrefab, "GemPrefab");
            var gemRMA = new RenderMeshArray(new[] { gemMat }, new[] { quadMesh });
            RenderMeshUtility.AddComponents(gemPrefab, em, renderDesc, gemRMA, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.AddComponentData(gemPrefab, Unity.Transforms.LocalTransform.FromPosition(new Unity.Mathematics.float3(0, 0, 0)));
            em.AddComponent<PresentationTransformOwner>(gemPrefab);
            em.AddComponentData(gemPrefab, new GemData());
            em.AddComponentData(gemPrefab, new GemVisualState());
            em.AddComponentData(gemPrefab, new SpriteUVOffset { Value = PresentationDepth.TierUV(0) });
            em.AddComponentData(gemPrefab, new BaseColorOverride { Value = new Unity.Mathematics.float4(1, 1, 1, 1) });
            em.AddComponentData(gemPrefab, new GemActiveTag());
            em.SetComponentEnabled<MaterialMeshInfo>(gemPrefab, false);
            em.AddComponent<Prefab>(gemPrefab);

            // 4. Save to PureDotsPrefabsSingleton
            var prefabsSingleton = em.CreateEntity();
            em.AddComponentData(prefabsSingleton, new PureDotsPrefabsSingleton
            {
                PlayerPrefab = playerPrefab,
                EnemyPrefab = enemyPrefab,
                PlayerProjPrefab = playerProjPrefab,
                EnemyProjPrefab = enemyProjPrefab,
                GemPrefab = gemPrefab
            });
        }
        protected override void OnDestroy()
        {
            EntityManager.CompleteAllTrackedJobs();
            if (m_OwnedAssets == null) return;
            foreach (var asset in m_OwnedAssets)
            {
                if (asset == null) continue;
                if (Application.isPlaying) Object.Destroy(asset); else Object.DestroyImmediate(asset);
            }
            m_OwnedAssets = null;
        }
    }
}
