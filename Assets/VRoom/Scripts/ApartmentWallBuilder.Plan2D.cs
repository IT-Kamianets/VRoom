using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// The 2D plan of the apartment seen from above. The camera looks straight down (orthographic), a metre grid and the
    /// size of every wall are drawn over it, corners can be dragged with the mouse (the walls follow, the contour stays
    /// closed), and a click on a wall selects it so that its width, height and corner angle can be typed in.
    /// </summary>
    public partial class ApartmentWallBuilder
    {
        const float HandleSize = 16f;      // corner handle, in screen pixels
        const float PickRadius = 14f;      // how close the mouse must be to a corner
        const float WallPick = 10f;        // how close the mouse must be to a wall line
        const float SnapStep = 0.05f;      // corners snap to 5 cm

        bool _plan2D;
        int _selected = -1;                // wall selected in the plan
        int _dragCorner = -1;              // corner being dragged
        string _selW = "", _selH = "", _selA = "";
        float _perspectiveFov = 60f;
        Vector2 _origin;                   // where the first corner is on the ground
        float _h0;                         // direction of the first wall, degrees
        Texture2D _px;
        GUIStyle _planLabel;

        // ---------------------------------------------------------------- the camera

        void TogglePlan()
        {
            var cam = Camera.main;
            if (cam == null) return;
            _plan2D = !_plan2D;
            _selected = -1;
            _drawInner = false;
            _drawStart = null;
            if (_plan2D)
            {
                if (_camMode == CamMode.Walk && _walker != null) { Destroy(_walker); _walker = null; _controller = null; }
                _camMode = CamMode.Overview;
                _perspectiveFov = 60f;
                cam.orthographic = true;
                FramePlan(Points());
                _error = false;
                _message = "Тягни кути мишкою або клацни стіну, щоб змінити її розміри.";
            }
            else
            {
                cam.orthographic = false;
                cam.fieldOfView = _perspectiveFov;
                FrameCamera(Points());
                _message = "";
            }
        }

        // Looks straight down at the contour, in the part of the screen the window does not cover.
        void FramePlan(List<Vector2> pts)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var min = pts[0];
            var max = pts[0];
            foreach (var p in pts)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            var centre = (min + max) * 0.5f;
            var extent = Vector2.Max(max - min, new Vector2(6f, 6f)) + new Vector2(2f, 2f);
            float s = Screen.height / 900f;
            float freeRight = Mathf.Clamp(1f - ReservedRightPx(s) / Screen.width, 0.35f, 0.95f);
            float size = Mathf.Max(extent.x / (freeRight * 2f * cam.aspect), extent.y * 0.5f);
            float visibleWidth = 2f * size * cam.aspect;
            cam.orthographic = true;
            cam.orthographicSize = size;
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.transform.position = new Vector3(centre.x + (0.5f - freeRight * 0.5f) * visibleWidth, 40f, centre.y);
        }

        static Vector2 WorldToGui(Camera cam, Vector2 p)
        {
            var sp = cam.WorldToScreenPoint(new Vector3(p.x, 0f, p.y));
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        static Vector2 GuiToWorld(Camera cam, Vector2 m)
        {
            var ray = cam.ScreenPointToRay(new Vector3(m.x, Screen.height - m.y, 0f));
            float t = Mathf.Abs(ray.direction.y) < 1e-5f ? 0f : -ray.origin.y / ray.direction.y;
            var hit = ray.origin + ray.direction * t;
            return new Vector2(hit.x, hit.z);
        }

        // ---------------------------------------------------------------- drawing and the mouse

        void EnsurePlanStyles()
        {
            if (_px == null)
            {
                _px = new Texture2D(1, 1);
                _px.SetPixel(0, 0, Color.white);
                _px.Apply();
            }

            if (_planLabel == null)
            {
                _planLabel = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _planLabel.normal.textColor = Color.white;
            }
        }

        void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, _px);
        }

        void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Box(new Rect(a.x, a.y - width * 0.5f, len, width), c);
            GUI.matrix = saved;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        void DrawGrid(Camera cam)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            var c = cam.transform.position;
            float step = halfW > 40f ? 5f : 1f;
            float major = step * 5f;
            var minor = new Color(1f, 1f, 1f, 0.07f);
            var strong = new Color(1f, 1f, 1f, 0.17f);
            for (float x = Mathf.Floor((c.x - halfW) / step) * step; x <= c.x + halfW; x += step)
            {
                var top = WorldToGui(cam, new Vector2(x, c.z + halfH));
                var bottom = WorldToGui(cam, new Vector2(x, c.z - halfH));
                Box(new Rect(top.x - 0.5f, Mathf.Min(top.y, bottom.y), 1f, Mathf.Abs(bottom.y - top.y)), Mathf.Abs(x - Mathf.Round(x / major) * major) < 1e-3f ? strong : minor);
            }

            for (float z = Mathf.Floor((c.z - halfH) / step) * step; z <= c.z + halfH; z += step)
            {
                var left = WorldToGui(cam, new Vector2(c.x - halfW, z));
                var right = WorldToGui(cam, new Vector2(c.x + halfW, z));
                Box(new Rect(Mathf.Min(left.x, right.x), left.y - 0.5f, Mathf.Abs(right.x - left.x), 1f), Mathf.Abs(z - Mathf.Round(z / major) * major) < 1e-3f ? strong : minor);
            }
        }

        void PlanGUI()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return;
            EnsurePlanStyles();
            var savedMatrix = GUI.matrix;
            var savedColor = GUI.color;
            GUI.matrix = Matrix4x4.identity;

            var e = Event.current;
            float s = Screen.height / 900f;
            var windowRect = _visible
                ? new Rect(_pos.x * s, _pos.y * s, WinW * s, WinH * s)
                : Rect.zero;
            bool overWindow = OverUi(e.mousePosition, s);
            float hs = HandleSize * s;                                  // everything is sized for the screen height, so it stays readable
            _planLabel.fontSize = Mathf.RoundToInt(17f * s);

            var pts = Points();
            int n = _walls.Count;
            int corners = n == 0 ? 0 : (_closed ? n : n + 1);

            if (e.type == EventType.Repaint)
            {
                DrawGrid(cam);
                DrawInnerRepaint(cam, s);

                for (int i = 0; i < n; i++)
                {
                    var a = WorldToGui(cam, pts[i]);
                    var b = WorldToGui(cam, pts[i + 1]);
                    bool sel = i == _selected;
                    Line(a, b, (sel ? 6f : 3f) * s, sel ? new Color(1f, 0.62f, 0.10f) : new Color(0.35f, 0.85f, 1f));

                    var wdir = (pts[i + 1] - pts[i]);
                    var outward = wdir.sqrMagnitude < 1e-6f ? Vector2.zero : new Vector2(wdir.y, wdir.x).normalized * 36f * s;
                    var mid = (a + b) * 0.5f + outward;
                    var lr = new Rect(mid.x - 50f * s, mid.y - 17f * s, 100f * s, 34f * s);
                    Box(lr, new Color(0.05f, 0.07f, 0.13f, 0.85f));
                    GUI.color = sel ? new Color(1f, 0.8f, 0.3f) : Color.white;
                    GUI.Label(lr, wdir.magnitude.ToString("0.00", CultureInfo.InvariantCulture) + " м", _planLabel);
                }

                for (int j = 0; j < corners; j++)
                {
                    var p = WorldToGui(cam, pts[j]);
                    bool active = j == _dragCorner;
                    Box(new Rect(p.x - hs * 0.5f, p.y - hs * 0.5f, hs, hs), new Color(1f, 0.62f, 0.10f));
                    Box(new Rect(p.x - hs * 0.5f + 3f * s, p.y - hs * 0.5f + 3f * s, hs - 6f * s, hs - 6f * s), active ? new Color(1f, 0.8f, 0.3f) : Color.white);
                }
            }

            bool consumed = HandleInnerEvents(cam, s, e, overWindow);
            if (_dragCorner >= 0)
            {
                if (e.type == EventType.MouseDrag)
                {
                    var world = GuiToWorld(cam, e.mousePosition);
                    MoveCorner(_dragCorner, new Vector2(Mathf.Round(world.x / SnapStep) * SnapStep, Mathf.Round(world.y / SnapStep) * SnapStep));
                    e.Use();
                }
                else if (e.rawType == EventType.MouseUp)
                {
                    _dragCorner = -1;
                    Save();
                    _message = _closed ? "Контур замкнено, зміни збережено." : "Зміни збережено.";
                    _error = false;
                    e.Use();
                }
            }
            else if (!consumed && !overWindow && e.type == EventType.MouseDown && e.button == 0)
            {
                int corner = -1;
                for (int j = 0; j < corners; j++)
                {
                    if (Vector2.Distance(WorldToGui(cam, pts[j]), e.mousePosition) <= PickRadius * s) { corner = j; break; }
                }

                if (corner >= 0)
                {
                    PushUndo();
                    _selInner = -1;
                    _dragCorner = corner;
                    e.Use();
                }
                else
                {
                    int wall = -1;
                    float best = WallPick * s;
                    for (int i = 0; i < n; i++)
                    {
                        float d = DistanceToSegment(e.mousePosition, WorldToGui(cam, pts[i]), WorldToGui(cam, pts[i + 1]));
                        if (d < best) { best = d; wall = i; }
                    }

                    _selected = wall;
                    if (wall >= 0) _selInner = -1;
                    FillSelected();
                    if (wall >= 0) e.Use();
                }
            }

            GUI.color = savedColor;
            GUI.matrix = savedMatrix;
        }

        // ---------------------------------------------------------------- editing from the plan

        // Moves a corner and rewrites every wall's width and corner angle so that all other corners stay where they are.
        void MoveCorner(int j, Vector2 position)
        {
            int n = _walls.Count;
            if (n == 0) return;
            var pts = Points();
            pts[j] = position;
            if (_closed)
            {
                if (j == 0) pts[n] = position;
                if (j == n) pts[0] = position;
            }

            for (int i = 0; i < n; i++)
            {
                if (Vector2.Distance(pts[i], pts[i + 1]) < 0.1f) return;   // a wall may not shrink to nothing
            }

            var dirs = new Vector2[n];
            for (int i = 0; i < n; i++) dirs[i] = (pts[i + 1] - pts[i]).normalized;

            for (int i = 0; i < n; i++)
            {
                var wall = _walls[i];
                wall.width = Vector2.Distance(pts[i], pts[i + 1]);
                if (i < n - 1) wall.angle = Mathf.Repeat(180f - Vector2.SignedAngle(dirs[i], dirs[i + 1]), 360f);
                else if (_closed) wall.angle = Mathf.Repeat(180f - Vector2.SignedAngle(dirs[n - 1], dirs[0]), 360f);
                _walls[i] = wall;
            }

            _origin = pts[0];
            _h0 = Mathf.Atan2(dirs[0].y, dirs[0].x) * Mathf.Rad2Deg;
            Rebuild();
            FillSelected();
        }

        void FillSelected()
        {
            if (_selected < 0 || _selected >= _walls.Count) { _selected = -1; return; }
            var wall = _walls[_selected];
            _selW = wall.width.ToString("0.00", CultureInfo.InvariantCulture);
            _selH = wall.height.ToString("0.00", CultureInfo.InvariantCulture);
            _selA = wall.angle.ToString("0", CultureInfo.InvariantCulture);
        }

        void ApplySelected()
        {
            if (_selected < 0 || _selected >= _walls.Count) return;
            if (!Parse(_selW, out var w) || !Parse(_selH, out var h) || !Parse(_selA, out var a)) { Fail("Введи числа."); return; }
            if (w < 0.1f || w > 50f || h < 1f || h > 10f || a < 30f || a > 330f)
            {
                Fail("Ширина 0.1–50, висота 1–10, кут 30–330.");
                return;
            }

            PushUndo();
            _walls[_selected] = new Wall { width = w, height = h, angle = a };
            _error = false;
            _message = "Стіну №" + (_selected + 1) + " змінено.";
            if (_closed)
            {
                _closed = false;
                float gap = Gap();
                if (gap < closeTolerance)
                {
                    _closed = true;
                }
                else
                {
                    _message += " Контур розімкнувся, до початку " + gap.ToString("0.00", CultureInfo.InvariantCulture) + " м: підправ сусідні стіни або натисни «Замкнути».";
                }
            }

            Rebuild();
        }

        void CloseFromPlan()
        {
            CloseNow();
            _visible = true;   // stay in the plan with the window open
        }

        void DeleteSelected()
        {
            if (_selected < 0 || _selected >= _walls.Count) return;
            PushUndo();
            _walls.RemoveAt(_selected);
            _selected = -1;
            _drawInner = false;
            _drawStart = null;
            _closed = false;
            _message = "Стіну видалено. Контур розімкнено.";
            _error = false;
            Rebuild();
        }

        // The window in the plan mode: the selected wall's numbers instead of the "add a wall" fields.
        void DrawPlanPanel()
        {
            string name = _cur >= 0 && _cur < _projects.Count ? _projects[_cur].name : "";
            GUILayout.Label(name + " · 2D план" + (_closed ? " · замкнено" : ""), _label);

            // the tool: draw a new interior wall by pressing, dragging and releasing on the plan
            var tool = _drawInner ? "✔ Малювати внутрішню стіну: увімкнено" : "Малювати внутрішню стіну";
            if (GUILayout.Button(tool, _button2, GUILayout.Height(40))) _pending = () => { _drawInner = !_drawInner; _drawStart = null; };

            if (_selInner >= 0 && _selInner < _inner.Count)
            {
                GUILayout.Label("Внутрішня стіна №" + (_selInner + 1) + " з " + _inner.Count, _label);
                GUILayout.Label("Довжина, м", _label);
                _inLen = GUILayout.TextField(_inLen, _field, GUILayout.Height(40));
                GUILayout.Label("Висота, м", _label);
                _inH = GUILayout.TextField(_inH, _field, GUILayout.Height(40));
                GUILayout.Label("Товщина, м", _label);
                _inT = GUILayout.TextField(_inT, _field, GUILayout.Height(40));
                if (GUILayout.Button("Застосувати", _button, GUILayout.Height(50))) _pending = ApplySelectedInner;
                if (GUILayout.Button("Видалити цю стіну", _button2, GUILayout.Height(38))) _pending = DeleteSelectedInner;
            }
            else if (_selected >= 0 && _selected < _walls.Count)
            {
                GUILayout.Label("Зовнішня стіна №" + (_selected + 1) + " з " + _walls.Count, _label);
                GUILayout.Label("Ширина, м", _label);
                _selW = GUILayout.TextField(_selW, _field, GUILayout.Height(40));
                GUILayout.Label("Висота, м", _label);
                _selH = GUILayout.TextField(_selH, _field, GUILayout.Height(40));
                GUILayout.Label("Кут у кінці стіни, °", _label);
                _selA = GUILayout.TextField(_selA, _field, GUILayout.Height(40));
                if (GUILayout.Button("Застосувати", _button, GUILayout.Height(50))) _pending = ApplySelected;
                if (GUILayout.Button("Видалити цю стіну", _button2, GUILayout.Height(38))) _pending = DeleteSelected;
            }
            else
            {
                GUILayout.Label("Клацни стіну, щоб змінити її. Тягни кути: помаранчеві (зовнішні) і блакитні (внутрішні).", _msg);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Замкнути контур", _button2, GUILayout.Height(38))) _pending = CloseFromPlan;
            if (GUILayout.Button("Внутрішні за планом", _button2, GUILayout.Height(38))) _pending = LoadPlanLayout;
            GUILayout.EndHorizontal();

            _msg.normal.textColor = _error ? new Color(1f, 0.45f, 0.45f) : new Color(0.6f, 0.95f, 0.6f);
            if (_message != "") GUILayout.Label(_message, _msg);

            if (GUILayout.Button("Закрити вікно", _button2, GUILayout.Height(42))) _visible = false;
            GUI.DragWindow(new Rect(0, 0, 10000, 50));
        }
    }
}
