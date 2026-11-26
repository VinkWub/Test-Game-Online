using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;

public class PlayerCameraSetup : MonoBehaviour
{
    private CinemachineCamera cinemachineCamera;

    private void Start()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsClient)
            return;

        if (NetworkManager.Singleton.LocalClient == null)
            return;

        NetworkObject player =
            NetworkManager.Singleton.LocalClient.PlayerObject;

        if (player == null)
            return;

        Transform target = player.transform.Find("CameraTarget");

        if (target == null)
            return;

        cinemachineCamera.Follow = target;
        cinemachineCamera.LookAt = target;
    }
}