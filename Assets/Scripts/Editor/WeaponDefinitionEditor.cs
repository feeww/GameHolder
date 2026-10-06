using UnityEditor;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
    [CustomEditor(typeof(WeaponDefinition), true), CanEditMultipleObjects]
    public class WeaponDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var stats = serializedObject.FindProperty("UpgradableStats");
            if (stats != null) EditorGUILayout.PropertyField(stats);
            DrawPropertiesExcluding(serializedObject, "UpgradableStats", "ProjectileSpeed", "ProjectileLifetime", "BlastRadius", "ProjectileCount", "SpreadAngle");
            var type = serializedObject.FindProperty("Type");
            bool mixed = type.hasMultipleDifferentValues;
            if (!mixed && type.intValue != (int)WeaponType.Laser)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ProjectileSpeed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ProjectileLifetime"),
                new GUIContent(type.intValue == (int)WeaponType.Laser ? "Beam Duration" : "Projectile Lifetime"));
            if (!mixed && type.intValue == (int)WeaponType.Explosive)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("BlastRadius"));
            if (!mixed && type.intValue == (int)WeaponType.Standard)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ProjectileCount"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("SpreadAngle"));
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
