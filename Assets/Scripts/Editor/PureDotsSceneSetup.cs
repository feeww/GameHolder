#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameHolder.PureDots.Editor
{
    public static class PureDotsSceneSetup
    {
        [MenuItem("Pure DOTS/Setup Active Scene")]
        public static void SetupActiveScene()
        {
            var existingBootstrap = Object.FindAnyObjectByType<GamePresentationBootstrap>();
            if (existingBootstrap == null)
            {
                var go = new GameObject("[PureDots_PresentationBootstrap]");
                go.AddComponent<GamePresentationBootstrap>();
                Undo.RegisterCreatedObjectUndo(go, "Create PureDots Presentation Bootstrap");
            }

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                Undo.RegisterCreatedObjectUndo(camGo, "Create Main Camera");
            }

            cam.orthographic = true;
            cam.orthographicSize = 10.0f;
            cam.nearClipPlane = -20.0f;
            cam.farClipPlane = 20.0f;
            cam.transform.position = new Vector3(0, 0, -10);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[PureDOTS] Scene successfully configured with PureDots Presentation Bootstrap and Camera!");
        }

        public static void SetupSceneBatchmode()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            SetupActiveScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[PureDOTS] SampleScene successfully saved with PureDots Presentation Bootstrap and Camera!");
        }
    }
}
#endif
