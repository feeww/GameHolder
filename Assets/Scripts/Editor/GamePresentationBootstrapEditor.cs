using UnityEditor;

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
}
