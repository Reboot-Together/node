using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using System.Reflection;

namespace AsterismApp;

public sealed class DirectionalCursorCanvas : Canvas
{
    private const string CursorModuleName = "Asterism.CursorResources.dll";

    private readonly Dictionary<GraphCursorDirection, InputCursor> _cursors = [];
    private Assembly? _cursorModule;
    private GraphCursorDirection? _direction;
    private bool _customCursorsUnavailable;

    public void SetCursor(GraphCursorDirection direction)
    {
        if (_customCursorsUnavailable || _direction == direction) return;

        try
        {
            if (!_cursors.TryGetValue(direction, out var cursor))
            {
                _cursorModule ??= Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, CursorModuleName));
                cursor = InputDesktopResourceCursor.CreateFromModule(
                    _cursorModule.ManifestModule.Name,
                    (uint)(201 + (int)direction));
                _cursors.Add(direction, cursor);
            }

            ProtectedCursor = cursor;
            _direction = direction;
        }
        catch
        {
            _customCursorsUnavailable = true;
            _direction = null;
            TryRestoreDefaultCursor();
        }
    }

    public void ResetCursor()
    {
        _direction = null;
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
