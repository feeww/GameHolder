using UnityEngine;

namespace GameHolder.PureDots
{
    public class GamePresentationBootstrap : MonoBehaviour
    {
        public static GamePresentationBootstrap Instance { get; private set; }
        [Header("Starting Character & Weapon (applied on Play)")]
        [Tooltip("Required starting character stats and artwork; applied on Play.")]
        [SerializeField] private CharacterDefinition m_StartingCharacter;
        [Tooltip("Overrides the character asset's weapon. Empty uses the character asset's weapon.")]
        [SerializeField] private CharacterWeaponDefinition m_StartingWeaponAsset;
        [Header("Enemy Roster (assign at least one asset)")]
        [Tooltip("Spawn roster; null entries are ignored. Assign at least one enemy.")]
        [SerializeField] private EnemyDefinition[] m_EnemyTypes = new EnemyDefinition[0];

        public CharacterDefinition StartingCharacter => m_StartingCharacter;
        public CharacterWeaponDefinition StartingWeaponAsset => m_StartingWeaponAsset;
        public EnemyDefinition[] EnemyTypes => m_EnemyTypes;

        [Header("Pool Limits (applied on Play)")]
        [Tooltip("Maximum simultaneously active enemies. Preallocated on Play; restart keeps this capacity.")]
        [Min(1)] [SerializeField] private int m_MaxEnemies = SimulationConstants.DefaultMaxEnemies;
        [Tooltip("Maximum visible XP gems. When full, new XP merges into existing gems.")]
        [Min(1)] [SerializeField] private int m_MaxGems = SimulationConstants.DefaultMaxGems;
        [Tooltip("Maximum active player projectiles, including lasers and explosion visuals. Shots stop when the pool is full.")]
        [Min(1)] [SerializeField] private int m_MaxPlayerProjectiles = SimulationConstants.DefaultMaxProjectiles;
        [Tooltip("Maximum active enemy projectiles, including laser warnings and explosion visuals. Attacks wait when the pool is full.")]
        [Min(1)] [SerializeField] private int m_MaxEnemyProjectiles = SimulationConstants.DefaultMaxProjectiles;
        public PoolLimits Pools => new PoolLimits { MaxEnemies = m_MaxEnemies, MaxGems = m_MaxGems,
            MaxPlayerProjectiles = m_MaxPlayerProjectiles, MaxEnemyProjectiles = m_MaxEnemyProjectiles };

        [Header("Enemy Spawning (applied on Play)")]
        [Tooltip("Enemy batches and difficulty scaling; applied on Play.")]
        [SerializeField] private EnemySpawnSettings m_EnemySpawning = new EnemySpawnSettings();
        public EnemySpawnSettings EnemySpawning => m_EnemySpawning;

        [Header("Elite Enemies (applied on Play)")]
        [Tooltip("Chance per spawn: 0.01 = 1%. Zero disables elites and preserves the wave RNG sequence.")]
        [Range(0, 1)] [SerializeField] private float m_EliteSpawnProbability;
        [Tooltip("Elite maximum-health multiplier, before run-time scaling.")]
        [Min(.01f)] [SerializeField] private float m_EliteHealthMultiplier = 2;
        [Tooltip("Elite movement-speed multiplier, before run-time scaling.")]
        [Min(.01f)] [SerializeField] private float m_EliteSpeedMultiplier = 1.25f;
        [Tooltip("Elite collision-radius and artwork-size multiplier.")]
        [Min(.01f)] [SerializeField] private float m_EliteSizeMultiplier = 1.25f;
        [Tooltip("Elite crowd-push mass multiplier.")]
        [Min(.01f)] [SerializeField] private float m_EliteMassMultiplier = 2;
        [Tooltip("Elite contact and ranged damage multiplier, before run-time scaling.")]
        [Min(.01f)] [SerializeField] private float m_EliteDamageMultiplier = 2;
        [Tooltip("Elite XP multiplier, rounded to whole points. Zero gives no XP.")]
        [Min(0)] [SerializeField] private float m_EliteExperienceMultiplier = 2;
        [Tooltip("Multiplies each enemy asset's chest drop chance, capped at 100%. Zero disables elite chest drops.")]
        [Min(0)] [SerializeField] private float m_EliteChestDropChanceMultiplier = 2;
        public EliteEnemyConfig Elites => new EliteEnemyConfig
        {
            SpawnProbability = m_EliteSpawnProbability,
            HealthMultiplier = m_EliteHealthMultiplier, SpeedMultiplier = m_EliteSpeedMultiplier,
            SizeMultiplier = m_EliteSizeMultiplier, MassMultiplier = m_EliteMassMultiplier,
            DamageMultiplier = m_EliteDamageMultiplier, ExperienceMultiplier = m_EliteExperienceMultiplier,
            ChestDropChanceMultiplier = m_EliteChestDropChanceMultiplier
        };

        [Header("Level-up Rewards")]
        [Tooltip("Level-up choices, weapon roster and shared rarity rules; applied on Play.")]
        [SerializeField] private RewardSettings m_Rewards = new RewardSettings();
        public RewardSettings Rewards => m_Rewards;

        [Header("Temporary Stat Zones (applied on Play)")]
        [Tooltip("Capture-zone rules, rewards and artwork; simulation rules apply on Play.")]
        [SerializeField] private TemporaryZoneSettings m_TemporaryZones = new TemporaryZoneSettings();
        public TemporaryZoneSettings TemporaryZones => m_TemporaryZones;
        public Camera GameCamera => m_Camera;
        private TemporaryZonePresentation m_ZonePresentation;

        [Header("Artifact Chests (choose one artifact per chest)")]
        [Tooltip("Chest reward roster; null and duplicate entries are ignored.")]
        [SerializeField] private ArtifactDefinition[] m_Artifacts = new ArtifactDefinition[0];
        [Tooltip("Chest artwork, choices and blocking allowance; applied on Play.")]
        [SerializeField] private ArtifactChestSettings m_ArtifactChests = new ArtifactChestSettings();
        public ArtifactChestSettings ArtifactChests => m_ArtifactChests;
        private void OnValidate() => m_Rewards?.EnsureRarityIds();
        [Tooltip("Inventory button artwork. Empty hides the icon; the Inventory button remains available.")]
        [SerializeField] private Texture2D m_InventoryTexture;
        [Tooltip("Normalized inventory-icon crop; x/y are the lower-left corner.")]
        [SerializeField] private Rect m_InventoryUV = new Rect(0, 0, 1, 1);
        public Texture2D InventoryTexture => m_InventoryTexture;
        public Rect InventoryUV => m_InventoryUV;
        public System.Collections.Generic.List<ArtifactDefinition> GetArtifacts()
        {
            var artifacts = new System.Collections.Generic.List<ArtifactDefinition>();
            if (m_Artifacts != null)
                foreach (var artifact in m_Artifacts) if (artifact != null && !artifacts.Contains(artifact)) artifacts.Add(artifact);
            return artifacts;
        }

        [Header("Rendering & Camera")]
        [Tooltip("Gameplay camera. Empty uses Main Camera or creates one. Startup applies orthographic camera defaults.")]
        [SerializeField] private Camera m_Camera;
        [Tooltip("Infinite floor material. Empty uses the default. A runtime copy preserves the source asset.")]
        [SerializeField] private Material m_FloorMaterial;

        [Header("Audio & VFX")]
        [Tooltip("Enemy death sound. Empty uses the generated placeholder until audio is configured.")]
        [SerializeField] private AudioClip m_EnemyDeathClip;
        [Tooltip("Gem collection sound. Empty uses the generated placeholder until audio is configured.")]
        [SerializeField] private AudioClip m_GemCollectClip;
        [Tooltip("Player hit and death sound. Empty uses the generated placeholder until audio is configured.")]
        [SerializeField] private AudioClip m_PlayerHitClip;

        public static AudioClip EnemyDeathClip { get; private set; }
        public static AudioClip GemCollectClip { get; private set; }
        public static AudioClip PlayerHitClip { get; private set; }

        private GameObject m_FloorQuad;
        private bool m_OwnEnemyClip, m_OwnGemClip, m_OwnHitClip, m_OwnFloorMaterial;
        private CameraPresentationController m_OwnCameraController;
        private GameObject m_OwnCamera;
        private MeshRenderer m_FloorRenderer;
        private Unity.Mathematics.float2 m_FloorPhase;
        private float m_TileScale = PresentationConstants.FloorTileScale;
        private static readonly int OriginTileOffset = Shader.PropertyToID("_OriginTileOffset");

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (m_EnemyDeathClip == null) { m_EnemyDeathClip = PureDotsAudioGenerator.CreateEnemyDeathClip(); m_OwnEnemyClip = true; }
            if (m_GemCollectClip == null) { m_GemCollectClip = PureDotsAudioGenerator.CreateGemCollectClip(); m_OwnGemClip = true; }
            if (m_PlayerHitClip == null) { m_PlayerHitClip = PureDotsAudioGenerator.CreatePlayerHitClip(); m_OwnHitClip = true; }

            EnemyDeathClip = m_EnemyDeathClip;
            GemCollectClip = m_GemCollectClip;
            PlayerHitClip = m_PlayerHitClip;
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null)
                {
                    var camGo = new GameObject("Main Camera");
                    m_OwnCamera = camGo;
                    m_Camera = camGo.AddComponent<Camera>();
                    camGo.tag = "MainCamera";
                }
            }

            m_Camera.orthographic = true;
            m_Camera.orthographicSize = PresentationConstants.CameraOrthographicSize;
            m_Camera.nearClipPlane = PresentationConstants.CameraNearClip;
            m_Camera.farClipPlane = PresentationConstants.CameraFarClip;
            m_Camera.transform.position = new Vector3(0, 0, PresentationConstants.CameraZPosition);

            // Ensure presentation controller exists
            if (FindAnyObjectByType<CameraPresentationController>() == null)
            {
                m_OwnCameraController = m_Camera.gameObject.AddComponent<CameraPresentationController>();
            }

            // Ensure audio throttling manager exists
            if (FindAnyObjectByType<AudioThrottlingManager>() == null)
            {
                var audioGo = new GameObject("AudioThrottlingManager");
                audioGo.transform.SetParent(transform);
                audioGo.AddComponent<AudioThrottlingManager>();
            }

            // Ensure batched particle manager exists
            if (FindAnyObjectByType<BatchedParticleManager>() == null)
            {
                var vfxGo = new GameObject("BatchedParticleManager");
                vfxGo.transform.SetParent(transform);
                var particleMgr = vfxGo.AddComponent<BatchedParticleManager>();
            }

            // Ensure HUD exists
            if (FindAnyObjectByType<PureDotsHUD>() == null)
            {
                var hudGo = new GameObject("PureDotsHUD");
                hudGo.transform.SetParent(transform);
                hudGo.AddComponent<PureDotsHUD>();
            }

            // Setup procedural infinite floor quad
            CreateFloorQuad();
            if (TryValidateConfiguration(out _))
            {
                m_ZonePresentation = gameObject.AddComponent<TemporaryZonePresentation>();
                m_ZonePresentation.Initialize(m_TemporaryZones, m_Camera);
            }
        }

        public bool TryValidateConfiguration(out string error)
        {
            if (!Pools.IsValid) { error = "Pool limits must be positive and fit simulation buffer capacities."; return false; }
            if (m_EnemySpawning == null) { error = "Configure Enemy Spawning."; return false; }
            if (!m_EnemySpawning.TryValidate(out error)) return false;
            var elites = Elites;
            if (!Unity.Mathematics.math.isfinite(elites.SpawnProbability) || elites.SpawnProbability < 0 || elites.SpawnProbability > 1)
            { error = "Elite spawn probability must be finite and between 0 and 1."; return false; }
            if (!Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(new Unity.Mathematics.float4(
                    elites.HealthMultiplier, elites.SpeedMultiplier, elites.SizeMultiplier, elites.MassMultiplier))) ||
                !Unity.Mathematics.math.isfinite(elites.DamageMultiplier) ||
                elites.HealthMultiplier <= 0 || elites.SpeedMultiplier <= 0 || elites.SizeMultiplier <= 0 ||
                elites.MassMultiplier <= 0 || elites.DamageMultiplier <= 0)
            { error = "Elite health, speed, size, mass, and damage multipliers must be finite and greater than zero."; return false; }
            if (!Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(new Unity.Mathematics.float2(elites.ExperienceMultiplier, elites.ChestDropChanceMultiplier))) ||
                elites.ExperienceMultiplier < 0 || elites.ChestDropChanceMultiplier < 0)
            { error = "Elite experience and chest drop chance multipliers must be finite and non-negative."; return false; }
            if (m_StartingCharacter == null) { error = "Assign a Starting Character asset."; return false; }
            if (m_StartingWeaponAsset == null && m_StartingCharacter.Weapon == null)
            { error = "Assign a character weapon to the Starting Character or Starting Weapon Asset field."; return false; }
            if (!m_StartingCharacter.TryValidate(out error, m_StartingWeaponAsset)) return false;
            bool hasEnemy = false;
            if (m_EnemyTypes != null)
                foreach (var enemy in m_EnemyTypes)
                {
                    if (enemy == null) continue;
                    hasEnemy = true;
                    if (!Unity.Mathematics.math.isfinite(enemy.AvailableAfterSeconds) || enemy.AvailableAfterSeconds < 0)
                    { error = $"Enemy '{enemy.name}' appearance time must be finite and nonnegative."; return false; }
                    if (!Unity.Mathematics.math.isfinite(enemy.ChestDropChance) || enemy.ChestDropChance < 0 || enemy.ChestDropChance > 1)
                    { error = $"Enemy '{enemy.name}' chest drop chance must be between 0 and 1."; return false; }
                    if (!enemy.TryValidate(out error)) return false;
                    if (enemy.Ranged && enemy.Weapon == null)
                    { error = $"Ranged enemy '{enemy.name}' requires an enemy weapon asset."; return false; }
                    var config = enemy.ToConfig(0, m_StartingCharacter.CollisionRadius);
                    if (!Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(new Unity.Mathematics.float4(
                        config.MaxHealth * elites.HealthMultiplier, config.MoveSpeed * elites.SpeedMultiplier,
                        config.BaseDamage * elites.DamageMultiplier, config.Weapon.Damage * elites.DamageMultiplier))) ||
                        !Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(new Unity.Mathematics.float2(
                            config.Mass * elites.MassMultiplier, config.CollisionRadius * elites.SizeMultiplier))))
                    { error = $"Enemy '{enemy.name}' elite stat multipliers overflow. Reduce the multipliers."; return false; }
                }
            if (!hasEnemy) { error = "Assign at least one enemy asset to Enemy Types."; return false; }
            var artifacts = GetArtifacts();
            if (artifacts.Count > ArtifactInventory.Capacity)
            { error = $"Assign at most {ArtifactInventory.Capacity} different artifacts."; return false; }
            if (m_Rewards == null) { error = "Configure Level-up Rewards."; return false; }
            if (!m_Rewards.TryValidate(m_StartingCharacter, m_StartingWeaponAsset != null ? m_StartingWeaponAsset : m_StartingCharacter.Weapon, out error)) return false;
            if (m_TemporaryZones == null) { error = "Configure Temporary Stat Zones."; return false; }
            if (!m_TemporaryZones.TryValidate(m_Rewards, m_StartingCharacter,
                m_StartingWeaponAsset != null ? m_StartingWeaponAsset : m_StartingCharacter.Weapon, out error)) return false;
            foreach (var artifact in artifacts) if (!artifact.TryValidate(out error, m_Rewards)) return false;
            if (m_ArtifactChests == null) { error = "Configure Artifact Chests."; return false; }
            if (!m_ArtifactChests.TryValidate(artifacts, m_Rewards, out error)) return false;
            error = null; return true;
        }

        private void CreateFloorQuad()
        {
            if (m_FloorQuad != null) return;

            m_FloorQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            m_FloorQuad.name = "InfiniteFloorQuad";
            m_FloorQuad.transform.SetParent(transform);
            m_FloorQuad.transform.localScale = new Vector3(PresentationConstants.FloorQuadSize, PresentationConstants.FloorQuadSize, 1.0f);
            m_FloorQuad.transform.position = new Vector3(0, 0, PresentationConstants.FloorZPosition); // Behind all sprites

            var collider = m_FloorQuad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            if (m_FloorMaterial == null)
            {
                var shader = Shader.Find("PureDots/InfiniteFloor");
                if (shader != null)
                {
                    m_FloorMaterial = new Material(shader);
                    m_OwnFloorMaterial = true;
                }
            }

            if (m_FloorMaterial != null)
            {
                if (!m_OwnFloorMaterial)
                { m_FloorMaterial = new Material(m_FloorMaterial); m_OwnFloorMaterial = true; }
                var renderer = m_FloorQuad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = m_FloorMaterial;
                m_FloorRenderer = renderer;
                m_TileScale = m_FloorMaterial.GetFloat("_TileScale");
            }
        }

        public void ApplyFloorRebase(Unity.Mathematics.float2 delta)
        {
            m_FloorPhase = PresentationDepth.RebaseFloorPhase(m_FloorPhase, delta, m_TileScale);
            UpdateFloorPhase();
        }
        public void ResetFloorPhase() { m_FloorPhase = Unity.Mathematics.float2.zero; UpdateFloorPhase(); }
        public void ApplyZoneSnapshot(SimulationSnapshot snapshot) => m_ZonePresentation?.ApplySnapshot(snapshot);
        private void UpdateFloorPhase()
        {
            if (m_FloorRenderer == null) return;
            m_FloorMaterial.SetVector(OriginTileOffset, new Vector4(m_FloorPhase.x, m_FloorPhase.y, 0, 0));
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null; EnemyDeathClip = null; GemCollectClip = null; PlayerHitClip = null;
            if (m_OwnEnemyClip) DestroyOwned(m_EnemyDeathClip);
            if (m_OwnGemClip) DestroyOwned(m_GemCollectClip);
            if (m_OwnHitClip) DestroyOwned(m_PlayerHitClip);
            if (m_OwnFloorMaterial) DestroyOwned(m_FloorMaterial);
            if (m_OwnCameraController != null) DestroyOwned(m_OwnCameraController);
            if (m_OwnCamera != null) DestroyOwned(m_OwnCamera);
        }
        public static void DestroyOwned(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset);
        }
        private void LateUpdate()
        {
            // Floor quad tracks camera viewport xy position directly
            if (m_FloorQuad != null && m_Camera != null)
            {
                Vector3 camPos = m_Camera.transform.position;
                m_FloorQuad.transform.position = new Vector3(camPos.x, camPos.y, PresentationConstants.FloorZPosition);
            }
        }
    }
}
