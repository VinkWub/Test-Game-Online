using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class ShootFireball : NetworkBehaviour
{
    [SerializeField] private GameObject bullet;
    [SerializeField] private Transform shootTransform;
    [SerializeField] private List<GameObject> spawnBullets = new List<GameObject>();

    [Header("Aim Settings")]
    [Tooltip("กล้องที่ใช้เล็ง ถ้าว่างจะใช้ Camera.main (ต้องติด Tag MainCamera)")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private float aimMaxDistance = 200f;

    [Header("Fire Settings")]
    [Tooltip("เวลาระหว่างนัด (วินาที) เช่น 0.15 = ยิงได้ ~6-7 นัด/วินาที")]
    [SerializeField] private float fireInterval = 0.15f;

    private PlayerAmmo ammo;
    private float nextFireTimeClient;   // ฝั่ง Client: ยิงนัดต่อไปได้เมื่อไหร่
    private float lastShotTimeServer;   // ฝั่ง Server: ใช้ตรวจอัตราการยิงกันโกง

    void Awake()
    {
        // PlayerAmmo อยู่ที่ Player Prefab เดียวกัน (ใช้ InParent เผื่อสคริปต์นี้อยู่ที่ลูก)
        ammo = GetComponentInParent<PlayerAmmo>();
    }

    void Update()
    {
        if (!IsOwner) return;

        // เมาส์ยังไม่ล็อก (เช่นกด Esc อยู่) ไม่ยิง
        if (Cursor.lockState != CursorLockMode.Locked) return;

        // กดค้างเพื่อยิงต่อเนื่อง ตามอัตราการยิง fireInterval
        if (Input.GetKey(KeyCode.Mouse0) && Time.time >= nextFireTimeClient)
        {
            // เช็กฝั่ง Client ก่อน กระสุนหมด/กำลังรีโหลด ไม่ต้องส่งคำสั่งยิง
            // (Server ยังตรวจซ้ำอีกชั้นใน ShootServerRpc)
            if (ammo != null && !ammo.CanShoot) return;

            nextFireTimeClient = Time.time + fireInterval;
            ShootServerRpc(GetAimDirection());
        }
    }

    // หาทิศที่กระสุนต้องพุ่ง: ยิงจากปากกระบอก ไปยังจุดที่กลางจอ (Crosshair) ชี้อยู่
    // ใช้ทิศของกล้องจึงปรับสูง/ต่ำได้ (ตัวละครหมุนแค่แกน Y เลยใช้ shootTransform.forward ไม่ได้)
    private Vector3 GetAimDirection()
    {
        Camera cam = aimCamera != null ? aimCamera : Camera.main;
        if (cam == null) return shootTransform.forward;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint = ray.GetPoint(aimMaxDistance);

        // หาจุดแรกที่รังสีจากกล้องชน (ข้ามตัวเราเอง และข้าม Trigger เช่นกระสุน)
        RaycastHit[] hits = Physics.RaycastAll(ray, aimMaxDistance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() == NetworkObject) continue;

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                targetPoint = hit.point;
            }
        }

        Vector3 dir = targetPoint - shootTransform.position;

        // กันกรณีจุดเล็งอยู่ใกล้/อยู่หลังปากกระบอก (เช่นกล้องชนกำแพงด้านหลังตัวละคร)
        if (dir.sqrMagnitude < 0.01f || Vector3.Dot(dir, ray.direction) < 0f)
            return ray.direction;

        return dir.normalized;
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 aimDirection)
    {
        // กัน Client ยิงถี่เกินอัตราที่กำหนด (เผื่อเวลาเน็ตกระตุกเล็กน้อย 10%)
        if (Time.time - lastShotTimeServer < fireInterval * 0.9f)
            return;

        // Server เป็นคนตัดสินว่ายิงได้ไหม และหักกระสุน 1 นัด
        if (ammo == null || !ammo.TryConsumeAmmoServer())
            return; // กระสุนหมด / กำลังรีโหลด / ตายอยู่ → ไม่สร้างกระสุน

        lastShotTimeServer = Time.time;

        if (aimDirection.sqrMagnitude < 0.0001f)
            aimDirection = shootTransform.forward;

        Quaternion rot = Quaternion.LookRotation(aimDirection.normalized, Vector3.up);

        GameObject go = Instantiate(bullet, shootTransform.position, rot);
        go.GetComponent<MoveProjectile>().shooterClientId = OwnerClientId;
        go.GetComponent<NetworkObject>().Spawn();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DestroyServerRpc()
    {
        GameObject toDestroy = spawnBullets[0];
        toDestroy.GetComponent<NetworkObject>().Despawn();
        spawnBullets.Remove(toDestroy);
        Destroy(toDestroy);
    }
}