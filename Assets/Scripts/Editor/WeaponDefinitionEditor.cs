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
            DrawPropertiesExcluding(serializedObject, "UpgradableStats", "ProjectileSpeed", "ProjectileLifetime", "BlastRadius", "ProjectileCount", "SpreadAngle", "WeaponTexture", "ProjectileTexture", "Tint");
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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("WeaponTexture"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ProjectileTexture"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Tint"));
            serializedObject.ApplyModifiedProperties();
            if (stats != null)
            {
                serializedObject.Update();
                var bootstrap = Object.FindAnyObjectByType<GamePresentationBootstrap>();
                var available = bootstrap?.Rewards?.WeaponStats ?? WeaponUpgradeStats.All;
                foreach (var selected in targets)
                    available &= ((CharacterWeaponDefinition)selected).GetApplicableUpgradeStats();
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Stats That Can Improve", EditorStyles.boldLabel);
                for (int i = 0; i < 6; i++)
                {
                    int bit = 1 << i;
                    if (((int)available & bit) == 0) continue;
                    bool mixedStat = false, enabled = (stats.intValue & bit) != 0;
                    foreach (var selected in targets)
                        mixedStat |= ((((CharacterWeaponDefinition)selected).UpgradableStats & (WeaponUpgradeStats)bit) != 0) != enabled;
                    EditorGUI.showMixedValue = mixedStat;
                    EditorGUI.BeginChangeCheck();
                    bool next = EditorGUILayout.Toggle(RaritySettingsWindow.StatNames[i + 2], enabled);
                    if (EditorGUI.EndChangeCheck())
                        foreach (var selected in targets)
                        {
                            var weapon = new SerializedObject(selected);
                            var selectedStats = weapon.FindProperty("UpgradableStats");
                            selectedStats.intValue = next ? selectedStats.intValue | bit : selectedStats.intValue & ~bit;
                            weapon.ApplyModifiedProperties();
                        }
                    EditorGUI.showMixedValue = false;
                }
                if (available == WeaponUpgradeStats.None) EditorGUILayout.HelpBox("No applicable upgrades are enabled for this weapon in Weapon Upgrades.", MessageType.Info);
                if (GUILayout.Button("Shared Weapon Upgrade Percentages")) RaritySettingsWindow.OpenWeaponUpgrades();
            }
        }
    }
}
