using UnityEngine;

namespace GameHolder.PureDots
{
    public class BatchedParticleManager : MonoBehaviour
    {
        public static BatchedParticleManager Instance { get; private set; }

        [Tooltip("Death effects. Empty creates a generated particle system.")]
        [SerializeField] private ParticleSystem m_DeathParticleSystem;
        [Tooltip("Player hit effects. Empty creates a generated particle system.")]
        [SerializeField] private ParticleSystem m_HitParticleSystem;
        [Tooltip("Gem collection effects. Empty creates a generated particle system.")]
        [SerializeField] private ParticleSystem m_GemCollectParticleSystem;

        private ParticleSystem.Particle[] m_ParticlesBuffer;
        private readonly Material[] m_OwnedMaterials = new Material[PresentationConstants.ParticleSystemCount];
        private int m_MaterialCount, m_EmitCalls, m_EmittedParticles;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            m_ParticlesBuffer = new ParticleSystem.Particle[PresentationConstants.ParticleCapacity];

            if (m_DeathParticleSystem == null)
            {
                m_DeathParticleSystem = CreateProceduralParticleSystem("ProceduralDeathFX", PresentationConstants.DeathParticleColor, PresentationConstants.DeathParticleSize, PresentationConstants.DeathParticleSpeed);
            }
            if (m_HitParticleSystem == null)
            {
                m_HitParticleSystem = CreateProceduralParticleSystem("ProceduralHitFX", PresentationConstants.HitParticleColor, PresentationConstants.HitParticleSize, PresentationConstants.HitParticleSpeed);
            }
            if (m_GemCollectParticleSystem == null)
            {
                m_GemCollectParticleSystem = CreateProceduralParticleSystem("ProceduralGemFX", PresentationConstants.GemParticleColor, PresentationConstants.GemParticleSize, PresentationConstants.GemParticleSpeed);
            }
        }

        private ParticleSystem CreateProceduralParticleSystem(string sysName, Color color, float startSize, float startSpeed)
        {
            var go = new GameObject(sysName);
            go.transform.SetParent(transform);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = PresentationConstants.ParticleCapacity;
            main.startLifetime = PresentationConstants.ParticleLifetime;
            main.startSpeed = startSpeed;
            main.startSize = startSize;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.loop = false;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = PresentationConstants.ParticleSpawnRadius;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader) { color = color };
                renderer.sharedMaterial = material;
                m_OwnedMaterials[m_MaterialCount++] = material;
            }

            return ps;
        }

        public void EmitDeathBurst(Vector2 position, int count = PresentationConstants.DeathBurstParticles)
        {
            if (m_DeathParticleSystem == null || !Reserve(ref count)) return;

            var emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, PresentationConstants.ParticleZ),
                applyShapeToPosition = true
            };
            m_DeathParticleSystem.Emit(emitParams, count);
        }

        public void EmitHitBurst(Vector2 position, int count = PresentationConstants.DefaultHitBurstParticles)
        {
            if (m_HitParticleSystem == null || !Reserve(ref count)) return;

            var emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, PresentationConstants.ParticleZ),
                applyShapeToPosition = true
            };
            m_HitParticleSystem.Emit(emitParams, count);
        }

        public void EmitGemCollectBurst(Vector2 position, int count = PresentationConstants.GemBurstParticles)
        {
            if (m_GemCollectParticleSystem == null || !Reserve(ref count)) return;

            var emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, PresentationConstants.ParticleZ),
                applyShapeToPosition = true
            };
            m_GemCollectParticleSystem.Emit(emitParams, count);
        }

        public void BeginFrame() { m_EmitCalls = 0; m_EmittedParticles = 0; }
        private bool Reserve(ref int count)
        {
            count = Mathf.Min(count, PresentationConstants.ParticlesPerFrame - m_EmittedParticles);
            if (count <= 0 || m_EmitCalls >= PresentationConstants.EmitCallsPerFrame) return false;
            m_EmitCalls++; m_EmittedParticles += count; return true;
        }
        public void ClearAll()
        {
            if (m_DeathParticleSystem != null) m_DeathParticleSystem.Clear();
            if (m_HitParticleSystem != null) m_HitParticleSystem.Clear();
            if (m_GemCollectParticleSystem != null) m_GemCollectParticleSystem.Clear();
        }
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            for (int i = 0; i < m_MaterialCount; i++) GamePresentationBootstrap.DestroyOwned(m_OwnedMaterials[i]);
        }
        public void ShiftAllParticles(Vector2 rebaseDelta)
        {
            Vector3 delta = new Vector3(rebaseDelta.x, rebaseDelta.y, 0.0f);

            ShiftSystemParticles(m_DeathParticleSystem, delta);
            ShiftSystemParticles(m_HitParticleSystem, delta);
            ShiftSystemParticles(m_GemCollectParticleSystem, delta);
        }

        private void ShiftSystemParticles(ParticleSystem ps, Vector3 delta)
        {
            if (ps == null) return;

            int count = ps.particleCount;
            if (count == 0) return;

            count = Mathf.Min(count, m_ParticlesBuffer.Length);

            int alive = ps.GetParticles(m_ParticlesBuffer, count);
            for (int i = 0; i < alive; i++)
            {
                m_ParticlesBuffer[i].position -= delta;
            }
            ps.SetParticles(m_ParticlesBuffer, alive);
        }
    }
}
