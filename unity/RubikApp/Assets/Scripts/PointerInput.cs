using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Input helpers shared by the camera and the controller. <see cref="Pointer.current"/> is the mouse on
/// desktop and the primary finger on a touchscreen, so one code path serves both.
/// </summary>
public static class PointerInput
{
    public static Pointer Current => Pointer.current;

    /// <summary>Number of fingers currently on the screen (0 without a touchscreen).</summary>
    public static int ActiveTouches
    {
        get
        {
            var screen = Touchscreen.current;
            if (screen == null) return 0;
            int count = 0;
            foreach (var touch in screen.touches)
                if (touch.isInProgress) count++;
            return count;
        }
    }

    /// <summary>Positions of the first two fingers, for pinch zoom. False if fewer than two are down.</summary>
    public static bool TryGetTwoTouches(out Vector2 a, out Vector2 b)
    {
        a = b = default;
        var screen = Touchscreen.current;
        if (screen == null) return false;
        int found = 0;
        foreach (var touch in screen.touches)
        {
            if (!touch.isInProgress) continue;
            if (found == 0) a = touch.position.ReadValue();
            else { b = touch.position.ReadValue(); return true; }
            found++;
        }
        return false;
    }

    /// <summary>True on phones and tablets (including mobile browsers), where hints talk about touch.</summary>
    public static bool IsTouchDevice => Application.isMobilePlatform || Touchscreen.current != null && Mouse.current == null;
}
