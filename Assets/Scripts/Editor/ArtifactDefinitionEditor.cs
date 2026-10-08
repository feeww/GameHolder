using UnityEditor;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
    [CustomEditor(typeof(ArtifactDefinition)), CanEditMultipleObjects]
    public class ArtifactDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "Rarity");
            var bootstrap = (GamePresentationBootstrap)EditorGUILayout.ObjectField(
                new GUIContent("Scene Settings", "Scene whose rarity names apply to this artifact. Select explicitly when several scenes are loaded."),
                RaritySettingsWindow.Context, typeof(GamePresentationBootstrap), true);
            RaritySettingsWindow.Context = bootstrap;
            if (bootstrap?.Rewards?.Rarities == null || bootstrap.Rewards.Rarities.Length == 0)
                EditorGUILayout.HelpBox("Select the scene settings and configure rarities in Pure DOTS > Rarities & Drop Chances.", MessageType.Info);
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
                int selected = EditorGUILayout.IntPopup(new GUIContent("Rarity", rarity.tooltip), bootstrap.Rewards.FindRarityIndex(rarity.intValue) >= 0 ? rarity.intValue : -1, names, ids);
                if (EditorGUI.EndChangeCheck()) rarity.intValue = selected;
                EditorGUI.showMixedValue = false;
            }
            if (GUILayout.Button(RaritySettingsWindow.Pages[0])) RaritySettingsWindow.OpenPage(0, bootstrap);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
