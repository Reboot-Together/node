using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using System.Reflection;

namespace AsterismApp;

public sealed class DirectionalCursorCanvas : Canvas
{
    private const string CursorModuleName = "Asterism.CursorResources.dll";

    private readonly Dictionary<(GraphCursorDirection Direction, bool Moving), InputCursor> _cursors = [];
    private Assembly? _cursorModule;
    private (GraphCursorDirection Direction, bool Moving)? _state;
    private bool _customCursorsUnavailable;

    public void SetCursor(GraphCursorDirection direction, bool moving = false)
    {
        var state = (direction, moving);
        if (_customCursorsUnavailable || _state == state) return;

        try
        {
            if (!_cursors.TryGetValue(state, out var cursor))
            {
                _cursorModule ??= Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, CursorModuleName));
                cursor = InputDesktopResourceCursor.CreateFromModule(
                    _cursorModule.ManifestModule.Name,
                    (uint)(201 + (int)direction + (moving ? 16 : 0)));
                _cursors.Add(state, cursor);
            }

            ProtectedCursor = cursor;
            _state = state;
        }
        catch
        {
            _customCursorsUnavailable = true;
            _state = null;
            TryRestoreDefaultCursor();
        }
    }

    public void ResetCursor()
    {
        _state = null;
        TryRestoreDefaultCursor();
    }

    private void TryRestoreDefaultCursor()
    {
        try
        {
            ProtectedCursor = null;
        }
        catch
        {
            // Cursor failures must never terminate the app. Windows restores the default cursor.
        }
    }
}
