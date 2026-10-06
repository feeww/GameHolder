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
            if (!((GamePresentationBootstrap)target).TryValidateConfiguration(out string error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
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
