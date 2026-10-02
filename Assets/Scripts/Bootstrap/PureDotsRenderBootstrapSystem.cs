using Unity.Entities;
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

            var em = EntityManager;

            // 1. Create shared bottom-center Quad mesh
            Mesh quadMesh = PureDotsAssetFactory.CreateBottomCenterQuadMesh();

            // 2. Generate procedural sprite textures & materials
            var playerTex = PureDotsAssetFactory.GeneratePlayerTexture();
            var enemyTankTex = PureDotsAssetFactory.GenerateEnemyTexture(true);
            var enemyRunnerTex = PureDotsAssetFactory.GenerateEnemyTexture(false);
            var enemySkirmisherTex = PureDotsAssetFactory.GenerateEnemyRangedSkirmisherTexture();
            var enemySniperTex = PureDotsAssetFactory.GenerateEnemyRangedSniperTexture();
            var projTex = PureDotsAssetFactory.GenerateProjectileTexture();
            var gemAtlasTex = PureDotsAssetFactory.GenerateGemAtlasTexture();

            var playerMat = PureDotsAssetFactory.CreateSpriteMaterial(playerTex);
            var enemyTankMat = PureDotsAssetFactory.CreateSpriteMaterial(enemyTankTex);
            var enemyRunnerMat = PureDotsAssetFactory.CreateSpriteMaterial(enemyRunnerTex);
            var enemySkirmisherMat = PureDotsAssetFactory.CreateSpriteMaterial(enemySkirmisherTex);
            var enemySniperMat = PureDotsAssetFactory.CreateSpriteMaterial(enemySniperTex);
            var projMat = PureDotsAssetFactory.CreateSpriteMaterial(projTex);
            var gemMat = PureDotsAssetFactory.CreateSpriteMaterial(gemAtlasTex);

            m_OwnedAssets = new UnityEngine.Object[] { quadMesh, playerTex, enemyTankTex, enemyRunnerTex, enemySkirmisherTex,
                enemySniperTex, projTex, gemAtlasTex, playerMat, enemyTankMat, enemyRunnerMat, enemySkirmisherMat, enemySniperMat, projMat, gemMat };
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
            em.AddComponentData(playerPrefab, new PlayerInvulnerability { Timer = 0.0f, InvulnerabilityDuration = SimulationConstants.PlayerDefaultInvulnDuration });
            em.AddComponentData(playerPrefab, new PlayerStats
            {
                MoveSpeed = SimulationConstants.PlayerDefaultMoveSpeed,
                MagnetRadius = SimulationConstants.PlayerDefaultMagnetRadius,
                CurrentHealth = SimulationConstants.PlayerDefaultMaxHealth,
                MaxHealth = SimulationConstants.PlayerDefaultMaxHealth,
                Experience = 0,
                Level = 1,
                IsDead = 0
            });
            em.AddComponentData(playerPrefab, new PlayerTag());

            em.AddComponent<Prefab>(playerPrefab);

            // Enemy Prefab (Array contains [0]=Tank, [1]=Runner, [2]=Skirmisher, [3]=Sniper)
            var enemyPrefab = em.CreateEntity();
            em.SetName(enemyPrefab, "EnemyPrefab");
            var enemyRMA = new RenderMeshArray(new[] { enemyTankMat, enemyRunnerMat, enemySkirmisherMat, enemySniperMat }, new[] { quadMesh });
            RenderMeshUtility.AddComponents(enemyPrefab, em, renderDesc, enemyRMA, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.AddComponentData(enemyPrefab, Unity.Transforms.LocalTransform.FromPosition(new Unity.Mathematics.float3(0, 0, 0)));
            em.AddComponent<PresentationTransformOwner>(enemyPrefab);
            em.AddComponentData(enemyPrefab, new MovementVelocity());
            em.AddComponentData(enemyPrefab, new PreviousPosition());
            em.AddComponentData(enemyPrefab, new SeparationCache());
            em.AddComponentData(enemyPrefab, new CurrentHealth { Value = 25.0f });
            em.AddComponentData(enemyPrefab, new TypeId { Value = 1 });
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
            var projRMA = new RenderMeshArray(new[] { projMat }, new[] { quadMesh });
            RenderMeshUtility.AddComponents(playerProjPrefab, em, renderDesc, projRMA, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.AddComponentData(playerProjPrefab, Unity.Transforms.LocalTransform.FromPosition(new Unity.Mathematics.float3(0, 0, 0)));
            em.AddComponent<PresentationTransformOwner>(playerProjPrefab);
            em.AddComponentData(playerProjPrefab, new MovementVelocity());
            em.AddComponentData(playerProjPrefab, new PreviousPosition());
            em.AddComponentData(playerProjPrefab, new ProjectileData
            {
                Damage = SimulationConstants.PlayerProjectileDamage,
                Radius = SimulationConstants.PlayerProjectileRadius,
                RemainingLifetime = SimulationConstants.PlayerProjectileLifetime
            });
            em.AddComponentData(playerProjPrefab, new SpriteUVOffset { Value = new Unity.Mathematics.float4(1, 1, 0, 0) });
            em.AddComponentData(playerProjPrefab, new BaseColorOverride { Value = new Unity.Mathematics.float4(0.2f, 0.9f, 1.0f, 1.0f) });
            em.AddComponentData(playerProjPrefab, new PlayerProjectileTag());
            em.AddComponentData(playerProjPrefab, new ProjectileActiveTag());
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
            em.AddComponentData(enemyProjPrefab, new ProjectileData
            {
                Damage = SimulationConstants.EnemyProjectileDamage,
                Radius = SimulationConstants.EnemyProjectileRadius,
                RemainingLifetime = SimulationConstants.EnemyProjectileLifetime
            });
            em.AddComponentData(enemyProjPrefab, new SpriteUVOffset { Value = new Unity.Mathematics.float4(1, 1, 0, 0) });
            em.AddComponentData(enemyProjPrefab, new BaseColorOverride { Value = new Unity.Mathematics.float4(1, 1, 1, 1) });
            em.AddComponentData(enemyProjPrefab, new EnemyProjectileTag());
            em.AddComponentData(enemyProjPrefab, new ProjectileActiveTag());
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
            em.AddComponentData(gemPrefab, new SpriteUVOffset { Value = new Unity.Mathematics.float4(0.5f, 0.5f, 0, 0) });
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