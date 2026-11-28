using System.Collections.Generic;
using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// The public face of the wall builder for the editor screen (<see cref="EditorShell"/>): reading the state of the open
    /// project, and running every action that the old IMGUI window used to run.
    /// </summary>
    public partial class ApartmentWallBuilder
    {
        /// <summary>When true, the old IMGUI window and side bar are not drawn: <see cref="EditorShell"/> is the interface.</summary>
        public bool UseShell { get; set; }

        /// <summary>The shell tells whether the pointer is over one of its panels, so clicks do not reach the scene behind them.</summary>
        public System.Func<bool> PointerOverUi;

        /// <summary>Goes up on every change of the walls, so the shell knows when to refresh.</summary>
        public int Version { get; private set; }

        float ReservedRightPx(float s) => UseShell ? 0f : (WinW + 60f + ToolW) * s;

        bool OverUi(Vector2 mouse, float s)
        {
            if (UseShell) return PointerOverUi != null && PointerOverUi();
            var windowRect = _visible ? new Rect(_pos.x * s, _pos.y * s, WinW * s, WinH * s) : Rect.zero;
            return windowRect.Contains(mouse) || SideBarRect(s).Contains(mouse);
        }

        // ---------------------------------------------------------------- reading the state

        public bool Ready => _cur >= 0 && _cur < _projects.Count;
        public string ProjectName => Ready ? _projects[_cur].name : "";
        public int ProjectCount => _projects.Count;
        public int CurrentProject => _cur;
        public string ProjectNameAt(int i) => _projects[i].name;
        public int WallsIn(int i) => i == _cur ? _walls.Count : _projects[i].walls.Count;
        public bool ClosedIn(int i) => i == _cur ? _closed : _projects[i].closed;
        public int WallCount => _walls.Count;
        public int InnerCount => _inner.Count;
        public bool Closed => _closed;
        public bool Plan2D => _plan2D;
        public bool DrawingInner => _drawInner;
        public int ViewModeIndex => (int)_camMode;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public string Message => _message;
        public bool MessageIsError => _error;

        /// <summary>The area inside a closed contour, in square metres (0 while it is open).</summary>
        public float Area
        {
            get
            {
                if (!_closed || _walls.Count < 3) return 0f;
                var pts = Points();
                float sum = 0f;
                for (int i = 0; i < pts.Count - 1; i++) sum += pts[i].x * pts[i + 1].y - pts[i + 1].x * pts[i].y;
                return Mathf.Abs(sum) * 0.5f;
            }
        }

        // ---------------------------------------------------------------- projects

        public void OpenProjectAt(int i) { if (i >= 0 && i < _projects.Count) OpenProject(i); }
        public void CreateProjectNamed(string name) { _newName = name ?? ""; CreateProject(); }
        public void DeleteProjectAt(int i) { if (i >= 0 && i < _projects.Count) DeleteProject(i); }
        public void SaveNow() { Save(); _message = "Збережено."; _error = false; Version++; }

        // ---------------------------------------------------------------- views

        public void SetPlan(bool on) { if (Ready && on != _plan2D) TogglePlan(); }
        public void SetViewMode(int mode) { if (Ready) SetMode((CamMode)mode); }

        // ---------------------------------------------------------------- history and walls

        public void UiUndo() => UndoAction();
        public void UiRedo() => RedoAction();
        public void UiAddWall(string width, string height, string angle) { _width = width; _height = height; _angle = angle; AddWall(); }
        public void UiCloseContour() => CloseNow();
        public void UiLoadLayout() => LoadPlanLayout();
        public void UiDeleteLast() => Undo();
        public void UiEraseAll() => Reset();
        public void UiToggleDrawInner() { _drawInner = !_drawInner; _drawStart = null; }

        // ---------------------------------------------------------------- the selected wall (properties panel)

        /// <summary>0 = nothing selected, 1 = an exterior wall, 2 = an interior wall.</summary>
        public int SelectedKind => _selInner >= 0 && _selInner < _inner.Count ? 2 : (_selected >= 0 && _selected < _walls.Count ? 1 : 0);
        public int SelectedNumber => SelectedKind == 2 ? _selInner + 1 : _selected + 1;

        public void SelectedFields(out string a, out string b, out string c)
        {
            if (SelectedKind == 2) { a = _inLen; b = _inH; c = _inT; }
            else { a = _selW; b = _selH; c = _selA; }
        }

        public void ApplySelectedFields(string a, string b, string c)
        {
            if (SelectedKind == 2) { _inLen = a; _inH = b; _inT = c; ApplySelectedInner(); }
            else if (SelectedKind == 1) { _selW = a; _selH = b; _selA = c; ApplySelected(); }
        }

        public void DeleteSelectedAny()
        {
            if (SelectedKind == 2) DeleteSelectedInner();
            else if (SelectedKind == 1) DeleteSelected();
        }
    }
}
