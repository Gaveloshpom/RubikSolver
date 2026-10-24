using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Camera orbiting the cube: drag with the mouse (left or right button), wheel to zoom.</summary>
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

    /// <summary>Returns true when a left drag starting at this screen point belongs to someone else (e.g. turning a layer).</summary>
    public Func<Vector2, bool> capturesLeftDrag;

    private float _yaw, _pitch, _distance;
    private bool _dragging;

    private void Start()
    {
        _yaw = yaw;
        _pitch = pitch;
        _distance = distance;
        Apply();
    }

    private void LateUpdate()
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            var position = mouse.position.ReadValue();

            // a drag that starts over the UI (or is captured by the cube) does not orbit
            if (mouse.rightButton.wasPressedThisFrame)
                _dragging = !CubeUI.IsPointerOverUi(position);
            else if (mouse.leftButton.wasPressedThisFrame)
                _dragging = !CubeUI.IsPointerOverUi(position) && (capturesLeftDrag == null || !capturesLeftDrag(position));
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
                _dragging = false;

            if (_dragging)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * rotateSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * rotateSpeed, -85f, 85f);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f && !CubeUI.IsPointerOverUi(position))
                distance = Mathf.Clamp(distance - scroll * zoomSpeed * distance, minDistance, maxDistance);
        }

        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        _yaw = Mathf.Lerp(_yaw, yaw, k);
        _pitch = Mathf.Lerp(_pitch, pitch, k);
        _distance = Mathf.Lerp(_distance, distance, k);
        Apply();
    }

    private void Apply()
    {
        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.SetPositionAndRotation(target - rotation * Vector3.forward * _distance, rotation);
    }
}
