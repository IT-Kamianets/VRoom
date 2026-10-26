using System;
using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// A page of the book turning over from the right side to the left, hinged at the spine. The leaf is a thin sheet
    /// of paper bent along its length: it lifts and curls while turning and settles on the page profile of the
    /// book (the gutter dip included) on both ends. Put it on the book object (local space of the book model).
    /// </summary>
    public class PageFlip : MonoBehaviour
    {
        [SerializeField] private Material paper;
        [Tooltip("The renderer of the book model; its UVs tell which half of the paper texture is on which page.")]
        [SerializeField] private MeshFilter bookModel;
        [SerializeField] private float pageWidth = 0.364f;
        [SerializeField] private float pageHeight = 0.38f;
        [SerializeField, Min(0.2f)] private float duration = 1.25f;
        [SerializeField, Min(8)] private int segments = 28;

        // geometry of the page stack (see Blender/make_login_book.py)
        private const float StackTop = 0.036f;
        private const float Lift = 0.0022f;       // the leaf lies this far above the page, so it never cuts into the waves of the paper
        private const float Thickness = 0.0012f;

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Vector3[] _vertices;
        private Vector2[] _uvs;
        private float _time;
        private float _length;
        private bool _playing;
        private Action _done;
        private bool _uAtPositiveXIsHigh;
        private bool _vAtPositiveZIsHigh;

        public bool IsPlaying => _playing;

        private void Awake()
        {
            Calibrate();
            Build();
            _renderer.enabled = false;
        }

        /// <summary>Turns the page. Calls <paramref name="done"/> when it has settled on the left side.</summary>
        public void Play(Action done = null, float seconds = 0f)
        {
            if (_playing)
            {
                return;
            }

            _done = done;
            _length = seconds > 0f ? seconds : duration;
            _time = 0f;
            _playing = true;
            Evaluate(0f);
            _renderer.enabled = true;
        }

        /// <summary>Hides the leaf again (the next spread is then drawn by the page UI).</summary>
        public void Hide()
        {
            _renderer.enabled = false;
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _time += Time.deltaTime;
            var t = Mathf.Clamp01(_time / _length);
            Evaluate(t);
            if (t >= 1f)
            {
                _playing = false;
                _done?.Invoke();
            }
        }

        // The book mesh decides which side of the paper texture belongs to which page: find where its highest U and V are.
        private void Calibrate()
        {
            _uAtPositiveXIsHigh = true;
            _vAtPositiveZIsHigh = true;
            if (bookModel == null || bookModel.sharedMesh == null)
            {
                return;
            }

            var mesh = bookModel.sharedMesh;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var bestU = float.MinValue;
            var bestV = float.MinValue;
            var xAtBestU = 0f;
            var zAtBestV = 0f;
            for (var i = 0; i < vertices.Length; i++)
            {
                // only the paper on top of the pages (normal pointing up, UV inside 0..1), not the page edges
                var up = bookModel.transform.TransformDirection(normals[i]);
                var p = transform.InverseTransformPoint(bookModel.transform.TransformPoint(vertices[i]));
                if (transform.InverseTransformDirection(up).y < 0.9f || Mathf.Abs(p.y - StackTop) > 0.012f || uvs[i].x > 1.001f || uvs[i].y > 1.001f)
                {
                    continue;
                }

                if (uvs[i].x > bestU)
                {
                    bestU = uvs[i].x;
                    xAtBestU = p.x;
                }

                if (uvs[i].y > bestV)
                {
                    bestV = uvs[i].y;
                    zAtBestV = p.z;
                }
            }

            _uAtPositiveXIsHigh = xAtBestU >= 0f;
            _vAtPositiveZIsHigh = zAtBestV >= 0f;
        }

        private void Build()
        {
            var columns = segments + 1;
            _vertices = new Vector3[columns * 2 * 2];
            _uvs = new Vector2[_vertices.Length];
            var triangles = new int[segments * 2 * 6];

            // surface 0 faces up when the leaf lies on the right, surface 1 is its back
            for (var surface = 0; surface < 2; surface++)
            {
                for (var i = 0; i < segments; i++)
                {
                    var a = surface * columns * 2 + i * 2;
                    var b = a + 2;
                    var k = (surface * segments + i) * 6;
                    if (surface == 0)
                    {
                        triangles[k] = a; triangles[k + 1] = a + 1; triangles[k + 2] = b;
                        triangles[k + 3] = b; triangles[k + 4] = a + 1; triangles[k + 5] = b + 1;
                    }
                    else
                    {
                        triangles[k] = a; triangles[k + 1] = b; triangles[k + 2] = a + 1;
                        triangles[k + 3] = b; triangles[k + 4] = b + 1; triangles[k + 5] = a + 1;
                    }
                }
            }

            for (var i = 0; i < columns; i++)
            {
                var s = pageWidth * i / segments;
                for (var row = 0; row < 2; row++)
                {
                    var z = row == 0 ? -pageHeight / 2 : pageHeight / 2;
                    var v = Mathf.Clamp01(((_vAtPositiveZIsHigh ? z : -z) + pageHeight / 2) / pageHeight);
                    _uvs[i * 2 + row] = new Vector2(U(s), v);
                    _uvs[columns * 2 + i * 2 + row] = new Vector2(U(-s), v);
                }
            }

            _mesh = new Mesh { name = "Page Leaf" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.triangles = triangles;

            var go = new GameObject("Page Leaf");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = paper;
        }

        // U of the paper texture at book-local x (the texture covers both pages: 0..0.5 left, 0.5..1 right)
        private float U(float x)
        {
            var u = 0.5f + 0.5f * x / pageWidth;
            return _uAtPositiveXIsHigh ? u : 1f - u;
        }

        private static float Profile(float s, float width)
        {
            var y = StackTop - 0.011f * Mathf.Exp(-Mathf.Pow(s / 0.022f, 2f)) - 0.002f * Smooth(width - 0.035f, width, s);
            return y + Lift;
        }

        private static float Smooth(float a, float b, float x)
        {
            var t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private void Evaluate(float t)
        {
            var columns = segments + 1;
            var ds = pageWidth / segments;
            var eased = Mathf.SmoothStep(0f, 1f, t);
            var alpha = Mathf.PI * eased;
            var curl = Mathf.Sin(Mathf.PI * t) * 0.75f;
            var wIn = Mathf.SmoothStep(0f, 1f, t / 0.12f);
            var wOut = Mathf.SmoothStep(0f, 1f, (t - 0.88f) / 0.12f);

            var arc = new Vector2(0f, Profile(0f, pageWidth));
            for (var i = 0; i < columns; i++)
            {
                var s = ds * i;
                if (i > 0)
                {
                    var angle = alpha - curl * ((i - 0.5f) / segments);
                    arc += ds * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }

                var right = new Vector2(s, Profile(s, pageWidth));
                var left = new Vector2(-s, Profile(s, pageWidth));
                var position = Vector2.Lerp(Vector2.Lerp(right, arc, wIn), left, wOut);
                _arcCache[Mathf.Min(i, _arcCache.Length - 1)] = position;
            }

            for (var i = 0; i < columns; i++)
            {
                var tangent = (_arcCache[Mathf.Min(i + 1, columns - 1)] - _arcCache[Mathf.Max(i - 1, 0)]).normalized;
                var normal = new Vector2(-tangent.y, tangent.x) * (Thickness * 0.5f);   // points "up" while the leaf lies on the right
                var front = _arcCache[i] + normal;
                var back = _arcCache[i] - normal;
                for (var row = 0; row < 2; row++)
                {
                    var z = row == 0 ? -pageHeight / 2 : pageHeight / 2;
                    _vertices[i * 2 + row] = new Vector3(front.x, front.y, z);
                    _vertices[columns * 2 + i * 2 + row] = new Vector3(back.x, back.y, z);
                }
            }

            _mesh.vertices = _vertices;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private Vector2[] _arcCache = new Vector2[64];

        private void OnValidate()
        {
            segments = Mathf.Clamp(segments, 8, 60);
        }
    }
}
