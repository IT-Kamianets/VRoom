using UnityEngine;
using UnityEngine.UI;

namespace VRoom
{
    /// <summary>Fades the full-screen image it sits on to nothing, then removes it. It runs on its own object so it
    /// keeps going when the object that started it is switched off.</summary>
    public class FadeOut : MonoBehaviour
    {
        public float Seconds = 0.8f;

        private Image _image;
        private float _time;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void Update()
        {
            _time += Time.deltaTime;
            var c = _image.color;
            c.a = 1f - Mathf.SmoothStep(0f, 1f, _time / Seconds);
            _image.color = c;
            if (_time >= Seconds)
            {
                Destroy(gameObject);
            }
        }
    }
}
