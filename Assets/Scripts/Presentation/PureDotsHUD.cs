using System.Collections.Generic;
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
        private BatchedHudText m_Stats, m_God, m_Attack, m_DeathStats, m_RewardTitle;
        private GameObject m_DeathPanel;
        private GameObject m_RewardPanel;
        private readonly BatchedHudText[] m_RewardLabels = new BatchedHudText[RewardSelection.MaxChoices];
        private readonly UnityEngine.UI.Button[] m_RewardButtons = new UnityEngine.UI.Button[RewardSelection.MaxChoices];
        private readonly UnityEngine.UI.RawImage[] m_RewardIcons = new UnityEngine.UI.RawImage[RewardSelection.MaxChoices];
        private readonly UnityEngine.UI.RawImage[] m_WeaponIcons = new UnityEngine.UI.RawImage[PlayerLoadout.Capacity];
        private readonly UnityEngine.UI.Button[] m_WeaponButtons = new UnityEngine.UI.Button[PlayerLoadout.Capacity];
        private readonly BatchedHudText[] m_WeaponLabels = new BatchedHudText[PlayerLoadout.Capacity];
        private IReadOnlyList<CharacterWeaponDefinition> m_Weapons = System.Array.Empty<CharacterWeaponDefinition>();
        private string[] m_WeaponNames = System.Array.Empty<string>();
        private GameObject m_WeaponDetailsPanel;
        private BatchedHudText m_WeaponDetails;
        private int m_InspectedWeapon = -1;
        private uint m_RewardPrompt, m_RewardGeneration;
        private bool m_RewardQueued;
        private GameObject m_InventoryPanel;
        private RectTransform m_InventoryContent, m_InventoryWindow;
        private BatchedHudText m_InventorySummary, m_InventoryEmpty;
        private UnityEngine.UI.Button m_InventoryButton;
        private RawImage m_InventoryIcon;
        private readonly BatchedHudText[] m_ArtifactLabels = new BatchedHudText[ArtifactInventory.Capacity];
        private readonly RawImage[] m_ArtifactIcons = new RawImage[ArtifactInventory.Capacity];
        private IReadOnlyList<ArtifactDefinition> m_Artifacts = System.Array.Empty<ArtifactDefinition>();
        private string[] m_ArtifactNames = System.Array.Empty<string>();
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
            var weapons = Panel("Selected weapons panel", root.transform, new Vector2(350, 180), new Vector2(-175, -190), new Vector2(.5f, 0));
            Text("Weapons title", weapons, new Vector2(320, 26), new Vector2(15, 8), "SELECTED WEAPONS - CLICK FOR STATS");
            for (int i = 0; i < m_WeaponButtons.Length; i++)
            {
                int slot = i;
                m_WeaponLabels[i] = RewardButton(weapons, new Vector2(15 + i * 165, 40), "Weapon " + (i + 1),
                    () => InspectWeapon(slot == 0 ? m_Snapshot.Loadout.FirstIndex : m_Snapshot.Loadout.SecondIndex), out m_WeaponButtons[i]);
                ((RectTransform)m_WeaponButtons[i].transform).sizeDelta = new Vector2(150, 124);
                m_WeaponLabels[i].rectTransform.sizeDelta = new Vector2(130, 48);
                m_WeaponLabels[i].rectTransform.anchoredPosition = new Vector2(10, -76);
                m_WeaponIcons[i] = WeaponIcon(m_WeaponButtons[i].transform, new Vector2(75, 38));
            }
            var death = Panel("Run ended panel", root.transform, HudLayout.DeathPanelSize, HudLayout.DeathPanelPosition, new Vector2(.5f, .5f));
            m_DeathPanel = death.gameObject;
            m_DeathStats = Text("Run ended stats", death, HudLayout.DeathTextSize, HudLayout.DeathTextPosition, "RUN ENDED");
            Button(death, HudLayout.DeathRestartY, "Restart run", RespawnPlayer);
            var rewards = Panel("Reward panel", root.transform, new Vector2(610, 220), new Vector2(-305, -110), new Vector2(.5f, .5f));
            m_RewardPanel = rewards.gameObject;
            m_RewardTitle = Text("Reward title", rewards, new Vector2(580, 48), new Vector2(15, 10), "LEVEL UP - CHOOSE ONE");
            for (int i = 0; i < m_RewardButtons.Length; i++)
            {
                int slot = i;
                m_RewardLabels[i] = RewardButton(rewards, new Vector2(15 + i % 2 * 295, 70 + i / 2 * 150),
                    "Choice " + (i + 1), () => ChooseReward(slot), out m_RewardButtons[i]);
                m_RewardIcons[i] = WeaponIcon(m_RewardButtons[i].transform, new Vector2(46, 46));
            }
            var details = Panel("Weapon details panel", root.transform, new Vector2(360, 340), new Vector2(-180, -170), new Vector2(.5f, .5f));
            m_WeaponDetailsPanel = details.gameObject;
            details.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var detailsColor = HudLayout.PanelColor; detailsColor.a = 1;
            details.GetComponent<UnityEngine.UI.Image>().color = detailsColor;
            m_WeaponDetails = Text("Weapon stats", details, new Vector2(330, 280), new Vector2(15, 12), "");
            Button(details, 298, "Close stats (Esc)", CloseWeaponDetails);
            var bag = Rect("Inventory button", root.transform, new Vector2(100, 100), new Vector2(-116, -116), new Vector2(1, 0));
            var bagBackground = bag.gameObject.AddComponent<Image>(); bagBackground.color = HudLayout.ButtonColor;
            m_InventoryButton = bag.gameObject.AddComponent<UnityEngine.UI.Button>(); m_InventoryButton.targetGraphic = bagBackground;
            m_InventoryButton.onClick.AddListener(OpenInventory);
            m_InventoryIcon = WeaponIcon(bag, new Vector2(50, 40)); m_InventoryIcon.gameObject.name = "Bag image";
            Text("Inventory label", bag, new Vector2(92, 26), new Vector2(7, 74), "Inventory");
            var overlay = Panel("Inventory overlay", root.transform, Vector2.zero, Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0, 0, 0, .65f); overlay.GetComponent<Image>().raycastTarget = true;
            m_InventoryPanel = overlay.gameObject;
            m_InventoryWindow = Panel("Inventory panel", overlay, new Vector2(650, 570), new Vector2(-325, -285), new Vector2(.5f, .5f));
            var inventoryBackground = m_InventoryWindow.GetComponent<Image>();
            var opaque = HudLayout.PanelColor; opaque.a = 1; inventoryBackground.color = opaque; inventoryBackground.raycastTarget = true;
            Text("Inventory title", m_InventoryWindow, new Vector2(610, 26), new Vector2(20, 12), "ARTIFACT INVENTORY - GAME PAUSED");
            m_InventorySummary = Text("Artifact totals", m_InventoryWindow, new Vector2(610, 78), new Vector2(20, 46), "");
            var viewport = Rect("Artifact viewport", m_InventoryWindow, new Vector2(610, 370), new Vector2(20, 132), Vector2.up);
            var viewportImage = viewport.gameObject.AddComponent<Image>(); viewportImage.color = new Color(0, 0, 0, .1f);
            viewport.gameObject.AddComponent<RectMask2D>();
            m_InventoryContent = Rect("Artifacts", viewport, new Vector2(610, 370), Vector2.zero, Vector2.up);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = m_InventoryContent;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            m_InventoryEmpty = Text("Empty inventory", m_InventoryContent, new Vector2(580, 78), new Vector2(15, 12),
                "No artifacts collected.\nEnemies may drop chests.\nWalk within pickup radius to open them.");
            for (int i = 0; i < m_ArtifactLabels.Length; i++)
            {
                var row = Panel("Artifact " + (i + 1), m_InventoryContent, new Vector2(605, 110), new Vector2(0, i * 116), Vector2.up);
                m_ArtifactIcons[i] = WeaponIcon(row, new Vector2(50, 55)); m_ArtifactIcons[i].gameObject.name = "Artifact image";
                m_ArtifactLabels[i] = Text("Artifact stats", row, new Vector2(490, 106), new Vector2(100, 4), "");
            }
            Button(m_InventoryWindow, 520, "Close inventory (Esc)", CloseInventory);
            if (EventSystem.current == null)
            {
                var events = new GameObject("HUD event system", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(root.transform, false);
            }
            // Reserve maximum glyph geometry once, before the first rendered frame.
            for (int i = 0; i < m_Text.Length; i++) m_Text[i] = 'W';
            m_Stats.SetText(m_Text, m_Text.Length); m_DeathStats.SetText(m_Text, m_Text.Length);
            for (int i = 0; i < m_RewardLabels.Length; i++) m_RewardLabels[i].SetText(m_Text, m_Text.Length);
            for (int i = 0; i < m_WeaponLabels.Length; i++) m_WeaponLabels[i].SetText(m_Text, m_Text.Length);
            m_WeaponDetails.SetText(m_Text, m_Text.Length);
            m_InventorySummary.SetText(m_Text, m_Text.Length);
            for (int i = 0; i < m_ArtifactLabels.Length; i++) m_ArtifactLabels[i].SetText(m_Text, m_Text.Length);
            Canvas.ForceUpdateCanvases();
            m_DeathPanel.SetActive(false);
            m_RewardPanel.SetActive(false);
            m_WeaponDetailsPanel.SetActive(false);
            m_InventoryPanel.SetActive(false);
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
            if (snapshot.Generation != m_Snapshot.Generation) CloseWeaponDetails();
            m_Snapshot = snapshot; m_Bound = true;
            // The bridge calls this after its simulation fence, so UI input never races native jobs.
            int sent = 0;
            while (sent < m_PendingCount && commands.IsCreated && commands.Count < SimulationConstants.CommandQueueCapacity)
                commands.Enqueue(m_Pending[sent++]);
            m_PendingCount -= sent;
            System.Array.Copy(m_Pending, sent, m_Pending, 0, m_PendingCount);
            RefreshText();
        }
        public void Unbind() { m_Bound = false; m_PendingCount = 0; CloseWeaponDetails(); if (m_InventoryPanel != null) m_InventoryPanel.SetActive(false); }
        public void BindArtifacts(IReadOnlyList<ArtifactDefinition> artifacts, Texture2D bagTexture, Rect? bagUV = null)
        {
            m_Artifacts = artifacts ?? System.Array.Empty<ArtifactDefinition>();
            m_ArtifactNames = new string[m_Artifacts.Count];
            for (int i = 0; i < m_ArtifactNames.Length; i++) m_ArtifactNames[i] = m_Artifacts[i].name;
            SetIcon(m_InventoryIcon, bagTexture, bagUV);
        }
        private void OpenInventory()
        {
            if (!m_Bound || m_Snapshot.InventoryOpen != 0 || m_PendingCount >= m_Pending.Length) return;
            CloseWeaponDetails();
            m_Pending[m_PendingCount++] = new SimulationCommand { Kind = SimulationCommandKind.Inventory, Value = 1, Generation = m_Snapshot.Generation };
        }
        private void CloseInventory()
        {
            if (!m_Bound || m_Snapshot.InventoryOpen == 0 || m_PendingCount >= m_Pending.Length) return;
            m_Pending[m_PendingCount++] = new SimulationCommand { Kind = SimulationCommandKind.Inventory, Generation = m_Snapshot.Generation };
        }
        private void RefreshInventory()
        {
            m_InventoryButton.interactable = m_Bound;
            bool open = m_Bound && m_Snapshot.InventoryOpen != 0;
            if (m_InventoryPanel.activeSelf != open) m_InventoryPanel.SetActive(open);
            if (!open) return;
            float scale = Mathf.Min(1, (Screen.width - 20f) / 650, (Screen.height - 20f) / 570);
            m_InventoryWindow.localScale = Vector3.one * scale; m_InventoryWindow.anchoredPosition = new Vector2(-325, 285) * scale;
            var bonuses = m_Snapshot.Inventory.Bonuses;
            m_Length = 0; Append("Max HP: "); AppendFixed(m_Snapshot.Player.MaxHealth); Append("  (artifacts +"); AppendFixed(bonuses.MaxHealth); Append(")\nPickup radius: ");
            AppendFixed(m_Snapshot.Player.MagnetRadius); Append("  (artifacts +"); AppendFixed(bonuses.PickupRadius); Append(")\nRegeneration: ");
            AppendFixed(m_Snapshot.Player.HealthRegeneration); Append(" HP/s  (artifacts +"); AppendFixed(bonuses.HealthRegeneration); Append(")");
            m_InventorySummary.SetText(m_Text, m_Length);
            int count = m_Snapshot.Inventory.Items.Length;
            m_InventoryEmpty.gameObject.SetActive(count == 0);
            m_InventoryContent.sizeDelta = new Vector2(610, Mathf.Max(370, count * 116));
            for (int i = 0; i < m_ArtifactLabels.Length; i++)
            {
                var row = m_ArtifactLabels[i].transform.parent.gameObject; row.SetActive(i < count);
                if (i >= count) continue;
                var stack = m_Snapshot.Inventory.Items[i];
                if (stack.Index < 0 || stack.Index >= m_Artifacts.Count) { row.SetActive(false); continue; }
                var artifact = m_Artifacts[stack.Index]; SetIcon(m_ArtifactIcons[i], artifact.Texture, artifact.IconUV);
                m_Length = 0; Append(m_ArtifactNames[stack.Index], 40); Append(" x"); AppendNumber(stack.Quantity);
                ArtifactStat("HP", artifact.MaxHealth, stack.Quantity);
                ArtifactStat("Pickup radius", artifact.PickupRadius, stack.Quantity);
                ArtifactStat("HP/s", artifact.HealthRegeneration, stack.Quantity);
                m_ArtifactLabels[i].SetText(m_Text, m_Length);
            }
        }
        private void ArtifactStat(string name, float value, uint quantity)
        {
            if (value <= 0) return;
            Append("\n+"); AppendFixed(value); Append(" "); Append(name); Append(" each  |  +"); AppendFixed(value * quantity); Append(" total");
        }
        public void BindWeapons(IReadOnlyList<CharacterWeaponDefinition> weapons)
        {
            m_Weapons = weapons ?? System.Array.Empty<CharacterWeaponDefinition>();
            m_WeaponNames = new string[m_Weapons.Count];
            for (int i = 0; i < m_Weapons.Count; i++) m_WeaponNames[i] = m_Weapons[i] != null ? m_Weapons[i].name : "Weapon";
            RefreshWeapons();
        }
        private static UnityEngine.UI.RawImage WeaponIcon(Transform parent, Vector2 center)
        {
            var rect = Rect("Weapon image", parent, new Vector2(72, 72), center, Vector2.up);
            rect.pivot = new Vector2(.5f, .5f);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.RawImage>(); image.raycastTarget = false;
            return image;
        }
        private CharacterWeaponDefinition WeaponAsset(int index) => index >= 0 && index < m_Weapons.Count ? m_Weapons[index] : null;
        private string WeaponName(int index) => index >= 0 && index < m_WeaponNames.Length ? m_WeaponNames[index] : "Weapon";
        private void SetWeaponIcon(UnityEngine.UI.RawImage image, int index)
        {
            var asset = WeaponAsset(index);
            var texture = asset != null ? asset.WeaponTexture : null;
            SetIcon(image, texture);
        }
        private static void SetIcon(RawImage image, Texture2D texture, Rect? crop = null)
        {
            if (image.gameObject.activeSelf != (texture != null)) image.gameObject.SetActive(texture != null);
            var uv = crop ?? new Rect(0, 0, 1, 1);
            if (image.texture == texture && image.uvRect == uv) return;
            image.texture = texture; image.uvRect = uv;
            if (texture != null)
            {
                var size = new Vector2(texture.width * uv.width, texture.height * uv.height);
                image.rectTransform.sizeDelta = size * (72f / Mathf.Max(size.x, size.y));
            }
        }
        private uint WeaponLevel(int slot) => System.Math.Max(1u, slot == 0 ? m_Snapshot.Loadout.FirstLevel : m_Snapshot.Loadout.SecondLevel);
        private int RewardWeaponIndex(RewardChoice choice) => choice.Kind == RewardKind.NewWeapon ? choice.WeaponIndex
            : choice.Target == 1 ? m_Snapshot.Loadout.FirstIndex : choice.Target == 2 ? m_Snapshot.Loadout.SecondIndex : -1;
        private void RefreshWeapons()
        {
            for (int i = 0; i < m_WeaponButtons.Length; i++)
            {
                bool equipped = i < m_Snapshot.WeaponCount;
                int index = i == 0 ? m_Snapshot.Loadout.FirstIndex : m_Snapshot.Loadout.SecondIndex;
                m_WeaponButtons[i].interactable = m_Bound && equipped && m_Snapshot.Player.IsDead == 0;
                SetWeaponIcon(m_WeaponIcons[i], equipped ? index : -1);
                m_Length = 0;
                if (equipped)
                {
                    Append(WeaponName(index), 20); Append("\nLevel "); AppendNumber(WeaponLevel(i));
                }
                else Append("Empty slot");
                m_WeaponLabels[i].SetText(m_Text, m_Length);
            }
            if (m_WeaponDetailsPanel.activeSelf) RefreshWeaponDetails();
        }
        private void InspectWeapon(int index)
        {
            if (!m_Bound || m_Snapshot.Player.IsDead != 0 || WeaponAsset(index) == null) return;
            m_InspectedWeapon = index; m_WeaponDetailsPanel.SetActive(true);
            var rect = (RectTransform)m_WeaponDetailsPanel.transform;
            float scale = Mathf.Min(1, (Screen.width - 20f) / 360, (Screen.height - 20f) / 340);
            rect.localScale = Vector3.one * scale; rect.anchoredPosition = new Vector2(-180, 170) * scale;
            RefreshWeaponDetails();
        }
        private void CloseWeaponDetails()
        { m_InspectedWeapon = -1; if (m_WeaponDetailsPanel != null) m_WeaponDetailsPanel.SetActive(false); }
        private void RefreshWeaponDetails()
        {
            var asset = WeaponAsset(m_InspectedWeapon);
            if (asset == null) { CloseWeaponDetails(); return; }
            bool first = m_Snapshot.WeaponCount > 0 && m_InspectedWeapon == m_Snapshot.Loadout.FirstIndex;
            bool second = m_Snapshot.WeaponCount > 1 && m_InspectedWeapon == m_Snapshot.Loadout.SecondIndex;
            var weapon = first ? m_Snapshot.FirstWeapon : second ? m_Snapshot.Loadout.SecondWeapon : asset.ToConfig();
            m_Length = 0; Append(WeaponName(m_InspectedWeapon), 40); Append("\nLevel "); AppendNumber(first ? WeaponLevel(0) : second ? WeaponLevel(1) : 1);
            Append(weapon.Type == WeaponType.Laser ? " - Laser" : weapon.Type == WeaponType.Explosive ? " - Explosive" : " - Standard");
            Append("\nDamage: "); AppendFixed(weapon.Damage);
            Append("\nAttack interval: "); AppendFixed(weapon.Interval); Append(" s");
            Append("\nRange: "); AppendFixed(weapon.Range); Append("\nRadius: "); AppendFixed(weapon.Radius);
            if (weapon.Type != WeaponType.Laser) { Append("\nSpeed: "); AppendFixed(weapon.Speed); }
            Append(weapon.Type == WeaponType.Laser ? "\nBeam duration: " : "\nLifetime: "); AppendFixed(weapon.Lifetime); Append(" s");
            if (weapon.Type == WeaponType.Explosive) { Append("\nBlast radius: "); AppendFixed(weapon.BlastRadius); }
            if (weapon.Type == WeaponType.Standard)
            {
                Append("\nProjectiles: "); AppendNumber((ulong)weapon.Count);
                Append("\nSpread: "); AppendFixed(weapon.SpreadAngle * Mathf.Rad2Deg); Append(" deg");
            }
            m_WeaponDetails.SetText(m_Text, m_Length);
        }
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
            if (!m_Bound || m_RewardQueued || m_Snapshot.InventoryOpen != 0 || m_Snapshot.Rewards.Active == 0 || slot < 0 ||
                slot >= m_Snapshot.Rewards.Choices.Length || m_PendingCount >= m_Pending.Length) return;
            m_Pending[m_PendingCount++] = new SimulationCommand { Kind = SimulationCommandKind.SelectReward, Value = slot,
                PromptId = m_Snapshot.Rewards.PromptId, Generation = m_Snapshot.Generation };
            m_RewardQueued = true;
            CloseWeaponDetails();
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
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { if (m_Snapshot.InventoryOpen != 0) CloseInventory(); else CloseWeaponDetails(); }
            if (Keyboard.current != null && m_Snapshot.Rewards.Active != 0 && m_Snapshot.InventoryOpen == 0)
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
            if (dead) CloseWeaponDetails();
            RefreshWeapons();
            RefreshRewards(!dead && m_Snapshot.Rewards.Active != 0);
            RefreshInventory();
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
                var choice = m_Snapshot.Rewards.Choices[i];
                if (choice.Kind == RewardKind.Artifact)
                {
                    var artifact = m_Artifacts[choice.ArtifactIndex];
                    SetIcon(m_RewardIcons[i], artifact.Texture, artifact.IconUV);
                }
                else SetWeaponIcon(m_RewardIcons[i], RewardWeaponIndex(choice));
                bool icon = m_RewardIcons[i].gameObject.activeSelf;
                m_RewardLabels[i].rectTransform.sizeDelta = new Vector2(icon ? 174 : 260, 130);
                m_RewardLabels[i].rectTransform.anchoredPosition = new Vector2(icon ? 96 : 10, -8);
            }
            m_RewardTitle.SetText(count > 0 && m_Snapshot.Rewards.Choices[0].Kind == RewardKind.Artifact
                ? "ARTIFACT CHEST - CHOOSE ONE\nClick a card or its image, or press its number, to collect."
                : "LEVEL UP - CHOOSE ONE\nClick a card or its image, or press its number, to select.");
            if (count > 0 && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(m_RewardButtons[0].gameObject);
        }
        private void RewardText(BatchedHudText label, RewardChoice choice, uint slot)
        {
            m_Length = 0; AppendNumber(slot); Append(". ");
            if (choice.Kind == RewardKind.Artifact)
            {
                switch ((ArtifactRarity)choice.Rarity)
                {
                    case ArtifactRarity.Rare: Append("Rare"); break;
                    case ArtifactRarity.Epic: Append("Epic"); break;
                    case ArtifactRarity.Legendary: Append("Legendary"); break;
                    default: Append("Common"); break;
                }
                Append("\n"); Append(m_ArtifactNames[choice.ArtifactIndex], 20);
                var artifact = m_Artifacts[choice.ArtifactIndex];
                if (artifact.MaxHealth > 0) { Append("\n+"); AppendFixed(artifact.MaxHealth); Append(" max HP"); }
                if (artifact.PickupRadius > 0) { Append("\n+"); AppendFixed(artifact.PickupRadius); Append(" pickup radius"); }
                if (artifact.HealthRegeneration > 0) { Append("\n+"); AppendFixed(artifact.HealthRegeneration); Append(" HP/s"); }
                label.color = new Color(choice.Color.x, choice.Color.y, choice.Color.z, choice.Color.w);
            }
            else if (choice.Kind == RewardKind.NewWeapon)
            {
                Append("NEW WEAPON\n");
                Append(choice.Name);
                Append("\nLevel 1\nEquip in free slot");
                label.color = Color.white;
            }
            else
            {
                Append(choice.Name);
                Append("\n");
                var asset = WeaponAsset(RewardWeaponIndex(choice));
                Append(choice.Target == 0 ? "Character" : asset != null ? WeaponName(RewardWeaponIndex(choice)) : choice.Target == 1 ? "Weapon 1" : "Weapon 2", 20);
                if (choice.Target != 0) { Append("\nLevel "); AppendNumber(WeaponLevel(choice.Target - 1)); Append(" -> "); AppendNumber(WeaponLevel(choice.Target - 1) + 1); }
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
        private void Append(string text, int limit = PresentationConstants.HudTextCapacity)
        { for (int i = 0; i < text.Length && i < limit && m_Length < m_Text.Length; i++) m_Text[m_Length++] = text[i]; }
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
