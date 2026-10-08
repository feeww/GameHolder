using UnityEditor;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
    [CustomEditor(typeof(GamePresentationBootstrap))]
    public class GamePresentationBootstrapEditor : UnityEditor.Editor
    {
        private readonly bool[] m_Expanded = { true, false, false, false, false };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            for (int i = 0; i < m_Expanded.Length; i++)
            {
                int page = i + 3;
                m_Expanded[i] = EditorGUILayout.Foldout(m_Expanded[i], RaritySettingsWindow.Pages[page], true);
                if (!m_Expanded[i]) continue;
                EditorGUI.indentLevel++;
                RaritySettingsWindow.DrawPage(serializedObject, page);
                EditorGUI.indentLevel--;
                if (GUILayout.Button(new GUIContent("Open " + RaritySettingsWindow.Pages[page].text, RaritySettingsWindow.Pages[page].tooltip)))
                    RaritySettingsWindow.OpenPage(page, (GamePresentationBootstrap)target);
            }
            serializedObject.ApplyModifiedProperties();
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
            EditorGUILayout.PropertyField(rarities, new GUIContent("Upgrade / Item Rarities", rarities.tooltip), true);
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
}
