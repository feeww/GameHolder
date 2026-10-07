using UnityEditor;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
    [CustomEditor(typeof(GamePresentationBootstrap))]
    public class GamePresentationBootstrapEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            serializedObject.Update();
            DrawRarities(serializedObject);
            serializedObject.ApplyModifiedProperties();
            if (GUILayout.Button("Character Upgrade Settings")) RaritySettingsWindow.OpenCharacterUpgrades();
            if (GUILayout.Button("Weapon Upgrade Settings")) RaritySettingsWindow.OpenWeaponUpgrades();
            if (!((GamePresentationBootstrap)target).TryValidateConfiguration(out string error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        internal static void DrawRarities(SerializedObject settings)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rarities & Drop Chances", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Set each rarity's name, color and shared drop chance. Chances are relative weights; zero disables a rarity. Configure stat percentages in Character Upgrades and Weapon Upgrades.", MessageType.Info);
            var rarities = settings.FindProperty("m_Rewards.Rarities");
            int previousCount = rarities.arraySize;
            EditorGUILayout.PropertyField(rarities, new GUIContent("Upgrade / Item Rarities"), true);
            for (int i = previousCount; i < rarities.arraySize; i++)
            {
                var tier = rarities.GetArrayElementAtIndex(i);
                tier.FindPropertyRelative("Id").intValue = -1;
                tier.FindPropertyRelative("Name").stringValue = "Rarity " + (i + 1);
                tier.FindPropertyRelative("Weight").floatValue = 1;
                tier.FindPropertyRelative("Color").colorValue = Color.gray;
                tier.FindPropertyRelative("m_StatBonusesInitialized").boolValue = true;
                var percentages = tier.FindPropertyRelative("BonusPercents");
                for (int stat = 0; stat <= (int)UpgradeStat.Lifetime; stat++)
                    percentages.FindPropertyRelative(((UpgradeStat)stat).ToString()).floatValue = 5;
            }
        }
    }

    [CustomPropertyDrawer(typeof(RewardTierSettings))]
    public class RewardTierSettingsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => 3 * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position.height = EditorGUIUtility.singleLineHeight;
            Draw("Name", "Name");
            Draw("Weight", "Chance");
            Draw("Color", "Color");
            EditorGUI.EndProperty();

            void Draw(string field, string title)
            {
                EditorGUI.PropertyField(position, property.FindPropertyRelative(field), new GUIContent(title));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }
        }
    }

    [CustomEditor(typeof(ArtifactDefinition)), CanEditMultipleObjects]
    public class ArtifactDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "Rarity");
            var bootstrap = Object.FindAnyObjectByType<GamePresentationBootstrap>();
            if (bootstrap?.Rewards?.Rarities == null || bootstrap.Rewards.Rarities.Length == 0)
                EditorGUILayout.HelpBox("Create rarities in Pure DOTS > Rarities & Drop Chances in the scene that uses this artifact.", MessageType.Info);
            else
            {
                bootstrap.Rewards.EnsureRarityIds();
                var tiers = bootstrap.Rewards.Rarities;
                var rarity = serializedObject.FindProperty("Rarity");
                var names = new GUIContent[tiers.Length + 1];
                var ids = new int[names.Length];
                names[0] = new GUIContent("Select rarity (missing or removed)"); ids[0] = -1;
                for (int i = 0; i < tiers.Length; i++)
                { names[i + 1] = new GUIContent(tiers[i]?.Name ?? "Unnamed rarity"); ids[i + 1] = tiers[i]?.Id ?? -1; }
                EditorGUI.showMixedValue = rarity.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUILayout.IntPopup(new GUIContent("Rarity"), bootstrap.Rewards.FindRarityIndex(rarity.intValue) >= 0 ? rarity.intValue : -1, names, ids);
                if (EditorGUI.EndChangeCheck()) rarity.intValue = selected;
                EditorGUI.showMixedValue = false;
            }
            if (GUILayout.Button("Rarities & Drop Chances")) RaritySettingsWindow.Open();
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomPropertyDrawer(typeof(CharacterUpgradeStats))]
    public class CharacterUpgradeStatsDrawer : PropertyDrawer
    {
        private static readonly string[] Names = { "Max Health", "Pickup Radius" };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            int current = property.intValue;
            int next = EditorGUI.MaskField(position, label, current, Names);
            if (next != current)
                property.intValue = next == -1 ? (int)CharacterUpgradeStats.All : (next & (int)CharacterUpgradeStats.All);
            EditorGUI.EndProperty();
        }
    }

    [CustomPropertyDrawer(typeof(WeaponUpgradeStats))]
    public class WeaponUpgradeStatsDrawer : PropertyDrawer
    {
        private static readonly string[] Names = { "Damage", "Attack Rate", "Range", "Size", "Blast Radius", "Lifetime" };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            int current = property.intValue;
            int next = EditorGUI.MaskField(position, label, current, Names);
            if (next != current)
                property.intValue = next == -1 ? (int)WeaponUpgradeStats.All : (next & (int)WeaponUpgradeStats.All);
            EditorGUI.EndProperty();
        }
    }
}
