using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// Interior walls: straight segments inside the exterior contour. They are drawn in the 2D plan (the "Малювати стіну"
    /// tool: press, drag, release), their end points can be dragged, a selected one can be resized or deleted, and the
    /// layout of the apartment plan can be loaded with one button. They get a mesh and a collider, like the exterior walls.
    /// </summary>
    public partial class ApartmentWallBuilder
    {
        [System.Serializable] struct Inner { public float x1, z1, x2, z2, height, thickness; }

        const float InnerHeight = 2.7f;
        const float InnerThickness = 0.15f;

        List<Inner> _inner = new List<Inner>();
        int _selInner = -1;
        int _dragInnerIndex = -1;
        int _dragInnerEnd;
        bool _drawInner;
        Vector2? _drawStart;
        Vector2 _drawEnd;
        string _inLen = "", _inH = "", _inT = "";
        GameObject _innerObject;

        bool Dragging => _dragCorner >= 0 || _dragInnerIndex >= 0;

        // ---------------------------------------------------------------- the layout from the apartment plan

        // Walls between the rooms of the 3A apartment (centre lines, metres, same coordinates as the exterior contour).
        // The gaps in them are the doorways.
        static readonly float[][] PlanLayout =
        {
            new[] { 0.77f, 4.45f, 4.85f, 4.45f },   // bedroom 1 / living room
            new[] { 5.75f, 4.45f, 6.16f, 4.45f },   //   (doorway 4.85 - 5.75 to the hall)
            new[] { 6.16f, 4.45f, 6.16f, 8.10f },   // between the two bedrooms
            new[] { 6.16f, 4.45f, 7.90f, 4.45f },   // bedroom 2 / hall
            new[] { 8.80f, 4.45f, 9.75f, 4.45f },   //   (doorway 7.90 - 8.80)
            new[] { 9.75f, 0.00f, 9.75f, 3.20f },   // hall / kitchen
            new[] { 9.75f, 4.10f, 9.75f, 8.10f },   //   (doorway 3.20 - 4.10), then bedroom 2 / kitchen and balcony
            new[] { 9.75f, 6.70f, 11.00f, 6.70f },  // kitchen / balcony
            new[] { 11.90f, 6.70f, 12.75f, 6.70f }, //   (balcony door 11.00 - 11.90)
            new[] { 0.77f, 4.79f, 0.77f, 6.00f },   // bedroom 1 / left balcony
            new[] { 0.77f, 6.90f, 0.77f, 8.10f },   //   (balcony door 6.00 - 6.90)
            new[] { 9.75f, 1.95f, 10.90f, 1.95f },  // kitchen / small bathroom
            new[] { 11.80f, 1.95f, 12.75f, 1.95f }, //   (door 10.90 - 11.80)
            new[] { 5.17f, 0.00f, 5.17f, 2.90f },   // bathroom west wall (the living room is open to the hall)
            new[] { 5.17f, 2.90f, 7.07f, 2.90f },   // bathroom north wall
            new[] { 7.07f, 0.00f, 7.07f, 1.00f },   // bathroom east wall
            new[] { 7.07f, 1.90f, 7.07f, 2.90f },   //   (bathroom door 1.00 - 1.90)
        };

        void LoadPlanLayout()
        {
            PushUndo();
            _inner = new List<Inner>();
            foreach (var w in PlanLayout)
            {
                _inner.Add(new Inner { x1 = w[0], z1 = w[1], x2 = w[2], z2 = w[3], height = InnerHeight, thickness = InnerThickness });
            }

            _selInner = -1;
            _error = false;
            _message = "Внутрішні стіни за планом додано (" + _inner.Count + "). Їх можна змінити в 2D плані.";
            Rebuild();
        }

        void ClearInner()
        {
            if (_inner.Count == 0) return;
            PushUndo();
            _inner.Clear();
            _selInner = -1;
            _message = "Внутрішні стіни прибрано.";
            _error = false;
            Rebuild();
        }

        // ---------------------------------------------------------------- the mesh

        void BuildInner()
        {
            if (_innerObject != null) Destroy(_innerObject);
            _innerObject = null;
            if (_inner.Count == 0) return;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            foreach (var w in _inner)
            {
                var a = new Vector2(w.x1, w.z1);
                var b = new Vector2(w.x2, w.z2);
                var dir = (b - a).normalized;
                if (dir.sqrMagnitude < 1e-6f) continue;
                float half = w.thickness * 0.5f;
                a -= dir * half;               // the ends overlap what they meet
                b += dir * half;
                var left = new Vector2(-dir.y, dir.x) * half;
                Vector2[] c = { a + left, b + left, b - left, a - left };
                float h = w.height;
                Vector3 Low(Vector2 p) => new Vector3(p.x, 0f, p.y);
                Vector3 High(Vector2 p) => new Vector3(p.x, h, p.y);
                Quad(High(c[0]), High(c[1]), High(c[2]), High(c[3]));
                for (int k = 0; k < 4; k++)
                {
                    var p = c[k];
                    var q = c[(k + 1) % 4];
                    Quad(Low(p), Low(q), High(q), High(p));
                }
            }

            var mesh = new Mesh { name = "InteriorWalls" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _innerObject = new GameObject("InteriorWalls", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            _innerObject.transform.SetParent(transform, false);
            _innerObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            _innerObject.GetComponent<MeshCollider>().sharedMesh = mesh;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = new Color(0.93f, 0.89f, 0.82f);
            _innerObject.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ---------------------------------------------------------------- the 2D plan: drawing, picking, dragging

        static Vector2 SnapPoint(Vector2 p) => new Vector2(Mathf.Round(p.x / SnapStep) * SnapStep, Mathf.Round(p.y / SnapStep) * SnapStep);

        // A line that is nearly horizontal or vertical becomes exactly so.
        static Vector2 AxisSnap(Vector2 start, Vector2 end)
        {
            if (Mathf.Abs(end.x - start.x) < 0.15f) end.x = start.x;
            if (Mathf.Abs(end.y - start.y) < 0.15f) end.y = start.y;
            return end;
        }

        void DrawInnerRepaint(Camera cam, float s)
        {
            for (int i = 0; i < _inner.Count; i++)
            {
                var w = _inner[i];
                var a = WorldToGui(cam, new Vector2(w.x1, w.z1));
                var b = WorldToGui(cam, new Vector2(w.x2, w.z2));
                bool sel = i == _selInner;
                Line(a, b, (sel ? 6f : 4f) * s, sel ? new Color(1f, 0.62f, 0.10f) : new Color(1f, 0.88f, 0.45f));

                var dir = new Vector2(w.x2 - w.x1, w.z2 - w.z1);
                var mid = (a + b) * 0.5f;
                if (dir.sqrMagnitude > 1e-6f) mid += new Vector2(dir.y, dir.x).normalized * 24f * s;
                var lr = new Rect(mid.x - 44f * s, mid.y - 14f * s, 88f * s, 28f * s);
                Box(lr, new Color(0.05f, 0.07f, 0.13f, 0.8f));
                GUI.color = sel ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.92f, 0.6f);
                GUI.Label(lr, dir.magnitude.ToString("0.00", CultureInfo.InvariantCulture), _planLabel);

                float es = 12f * s;
                foreach (var p in new[] { a, b })
                {
                    Box(new Rect(p.x - es * 0.5f, p.y - es * 0.5f, es, es), new Color(0.35f, 0.85f, 1f));
                }
            }

            if (_drawStart.HasValue)
            {
                Line(WorldToGui(cam, _drawStart.Value), WorldToGui(cam, _drawEnd), 4f * s, new Color(1f, 1f, 1f, 0.85f));
                var d = _drawEnd - _drawStart.Value;
                var mid = (WorldToGui(cam, _drawStart.Value) + WorldToGui(cam, _drawEnd)) * 0.5f;
                var lr = new Rect(mid.x - 44f * s, mid.y - 14f * s - 28f * s, 88f * s, 28f * s);
                Box(lr, new Color(0.05f, 0.07f, 0.13f, 0.8f));
                GUI.color = Color.white;
                GUI.Label(lr, d.magnitude.ToString("0.00", CultureInfo.InvariantCulture), _planLabel);
            }
        }

        // Returns true when the event was used by interior walls (drawing, dragging an end, selecting a wall).
        bool HandleInnerEvents(Camera cam, float s, Event e, bool overWindow)
        {
            if (_dragInnerIndex >= 0)
            {
                if (e.type == EventType.MouseDrag)
                {
                    MoveInnerEnd(_dragInnerIndex, _dragInnerEnd, SnapPoint(GuiToWorld(cam, e.mousePosition)));
                    e.Use();
                }
                else if (e.rawType == EventType.MouseUp)
                {
                    _dragInnerIndex = -1;
                    Save();
                    _message = "Зміни збережено.";
                    _error = false;
                    e.Use();
                }

                return true;
            }

            if (_drawStart.HasValue)
            {
                if (e.type == EventType.MouseDrag)
                {
                    _drawEnd = AxisSnap(_drawStart.Value, SnapPoint(GuiToWorld(cam, e.mousePosition)));
                    e.Use();
                }
                else if (e.rawType == EventType.MouseUp)
                {
                    var start = _drawStart.Value;
                    var end = _drawEnd;
                    _drawStart = null;
                    if (Vector2.Distance(start, end) >= 0.2f)
                    {
                        PushUndo();
                        _inner.Add(new Inner { x1 = start.x, z1 = start.y, x2 = end.x, z2 = end.y, height = InnerHeight, thickness = InnerThickness });
                        _selInner = _inner.Count - 1;
                        _selected = -1;
                        FillSelectedInner();
                        _message = "Стіну додано: " + Vector2.Distance(start, end).ToString("0.00", CultureInfo.InvariantCulture) + " м.";
                        _error = false;
                        Rebuild();
                    }

                    e.Use();
                }

                return true;
            }

            if (overWindow || e.type != EventType.MouseDown || e.button != 0) return false;

            if (_drawInner)
            {
                _drawStart = SnapPoint(GuiToWorld(cam, e.mousePosition));
                _drawEnd = _drawStart.Value;
                e.Use();
                return true;
            }

            for (int i = 0; i < _inner.Count; i++)
            {
                for (int end = 0; end < 2; end++)
                {
                    var p = end == 0 ? new Vector2(_inner[i].x1, _inner[i].z1) : new Vector2(_inner[i].x2, _inner[i].z2);
                    if (Vector2.Distance(WorldToGui(cam, p), e.mousePosition) <= PickRadius * s)
                    {
                        PushUndo();
                        _dragInnerIndex = i;
                        _dragInnerEnd = end;
                        _selInner = i;
                        _selected = -1;
                        FillSelectedInner();
                        e.Use();
                        return true;
                    }
                }
            }

            int pick = -1;
            float best = WallPick * s;
            for (int i = 0; i < _inner.Count; i++)
            {
                float d = DistanceToSegment(e.mousePosition, WorldToGui(cam, new Vector2(_inner[i].x1, _inner[i].z1)), WorldToGui(cam, new Vector2(_inner[i].x2, _inner[i].z2)));
                if (d < best) { best = d; pick = i; }
            }

            if (pick >= 0)
            {
                _selInner = pick;
                _selected = -1;
                FillSelectedInner();
                e.Use();
                return true;
            }

            return false;
        }

        void MoveInnerEnd(int index, int end, Vector2 p)
        {
            if (index < 0 || index >= _inner.Count) return;
            var w = _inner[index];
            var other = end == 0 ? new Vector2(w.x2, w.z2) : new Vector2(w.x1, w.z1);
            if (Vector2.Distance(p, other) < 0.2f) return;
            if (end == 0) { w.x1 = p.x; w.z1 = p.y; }
            else { w.x2 = p.x; w.z2 = p.y; }
            _inner[index] = w;
            Rebuild();
            FillSelectedInner();
        }

        // ---------------------------------------------------------------- the panel of the selected interior wall

        void FillSelectedInner()
        {
            if (_selInner < 0 || _selInner >= _inner.Count) { _selInner = -1; return; }
            var w = _inner[_selInner];
            _inLen = new Vector2(w.x2 - w.x1, w.z2 - w.z1).magnitude.ToString("0.00", CultureInfo.InvariantCulture);
            _inH = w.height.ToString("0.00", CultureInfo.InvariantCulture);
            _inT = w.thickness.ToString("0.00", CultureInfo.InvariantCulture);
        }

        void ApplySelectedInner()
        {
            if (_selInner < 0 || _selInner >= _inner.Count) return;
            if (!Parse(_inLen, out var len) || !Parse(_inH, out var h) || !Parse(_inT, out var t)) { Fail("Введи числа."); return; }
            if (len < 0.2f || len > 50f || h < 1f || h > 10f || t < 0.05f || t > 1f)
            {
                Fail("Довжина 0.2–50, висота 1–10, товщина 0.05–1.");
                return;
            }

            PushUndo();
            var w = _inner[_selInner];
            var a = new Vector2(w.x1, w.z1);
            var dir = (new Vector2(w.x2, w.z2) - a).normalized;
            var b = a + dir * len;
            _inner[_selInner] = new Inner { x1 = a.x, z1 = a.y, x2 = b.x, z2 = b.y, height = h, thickness = t };
            _error = false;
            _message = "Стіну змінено.";
            Rebuild();
        }

        void DeleteSelectedInner()
        {
            if (_selInner < 0 || _selInner >= _inner.Count) return;
            PushUndo();
            _inner.RemoveAt(_selInner);
            _selInner = -1;
            _message = "Стіну видалено.";
            _error = false;
            Rebuild();
        }
    }
}
