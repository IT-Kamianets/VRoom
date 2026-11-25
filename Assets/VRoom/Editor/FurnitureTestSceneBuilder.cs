using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRoom.EditorTools
{
    /// <summary>Creates Assets/Scenes/FurnitureTest.unity: a camera, a sun and the ApartmentWallBuilder window.</summary>
    public static class FurnitureTestSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/FurnitureTest.unity";

        [MenuItem("VRoom/Create FurnitureTest Scene")]
        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(4.5f, 11f, -4f);
            cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            var camera = cam.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;

            var sun = new GameObject("Directional Light", typeof(Light));
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            new GameObject("ApartmentWallBuilder", typeof(ApartmentWallBuilder));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("FurnitureTest scene created: " + ScenePath);
        }
    }
}
