using System.Collections.Generic;
using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// The 3D ways to look at the apartment: the automatic overview, "god mode" (fly around the apartment: drag to
    /// turn, wheel to zoom) and walking inside it (WASD, drag to look around, walls stop you). Input comes through the
    /// GUI events, like the rest of this window.
    /// </summary>
    public partial class ApartmentWallBuilder
    {
        enum CamMode { Overview, God, Walk }

        const float EyeHeight = 1.65f;
        const float WalkSpeed = 2.2f;
        const float RunSpeed = 4.5f;

        CamMode _camMode = CamMode.Overview;
        float _godYaw = 25f, _godPitch = 50f, _godDistance = 20f;
        Vector3 _godTarget;
        float _walkYaw, _walkPitch;
        GameObject _walker;
        CharacterController _controller;
        GameObject _floor;
        bool _keyForward, _keyBack, _keyLeft, _keyRight, _keyRun, _looking;

        // ---------------------------------------------------------------- switching

        void SetMode(CamMode mode)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (_plan2D) TogglePlan();   // back to the perspective camera first

            if (_camMode == CamMode.Walk && _walker != null)
            {
                Destroy(_walker);
                _walker = null;
                _controller = null;
            }

            cam.fieldOfView = 60f;
            _camMode = mode;
            _looking = false;
            _keyForward = _keyBack = _keyLeft = _keyRight = _keyRun = false;
            _error = false;

            switch (mode)
            {
                case CamMode.Overview:
                    FrameCamera(Points());
                    _message = "";
                    break;
                case CamMode.God:
                    FitGod(Points());
                    _message = "Режим бога: тягни мишкою, щоб обертати, коліщатко — наближення.";
                    break;
                case CamMode.Walk:
                    if (!StartWalking()) _camMode = CamMode.Overview;
                    break;
            }
        }

        // ---------------------------------------------------------------- god mode

        void FitGod(List<Vector2> pts)
        {
            var min = pts[0];
            var max = pts[0];
            foreach (var p in pts)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            var c = (min + max) * 0.5f;
            _godTarget = new Vector3(c.x, 0.5f, c.y);
            _godDistance = Mathf.Max(Mathf.Max(max.x - min.x, max.y - min.y), 6f) * 1.4f;
            ApplyGod();
        }

        void ApplyGod()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.orthographic = false;
            cam.transform.position = _godTarget + Quaternion.Euler(_godPitch, _godYaw, 0f) * (Vector3.back * _godDistance);
            cam.transform.LookAt(_godTarget);
        }

        // ---------------------------------------------------------------- walking

        // A point inside the contour to start from: the grid point nearest to the centre that is inside every wall.
        bool StartWalking()
        {
            if (!_closed || _walls.Count < 3)
            {
                Fail("Щоб ходити всередині, спочатку замкни контур.");
                return false;
            }

            var pts = Points();
            pts.RemoveAt(pts.Count - 1);
            var min = pts[0];
            var max = pts[0];
            foreach (var p in pts)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            var centre = (min + max) * 0.5f;
            Vector2? best = null;
            float bestDistance = float.MaxValue;
            for (float x = min.x; x <= max.x; x += 0.25f)
            {
                for (float z = min.y; z <= max.y; z += 0.25f)
                {
                    var q = new Vector2(x, z);
                    if (!Inside(q, pts) || DistanceToContour(q, pts) < 0.6f) continue;
                    float d = (q - centre).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = q; }
                }
            }

            if (best == null)
            {
                Fail("Не знайшов місця всередині квартири.");
                return false;
            }

            _walker = new GameObject("Walker", typeof(CharacterController));
            _walker.transform.SetParent(transform, false);
            _walker.transform.position = new Vector3(best.Value.x, 0f, best.Value.y);
            _controller = _walker.GetComponent<CharacterController>();
            _controller.height = 1.8f;
            _controller.radius = 0.25f;
            _controller.center = new Vector3(0f, 0.9f, 0f);

            _walkYaw = 0f;
            _walkPitch = 0f;
            var cam = Camera.main;
            cam.orthographic = false;
            cam.fieldOfView = 70f;
            ApplyWalk();
            _message = "Ходьба: W A S D, Shift — біг. Затисни ліву кнопку миші й рухай, щоб дивитися навколо.";
            return true;
        }

        void ApplyWalk()
        {
            var cam = Camera.main;
            if (cam == null || _walker == null) return;
            cam.transform.position = _walker.transform.position + Vector3.up * EyeHeight;
            cam.transform.rotation = Quaternion.Euler(_walkPitch, _walkYaw, 0f);
        }

        static bool Inside(Vector2 p, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        static float DistanceToContour(Vector2 p, List<Vector2> poly)
        {
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                best = Mathf.Min(best, DistanceToSegment(p, poly[i], poly[(i + 1) % poly.Count]));
            }

            return best;
        }

        // ---------------------------------------------------------------- input (GUI events) and per-frame update

        void ModesGUI()
        {
            if (_plan2D || _camMode == CamMode.Overview) return;
            var e = Event.current;

            if (e.type == EventType.KeyDown || e.type == EventType.KeyUp)
            {
                bool down = e.type == EventType.KeyDown;
                bool typing = GUIUtility.keyboardControl != 0;
                if (down && typing) return;
                switch (e.keyCode)
                {
                    case KeyCode.W: case KeyCode.UpArrow: _keyForward = down; break;
                    case KeyCode.S: case KeyCode.DownArrow: _keyBack = down; break;
                    case KeyCode.A: case KeyCode.LeftArrow: _keyLeft = down; break;
                    case KeyCode.D: case KeyCode.RightArrow: _keyRight = down; break;
                    case KeyCode.LeftShift: case KeyCode.RightShift: _keyRun = down; break;
                }

                return;
            }

            float s = Screen.height / 900f;
            var windowRect = _visible
                ? new Rect(_pos.x * s, _pos.y * s, WinW * s, WinH * s)
                : Rect.zero;

            if (e.type == EventType.MouseDown && !(windowRect.Contains(e.mousePosition) || SideBarRect(s).Contains(e.mousePosition)))
            {
                _looking = true;
                GUI.FocusControl(null);   // clicking the scene leaves the text fields, so W A S D move again
            }
            else if (e.rawType == EventType.MouseUp)
            {
                _looking = false;
            }
            else if (e.type == EventType.MouseDrag && _looking)
            {
                float k = 0.12f * (1440f / Screen.height);
                if (_camMode == CamMode.God)
                {
                    _godYaw += e.delta.x * k;
                    _godPitch = Mathf.Clamp(_godPitch + e.delta.y * k, 5f, 89f);
                    ApplyGod();
                }
                else
                {
                    _walkYaw += e.delta.x * k;
                    _walkPitch = Mathf.Clamp(_walkPitch + e.delta.y * k, -80f, 80f);
                    ApplyWalk();
                }

                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && _camMode == CamMode.God && !(windowRect.Contains(e.mousePosition) || SideBarRect(s).Contains(e.mousePosition)))
            {
                _godDistance = Mathf.Clamp(_godDistance * (1f + e.delta.y * 0.06f), 2f, 200f);
                ApplyGod();
                e.Use();
            }
        }

        void UpdateModes()
        {
            if (_camMode != CamMode.Walk || _walker == null || _controller == null) return;
            var forward = Quaternion.Euler(0f, _walkYaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, _walkYaw, 0f) * Vector3.right;
            var move = Vector3.zero;
            if (_keyForward) move += forward;
            if (_keyBack) move -= forward;
            if (_keyRight) move += right;
            if (_keyLeft) move -= right;
            if (move.sqrMagnitude > 0f)
            {
                move = move.normalized * (_keyRun ? RunSpeed : WalkSpeed);
                _controller.Move(move * Time.deltaTime);
                var p = _walker.transform.position;
                _walker.transform.position = new Vector3(p.x, 0f, p.z);   // the floor is flat
            }

            ApplyWalk();
        }

        // ---------------------------------------------------------------- floor and wall collider

        // A light floor inside a closed contour (ear clipping of the corner polygon) and a collider on the walls.
        void UpdateFloorAndCollider(List<Vector2> pts, Mesh wallMesh)
        {
            if (_floor != null) Destroy(_floor);
            _floor = null;

            if (_wall != null && wallMesh != null)
            {
                var collider = _wall.AddComponent<MeshCollider>();
                collider.sharedMesh = wallMesh;
            }

            int n = _walls.Count;
            if (!_closed || n < 3) return;

            var poly = new List<Vector2>(pts);
            poly.RemoveAt(poly.Count - 1);
            var triangles = Triangulate(poly);
            if (triangles.Count < 3) return;

            var vertices = new List<Vector3>();
            foreach (var p in poly) vertices.Add(new Vector3(p.x, 0.01f, p.y));
            // the polygon runs counter-clockwise seen from above; Unity wants the front faces clockwise, so the order is reversed
            var order = new List<int>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                order.Add(triangles[i]);
                order.Add(triangles[i + 2]);
                order.Add(triangles[i + 1]);
            }

            var mesh = new Mesh { name = "Floor" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(order, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _floor = new GameObject("Floor", typeof(MeshFilter), typeof(MeshRenderer));
            _floor.transform.SetParent(transform, false);
            _floor.GetComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = new Color(0.90f, 0.76f, 0.57f);
            material.SetFloat("_Smoothness", 0.3f);
            _floor.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ---------------------------------------------------------------- the buttons at the side

        const float ToolW = 160f;      // width of the side bar, in the window's "virtual" pixels
        const float SideTop = 150f;
        const int SideItems = 9;

        Rect SideBarRect(float s) => new Rect((Screen.width / s - ToolW - 16f) * s, SideTop * s, ToolW * s, SideItems * 54f * s);

        static bool Tool(GUIStyle style, string text, bool active) =>
            GUILayout.Button((active ? "● " : "") + text, style, GUILayout.Height(48));

        // Everything that is not about typing wall numbers lives here, as separate buttons: the window, the projects
        // list, the 2D plan, the ways to look at the apartment, and the undo and redo arrows.
        void DrawSideBar(float s)
        {
            GUILayout.BeginArea(new Rect(Screen.width / s - ToolW - 16f, SideTop, ToolW, SideItems * 54f));

            if (Tool(_button2, "Вікно", _visible)) _visible = !_visible;

            if (Tool(_button2, "Проєкти", _visible && _page == Page.Projects))
            {
                _pending = () => { if (_plan2D) TogglePlan(); ShowProjects(); _visible = true; };
            }

            if (Tool(_button2, "Стіни", _visible && _page == Page.Editor && !_plan2D))
            {
                _pending = () => OpenWalls(false);
            }

            if (Tool(_button2, "2D план", _plan2D))
            {
                _pending = () => OpenWalls(!_plan2D);
            }

            if (Tool(_button2, "Огляд", !_plan2D && _camMode == CamMode.Overview)) _pending = () => SetMode(CamMode.Overview);
            if (Tool(_button2, "Режим бога", !_plan2D && _camMode == CamMode.God)) _pending = () => SetMode(CamMode.God);
            if (Tool(_button2, "Ходьба", !_plan2D && _camMode == CamMode.Walk))
            {
                _pending = () => { SetMode(CamMode.Walk); if (_camMode == CamMode.Walk) _visible = false; };
            }

            GUI.enabled = _undo.Count > 0;
            if (Tool(_button2, "↶ Назад", false)) _pending = UndoAction;
            GUI.enabled = _redo.Count > 0;
            if (Tool(_button2, "Вперед ↷", false)) _pending = RedoAction;
            GUI.enabled = true;

            GUILayout.EndArea();
        }

        // "Стіни" opens the wall window, "2D план" switches the plan on or off; both need an open project.
        void OpenWalls(bool plan)
        {
            if (_cur < 0 || _cur >= _projects.Count)
            {
                _page = Page.Projects;
                _visible = true;
                Fail("Спочатку відкрий або створи проєкт.");
                return;
            }

            _page = Page.Editor;
            _visible = true;
            if (plan != _plan2D) TogglePlan();
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        // Ear clipping for a counter-clockwise polygon.
        static List<int> Triangulate(List<Vector2> poly)
        {
            var indices = new List<int>();
            for (int i = 0; i < poly.Count; i++) indices.Add(i);
            var triangles = new List<int>();
            int guard = 0;
            while (indices.Count > 3 && guard++ < 2000)
            {
                bool clipped = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int i0 = indices[(i + indices.Count - 1) % indices.Count], i1 = indices[i], i2 = indices[(i + 1) % indices.Count];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    if (Cross(b - a, c - b) <= 1e-6f) continue;   // reflex or flat corner
                    bool blocked = false;
                    foreach (int j in indices)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTriangle(poly[j], a, b, c)) { blocked = true; break; }
                    }

                    if (blocked) continue;
                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(i2);
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped) break;
            }

            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }

            return triangles;
        }
    }
}
