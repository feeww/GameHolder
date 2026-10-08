using UnityEditor;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
    public class RaritySettingsWindow : EditorWindow
    {
        [SerializeField] private GamePresentationBootstrap m_Bootstrap;
        private Vector2 m_Scroll;
        [SerializeField] private int m_Menu;
        internal static readonly GUIContent[] Pages = {
            new GUIContent("Rarities & Drop Chances", "Shared rarity names, colors and relative drop weights."),
            new GUIContent("Character Upgrades", "Eligible character stats and bonus percentages per rarity."),
            new GUIContent("Weapon Upgrades", "Eligible weapon stats and bonus percentages per rarity."),
            new GUIContent("Run Setup", "Starting character, starting weapon and enemy roster."),
            new GUIContent("Enemies", "Enemy batches, scaling, large spawns and elite multipliers."),
            new GUIContent("Progression", "Level-up choices, XP, rerolls and available weapons."),
            new GUIContent("World Rewards", "Capture zones, artifact roster and chest rewards."),
            new GUIContent("Presentation", "Camera, floor, inventory artwork and placeholder audio.") };
        private static readonly string[][] PageProperties = { null, null, null,
            new[] { "m_StartingCharacter", "m_StartingWeaponAsset", "m_EnemyTypes", "m_MaxEnemies", "m_MaxGems", "m_MaxPlayerProjectiles", "m_MaxEnemyProjectiles" },
            new[] { "m_EnemySpawning", "m_EliteSpawnProbability", "m_EliteHealthMultiplier", "m_EliteSpeedMultiplier", "m_EliteSizeMultiplier", "m_EliteMassMultiplier", "m_EliteDamageMultiplier", "m_EliteExperienceMultiplier", "m_EliteChestDropChanceMultiplier" },
            new[] { "m_Rewards" },
            new[] { "m_TemporaryZones", "m_Artifacts", "m_ArtifactChests" },
            new[] { "m_InventoryTexture", "m_InventoryUV", "m_Camera", "m_FloorMaterial", "m_EnemyDeathClip", "m_GemCollectClip", "m_PlayerHitClip" } };
        internal static readonly string[] StatNames = { "Max Health", "Pickup Radius", "Damage", "Attack Rate", "Range", "Size", "Blast Radius", "Lifetime" };
        internal static readonly string[] StatDescriptions = {
            "Additive percentage of the character's original maximum health.",
            "Additive percentage of the character's original pickup radius.",
            "Additive percentage of the weapon's original damage.",
            "Additive percentage of the weapon's original attack rate; reduces time between attacks.",
            "Additive percentage of the weapon's original targeting range and laser length.",
            "Additive percentage of the weapon's original projectile radius or laser half-width.",
            "Additive percentage of the weapon's original explosion radius; Explosive weapons only.",
            "Additive percentage of the weapon's original projectile lifetime; excluded for lasers." };
        private static GamePresentationBootstrap s_Context;
        internal static GamePresentationBootstrap Context
        {
            get
            {
                if (s_Context == null)
                {
                    var candidates = Object.FindObjectsByType<GamePresentationBootstrap>();
                    if (candidates.Length == 1) s_Context = candidates[0];
                }
                return s_Context;
            }
            set => s_Context = value;
        }

        [MenuItem("Pure DOTS/Rarities & Drop Chances")]
        public static void Open() => OpenPage(0);

        [MenuItem("Pure DOTS/Character Upgrades")]
        public static void OpenCharacterUpgrades() => OpenPage(1);

        [MenuItem("Pure DOTS/Weapon Upgrades")]
        public static void OpenWeaponUpgrades() => OpenPage(2);

        [MenuItem("Pure DOTS/Settings/Run Setup")]
        public static void OpenRunSetup() => OpenPage(3);
        [MenuItem("Pure DOTS/Settings/Enemies")]
        public static void OpenEnemies() => OpenPage(4);
        [MenuItem("Pure DOTS/Settings/Progression")]
        public static void OpenProgression() => OpenPage(5);
        [MenuItem("Pure DOTS/Settings/World Rewards")]
        public static void OpenWorldRewards() => OpenPage(6);
        [MenuItem("Pure DOTS/Settings/Presentation")]
        public static void OpenPresentation() => OpenPage(7);

        internal static void OpenPage(int menu, GamePresentationBootstrap bootstrap = null)
        {
            var window = GetWindow<RaritySettingsWindow>("Pure DOTS Settings");
            window.m_Menu = menu;
            window.m_Bootstrap = bootstrap != null ? bootstrap : Context;
            Context = window.m_Bootstrap;
        }

        private void OnGUI()
        {
            if (m_Bootstrap == null) m_Bootstrap = Context;
            m_Bootstrap = (GamePresentationBootstrap)EditorGUILayout.ObjectField(new GUIContent("Scene Settings", "Scene bootstrap edited by these pages. Select explicitly when several scenes are loaded."), m_Bootstrap, typeof(GamePresentationBootstrap), true);
            Context = m_Bootstrap;
            if (m_Bootstrap == null)
            { EditorGUILayout.HelpBox("Select a Game Presentation Bootstrap from the scene to edit its settings.", MessageType.Info); return; }
            m_Menu = EditorGUILayout.Popup(new GUIContent("Settings Page", "Choose which group of scene settings to edit."), m_Menu, Pages);
            var settings = new SerializedObject(m_Bootstrap);
            settings.Update();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawPage(settings, m_Menu);
            EditorGUILayout.EndScrollView();
            settings.ApplyModifiedProperties();
            m_Bootstrap.Rewards.EnsureRarityIds();
            if (!m_Bootstrap.TryValidateConfiguration(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void OnInspectorUpdate() => Repaint();

        internal static void DrawPage(SerializedObject settings, int page)
        {
            if (page == 0) GamePresentationBootstrapEditor.DrawRarities(settings);
            else if (page <= 2) DrawUpgrades(settings, page == 1);
            else
                foreach (string name in PageProperties[page]) EditorGUILayout.PropertyField(settings.FindProperty(name), true);
            if (page == 5)
                for (int i = 0; i < 3; i++)
                    if (GUILayout.Button(Pages[i])) OpenPage(i, (GamePresentationBootstrap)settings.targetObject);
        }

        private static void DrawUpgrades(SerializedObject settings, bool character)
        {
            EditorGUILayout.HelpBox(character
                ? "Select character stats and set each rarity's additive bonus percentage. Character assets can further restrict eligible stats."
                : "These percentages apply to all weapons. Each weapon asset selects its eligible stats; weapon type excludes inapplicable upgrades.", MessageType.Info);
            var enabled = settings.FindProperty(character ? "m_Rewards.CharacterStats" : "m_Rewards.WeaponStats");
            EditorGUILayout.PropertyField(enabled, new GUIContent("Stats That Can Improve", enabled.tooltip));
            int mask = character ? enabled.intValue : enabled.intValue << 2;
            var rarities = settings.FindProperty("m_Rewards.Rarities");
            for (int i = 0; i < rarities.arraySize; i++)
            {
                var rarity = rarities.GetArrayElementAtIndex(i);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(rarity.FindPropertyRelative("Name").stringValue, EditorStyles.boldLabel);
                var percentages = rarity.FindPropertyRelative("BonusPercents");
                for (int stat = character ? 0 : 2; stat <= (character ? 1 : (int)UpgradeStat.Lifetime); stat++)
                    if ((mask & (1 << stat)) != 0)
                        EditorGUILayout.PropertyField(percentages.FindPropertyRelative(((UpgradeStat)stat).ToString()), new GUIContent(StatNames[stat] + " (%)", StatDescriptions[stat]));
            }
        }
    }
}
