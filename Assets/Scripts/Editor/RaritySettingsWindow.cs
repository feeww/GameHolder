using UnityEditor;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
    public class RaritySettingsWindow : EditorWindow
    {
        [SerializeField] private GamePresentationBootstrap m_Bootstrap;
        private Vector2 m_Scroll;
        [SerializeField] private int m_Menu;
        private static readonly string[] Menus = { "Rarities", "Character Upgrades", "Weapon Upgrades" };
        internal static readonly string[] StatNames = { "Max Health", "Pickup Radius", "Damage", "Attack Rate", "Range", "Size", "Blast Radius", "Lifetime" };

        [MenuItem("Pure DOTS/Rarities & Drop Chances")]
        public static void Open() => OpenMenu(0);

        [MenuItem("Pure DOTS/Character Upgrades")]
        public static void OpenCharacterUpgrades() => OpenMenu(1);

        [MenuItem("Pure DOTS/Weapon Upgrades")]
        public static void OpenWeaponUpgrades() => OpenMenu(2);

        private static void OpenMenu(int menu)
        {
            var window = GetWindow<RaritySettingsWindow>(menu == 0 ? "Rarities & Drop Chances" : Menus[menu]);
            window.m_Menu = menu;
        }

        private void OnGUI()
        {
            if (m_Bootstrap == null) m_Bootstrap = Object.FindAnyObjectByType<GamePresentationBootstrap>();
            m_Bootstrap = (GamePresentationBootstrap)EditorGUILayout.ObjectField("Scene Settings", m_Bootstrap, typeof(GamePresentationBootstrap), true);
            if (m_Bootstrap == null)
            { EditorGUILayout.HelpBox("Open a scene with a Game Presentation Bootstrap to configure its rarities.", MessageType.Info); return; }
            m_Menu = GUILayout.Toolbar(m_Menu, Menus);
            var settings = new SerializedObject(m_Bootstrap);
            settings.Update();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (m_Menu == 0) GamePresentationBootstrapEditor.DrawRarities(settings);
            else DrawUpgrades(settings, m_Menu == 1);
            EditorGUILayout.EndScrollView();
            settings.ApplyModifiedProperties();
            m_Bootstrap.Rewards.EnsureRarityIds();
            if (!m_Bootstrap.TryValidateConfiguration(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void OnInspectorUpdate() => Repaint();

        private static void DrawUpgrades(SerializedObject settings, bool character)
        {
            EditorGUILayout.HelpBox(character
                ? "Select character stats and set each rarity's additive bonus percentage. Character assets can further restrict eligible stats."
                : "These percentages apply to all weapons. Each weapon asset selects its eligible stats; weapon type excludes inapplicable upgrades.", MessageType.Info);
            var enabled = settings.FindProperty(character ? "m_Rewards.CharacterStats" : "m_Rewards.WeaponStats");
            EditorGUILayout.PropertyField(enabled, new GUIContent("Stats That Can Improve"));
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
                        EditorGUILayout.PropertyField(percentages.FindPropertyRelative(((UpgradeStat)stat).ToString()), new GUIContent(StatNames[stat] + " (%)"));
            }
        }
    }
}
