using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace GameHolder.PureDots
{
    public class PureDotsHUD : MonoBehaviour
    {
        public static PureDotsHUD Instance { get; private set; }

        private float m_DeltaTimeAccumulator;
        private int m_FrameCount;
        private float m_Fps;
        private float m_FrameTimeMs;

        private int m_ActiveEnemiesCount;
        private int m_ActiveProjectilesCount;
        private int m_ActiveGemsCount;

        private float m_PlayerHealth;
        private float m_PlayerMaxHealth;
        private float m_PlayerInvulnTimer;
        private Vector2 m_PlayerCoords;
        private int m_TotalGemsCollected;
        private int m_RebaseCount;
        private uint m_PlayerLevel = 1;
        private uint m_PlayerExperience;
        private bool m_PlayerIsDead;

        private bool m_GodMode;
        private bool m_AutoAttackEnabled = true;

        private GUIStyle m_TitleStyle;
        private GUIStyle m_PanelStyle;
        private GUIStyle m_LabelStyle;
        private GUIStyle m_HeaderStyle;
        private GUIStyle m_ButtonStyle;
        private bool m_StylesInitialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void NotifyGemCollected(uint exp)
        {
            m_TotalGemsCollected += (int)exp;
        }

        public void NotifyRebase()
        {
            m_RebaseCount++;
        }

        private void Update()
        {
            // FPS calculation
            m_DeltaTimeAccumulator += Time.unscaledDeltaTime;
            m_FrameCount++;
            if (m_DeltaTimeAccumulator >= 0.5f)
            {
                m_Fps = m_FrameCount / m_DeltaTimeAccumulator;
                m_FrameTimeMs = (m_DeltaTimeAccumulator / m_FrameCount) * 1000.0f;
                m_FrameCount = 0;
                m_DeltaTimeAccumulator = 0.0f;
            }

            // Query ECS world metrics safely
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // 1. Query Active Entities
            var enemyQuery = em.CreateEntityQuery(ComponentType.ReadOnly<EnemyActiveTag>());
            m_ActiveEnemiesCount = enemyQuery.CalculateEntityCount();

            var projQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ProjectileActiveTag>());
            m_ActiveProjectilesCount = projQuery.CalculateEntityCount();

            var gemQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GemActiveTag>());
            m_ActiveGemsCount = gemQuery.CalculateEntityCount();

            // 2. Query Player Status
            var playerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerStats>(),
                ComponentType.ReadWrite<PlayerInvulnerability>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );

            if (playerQuery.CalculateEntityCount() > 0)
            {
                using var players = playerQuery.ToEntityArray(Allocator.Temp);
                Entity p = players[0];

                var stats = em.GetComponentData<PlayerStats>(p);
                var invuln = em.GetComponentData<PlayerInvulnerability>(p);
                var transform = em.GetComponentData<Unity.Transforms.LocalTransform>(p);

                if (m_GodMode)
                {
                    stats.CurrentHealth = stats.MaxHealth;
                    stats.IsDead = 0;
                    em.SetComponentData(p, stats);
                }

                m_PlayerHealth = stats.CurrentHealth;
                m_PlayerMaxHealth = stats.MaxHealth;
                m_PlayerInvulnTimer = invuln.Timer;
                m_PlayerLevel = stats.Level;
                m_PlayerExperience = stats.Experience;
                m_PlayerIsDead = stats.IsDead != 0;
                m_PlayerCoords = new Vector2(transform.Position.x, transform.Position.y);
            }
        }

        private void InitStyles()
        {
            if (m_StylesInitialized) return;
            m_StylesInitialized = true;

            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.08f, 0.1f, 0.14f, 0.88f));
            bgTex.Apply();

            m_PanelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = bgTex },
                padding = new RectOffset(12, 12, 12, 12)
            };

            m_TitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 0.9f, 1.0f) }
            };

            m_HeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1.0f, 0.85f, 0.3f) }
            };

            m_LabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            m_ButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                fixedHeight = 26
            };
        }

        private void OnGUI()
        {
            InitStyles();

            // Left Panel: Engine Metrics & Player Status
            GUILayout.BeginArea(new Rect(16, 16, 320, 460), m_PanelStyle);
            {
                GUILayout.Label("⚡ PURE DOTS 2D / TOP-DOWN ENGINE", m_TitleStyle);
                GUILayout.Label("Unity 6 (6000.6) Pure Unmanaged Burst Pipeline", m_LabelStyle);
                GUILayout.Space(8);

                // FPS & Frame Timing
                Color fpsColor = m_Fps >= 55 ? Color.green : (m_Fps >= 30 ? Color.yellow : Color.red);
                var prevColor = GUI.color;
                GUI.color = fpsColor;
                GUILayout.Label($"<b>FPS:</b> {m_Fps:F1} ({m_FrameTimeMs:F1} ms)", m_HeaderStyle);
                GUI.color = prevColor;

                GUILayout.Space(6);
                GUILayout.Label("--- SIMULATION METRICS ---", m_HeaderStyle);
                GUILayout.Label($"• Active Swarm Enemies: <b>{m_ActiveEnemiesCount}</b> / {EnemyPoolSingleton.Capacity:N0}", m_LabelStyle);
                GUILayout.Label($"• Active Projectiles: <b>{m_ActiveProjectilesCount}</b> / {PlayerProjectilePoolSingleton.Capacity:N0}", m_LabelStyle);
                GUILayout.Label($"• Active Gems in Field: <b>{m_ActiveGemsCount}</b> / {GemPoolSingleton.Capacity:N0} (L1 Cache)", m_LabelStyle);
                GUILayout.Label($"• Total Gems Collected: <b>{m_TotalGemsCollected}</b> EXP", m_LabelStyle);

                GUILayout.Space(6);
                GUILayout.Label("--- PLAYER STATUS ---", m_HeaderStyle);
                GUILayout.Label($"• Level: <b>{m_PlayerLevel}</b>  (EXP: {m_PlayerExperience} / {m_PlayerLevel * SimulationConstants.ExpPerLevelMultiplier})", m_LabelStyle);
                float hpPercent = m_PlayerMaxHealth > 0 ? Mathf.Clamp01(m_PlayerHealth / m_PlayerMaxHealth) : 0;
                string hpBar = $"HP: {m_PlayerHealth:F0}/{m_PlayerMaxHealth:F0} " + (m_PlayerInvulnTimer > 0 ? $"[i-FRAME {m_PlayerInvulnTimer:F2}s]" : "");
                GUILayout.Label(hpBar, m_LabelStyle);

                // Simple HP Visual Bar
                Rect barRect = GUILayoutUtility.GetRect(290, 14);
                GUI.Box(barRect, "");
                Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * hpPercent, barRect.height);
                Color barCol = hpPercent > 0.5f ? Color.green : (hpPercent > 0.25f ? Color.yellow : Color.red);
                var oldCol = GUI.color;
                GUI.color = barCol;
                GUI.Box(fillRect, "");
                GUI.color = oldCol;

                GUILayout.Space(6);
                GUILayout.Label("--- FLOATING ORIGIN ---", m_HeaderStyle);
                float distFromOrigin = m_PlayerCoords.magnitude;
                GUILayout.Label($"• Local Coords: ({m_PlayerCoords.x:F1}, {m_PlayerCoords.y:F1})", m_LabelStyle);
                GUILayout.Label($"• Distance to Rebase: {distFromOrigin:F0}m / {SimulationConstants.FloatingOriginThreshold:N0}m", m_LabelStyle);
                GUILayout.Label($"• Total Rebases: <b>{m_RebaseCount}</b>", m_LabelStyle);

                GUILayout.Space(8);
                GUILayout.Label("<color=#77aaff>Controls:</color> W/A/S/D or Arrows to Move\nProjectiles auto-fire in 3-way spread pattern", m_LabelStyle);
            }
            GUILayout.EndArea();

            // Right Panel: Interactive Stress Test Controls
            GUILayout.BeginArea(new Rect(Screen.width - 240, 16, 224, 380), m_PanelStyle);
            {
                GUILayout.Label("🛠️ TEST CONTROLS", m_TitleStyle);
                GUILayout.Space(6);

                if (GUILayout.Button("Spawn +50 Enemies", m_ButtonStyle))
                {
                    SpawnExtraEnemies(50);
                }

                if (GUILayout.Button("Spawn +250 Enemies", m_ButtonStyle))
                {
                    SpawnExtraEnemies(250);
                }

                if (GUILayout.Button("Spawn +1,000 Enemies", m_ButtonStyle))
                {
                    SpawnExtraEnemies(1000);
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Force Origin Rebase", m_ButtonStyle))
                {
                    ForceFloatingOriginRebase();
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Kill All Enemies", m_ButtonStyle))
                {
                    KillAllActiveEnemies();
                }

                GUILayout.Space(6);
                string godModeText = m_GodMode ? "God Mode: [ON]" : "God Mode: [OFF]";
                if (GUILayout.Button(godModeText, m_ButtonStyle))
                {
                    m_GodMode = !m_GodMode;
                }

                string autoAttackText = m_AutoAttackEnabled ? "Auto-Attack: [ON]" : "Auto-Attack: [OFF]";
                if (GUILayout.Button(autoAttackText, m_ButtonStyle))
                {
                    m_AutoAttackEnabled = !m_AutoAttackEnabled;
                    ToggleAutoAttack(m_AutoAttackEnabled);
                }
            }
            GUILayout.EndArea();

            // Center Screen: Game Over Modal if dead
            if (m_PlayerIsDead || m_PlayerHealth <= 0.0f)
            {
                float modalW = 340f;
                float modalH = 220f;
                Rect modalRect = new Rect((Screen.width - modalW) * 0.5f, (Screen.height - modalH) * 0.5f, modalW, modalH);

                GUILayout.BeginArea(modalRect, m_PanelStyle);
                {
                    GUILayout.Space(8);
                    var deathTitle = new GUIStyle(m_TitleStyle)
                    {
                        fontSize = 22,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(1.0f, 0.25f, 0.25f) }
                    };
                    GUILayout.Label("☠️ YOU DIED ☠️", deathTitle);
                    GUILayout.Space(12);

                    GUILayout.Label($"• Final Level: <b>{m_PlayerLevel}</b>", m_LabelStyle);
                    GUILayout.Label($"• Total Gems Collected: <b>{m_TotalGemsCollected}</b> EXP", m_LabelStyle);
                    GUILayout.Label($"• Swarm Eliminations: <b>{EnemyPoolSingleton.Capacity - m_ActiveEnemiesCount}</b>", m_LabelStyle);

                    GUILayout.Space(16);
                    var respawnBtnStyle = new GUIStyle(m_ButtonStyle)
                    {
                        fixedHeight = 36,
                        fontSize = 14
                    };
                    if (GUILayout.Button("🔄 RESPAWN & RESTART RUN", respawnBtnStyle))
                    {
                        RespawnPlayer();
                    }
                }
                GUILayout.EndArea();
            }
        }

        private void SpawnExtraEnemies(int count)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var spawnerQuery = em.CreateEntityQuery(ComponentType.ReadWrite<WaveSpawnerConfig>());
            if (spawnerQuery.CalculateEntityCount() > 0)
            {
                var spawnerEntity = spawnerQuery.GetSingletonEntity();
                var config = em.GetComponentData<WaveSpawnerConfig>(spawnerEntity);
                config.BatchSize += count;
                config.Timer = config.SpawnInterval; // Trigger immediately
                em.SetComponentData(spawnerEntity, config);
            }
        }

        private void ForceFloatingOriginRebase()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var playerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<Unity.Transforms.LocalTransform>()
            );

            if (playerQuery.CalculateEntityCount() > 0)
            {
                using var players = playerQuery.ToEntityArray(Allocator.Temp);
                Entity p = players[0];
                var transform = em.GetComponentData<Unity.Transforms.LocalTransform>(p);
                // Set beyond threshold to trigger rebase
                transform.Position.x = SimulationConstants.FloatingOriginThreshold + 100.0f;
                transform.Position.y = SimulationConstants.FloatingOriginThreshold + 100.0f;
                em.SetComponentData(p, transform);
            }
        }

        private void KillAllActiveEnemies()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var damageQueueQuery = em.CreateEntityQuery(ComponentType.ReadOnly<DamageEventQueueSingleton>());
            if (damageQueueQuery.CalculateEntityCount() == 0) return;
            var damageSingleton = damageQueueQuery.GetSingleton<DamageEventQueueSingleton>();

            var enemyQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<EnemyActiveTag>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );

            using var enemies = enemyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < enemies.Length; i++)
            {
                Entity enemy = enemies[i];
                ulong targetKey = DamageEvent.CreateTargetKey(enemy);
                damageSingleton.DamageQueue.Enqueue(new DamageEvent
                {
                    TargetKey = targetKey,
                    TargetEntity = enemy,
                    Damage = 9999.0f,
                    HitFlags = 0,
                    Padding = float2.zero
                });
            }
        }

        private void ToggleAutoAttack(bool enabled)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var sys = world.GetExistingSystemManaged<PlayerInputReaderSystem>();
            // Auto attack system can be enabled / disabled
            var attackSys = world.GetExistingSystem<PlayerAutoAttackSystem>();
            if (attackSys != SystemHandle.Null)
            {
                ref var state = ref world.Unmanaged.ResolveSystemStateRef(attackSys);
                state.Enabled = enabled;
            }
        }

        public void RespawnPlayer()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var playerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerStats>(),
                ComponentType.ReadWrite<PlayerInvulnerability>(),
                ComponentType.ReadWrite<Unity.Transforms.LocalTransform>(),
                ComponentType.ReadWrite<MovementVelocity>()
            );

            if (playerQuery.CalculateEntityCount() > 0)
            {
                using var players = playerQuery.ToEntityArray(Allocator.Temp);
                Entity p = players[0];
                var stats = em.GetComponentData<PlayerStats>(p);
                stats.CurrentHealth = stats.MaxHealth;
                stats.IsDead = 0;
                em.SetComponentData(p, stats);

                var invuln = em.GetComponentData<PlayerInvulnerability>(p);
                invuln.Timer = 1.5f; // 1.5s grace period on respawn
                em.SetComponentData(p, invuln);

                var transform = em.GetComponentData<Unity.Transforms.LocalTransform>(p);
                transform.Position = new float3(0, 0, 0);
                em.SetComponentData(p, transform);

                var vel = em.GetComponentData<MovementVelocity>(p);
                vel.Value = float2.zero;
                em.SetComponentData(p, vel);
            }

            m_PlayerHealth = m_PlayerMaxHealth;
            m_PlayerIsDead = false;

            KillAllActiveEnemies();
        }
    }
}
