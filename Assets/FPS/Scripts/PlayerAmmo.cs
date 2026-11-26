using System.Collections;
using Unity.Netcode;
using UnityEngine;

// แปะที่ Player Prefab (ตัวเดียวกับ PlayerHealth)
public class PlayerAmmo : NetworkBehaviour
{
    [Header("Ammo Settings")]
    [SerializeField] private int magazineSize = 10;      // กระสุนต่อ 1 แมกกาซีน
    [SerializeField] private float reloadTime = 2f;      // เวลารีโหลด (วินาที)
    [SerializeField] private bool autoReloadWhenEmpty = true; // กระสุนหมดแล้วรีโหลดเอง
    [SerializeField] private KeyCode reloadKey = KeyCode.R;

    // กระสุนในแมกกาซีนตอนนี้ (Server เขียน ทุกคนอ่านได้)
    private NetworkVariable<int> ammoInMag = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> isReloading = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private PlayerHealth health;
    private Coroutine reloadRoutine;

    public int AmmoInMag => ammoInMag.Value;
    public int MagazineSize => magazineSize;
    public bool IsReloading => isReloading.Value;

    // ให้สคริปต์ยิงเช็กฝั่ง Client ได้ว่ายิงได้ไหม (Server ยังตรวจซ้ำอีกชั้น)
    public bool CanShoot => !isReloading.Value && ammoInMag.Value > 0;

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ammoInMag.Value = magazineSize;
            isReloading.Value = false;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (Input.GetKeyDown(reloadKey))
        {
            RequestReloadServerRpc();
        }
    }

    // ---------------- ฝั่ง Server ----------------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestReloadServerRpc()
    {
        StartReloadServer();
    }

    // สคริปต์ยิง (ฝั่ง Server) เรียกก่อนสร้างกระสุนทุกครั้ง
    // คืนค่า true = ยิงได้และหักกระสุนให้แล้ว, false = ยิงไม่ได้ (ห้ามสร้างกระสุน)
    public bool TryConsumeAmmoServer()
    {
        if (!IsServer) return false;
        if (health != null && health.IsDead) return false;
        if (isReloading.Value) return false;
        if (ammoInMag.Value <= 0) return false;

        ammoInMag.Value -= 1;

        if (ammoInMag.Value <= 0 && autoReloadWhenEmpty)
        {
            StartReloadServer();
        }

        return true;
    }

    private void StartReloadServer()
    {
        if (!IsServer) return;
        if (health != null && health.IsDead) return;
        if (isReloading.Value) return;
        if (ammoInMag.Value >= magazineSize) return; // เต็มอยู่แล้ว

        reloadRoutine = StartCoroutine(ReloadCoroutine());
    }

    private IEnumerator ReloadCoroutine()
    {
        isReloading.Value = true;

        yield return new WaitForSeconds(reloadTime);

        ammoInMag.Value = magazineSize;
        isReloading.Value = false;
        reloadRoutine = null;
    }

    // เรียกจาก PlayerHealth.RespawnServer ตอนเกิดใหม่: กระสุนเต็ม ยกเลิกการรีโหลดที่ค้างอยู่
    public void ResetAmmoServer()
    {
        if (!IsServer) return;

        if (reloadRoutine != null)
        {
            StopCoroutine(reloadRoutine);
            reloadRoutine = null;
        }

        ammoInMag.Value = magazineSize;
        isReloading.Value = false;
    }
}