using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace GameHolder.PureDots
{
    public class PureDotsHUD : MonoBehaviour
    {
        public static PureDotsHUD Instance { get; private set; }
        private SimulationSnapshot m_Snapshot;
        private bool m_Bound;
        private readonly char[] m_Text = new char[PresentationConstants.HudTextCapacity];
        private readonly SimulationCommand[] m_Pending = new SimulationCommand[SimulationConstants.CommandQueueCapacity];
        private BatchedHudText m_Stats, m_God, m_Attack, m_DeathStats;
        private GameObject m_DeathPanel;
        private int m_Length, m_PendingCount, m_Frames;
        private float m_Elapsed, m_Fps, m_Milliseconds;
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var root = new GameObject("HUD canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = HudLayout.SortingOrder; canvas.pixelPerfect = true;
            var left = Panel("Stats panel", root.transform, HudLayout.StatsPanelSize, HudLayout.StatsPanelPosition, Vector2.up);
            m_Stats = Text("Stats", left, HudLayout.StatsTextSize, HudLayout.StatsTextPosition, "");
            var right = Panel("Controls panel", root.transform, HudLayout.ControlsPanelSize, HudLayout.ControlsPanelPosition, Vector2.one);
            Text("Controls title", right, HudLayout.ControlsTitleSize, HudLayout.ControlsTitlePosition, "RUN CONTROLS");
            m_God = Button(right, HudLayout.FirstControlY, "God mode: OFF", ToggleGod);
            m_Attack = Button(right, HudLayout.FirstControlY + 1 * HudLayout.ControlSpacing, "Auto attack: ON", ToggleAttack);
            Button(right, HudLayout.FirstControlY + 2 * HudLayout.ControlSpacing, "Spawn +100", Spawn100);
            Button(right, HudLayout.FirstControlY + 3 * HudLayout.ControlSpacing, "Spawn +1,000", Spawn1000);
            Button(right, HudLayout.FirstControlY + 4 * HudLayout.ControlSpacing, "Spawn +10,000", Spawn10000);
            Button(right, HudLayout.FirstControlY + 5 * HudLayout.ControlSpacing, "Kill all enemies", KillAll);
            Button(right, HudLayout.FirstControlY + 6 * HudLayout.ControlSpacing, "Teleport / rebase", Rebase);
            Button(right, HudLayout.FirstControlY + 7 * HudLayout.ControlSpacing, "Restart run", RespawnPlayer);
            var death = Panel("Run ended panel", root.transform, HudLayout.DeathPanelSize, HudLayout.DeathPanelPosition, new Vector2(.5f, .5f));
            m_DeathPanel = death.gameObject;
            m_DeathStats = Text("Run ended stats", death, HudLayout.DeathTextSize, HudLayout.DeathTextPosition, "RUN ENDED");
            Button(death, HudLayout.DeathRestartY, "Restart run", RespawnPlayer);
            if (EventSystem.current == null)
            {
                var events = new GameObject("HUD event system", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(root.transform, false);
            }
            // Reserve maximum glyph geometry once, before the first rendered frame.
            for (int i = 0; i < m_Text.Length; i++) m_Text[i] = 'W';
            m_Stats.SetText(m_Text, m_Text.Length); m_DeathStats.SetText(m_Text, m_Text.Length);
            Canvas.ForceUpdateCanvases();
            m_DeathPanel.SetActive(false);
            RefreshText();
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = Vector2.up;
            rect.sizeDelta = size; rect.anchoredPosition = new Vector2(position.x, -position.y);
            return rect;
        }
        private static RectTransform Panel(string name, Transform parent, Vector2 size, Vector2 position, Vector2 anchor)
        {
            var rect = Rect(name, parent, size, position, anchor);
            var image = rect.gameObject.AddComponent<Image>(); image.color = HudLayout.PanelColor; image.raycastTarget = false;
            return rect;
        }
        private static BatchedHudText Text(string name, Transform parent, Vector2 size, Vector2 position, string text)
        {
            var label = Rect(name, parent, size, position, Vector2.up).gameObject.AddComponent<BatchedHudText>();
            label.color = Color.white; label.SetText(text); return label;
        }
        private static BatchedHudText Button(Transform parent, float y, string text, UnityAction action)
        {
            var rect = Rect(text, parent, HudLayout.ButtonSize, new Vector2(HudLayout.ButtonX, y), Vector2.up);
            var image = rect.gameObject.AddComponent<Image>(); image.color = HudLayout.ButtonColor;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(action);
            return Text("Label", rect, HudLayout.ButtonTextSize, HudLayout.ButtonTextPosition, text);
        }
        public void ApplySnapshot(SimulationSnapshot snapshot, UnsafeQueue<SimulationCommand> commands)
        {
            m_Snapshot = snapshot; m_Bound = true;
            // The bridge calls this after its simulation fence, so UI input never races native jobs.
            for (int i = 0; i < m_PendingCount; i++) if (commands.IsCreated && commands.Count < SimulationConstants.CommandQueueCapacity) commands.Enqueue(m_Pending[i]);
            m_PendingCount = 0;
            RefreshText();
        }
        public void Unbind() { m_Bound = false; m_PendingCount = 0; }
        public void RespawnPlayer() => Enqueue(SimulationCommandKind.Restart);
        private void ToggleGod() => Enqueue(SimulationCommandKind.GodMode, m_Snapshot.GodMode == 0 ? 1 : 0);
        private void ToggleAttack() => Enqueue(SimulationCommandKind.AutoAttack, m_Snapshot.AutoAttack == 0 ? 1 : 0);
        private void Spawn100() => Enqueue(SimulationCommandKind.SpawnExtra, 100);
        private void Spawn1000() => Enqueue(SimulationCommandKind.SpawnExtra, 1000);
        private void Spawn10000() => Enqueue(SimulationCommandKind.SpawnExtra, 10000);
        private void KillAll() => Enqueue(SimulationCommandKind.KillAll);
        private void Rebase() => Enqueue(SimulationCommandKind.ForceRebase);
        private void Enqueue(SimulationCommandKind kind, int value = 0)
        {
            if (m_Bound && m_PendingCount < m_Pending.Length)
                m_Pending[m_PendingCount++] = new SimulationCommand { Kind = kind, Value = value };
        }
        private void Update()
        {
            m_Elapsed += Time.unscaledDeltaTime; m_Frames++;
            if (m_Elapsed < PresentationConstants.FpsSampleInterval) return;
            m_Fps = m_Frames / m_Elapsed; m_Milliseconds = m_Elapsed * 1000 / m_Frames;
            m_Elapsed = 0; m_Frames = 0;
        }
        private void RefreshText()
        {
            m_Length = 0;
            Append("SURVIVOR\nFPS: "); AppendFixed(m_Fps); Append("  "); AppendFixed(m_Milliseconds); Append(" ms\nEnemies: ");
            AppendNumber((ulong)Mathf.Max(0, m_Snapshot.ActiveEnemies)); Append("\nProjectiles: ");
            AppendNumber((ulong)Mathf.Max(0, m_Snapshot.PlayerProjectiles + m_Snapshot.EnemyProjectiles)); Append("\nGems: ");
            AppendNumber((ulong)Mathf.Max(0, m_Snapshot.ActiveGems)); Append("\nKills: "); AppendNumber(m_Snapshot.Kills);
            Append("\nCollected XP: "); AppendNumber(m_Snapshot.TotalExperience); Append("\nLevel "); AppendNumber(m_Snapshot.Player.Level);
            Append("  XP "); AppendNumber(m_Snapshot.Player.Experience); Append("\nHealth: ");
            AppendNumber((ulong)Mathf.Max(0, m_Snapshot.Player.CurrentHealth)); Append(" / ");
            AppendNumber((ulong)Mathf.Max(0, m_Snapshot.Player.MaxHealth)); Append("\nPosition: ");
            AppendFixed(m_Snapshot.PlayerPosition.x); Append(", "); AppendFixed(m_Snapshot.PlayerPosition.y);
            m_Stats.SetText(m_Text, m_Length);
            m_God.SetText(m_Snapshot.GodMode != 0 ? "God mode: ON" : "God mode: OFF");
            m_Attack.SetText(m_Snapshot.AutoAttack != 0 ? "Auto attack: ON" : "Auto attack: OFF");
            bool dead = m_Snapshot.Player.IsDead != 0;
            if (m_DeathPanel.activeSelf != dead) m_DeathPanel.SetActive(dead);
            if (!dead) return;
            m_Length = 0; Append("RUN ENDED\nKills: "); AppendNumber(m_Snapshot.Kills);
            Append("\nLevel: "); AppendNumber(m_Snapshot.Player.Level); m_DeathStats.SetText(m_Text, m_Length);
        }
        private void Append(string text) { for (int i = 0; i < text.Length && m_Length < m_Text.Length; i++) m_Text[m_Length++] = text[i]; }
        private void AppendNumber(ulong number)
        {
            int start = m_Length;
            do { if (m_Length == m_Text.Length) break; m_Text[m_Length++] = (char)('0' + number % 10); number /= 10; } while (number > 0);
            for (int left = start, right = m_Length - 1; left < right; left++, right--)
            { char character = m_Text[left]; m_Text[left] = m_Text[right]; m_Text[right] = character; }
        }
        private void AppendFixed(float number)
        {
            if (number < 0 && m_Length < m_Text.Length) { m_Text[m_Length++] = '-'; number = -number; }
            uint tenths = (uint)Mathf.Min(number * 10, uint.MaxValue);
            AppendNumber(tenths / 10);
            if (m_Length + 2 > m_Text.Length) return;
            m_Text[m_Length++] = '.'; m_Text[m_Length++] = (char)('0' + tenths % 10);
        }
        private void OnDestroy() { Unbind(); if (Instance == this) Instance = null; }
    }
}
