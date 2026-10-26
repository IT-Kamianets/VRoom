using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// A candle flame: the warm light flickers, and the flame sprite always turns towards the viewer, sways in the
    /// draught, and stretches and shrinks with the light. Each candle has its own rhythm.
    /// </summary>
    public class CandleFlame : MonoBehaviour
    {
        [SerializeField] private Light flameLight;
        [Tooltip("Pivot of the flame at the top of the wick; its child is the flame quad.")]
        [SerializeField] private Transform flame;
        [SerializeField, Min(0f)] private float baseIntensity = 2.5f;
        [SerializeField, Range(0f, 1f)] private float flicker = 0.28f;
        [SerializeField, Min(0.1f)] private float speed = 2.2f;

        private float _seed;
        private Vector3 _lightStart;
        private Vector3 _flameScale;
        private Transform _camera;

        private void Awake()
        {
            _seed = Random.value * 100f;
            _lightStart = flameLight.transform.localPosition;
            _flameScale = flame.localScale;
        }

        private void LateUpdate()
        {
            var t = Time.time * speed + _seed;
            // a slow breathing plus a quick shiver, and now and then a stronger gust
            var n = Mathf.PerlinNoise(t, 0.3f) * 0.6f + Mathf.PerlinNoise(t * 4.3f, 7.7f) * 0.3f + Mathf.PerlinNoise(t * 11f, 3.1f) * 0.1f;
            var gust = Mathf.Pow(Mathf.PerlinNoise(t * 0.35f, 9.4f), 4f);

            flameLight.intensity = baseIntensity * (1f - flicker * 0.5f + flicker * n - gust * 0.2f);
            var sway = new Vector3(Mathf.PerlinNoise(t * 1.3f, 2f) - 0.5f, 0f, Mathf.PerlinNoise(t * 1.1f, 5f) - 0.5f);
            flameLight.transform.localPosition = _lightStart + sway * 0.008f;

            if (_camera == null && Camera.main != null)
            {
                _camera = Camera.main.transform;
            }

            // face the viewer around the vertical axis, lean with the draught
            var yaw = Quaternion.identity;
            if (_camera != null)
            {
                var away = flame.position - _camera.position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-6f)
                {
                    yaw = Quaternion.LookRotation(away.normalized, Vector3.up);
                }
            }

            flame.rotation = yaw * Quaternion.Euler(sway.z * 16f + gust * 8f, 0f, -sway.x * 16f);
            var stretch = 0.88f + 0.28f * n - gust * 0.12f;
            flame.localScale = new Vector3(_flameScale.x * (1.05f - 0.12f * (stretch - 1f)), _flameScale.y * stretch, _flameScale.z);
        }
    }
}
