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
            cam.orthographicSize = PresentationConstants.CameraOrthographicSize;
            cam.nearClipPlane = PresentationConstants.CameraNearClip;
            cam.farClipPlane = PresentationConstants.CameraFarClip;
            cam.transform.position = new Vector3(0, 0, PresentationConstants.CameraZPosition);

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
