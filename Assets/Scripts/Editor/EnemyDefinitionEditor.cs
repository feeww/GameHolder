using UnityEditor;

namespace GameHolder.PureDots.Editor
{
    [CustomEditor(typeof(EnemyDefinition)), CanEditMultipleObjects]
    public class EnemyDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "Ranged", "Weapon", "AttackRange", "RetreatRange", "MeleeStoppingDistance", "Texture", "Tint");
            var ranged = serializedObject.FindProperty("Ranged");
            EditorGUILayout.PropertyField(ranged);
            if (ranged.boolValue || ranged.hasMultipleDifferentValues)
            {
                var weapon = serializedObject.FindProperty("Weapon");
                EditorGUILayout.PropertyField(weapon);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("AttackRange"));
                bool laser = weapon.objectReferenceValue is EnemyWeaponDefinition definition && definition.Type == WeaponType.Laser;
                if (!laser || weapon.hasMultipleDifferentValues)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("RetreatRange"));
                if (laser || weapon.hasMultipleDifferentValues)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("MeleeStoppingDistance"));
                if (weapon.objectReferenceValue == null && !weapon.hasMultipleDifferentValues)
                    EditorGUILayout.HelpBox("Assign an enemy weapon asset for ranged attacks.", MessageType.Error);
            }
            if (!ranged.boolValue || ranged.hasMultipleDifferentValues)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("MeleeStoppingDistance"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Texture"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Tint"));
            serializedObject.ApplyModifiedProperties();
            if (!((EnemyDefinition)target).TryValidate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
