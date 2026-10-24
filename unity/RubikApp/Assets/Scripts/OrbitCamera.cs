using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Камера, що обертається навколо куба: тягни мишею (ЛКМ або ПКМ), коліщатко — зум.</summary>
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
    [Tooltip("Чим більше, тим швидше камера доганяє ціль")]
    public float smoothing = 12f;

    private float _yaw, _pitch, _distance;

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
            if (mouse.leftButton.isPressed || mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * rotateSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * rotateSpeed, -85f, 85f);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
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
