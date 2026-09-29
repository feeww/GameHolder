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

        // Zero-allocation cached string buffers
        private string m_CachedFpsText = "<b>FPS:</b> -- (- ms)";
        private string m_CachedEnemiesText = "";
        private string m_CachedProjectilesText = "";
        private string m_CachedGemsText = "";
        private string m_CachedTotalGemsText = "";
        private string m_CachedLevelText = "";
        private string m_CachedHpBarText = "";
        private string m_CachedCoordsText = "";
        private string m_CachedRebaseText = "";
        private string m_CachedRebaseCountText = "";
        private string m_CachedGameOverLevelText = "";
        private string m_CachedGameOverGemsText = "";
        private string m_CachedGameOverSwarmText = "";

        private int m_PrevEnemiesCount = -1;
        private int m_PrevProjectilesCount = -1;
        private int m_PrevGemsCount = -1;
        private int m_PrevTotalGemsCollected = -1;
        private uint m_PrevPlayerLevel = 0;
        private uint m_PrevPlayerExperience = uint.MaxValue;
        private int m_PrevPlayerHealthInt = -1;
        private int m_PrevPlayerMaxHealthInt = -1;
        private int m_PrevPlayerInvulnInt = -1;
        private int m_PrevCoordsX10 = int.MinValue;
        private int m_PrevCoordsY10 = int.MinValue;
        private int m_PrevDistanceToRebaseInt = -1;
        private int m_PrevRebaseCount = -1;

        private GUIStyle m_TitleStyle;
        private GUIStyle m_PanelStyle;
        private GUIStyle m_LabelStyle;
        private GUIStyle m_HeaderStyle;
        private GUIStyle m_ButtonStyle;
        private GUIStyle m_DeathTitleStyle;
        private GUIStyle m_RespawnBtnStyle;
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
                m_CachedFpsText = $"<b>FPS:</b> {m_Fps:F1} ({m_FrameTimeMs:F1} ms)";
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

            // Update cached UI strings only on state change to guarantee zero-allocation OnGUI passes
            if (m_ActiveEnemiesCount != m_PrevEnemiesCount)
            {
                m_PrevEnemiesCount = m_ActiveEnemiesCount;
                m_CachedEnemiesText = $"• Active Swarm Enemies: <b>{m_ActiveEnemiesCount}</b> / {EnemyPoolSingleton.Capacity:N0}";
                m_CachedGameOverSwarmText = $"• Swarm Eliminations: <b>{EnemyPoolSingleton.Capacity - m_ActiveEnemiesCount}</b>";
            }

            if (m_ActiveProjectilesCount != m_PrevProjectilesCount)
            {
                m_PrevProjectilesCount = m_ActiveProjectilesCount;
                m_CachedProjectilesText = $"• Active Projectiles: <b>{m_ActiveProjectilesCount}</b> / {PlayerProjectilePoolSingleton.Capacity:N0}";
            }

            if (m_ActiveGemsCount != m_PrevGemsCount)
            {
                m_PrevGemsCount = m_ActiveGemsCount;
                m_CachedGemsText = $"• Active Gems in Field: <b>{m_ActiveGemsCount}</b> / {GemPoolSingleton.Capacity:N0} (L1 Cache)";
            }

            if (m_TotalGemsCollected != m_PrevTotalGemsCollected)
            {
                m_PrevTotalGemsCollected = m_TotalGemsCollected;
                m_CachedTotalGemsText = $"• Total Gems Collected: <b>{m_TotalGemsCollected}</b> EXP";
                m_CachedGameOverGemsText = $"• Total Gems Collected: <b>{m_TotalGemsCollected}</b> EXP";
            }

            if (m_PlayerLevel != m_PrevPlayerLevel || m_PlayerExperience != m_PrevPlayerExperience)
            {
                m_PrevPlayerLevel = m_PlayerLevel;
                m_PrevPlayerExperience = m_PlayerExperience;
                m_CachedLevelText = $"• Level: <b>{m_PlayerLevel}</b>  (EXP: {m_PlayerExperience} / {m_PlayerLevel * SimulationConstants.ExpPerLevelMultiplier})";
                m_CachedGameOverLevelText = $"• Final Level: <b>{m_PlayerLevel}</b>";
            }

            int hpInt = (int)m_PlayerHealth;
            int maxHpInt = (int)m_PlayerMaxHealth;
            int invulnInt = (int)(m_PlayerInvulnTimer * 10.0f);
            if (hpInt != m_PrevPlayerHealthInt || maxHpInt != m_PrevPlayerMaxHealthInt || invulnInt != m_PrevPlayerInvulnInt)
            {
                m_PrevPlayerHealthInt = hpInt;
                m_PrevPlayerMaxHealthInt = maxHpInt;
                m_PrevPlayerInvulnInt = invulnInt;
                m_CachedHpBarText = $"HP: {m_PlayerHealth:F0}/{m_PlayerMaxHealth:F0} " + (m_PlayerInvulnTimer > 0 ? $"[i-FRAME {m_PlayerInvulnTimer:F2}s]" : "");
            }

            int cx10 = (int)(m_PlayerCoords.x * 10.0f);
            int cy10 = (int)(m_PlayerCoords.y * 10.0f);
            if (cx10 != m_PrevCoordsX10 || cy10 != m_PrevCoordsY10)
            {
                m_PrevCoordsX10 = cx10;
                m_PrevCoordsY10 = cy10;
                m_CachedCoordsText = $"• Local Coords: ({m_PlayerCoords.x:F1}, {m_PlayerCoords.y:F1})";
            }

            float distFromOrigin = m_PlayerCoords.magnitude;
            int distInt = (int)distFromOrigin;
            if (distInt != m_PrevDistanceToRebaseInt)
            {
                m_PrevDistanceToRebaseInt = distInt;
                m_CachedRebaseText = $"• Distance to Rebase: {distFromOrigin:F0}m / {SimulationConstants.FloatingOriginThreshold:N0}m";
            }

            if (m_RebaseCount != m_PrevRebaseCount)
            {
                m_PrevRebaseCount = m_RebaseCount;
                m_CachedRebaseCountText = $"• Total Rebases: <b>{m_RebaseCount}</b>";
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

            m_DeathTitleStyle = new GUIStyle(m_TitleStyle)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1.0f, 0.25f, 0.25f) }
            };

            m_RespawnBtnStyle = new GUIStyle(m_ButtonStyle)
            {
                fixedHeight = 36,
                fontSize = 14
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
                GUILayout.Label(m_CachedFpsText, m_HeaderStyle);
                GUI.color = prevColor;

                GUILayout.Space(6);
                GUILayout.Label("--- SIMULATION METRICS ---", m_HeaderStyle);
                GUILayout.Label(m_CachedEnemiesText, m_LabelStyle);
                GUILayout.Label(m_CachedProjectilesText, m_LabelStyle);
                GUILayout.Label(m_CachedGemsText, m_LabelStyle);
                GUILayout.Label(m_CachedTotalGemsText, m_LabelStyle);

                GUILayout.Space(6);
                GUILayout.Label("--- PLAYER STATUS ---", m_HeaderStyle);
                GUILayout.Label(m_CachedLevelText, m_LabelStyle);
                float hpPercent = m_PlayerMaxHealth > 0 ? Mathf.Clamp01(m_PlayerHealth / m_PlayerMaxHealth) : 0;
                GUILayout.Label(m_CachedHpBarText, m_LabelStyle);

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
                GUILayout.Label(m_CachedCoordsText, m_LabelStyle);
                GUILayout.Label(m_CachedRebaseText, m_LabelStyle);
                GUILayout.Label(m_CachedRebaseCountText, m_LabelStyle);

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
                if (GUILayout.Button(m_GodMode ? "God Mode: [ON]" : "God Mode: [OFF]", m_ButtonStyle))
                {
                    m_GodMode = !m_GodMode;
                }

                if (GUILayout.Button(m_AutoAttackEnabled ? "Auto-Attack: [ON]" : "Auto-Attack: [OFF]", m_ButtonStyle))
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
                    GUILayout.Label("☠️ YOU DIED ☠️", m_DeathTitleStyle);
                    GUILayout.Space(12);

                    GUILayout.Label(m_CachedGameOverLevelText, m_LabelStyle);
                    GUILayout.Label(m_CachedGameOverGemsText, m_LabelStyle);
                    GUILayout.Label(m_CachedGameOverSwarmText, m_LabelStyle);

                    GUILayout.Space(16);
                    if (GUILayout.Button("🔄 RESPAWN & RESTART RUN", m_RespawnBtnStyle))
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
