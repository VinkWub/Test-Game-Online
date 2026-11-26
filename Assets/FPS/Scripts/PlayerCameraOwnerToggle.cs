using UnityEngine;
using Unity.Netcode;

public class PlayerCameraOwnerToggle : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;

    public override void OnNetworkSpawn()
    {
        if (playerCamera != null) playerCamera.enabled = IsOwner;
        if (audioListener != null) audioListener.enabled = IsOwner;
    }
}