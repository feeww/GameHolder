using UnityEngine;

namespace GameHolder.PureDots
{
    public class CameraPresentationController : MonoBehaviour
    {
        public static CameraPresentationController Instance { get; private set; }

        [Tooltip("Camera followed by this controller. Empty uses Main Camera.")]
        [SerializeField] private Camera m_TargetCamera;
        [Tooltip("Follow response per second. Higher follows faster; zero snaps to the player.")]
        [SerializeField] private float m_SmoothSpeed = PresentationConstants.CameraSmoothSpeed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (m_TargetCamera == null)
            {
                m_TargetCamera = Camera.main;
            }
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
        public void ApplyRebaseOffset(Vector2 rebaseDelta)
        {
            if (m_TargetCamera != null)
            {
                Vector3 camPos = m_TargetCamera.transform.position;
                camPos.x -= rebaseDelta.x;
                camPos.y -= rebaseDelta.y;
                m_TargetCamera.transform.position = camPos;
            }
        }

        public void UpdateCameraPosition(Vector2 targetPos, float deltaTime)
        {
            if (m_TargetCamera == null) return;

            Vector3 currentPos = m_TargetCamera.transform.position;
            Vector3 desiredPos = new Vector3(targetPos.x, targetPos.y, currentPos.z);

            if (m_SmoothSpeed > 0.0f && deltaTime > 0.0f)
            {
                m_TargetCamera.transform.position = Vector3.Lerp(currentPos, desiredPos, 1.0f - Mathf.Exp(-m_SmoothSpeed * deltaTime));
            }
            else
            {
                m_TargetCamera.transform.position = desiredPos;
            }
        }
    }
}
