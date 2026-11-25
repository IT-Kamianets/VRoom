using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// Window with 3 inputs (wall width, wall height, right corner angle). Every press of "Побудувати стіну" adds the
    /// next exterior wall of the apartment: the first one starts at the origin and runs along +x, each next wall starts
    /// where the previous one ended and turns by the previous wall's right corner angle (interior angle, 90 = right angle),
    /// so the contour goes counter-clockwise seen from above. When the end of a wall meets the start of the first wall
    /// the contour is closed and the corner between the last and the first wall is joined.
    /// </summary>
    public class ApartmentWallBuilder : MonoBehaviour
    {
        [SerializeField] float thickness = 0.3f;
        [SerializeField] Color wallColor = new Color(0.85f, 0.82f, 0.76f);
        [SerializeField] float closeTolerance = 0.15f;

        struct Wall { public float width, height, angle; }

        readonly List<Wall> _walls = new List<Wall>();
        bool _closed;

        string _width = "6", _height = "2.7", _angle = "90";
        string _message = "";
        bool _error;
        bool _visible = true;
        GameObject _wall;

        // Window size is fixed here (in "virtual" pixels, scaled to the screen); only the dragged position is remembered.
        const float WinW = 560f, WinH = 850f;
        Vector2 _pos = new Vector2(-1f, 20f); // x < 0: not placed yet, docks to the right edge
        GUIStyle _win, _label, _field, _button, _button2, _msg;
        Texture2D _bg, _fieldBg, _btnBg, _btn2Bg;

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void InitStyles()
        {
            if (_win != null && _bg != null) return;
            _bg = Tex(new Color(0.10f, 0.12f, 0.20f, 0.96f));
            _fieldBg = Tex(Color.white);
            _btnBg = Tex(new Color(0.95f, 0.62f, 0.10f));
            _btn2Bg = Tex(new Color(0.75f, 0.78f, 0.88f));
            _win = new GUIStyle(GUI.skin.window) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, padding = new RectOffset(30, 30, 70, 30) };
            _win.normal.background = _win.onNormal.background = _bg;
            _win.normal.textColor = _win.onNormal.textColor = new Color(1f, 0.8f, 0.3f);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 24, margin = new RectOffset(0, 0, 14, 4) };
            _label.normal.textColor = Color.white;
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 28, alignment = TextAnchor.MiddleLeft };
            _field.normal.background = _field.focused.background = _field.hover.background = _fieldBg;
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Color.black;
            _button = new GUIStyle(GUI.skin.button) { fontSize = 28, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 24, 0) };
            _button.normal.background = _button.hover.background = _button.active.background = _btnBg;
            _button.normal.textColor = _button.hover.textColor = _button.active.textColor = Color.black;
            _button2 = new GUIStyle(_button) { fontSize = 22, margin = new RectOffset(0, 0, 10, 0) };
            _button2.normal.background = _button2.hover.background = _button2.active.background = _btn2Bg;
            _msg = new GUIStyle(_label) { wordWrap = true, fontSize = 22 };
        }

        void OnGUI()
        {
            InitStyles();
            float s = Screen.height / 900f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            if (_pos.x < 0f) _pos.x = Screen.width / s - WinW - 30f;
            if (!_visible)
            {
                // The window is hidden (closed by the user or after the contour was closed): a small button brings it back.
                if (GUI.Button(new Rect(Screen.width / s - 290f, 30f, 260f, 60f), "Показати вікно", _button2)) _visible = true;
                return;
            }
            var r = GUI.Window(7421, new Rect(_pos.x, _pos.y, WinW, WinH), DrawWindow, "Зовнішня стіна квартири", _win);
            _pos = r.position;
        }

        void DrawWindow(int id)
        {
            GUILayout.Label(_closed ? "Контур замкнено" : "Стіна №" + (_walls.Count + 1), _label);

            GUI.enabled = !_closed;
            GUILayout.Label("Ширина стіни, м", _label);
            _width = GUILayout.TextField(_width, _field, GUILayout.Height(56));
            GUILayout.Label("Висота стіни, м", _label);
            _height = GUILayout.TextField(_height, _field, GUILayout.Height(56));
            GUILayout.Label("Правий кут, °", _label);
            _angle = GUILayout.TextField(_angle, _field, GUILayout.Height(56));
            if (GUILayout.Button("Побудувати стіну", _button, GUILayout.Height(70))) AddWall();
            if (GUILayout.Button("Замкнути контур", _button2, GUILayout.Height(50))) CloseNow();
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Скасувати останню", _button2, GUILayout.Height(50))) Undo();
            if (GUILayout.Button("Почати спочатку", _button2, GUILayout.Height(50))) Reset();
            GUILayout.EndHorizontal();

            _msg.normal.textColor = _error ? new Color(1f, 0.45f, 0.45f) : new Color(0.6f, 0.95f, 0.6f);
            if (_message != "") GUILayout.Label(_message, _msg);
            if (GUILayout.Button("Закрити вікно", _button2, GUILayout.Height(50))) _visible = false;
            GUI.DragWindow(new Rect(0, 0, 10000, 60));
        }

        static bool Parse(string s, out float v) =>
            float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        void Fail(string text) { _message = text; _error = true; }

        void AddWall()
        {
            if (!Parse(_width, out var w) || !Parse(_height, out var h) || !Parse(_angle, out var a)) { Fail("Введи числа."); return; }
            if (w < 0.5f || w > 50f || h < 1f || h > 10f || a < 30f || a > 330f)
            {
                Fail("Ширина 0.5–50, висота 1–10, кут 30–330 (270 = внутрішній кут назовні).");
                return;
            }
            _walls.Add(new Wall { width = w, height = h, angle = a });

            var pts = Points();
            if (_walls.Count >= 3 && Vector2.Distance(pts[pts.Count - 1], pts[0]) < closeTolerance)
            {
                _closed = true;
                _message = "Контур замкнено: " + _walls.Count + " стін.";
                _visible = false;
            }
            else
            {
                _message = "Стіну №" + _walls.Count + " побудовано. До початку контуру: " + Gap().ToString("0.00", CultureInfo.InvariantCulture) + " м. Вводь наступну.";
            }
            _error = false;
            Rebuild();
        }

        float Gap()
        {
            var pts = Points();
            return Vector2.Distance(pts[pts.Count - 1], pts[0]);
        }

        // Closes the contour by pulling the last corner to the start (for measurements that are a little off).
        void CloseNow()
        {
            if (_closed || _walls.Count < 3) { Fail("Для замикання потрібно щонайменше 3 стіни."); return; }
            float gap = Gap();
            if (gap > 1.5f) { Fail("До початку ще " + gap.ToString("0.00", CultureInfo.InvariantCulture) + " м: контур не замикається. Перевір довжини й кути (внутрішній кут назовні = 270)."); return; }
            _closed = true;
            _error = false;
            _message = "Контур замкнено, останній кут підтягнуто на " + gap.ToString("0.00", CultureInfo.InvariantCulture) + " м.";
            _visible = false;
            Rebuild();
        }

        void Undo()
        {
            if (_walls.Count == 0) return;
            _walls.RemoveAt(_walls.Count - 1);
            _closed = false;
            _message = "";
            Rebuild();
        }

        void Reset()
        {
            _walls.Clear();
            _closed = false;
            _message = "";
            Rebuild();
        }

        /// <summary>Corner centres in plan (x, z): start of wall 0, then the end of every wall.</summary>
        List<Vector2> Points()
        {
            var pts = new List<Vector2> { Vector2.zero };
            float heading = 0f;
            foreach (var wall in _walls)
            {
                var dir = new Vector2(Mathf.Cos(heading * Mathf.Deg2Rad), Mathf.Sin(heading * Mathf.Deg2Rad));
                pts.Add(pts[pts.Count - 1] + dir * wall.width);
                heading += 180f - wall.angle;
            }
            if (_closed && pts.Count > 1) pts[pts.Count - 1] = pts[0]; // snap the last corner to the start
            return pts;
        }

        void Rebuild()
        {
            if (_wall != null) Destroy(_wall);
            if (_walls.Count == 0) return;

            _wall = new GameObject("ExteriorWalls", typeof(MeshFilter), typeof(MeshRenderer));
            _wall.transform.SetParent(transform, false);
            var pts = Points();
            _wall.GetComponent<MeshFilter>().sharedMesh = BuildMesh(pts, _walls, _closed, thickness);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = wallColor;
            _wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
            FrameCamera(pts);
        }

        // Frames the contour in the left part of the screen so that the window on the right never covers it.
        void FrameCamera(List<Vector2> pts)
        {
            var cam = Camera.main;
            if (cam == null) return;
            float maxH = 0f;
            foreach (var wall in _walls) maxH = Mathf.Max(maxH, wall.height);

            var corners = new List<Vector3>();
            var min = pts[0]; var max = pts[0];
            foreach (var p in pts)
            {
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                corners.Add(new Vector3(p.x, 0f, p.y));
                corners.Add(new Vector3(p.x, maxH, p.y));
            }
            var c = new Vector3((min.x + max.x) * 0.5f, maxH * 0.5f, (min.y + max.y) * 0.5f);

            float s = Screen.height / 900f;
            float freeRight = 1f - (WinW + 60f) * s / Screen.width;   // the window takes the right part of the screen
            freeRight = Mathf.Clamp(freeRight, 0.35f, 0.95f);
            const float margin = 0.04f;
            float targetX = freeRight * 0.5f;
            float availW = freeRight - 2f * margin, availH = 1f - 2f * margin;

            cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            float d = 6f;
            for (int step = 0; step < 60; step++)
            {
                cam.transform.position = c - cam.transform.forward * d;
                GetViewportBounds(cam, corners, out var vmin, out var vmax);
                if (vmax.x - vmin.x <= availW && vmax.y - vmin.y <= availH) break;
                d *= 1.1f;
            }
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * cam.aspect;
            for (int it = 0; it < 4; it++)
            {
                GetViewportBounds(cam, corners, out var vmin, out var vmax);
                var center = (vmin + vmax) * 0.5f;
                cam.transform.position += cam.transform.right * ((center.x - targetX) * 2f * d * tanH)
                                        + cam.transform.up * ((center.y - 0.5f) * 2f * d * tanV);
            }
        }

        static void GetViewportBounds(Camera cam, List<Vector3> points, out Vector2 vmin, out Vector2 vmax)
        {
            vmin = new Vector2(float.MaxValue, float.MaxValue);
            vmax = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in points)
            {
                var v = cam.WorldToViewportPoint(p);
                vmin = Vector2.Min(vmin, v);
                vmax = Vector2.Max(vmax, v);
            }
        }

        static Mesh BuildMesh(List<Vector2> pts, List<Wall> walls, bool closed, float t)
        {
            int n = walls.Count;
            float half = t * 0.5f;

            var dirs = new Vector2[n];
            for (int i = 0; i < n; i++) dirs[i] = (pts[i + 1] - pts[i]).normalized;
            Vector2 Normal(Vector2 d) => new Vector2(-d.y, d.x); // left = inside of the counter-clockwise contour

            // Offset points at corner j; corners 0 and n coincide when the contour is closed.
            var left = new Vector2[n + 1];
            var right = new Vector2[n + 1];
            for (int j = 0; j <= n; j++)
            {
                bool hasPrev = j > 0 || closed, hasNext = j < n || closed;
                Vector2 da = dirs[j > 0 ? j - 1 : n - 1], db = dirs[j < n ? j : 0];
                Vector2 p = pts[j];
                if (hasPrev && hasNext)
                {
                    Vector2 na = Normal(da), nb = Normal(db);
                    var m = (na + nb).normalized;
                    float k = half / Mathf.Max(0.2f, Vector2.Dot(m, na));
                    left[j] = p + m * k;
                    right[j] = p - m * k;
                }
                else
                {
                    var nrm = Normal(hasNext ? db : da);
                    left[j] = p + nrm * half;
                    right[j] = p - nrm * half;
                }
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

            for (int i = 0; i < n; i++)
            {
                float h = walls[i].height;
                Vector2 ls = left[i], le = left[i + 1], rs = right[i], re = right[i + 1];
                Quad(V(ls, h), V(le, h), V(re, h), V(rs, h));
                Quad(V(ls, 0), V(le, 0), V(le, h), V(ls, h));
                Quad(V(re, 0), V(rs, 0), V(rs, h), V(re, h));
                if (!closed && i == 0) Quad(V(rs, 0), V(ls, 0), V(ls, h), V(rs, h));
                if (!closed && i == n - 1) Quad(V(le, 0), V(re, 0), V(re, h), V(le, h));
            }

            var mesh = new Mesh { name = "ExteriorWalls" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
