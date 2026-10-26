using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace VRoom
{
    /// <summary>
    /// The basic empty Unity world a project starts in: a plain ground under the default sky and sun. It is built at
    /// runtime next to the entrance room, which is switched off when the player enters.
    /// </summary>
    public static class EmptyWorld
    {
        public static GameObject Create(Vector3 origin)
        {
            var root = new GameObject("Project World");
            root.transform.position = origin;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var renderer = ground.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            renderer.sharedMaterial = new Material(shader) { color = new Color(0.55f, 0.57f, 0.6f) };
            return root;
        }

        /// <summary>The default Unity look: skybox ambient light and a white sun.</summary>
        public static void UseDefaultLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = false;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    light.color = Color.white;
                    light.intensity = 1f;
                    light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                    return;
                }
            }
        }

        /// <summary>A full-screen colour that fades in and out above everything.</summary>
        public static Image CreateFade(Color color)
        {
            var go = new GameObject("Fade", typeof(Canvas), typeof(Image));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var image = go.GetComponent<Image>();
            image.color = new Color(color.r, color.g, color.b, 0f);
            image.raycastTarget = false;
            return image;
        }
    }
}
