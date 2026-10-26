using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// A candle flame: the warm light flickers a little and the flame itself sways and changes size,
    /// each candle with its own rhythm.
    /// </summary>
    public class CandleFlame : MonoBehaviour
    {
        [SerializeField] private Light flameLight;
        [SerializeField] private Transform flame;
        [SerializeField, Min(0f)] private float baseIntensity = 6f;
        [SerializeField, Range(0f, 1f)] private float flicker = 0.25f;
        [SerializeField, Min(0.1f)] private float speed = 2.2f;

        private float _seed;
        private Vector3 _lightStart;
        private Vector3 _flameScale;

        private void Awake()
        {
            _seed = Random.value * 100f;
            _lightStart = flameLight.transform.localPosition;
            _flameScale = flame.localScale;
        }

        private void Update()
        {
            var t = Time.time * speed + _seed;
            // two noise layers: a slow breathing and a quick shiver
            var n = Mathf.PerlinNoise(t, 0.3f) * 0.7f + Mathf.PerlinNoise(t * 4.1f, 7.7f) * 0.3f;
            flameLight.intensity = baseIntensity * (1f - flicker * 0.5f + flicker * n);

            var sway = new Vector3(Mathf.PerlinNoise(t * 1.3f, 2f) - 0.5f, 0f, Mathf.PerlinNoise(t * 1.1f, 5f) - 0.5f);
            flameLight.transform.localPosition = _lightStart + sway * 0.006f;
            flame.localScale = new Vector3(_flameScale.x * (0.92f + 0.16f * n), _flameScale.y * (0.85f + 0.3f * n), _flameScale.z * (0.92f + 0.16f * n));
            flame.localRotation = Quaternion.Euler(sway.z * 14f, 0f, -sway.x * 14f);
        }
    }
}
