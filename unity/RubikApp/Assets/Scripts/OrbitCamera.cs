using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Camera orbiting the cube. Mouse: drag (left or right button) to orbit, wheel to zoom.
/// Touch: drag one finger to orbit, pinch to zoom. On narrow (portrait) screens it backs off
/// so the whole cube fits the width.
/// </summary>
[RequireComponent(typeof(Camera))]
public class OrbitCamera : MonoBehaviour
{
    public Vector3 target = Vector3.zero;
    public float distance = 9f;
    public float minDistance = 5f;
    public float maxDistance = 20f;
    public float yaw = -30f;
    public float pitch = 25f;
    public float rotateSpeed = 0.25f;
    public float zoomSpeed = 0.01f;
    [Tooltip("Higher = the camera catches up with the target faster")]
    public float smoothing = 12f;
    [Tooltip("Below this aspect ratio (width / height) the camera moves back so the cube fits the width")]
    public float fitAspect = 1.1f;

    /// <summary>Returns true when a primary drag starting at this screen point belongs to someone else (e.g. turning a layer).</summary>
    public Func<Vector2, bool> capturesLeftDrag;

    private Camera _camera;
    private float _yaw, _pitch, _distance;
    private bool _dragging;
    private float _pinchSpan;

    private void Start()
    {
        _camera = GetComponent<Camera>();
        _yaw = yaw;
        _pitch = pitch;
        _distance = distance;
        Apply();
    }

    private void LateUpdate()
    {
        HandleInput();

        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        _yaw = Mathf.Lerp(_yaw, yaw, k);
        _pitch = Mathf.Lerp(_pitch, pitch, k);
        _distance = Mathf.Lerp(_distance, distance, k);
        Apply();
    }

    private void HandleInput()
    {
        // two fingers: pinch zoom only
        if (PointerInput.TryGetTwoTouches(out var a, out var b))
        {
            _dragging = false;
            float span = Vector2.Distance(a, b);
            if (_pinchSpan > 0f && span > 0f)
                distance = Mathf.Clamp(distance * _pinchSpan / span, minDistance, maxDistance);
            _pinchSpan = span;
            return;
        }
        _pinchSpan = 0f;

        var pointer = PointerInput.Current;
        var mouse = Mouse.current;
        if (pointer == null) return;
        var position = pointer.position.ReadValue();

        // a drag that starts over the UI (or is captured by the cube) does not orbit
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            _dragging = !CubeUI.IsPointerOverUi(position);
        else if (pointer.press.wasPressedThisFrame)
            _dragging = !CubeUI.IsPointerOverUi(position) && (capturesLeftDrag == null || !capturesLeftDrag(position));
        bool held = pointer.press.isPressed || mouse != null && mouse.rightButton.isPressed;
        if (!held) _dragging = false;

        if (_dragging)
        {
            // normalized to a 900 px tall screen so phones with dense pixels do not spin the cube wildly
            var delta = pointer.delta.ReadValue() * (900f / Mathf.Max(Screen.height, 1));
            yaw += delta.x * rotateSpeed;
            pitch = Mathf.Clamp(pitch - delta.y * rotateSpeed, -85f, 85f);
        }

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f && !CubeUI.IsPointerOverUi(mouse.position.ReadValue()))
                distance = Mathf.Clamp(distance - scroll * zoomSpeed * distance, minDistance, maxDistance);
        }
    }

    private void Apply()
    {
        float fit = _camera != null && _camera.aspect < fitAspect ? fitAspect / _camera.aspect : 1f;
        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.SetPositionAndRotation(target - rotation * Vector3.forward * (_distance * fit), rotation);
    }
}
