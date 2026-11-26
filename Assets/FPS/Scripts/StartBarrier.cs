using System.Collections.Generic;
using UnityEngine;

// แปะที่ "กำแพงล่องหน" (GameObject ที่มี Collider) ที่กั้นทางออกของแต่ละฝั่ง
// กำแพงจะปิดอยู่จนกว่าแมตช์จะเริ่ม (ผู้เล่นครบและนับถอยหลังจบ) แล้วจะเปิดทางให้ทุกคนพร้อมกัน
//
// คำแนะนำ: ใช้ BoxCollider และทำกำแพงให้สูงพอ (เช่นสูง 3 เมตรขึ้นไป) ติ๊ก Is Trigger หรือไม่ติ๊กก็ได้
// ปิด Mesh Renderer (หรือไม่ใส่เลย) เพื่อให้มองไม่เห็น
public class StartBarrier : MonoBehaviour
{
    private const float ProbeHeight = 1f;   // เช็กที่ระดับอกตัวละคร (ตัวละครอ้างอิงที่เท้า)

    private static readonly List<StartBarrier> all = new List<StartBarrier>();

    private Collider col;
    private GameManager gm;
    private bool closed = true;

    private void Awake()
    {
        col = GetComponent<Collider>();
    }

    private void OnEnable()
    {
        all.Add(this);
    }

    private void OnDisable()
    {
        all.Remove(this);
    }

    private void Update()
    {
        if (gm == null)
        {
            gm = FindFirstObjectByType<GameManager>();
            if (gm == null) return;
        }

        if (!gm.IsSpawned) return;

        // เปิดทางเมื่อแมตช์เริ่ม (ทำงานบนทุกเครื่องจากค่าที่ Server ซิงก์มา)
        bool shouldClose = !gm.MatchStarted;
        if (shouldClose != closed)
        {
            closed = shouldClose;
            if (col != null) col.enabled = closed;   // ปิด Collider แต่ GameObject ยังเปิดอยู่ สคริปต์จึงยังทำงาน
        }
    }

    // ตำแหน่งนี้ (พร้อมรัศมีตัวละคร) ชนกำแพงที่ปิดอยู่หรือไม่
    // ใช้ในสคริปต์เดิน เพราะตัวละครเคลื่อนที่ด้วย Transform จึงไม่ถูก Collider ขวางโดยอัตโนมัติ
    public static bool IsBlocked(Vector3 position, float radius)
    {
        if (all.Count == 0) return false;

        Vector3 probe = position + Vector3.up * ProbeHeight;

        foreach (StartBarrier b in all)
        {
            if (!b.closed || b.col == null || !b.col.enabled) continue;

            Vector3 closest = b.col.ClosestPoint(probe);
            if ((closest - probe).sqrMagnitude <= radius * radius) return true;
        }

        return false;
    }
}