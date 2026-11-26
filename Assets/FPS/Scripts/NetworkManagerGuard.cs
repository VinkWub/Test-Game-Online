using Unity.Netcode;
using UnityEngine;

// แปะที่ GameObject ที่มี NetworkManager ในฉาก Lobby
// NetworkManager ถูกตั้งให้ไม่ถูกทำลายตอนเปลี่ยนฉาก (Don't Destroy) พอกลับมาฉาก Lobby อีกครั้ง
// ฉากจะสร้าง NetworkManager ตัวใหม่ซ้อนขึ้นมา สคริปต์นี้จะลบตัวใหม่ทิ้ง เหลือตัวเดิมที่เชื่อมต่ออยู่
[DefaultExecutionOrder(-1000)]
public class NetworkManagerGuard : MonoBehaviour
{
    private void Awake()
    {
        NetworkManager existing = NetworkManager.Singleton;

        if (existing != null && existing.gameObject != gameObject)
        {
            Destroy(gameObject);
        }
    }
}