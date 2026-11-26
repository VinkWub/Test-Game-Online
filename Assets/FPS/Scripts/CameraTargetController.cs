using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class CameraTargetController : NetworkBehaviour
{
    public Transform cameraTarget;

    public float sensitivity = 0.15f;
    public float minPitch = -35f;
    public float maxPitch = 60f;

    private float yaw;
    private float pitch;

    private void Start()
    {
        if (!IsOwner)
            return;

        yaw = transform.eulerAngles.y;
    }

    private void LateUpdate()
    {
        if (!IsOwner)
            return;

        if (cameraTarget == null)
            return;

        if (Mouse.current == null)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        yaw += mouseDelta.x * sensitivity;
        pitch -= mouseDelta.y * sensitivity;

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        cameraTarget.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}