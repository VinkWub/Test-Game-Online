using UnityEngine;
using Unity.Netcode;

public class HPboxHeal : NetworkBehaviour
{
    private const int HealAmount = 50;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Player"))
        {
            PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();

            if (playerHealth != null)
            {
                // เพิ่มเลือด
                playerHealth.PlayerHealServerRpc(HealAmount);

                // แจ้ง GameManager ว่า HP Box ถูกเก็บแล้ว
                GameManager gameManager = FindFirstObjectByType<GameManager>();

                if (gameManager != null)
                {
                    gameManager.OnHPBoxCollected();
                }
            }
        }
    }
}