using UnityEngine;

namespace GameHolder.PureDots
{
    public class GamePresentationBootstrap : MonoBehaviour
    {
        public static GamePresentationBootstrap Instance { get; private set; }
        [Header("Starting Character & Weapon (applied on Play)")]
        [SerializeField] private CharacterDefinition m_StartingCharacter;
        [Tooltip("Overrides the character asset's weapon. Empty uses the character asset's weapon.")]
        [SerializeField] private CharacterWeaponDefinition m_StartingWeaponAsset;
        [Header("Enemy Roster (assign at least one asset)")]
        [SerializeField] private EnemyDefinition[] m_EnemyTypes = new EnemyDefinition[0];

        public CharacterDefinition StartingCharacter => m_StartingCharacter;
        public CharacterWeaponDefinition StartingWeaponAsset => m_StartingWeaponAsset;
        public EnemyDefinition[] EnemyTypes => m_EnemyTypes;

        [Header("Level-up Rewards")]
        [SerializeField] private RewardSettings m_Rewards = new RewardSettings();
        public RewardSettings Rewards => m_Rewards;

        [Header("Rendering & Camera")]
        [SerializeField] private Camera m_Camera;
        [SerializeField] private Material m_FloorMaterial;

        [Header("Audio & VFX")]
        [SerializeField] private AudioClip m_EnemyDeathClip;
        [SerializeField] private AudioClip m_GemCollectClip;
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
        }

        public bool TryValidateConfiguration(out string error)
        {
            if (m_StartingCharacter == null) { error = "Assign a Starting Character asset."; return false; }
            if (m_StartingWeaponAsset == null && m_StartingCharacter.Weapon == null)
            { error = "Assign a character weapon to the Starting Character or Starting Weapon Asset field."; return false; }
            bool hasEnemy = false;
            if (m_EnemyTypes != null)
                foreach (var enemy in m_EnemyTypes)
                {
                    if (enemy == null) continue;
                    hasEnemy = true;
                    if (enemy.Ranged && enemy.Weapon == null)
                    { error = $"Ranged enemy '{enemy.name}' requires an enemy weapon asset."; return false; }
                }
            if (!hasEnemy) { error = "Assign at least one enemy asset to Enemy Types."; return false; }
            if (m_Rewards == null) { error = "Configure Level-up Rewards."; return false; }
            if (!m_Rewards.TryValidate(m_StartingCharacter, m_StartingWeaponAsset != null ? m_StartingWeaponAsset : m_StartingCharacter.Weapon, out error)) return false;
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
