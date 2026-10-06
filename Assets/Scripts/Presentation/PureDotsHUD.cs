using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem;
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
        private GameObject m_RewardPanel;
        private readonly BatchedHudText[] m_RewardLabels = new BatchedHudText[RewardSelection.MaxChoices];
        private readonly UnityEngine.UI.Button[] m_RewardButtons = new UnityEngine.UI.Button[RewardSelection.MaxChoices];
        private uint m_RewardPrompt, m_RewardGeneration;
        private bool m_RewardQueued;
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
            var rewards = Panel("Reward panel", root.transform, new Vector2(610, 220), new Vector2(-305, -110), new Vector2(.5f, .5f));
            m_RewardPanel = rewards.gameObject;
            Text("Reward title", rewards, new Vector2(580, 48), new Vector2(15, 10), "LEVEL UP - CHOOSE ONE\nGame paused. Click a card or press its number.");
            for (int i = 0; i < m_RewardButtons.Length; i++)
            {
                int slot = i;
                m_RewardLabels[i] = RewardButton(rewards, new Vector2(15 + i % 2 * 295, 70 + i / 2 * 150),
                    "Choice " + (i + 1), () => ChooseReward(slot), out m_RewardButtons[i]);
            }
            if (EventSystem.current == null)
            {
                var events = new GameObject("HUD event system", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(root.transform, false);
            }
            // Reserve maximum glyph geometry once, before the first rendered frame.
            for (int i = 0; i < m_Text.Length; i++) m_Text[i] = 'W';
            m_Stats.SetText(m_Text, m_Text.Length); m_DeathStats.SetText(m_Text, m_Text.Length);
            for (int i = 0; i < m_RewardLabels.Length; i++) m_RewardLabels[i].SetText(m_Text, m_Text.Length);
            Canvas.ForceUpdateCanvases();
            m_DeathPanel.SetActive(false);
            m_RewardPanel.SetActive(false);
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
            int sent = 0;
            while (sent < m_PendingCount && commands.IsCreated && commands.Count < SimulationConstants.CommandQueueCapacity)
                commands.Enqueue(m_Pending[sent++]);
            m_PendingCount -= sent;
            System.Array.Copy(m_Pending, sent, m_Pending, 0, m_PendingCount);
            RefreshText();
        }
        public void Unbind() { m_Bound = false; m_PendingCount = 0; }
        private static BatchedHudText RewardButton(Transform parent, Vector2 position, string name, UnityAction action, out UnityEngine.UI.Button button)
        {
            var rect = Rect(name, parent, new Vector2(280, 140), position, Vector2.up);
            var image = rect.gameObject.AddComponent<Image>(); image.color = HudLayout.ButtonColor;
            button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            button.onClick.AddListener(action);
            return Text("Label", rect, new Vector2(260, 130), new Vector2(10, 8), "");
        }
        private void ChooseReward(int slot)
        {
            if (!m_Bound || m_RewardQueued || m_Snapshot.Rewards.Active == 0 || slot < 0 ||
                slot >= m_Snapshot.Rewards.Choices.Length || m_PendingCount >= m_Pending.Length) return;
            m_Pending[m_PendingCount++] = new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = slot,
                PromptId = m_Snapshot.Rewards.PromptId, Generation = m_Snapshot.Generation };
            m_RewardQueued = true;
            for (int i = 0; i < m_RewardButtons.Length; i++) m_RewardButtons[i].interactable = false;
        }
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
            if (Keyboard.current != null && m_Snapshot.Rewards.Active != 0)
            {
                for (int i = 0; i < m_Snapshot.Rewards.Choices.Length; i++)
                    if (Keyboard.current[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) { ChooseReward(i); break; }
            }
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
            Append("\nWeapons: "); AppendNumber((ulong)Mathf.Max(0, m_Snapshot.WeaponCount)); Append(" / ");
            AppendNumber((ulong)Mathf.Max(1, m_Snapshot.WeaponCapacity));
            m_Stats.SetText(m_Text, m_Length);
            m_God.SetText(m_Snapshot.GodMode != 0 ? "God mode: ON" : "God mode: OFF");
            m_Attack.SetText(m_Snapshot.AutoAttack != 0 ? "Auto attack: ON" : "Auto attack: OFF");
            bool dead = m_Snapshot.Player.IsDead != 0;
            RefreshRewards(!dead && m_Snapshot.Rewards.Active != 0);
            if (m_DeathPanel.activeSelf != dead) m_DeathPanel.SetActive(dead);
            if (!dead) return;
            m_Length = 0; Append("RUN ENDED\nKills: "); AppendNumber(m_Snapshot.Kills);
            Append("\nLevel: "); AppendNumber(m_Snapshot.Player.Level); m_DeathStats.SetText(m_Text, m_Length);
        }
        private void RefreshRewards(bool active)
        {
            bool opened = active && !m_RewardPanel.activeSelf;
            if (m_RewardPanel.activeSelf != active) m_RewardPanel.SetActive(active);
            if (!active) { m_RewardQueued = false; return; }
            if (!opened && m_RewardPrompt == m_Snapshot.Rewards.PromptId && m_RewardGeneration == m_Snapshot.Generation) return;
            m_RewardPrompt = m_Snapshot.Rewards.PromptId; m_RewardGeneration = m_Snapshot.Generation; m_RewardQueued = false;
            int count = m_Snapshot.Rewards.Choices.Length;
            float height = 70 + 150 * ((count + 1) / 2);
            var rect = (RectTransform)m_RewardPanel.transform;
            rect.sizeDelta = new Vector2(610, height);
            float scale = Mathf.Min(1, (Screen.height - 20f) / height, (Screen.width - 20f) / 610);
            rect.localScale = Vector3.one * scale;
            rect.anchoredPosition = new Vector2(-305 * scale, height / 2 * scale);
            for (int i = 0; i < m_RewardButtons.Length; i++)
            {
                var button = m_RewardButtons[i];
                button.gameObject.SetActive(i < count);
                if (i >= count) continue;
                button.interactable = true;
                button.navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = i % 2 == 1 ? m_RewardButtons[i - 1] : null,
                    selectOnRight = i % 2 == 0 && i + 1 < count ? m_RewardButtons[i + 1] : null,
                    selectOnUp = i >= 2 ? m_RewardButtons[i - 2] : null,
                    selectOnDown = i + 2 < count ? m_RewardButtons[i + 2] : null };
                RewardText(m_RewardLabels[i], m_Snapshot.Rewards.Choices[i], (uint)i + 1);
            }
            if (count > 0 && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(m_RewardButtons[0].gameObject);
        }
        private void RewardText(BatchedHudText label, RewardChoice choice, uint slot)
        {
            m_Length = 0; AppendNumber(slot); Append(". ");
            if (choice.Kind == RewardKind.NewWeapon)
            {
                Append("NEW WEAPON\n");
                Append(choice.Name);
                Append("\nEquip in free slot");
                label.color = Color.white;
            }
            else
            {
                Append(choice.Name);
                Append("\n"); Append(choice.Target == 0 ? "Character" : choice.Target == 1 ? "Weapon 1" : "Weapon 2");
                Append("\n");
                switch (choice.Stat)
                {
                    case UpgradeStat.MaxHealth: Append("Max health"); break;
                    case UpgradeStat.PickupRadius: Append("Pickup radius"); break;
                    case UpgradeStat.Damage: Append("Damage"); break;
                    case UpgradeStat.AttackRate: Append("Attack rate"); break;
                    case UpgradeStat.Range: Append("Attack range"); break;
                    case UpgradeStat.Size: Append("Projectile / beam size"); break;
                    case UpgradeStat.BlastRadius: Append("Blast radius"); break;
                    case UpgradeStat.Lifetime: Append("Projectile lifetime"); break;
                }
                Append("\n+"); AppendFixed(choice.Bonus * 100); Append("%");
                label.color = new Color(choice.Color.x, choice.Color.y, choice.Color.z, choice.Color.w);
            }
            label.SetText(m_Text, m_Length);
        }
        private void Append(string text) { for (int i = 0; i < text.Length && m_Length < m_Text.Length; i++) m_Text[m_Length++] = text[i]; }
        private void Append(FixedString64Bytes text)
        {
            for (int i = 0; i < text.Length && m_Length < m_Text.Length; i++) m_Text[m_Length++] = text[i] < 128 ? (char)text[i] : '?';
        }
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
