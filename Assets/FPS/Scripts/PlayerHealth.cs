using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using System.Collections;

public class PlayerHealth : NetworkBehaviour
{
    [SerializeField] private NetworkVariable<int> currentHP = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField] private NetworkVariable<bool> isDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ช่องเกิดของผู้เล่นคนนี้ (0 หรือ 1) Server เป็นคนกำหนด
    private NetworkVariable<int> spawnSlot = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // จำนวน Kill ของผู้เล่นคนนี้ (Server เขียน ทุกคนอ่านได้)
    private NetworkVariable<int> kills = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField] private RectTransform HPbar;

    const int maxhp = 100;

    public GameObject gameManager;
    private GameManager gm;

    public bool IsDead => isDead.Value;
    public int Kills => kills.Value;

    public override void OnNetworkSpawn()
    {
        gameManager = GameObject.Find("GameManager");
        if (gameManager != null)
            gm = gameManager.GetComponent<GameManager>();

        if (IsServer)
        {
            currentHP.Value = maxhp;
            spawnSlot.Value = PickFreeSlot();

            GetSpawnPose(out Vector3 pos, out Quaternion rot);
            transform.SetPositionAndRotation(pos, rot);

            StartCoroutine(AddPlayer());
        }

        currentHP.OnValueChanged += OnHPChanged;
        isDead.OnValueChanged += OnDeadChanged;

        // กรณีเข้าเกมมาทีหลัง ให้สถานะภาพตรงกับ Server
        SetAlive(!isDead.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentHP.OnValueChanged -= OnHPChanged;
        isDead.OnValueChanged -= OnDeadChanged;
    }

    private IEnumerator AddPlayer()
    {
        yield return null;
        if (gm != null)
        {
            gm.AddPlayerServerRpc(NetworkObject);
        }
        else
        {
            print("GameManager not found");
        }
    }

    // เลือกช่อง 0 ถ้าว่าง ไม่งั้นช่อง 1
    private int PickFreeSlot()
    {
        bool slot0Used = false;
        foreach (var p in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (p == this) continue;
            if (p.spawnSlot.Value == 0) slot0Used = true;
        }
        return slot0Used ? 1 : 0;
    }

    // หาจุดเกิดที่ตรงกับ slot ของผู้เล่นคนนี้
    private void GetSpawnPose(out Vector3 pos, out Quaternion rot)
    {
        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
        {
            if (sp.slot == spawnSlot.Value)
            {
                pos = sp.transform.position;
                rot = sp.transform.rotation;
                return;
            }
        }

        Debug.LogWarning($"ไม่พบ SpawnPoint slot {spawnSlot.Value}");
        pos = transform.position;
        rot = transform.rotation;
    }

    private SpawnZone cachedZone;
    private int zoneLookupSlot = -999;   // slot ที่ค้นหา SpawnZone ไปแล้ว (กันค้นซ้ำทุกเฟรม)

    // หา SpawnZone (พื้นที่สีเขียว) ของฝั่งตัวเอง ค้นครั้งเดียวต่อ slot แล้วจำไว้
    private SpawnZone GetMyZone()
    {
        if (zoneLookupSlot != spawnSlot.Value)
        {
            zoneLookupSlot = spawnSlot.Value;
            cachedZone = null;

            foreach (var z in FindObjectsByType<SpawnZone>(FindObjectsSortMode.None))
            {
                if (z.slot == spawnSlot.Value) { cachedZone = z; break; }
            }
        }
        return cachedZone;
    }

    private SpawnPoint cachedSpawn;

    // อยู่ในเขตจุดเกิดของตัวเองไหม (วัดระยะแนวราบ) ใช้กำหนดว่าเปลี่ยนปืนได้หรือเปล่า
    public bool IsInSpawnZone(float radius)
    {
        if (spawnSlot.Value < 0) return false;

        // ถ้ามี SpawnZone (พื้นที่สีเขียว) ของฝั่งนี้ ใช้กรอบนั้นเป็นเขตเลือกปืน
        SpawnZone zone = GetMyZone();
        if (zone != null) return zone.Contains(transform.position);

        // ไม่มี SpawnZone: ใช้รัศมีรอบ SpawnPoint แบบเดิม
        if (cachedSpawn == null || cachedSpawn.slot != spawnSlot.Value)
        {
            cachedSpawn = null;
            foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            {
                if (sp.slot == spawnSlot.Value) { cachedSpawn = sp; break; }
            }
            if (cachedSpawn == null) return false;
        }

        Vector3 d = transform.position - cachedSpawn.transform.position;
        d.y = 0f;
        return d.sqrMagnitude <= radius * radius;
    }

    private void OnHPChanged(int previousValue, int newValue)
    {
        if (HPbar != null)
        {
            HPbar.sizeDelta = new Vector2(newValue, HPbar.sizeDelta.y);
        }
    }

    // รันบนทุกเครื่อง: ทำแค่เรื่องภาพ/การควบคุม ไม่แตะค่า Network
    private void OnDeadChanged(bool previousValue, bool newValue)
    {
        SetAlive(!newValue);
    }

    private void SetAlive(bool alive)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = alive;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = alive;

        var move = GetComponent<ClienPlayerMovement>();
        if (move != null) move.enabled = alive;
    }

    // กรณีสคริปต์ยิงเรียก RPC นี้จาก Client โดยตรง Server จะรู้ตัวคนยิงจาก SenderClientId
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DealDamageServerRpc(int damage, RpcParams rpcParams = default)
    {
        ApplyDamageServer(damage, rpcParams.Receive.SenderClientId);
    }

    // ฝั่ง Server เท่านั้น: ใช้เมื่อสคริปต์ยิงรู้ attackerId เองอยู่แล้ว
    public void ApplyDamageServer(int damage, ulong attackerId)
    {
        if (!IsServer) return;
        if (isDead.Value) return;                  // ตายแล้วไม่ต้องรับดาเมจซ้ำ
        if (gm != null && gm.IsGameOver) return;   // จบเกมแล้ว ไม่รับดาเมจ

        Debug.Log($"[Damage] victim(owner)={OwnerClientId} attacker={attackerId} damage={damage}");

        currentHP.Value -= damage;
        if (currentHP.Value <= 0)
        {
            currentHP.Value = 0;
            isDead.Value = true;

            // บอก GameManager ว่าใครตาย และใครเป็นคนฆ่า
            if (gm != null)
            {
                gm.HandlePlayerDeath(NetworkObject, attackerId);
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlayerHealServerRpc(int hp)
    {
        if (isDead.Value) return;

        if (currentHP.Value + hp > maxhp)
            currentHP.Value = maxhp;
        else
            currentHP.Value += hp;
    }

    // เรียกจาก GameManager (ฝั่ง Server เท่านั้น)
    public void AddKillServer()
    {
        if (!IsServer) return;
        kills.Value += 1;
    }

    // เรียกจาก GameManager (ฝั่ง Server เท่านั้น)
    public void RespawnServer()
    {
        if (!IsServer) return;

        currentHP.Value = maxhp;
        isDead.Value = false;

        // กระสุนกลับเต็ม และยกเลิกรีโหลดที่ค้างอยู่
        GetComponent<PlayerWeapon>()?.ResetAmmoServer();

        GetSpawnPose(out Vector3 pos, out Quaternion rot);
        transform.SetPositionAndRotation(pos, rot);

        // ผู้เล่นคุมตำแหน่งตัวเองอยู่ (Owner-authoritative) จึงต้องสั่งย้ายที่ฝั่ง Owner ด้วย
        TeleportOwnerRpc(pos, rot);
    }

    [Rpc(SendTo.Owner)]
    private void TeleportOwnerRpc(Vector3 pos, Quaternion rot)
    {
        var nt = GetComponent<NetworkTransform>();
        if (nt != null)
            nt.Teleport(pos, rot, transform.localScale);
        else
            transform.SetPositionAndRotation(pos, rot);
    }
}