using UnityEngine;
using Unity.Netcode;

public class MoveProjectile : NetworkBehaviour
{
    [SerializeField] private GameObject hitParticles;
    [SerializeField] private float shootForce;
    [SerializeField] private float lifeTime = 5f;

    [Header("Hit Detection (Server)")]
    [Tooltip("ชั้นวัตถุที่กระสุนชนได้ (ค่าเริ่มต้น Everything) ไม่ชน Trigger เช่นกล่อง HP")]
    [SerializeField] private LayerMask hitMask = ~0;
    [Tooltip("หลังชน ค้างกระสุนไว้ที่จุดชนอีกกี่วินาทีก่อนหาย เพื่อให้ภาพฝั่งผู้เล่นไล่ทันแล้วเห็นกระสุนถึงกำแพงจริง (ถ้ายังหายก่อนถึงให้เพิ่มค่านี้)")]
    [SerializeField] private float despawnDelayAfterHit = 0.2f;

    // ตั้งค่าโดย Server ตอนยิง ใช้กันลูกไฟชนตัวผู้ยิงเอง และใช้นับ Kill ให้คนยิง
    [HideInInspector] public ulong shooterClientId;
    // ตั้งค่าโดย PlayerWeapon ตอนยิง (ดาเมจ/ความเร็วตามปืนที่ใช้)
    [HideInInspector] public int damage = 10;
    [HideInInspector] public float speedOverride = 0f;   // 0 = ใช้ shootForce ของ Prefab

    private Rigidbody rb;
    private bool hasHit;

    // ใช้ซ้ำทุกเฟรมกันสร้าง Array ใหม่ (Server ตรวจชนเฉพาะที่ Server)
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            // Client แค่รับตำแหน่งจาก NetworkTransform ไม่ต้องใช้ฟิสิกส์
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            return;
        }

        Invoke(nameof(DespawnSelf), lifeTime);
    }

    // ขยับและตรวจการชนใน FixedUpdate (ตรงกับรอบฟิสิกส์)
    void FixedUpdate()
    {
        if (!IsServer || hasHit) return;
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null) return;

        float speed = speedOverride > 0f ? speedOverride : shootForce;
        Vector3 dir = transform.forward;

        // กระสุนเร็วมากจะ "ข้าม" กำแพงบางๆ ได้ถ้าพึ่ง OnTriggerEnter อย่างเดียว
        // จึงยิง Raycast ไปข้างหน้าเท่าระยะที่กระสุนจะเคลื่อนที่ในรอบฟิสิกส์นี้ ตรวจเส้นทางทั้งหมดแทนการเช็กแค่ตำแหน่งปลายทาง
        float stepDistance = speed * Time.fixedDeltaTime;
        Vector3 origin = rb.position;

        int count = Physics.RaycastNonAlloc(
            origin, dir, hitBuffer, stepDistance, hitMask, QueryTriggerInteraction.Ignore);

        // หาจุดชนที่ใกล้ที่สุดที่ไม่ใช่ตัวผู้ยิง / กระสุนด้วยกัน
        float nearest = float.MaxValue;
        int nearestIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (ShouldIgnore(hitBuffer[i].collider)) continue;

            if (hitBuffer[i].distance < nearest)
            {
                nearest = hitBuffer[i].distance;
                nearestIndex = i;
            }
        }

        if (nearestIndex >= 0)
        {
            RaycastHit hit = hitBuffer[nearestIndex];
            rb.position = hit.point;
            OnHit(hit.collider);
            return;
        }

        rb.linearVelocity = dir * speed;
    }

    // สำรอง: กรณีกระสุนชนผู้เล่นแบบ Trigger ก่อนที่ Raycast จะจับได้
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;   // ให้ Server ตัดสินการชนเท่านั้น
        if (hasHit) return;      // กันชนซ้ำ
        if (ShouldIgnore(other)) return;

        OnHit(other);
    }

    // ข้ามตัวผู้ยิง, กระสุนลูกอื่น และ Trigger (เช่นกล่อง HP)
    private bool ShouldIgnore(Collider other)
    {
        if (other == null || other.isTrigger) return true;

        // กระสุนลูกอื่น
        if (other.GetComponentInParent<MoveProjectile>() != null) return true;

        // ตัวผู้ยิงเอง
        NetworkObject hitObject = other.GetComponentInParent<NetworkObject>();
        if (hitObject != null && hitObject.IsPlayerObject
            && hitObject.OwnerClientId == shooterClientId)
            return true;

        return false;
    }

    private void OnHit(Collider other)
    {
        if (hasHit) return;
        hasHit = true;

        // ส่ง shooterClientId เข้าไปตรงๆ เพื่อให้ Kill นับถูกคน
        other.GetComponentInParent<PlayerHealth>()?.ApplyDamageServer(damage, shooterClientId);

        // Server เห็นว่ากระสุนชนแล้ว แต่ภาพที่ผู้เล่นเห็นตามหลัง Server อยู่เล็กน้อย (Interpolation + เน็ต)
        // ถ้าลบกระสุนทันที ผู้เล่นจะเห็นกระสุนหายไปก่อนถึงกำแพง จึงหยุดกระสุนไว้ที่จุดชนก่อน แล้วค่อยลบ
        StopAtHitPoint();
        CancelInvoke(nameof(DespawnSelf));
        Invoke(nameof(DespawnSelf), despawnDelayAfterHit);
    }

    private void StopAtHitPoint()
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;   // ตั้งหลังเคลียร์ความเร็ว (Kinematic ตั้งความเร็วไม่ได้)
        }

        // ปิด Collider กันกระสุนที่ค้างอยู่ไปชนหรือดันอะไรอีก
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;
    }

    private void DespawnSelf()
    {
        if (!IsSpawned) return;
        NetworkObject.Despawn();
    }
}