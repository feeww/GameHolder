using UnityEngine;

namespace GameHolder.PureDots
{
    public class BatchedParticleManager : MonoBehaviour
    {
        public static BatchedParticleManager Instance { get; private set; }

        [SerializeField] private ParticleSystem m_DeathParticleSystem;
        [SerializeField] private ParticleSystem m_HitParticleSystem;
        [SerializeField] private ParticleSystem m_GemCollectParticleSystem;

        private ParticleSystem.Particle[] m_ParticlesBuffer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            m_ParticlesBuffer = new ParticleSystem.Particle[2048];

            if (m_DeathParticleSystem == null)
            {
                m_DeathParticleSystem = CreateProceduralParticleSystem("ProceduralDeathFX", new Color(1.0f, 0.35f, 0.2f, 1.0f), 0.35f, 4.0f);
            }
            if (m_HitParticleSystem == null)
            {
                m_HitParticleSystem = CreateProceduralParticleSystem("ProceduralHitFX", new Color(1.0f, 0.9f, 0.2f, 1.0f), 0.25f, 3.0f);
            }
            if (m_GemCollectParticleSystem == null)
            {
                m_GemCollectParticleSystem = CreateProceduralParticleSystem("ProceduralGemFX", new Color(0.2f, 1.0f, 0.8f, 1.0f), 0.2f, 2.5f);
            }
        }

        private ParticleSystem CreateProceduralParticleSystem(string sysName, Color color, float startSize, float startSpeed)
        {
            var go = new GameObject(sysName);
            go.transform.SetParent(transform);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.35f;
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
            shape.radius = 0.2f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                renderer.sharedMaterial = new Material(shader) { color = color };
            }

            return ps;
        }

        public void EmitDeathBurst(Vector2 position, int count = 8)
        {
            if (m_DeathParticleSystem == null) return;

            var emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, -0.1f),
                applyShapeToPosition = true
            };
            m_DeathParticleSystem.Emit(emitParams, count);
        }

        public void EmitHitBurst(Vector2 position, int count = 5)
        {
            if (m_HitParticleSystem == null) return;

            var emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, -0.1f),
                applyShapeToPosition = true
            };
            m_HitParticleSystem.Emit(emitParams, count);
        }

        public void EmitGemCollectBurst(Vector2 position, int count = 6)
        {
            if (m_GemCollectParticleSystem == null) return;

            var emitParams = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, position.y, -0.1f),
                applyShapeToPosition = true
            };
            m_GemCollectParticleSystem.Emit(emitParams, count);
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

            if (m_ParticlesBuffer.Length < count)
            {
                m_ParticlesBuffer = new ParticleSystem.Particle[count + 256];
            }

            int alive = ps.GetParticles(m_ParticlesBuffer, count);
            for (int i = 0; i < alive; i++)
            {
                m_ParticlesBuffer[i].position -= delta;
            }
            ps.SetParticles(m_ParticlesBuffer, alive);
        }
    }
}
