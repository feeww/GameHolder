using UnityEditor;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
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
                var child = property.FindPropertyRelative(field);
                EditorGUI.PropertyField(position, child, new GUIContent(title, child.tooltip));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }
        }
    }
}
