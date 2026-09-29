using UnityEngine;

namespace GameHolder.PureDots
{
    public class GamePresentationBootstrap : MonoBehaviour
    {
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

        private void Awake()
        {
            if (m_EnemyDeathClip == null) m_EnemyDeathClip = PureDotsAudioGenerator.CreateEnemyDeathClip();
            if (m_GemCollectClip == null) m_GemCollectClip = PureDotsAudioGenerator.CreateGemCollectClip();
            if (m_PlayerHitClip == null) m_PlayerHitClip = PureDotsAudioGenerator.CreatePlayerHitClip();

            EnemyDeathClip = m_EnemyDeathClip;
            GemCollectClip = m_GemCollectClip;
            PlayerHitClip = m_PlayerHitClip;
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null)
                {
                    var camGo = new GameObject("Main Camera");
                    m_Camera = camGo.AddComponent<Camera>();
                    camGo.tag = "MainCamera";
                }
            }

            m_Camera.orthographic = true;
            m_Camera.orthographicSize = SimulationConstants.CameraOrthographicSize;
            m_Camera.nearClipPlane = SimulationConstants.CameraNearClip;
            m_Camera.farClipPlane = SimulationConstants.CameraFarClip;
            m_Camera.transform.position = new Vector3(0, 0, SimulationConstants.CameraZPosition);

            // Ensure presentation controller exists
            if (FindAnyObjectByType<CameraPresentationController>() == null)
            {
                var camCtrl = m_Camera.gameObject.AddComponent<CameraPresentationController>();
            }

            // Ensure audio throttling manager exists
            if (FindAnyObjectByType<AudioThrottlingManager>() == null)
            {
                var audioGo = new GameObject("AudioThrottlingManager");
                audioGo.AddComponent<AudioThrottlingManager>();
            }

            // Ensure batched particle manager exists
            if (FindAnyObjectByType<BatchedParticleManager>() == null)
            {
                var vfxGo = new GameObject("BatchedParticleManager");
                var particleMgr = vfxGo.AddComponent<BatchedParticleManager>();
            }

            // Ensure HUD exists
            if (FindAnyObjectByType<PureDotsHUD>() == null)
            {
                var hudGo = new GameObject("PureDotsHUD");
                hudGo.AddComponent<PureDotsHUD>();
            }

            // Setup procedural infinite floor quad
            CreateFloorQuad();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (FindAnyObjectByType<GamePresentationBootstrap>() == null)
            {
                var go = new GameObject("[PureDots_PresentationBootstrap]");
                go.AddComponent<GamePresentationBootstrap>();
            }
        }

        private void CreateFloorQuad()
        {
            if (m_FloorQuad != null) return;

            m_FloorQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            m_FloorQuad.name = "InfiniteFloorQuad";
            m_FloorQuad.transform.localScale = new Vector3(SimulationConstants.FloorQuadSize, SimulationConstants.FloorQuadSize, 1.0f);
            m_FloorQuad.transform.position = new Vector3(0, 0, SimulationConstants.FloorZPosition); // Behind all sprites

            var collider = m_FloorQuad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            if (m_FloorMaterial == null)
            {
                var shader = Shader.Find("PureDots/InfiniteFloor");
                if (shader != null)
                {
                    m_FloorMaterial = new Material(shader);
                }
            }

            if (m_FloorMaterial != null)
            {
                var renderer = m_FloorQuad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = m_FloorMaterial;
            }
        }

        private void LateUpdate()
        {
            // Floor quad tracks camera viewport xy position directly
            if (m_FloorQuad != null && m_Camera != null)
            {
                Vector3 camPos = m_Camera.transform.position;
                m_FloorQuad.transform.position = new Vector3(camPos.x, camPos.y, SimulationConstants.FloorZPosition);
            }
        }
    }
}
