using System.Collections.Generic;
using UnityEngine;

namespace VRoom
{
    /// <summary>
    /// Undo and redo for every change of the walls: adding, editing and deleting walls, dragging a corner, drawing
    /// interior walls, loading the layout. The arrows in the window and Ctrl+Z / Ctrl+Y walk through the history.
    /// </summary>
    public partial class ApartmentWallBuilder
    {
        const int HistoryLimit = 100;

        class Snapshot
        {
            public List<Wall> walls;
            public bool closed;
            public Vector2 origin;
            public float h0;
            public List<Inner> inner;
        }

        readonly List<Snapshot> _undo = new List<Snapshot>();
        readonly List<Snapshot> _redo = new List<Snapshot>();

        Snapshot Take()
        {
            return new Snapshot { walls = new List<Wall>(_walls), closed = _closed, origin = _origin, h0 = _h0, inner = new List<Inner>(_inner) };
        }

        void Restore(Snapshot s)
        {
            _walls = new List<Wall>(s.walls);
            _closed = s.closed;
            _origin = s.origin;
            _h0 = s.h0;
            _inner = new List<Inner>(s.inner);
            _selected = -1;
            _selInner = -1;
        }

        // Call this right before a change, so that the change can be undone.
        void PushUndo()
        {
            _undo.Add(Take());
            if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
            _redo.Clear();
        }

        void ClearHistory()
        {
            _undo.Clear();
            _redo.Clear();
        }

        void UndoAction()
        {
            if (_undo.Count == 0) { _message = "Більше нічого скасовувати."; _error = false; return; }
            _redo.Add(Take());
            var s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Restore(s);
            _error = false;
            _message = "Скасовано. Кроків назад: " + _undo.Count + ".";
            Rebuild();
        }

        void RedoAction()
        {
            if (_redo.Count == 0) { _message = "Нічого повертати."; _error = false; return; }
            _undo.Add(Take());
            var s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Restore(s);
            _error = false;
            _message = "Повернуто. Кроків вперед: " + _redo.Count + ".";
            Rebuild();
        }

        // The two arrows for the window; they are greyed out when there is nothing to undo or redo.
        void DrawHistoryButtons()
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = _undo.Count > 0;
            if (GUILayout.Button("↶ Назад", _button2, GUILayout.Height(40))) _pending = UndoAction;
            GUI.enabled = _redo.Count > 0;
            if (GUILayout.Button("Вперед ↷", _button2, GUILayout.Height(40))) _pending = RedoAction;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        // Ctrl+Z and Ctrl+Y (Ctrl+Shift+Z too), unless a text field is being typed in.
        void HistoryKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || !(e.control || e.command) || GUIUtility.keyboardControl != 0) return;
            if (e.keyCode == KeyCode.Z && !e.shift) { _pending = UndoAction; e.Use(); }
            else if (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift)) { _pending = RedoAction; e.Use(); }
        }
    }
}
