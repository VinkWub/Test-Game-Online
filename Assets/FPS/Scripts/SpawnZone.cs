using UnityEngine;

// แปะที่ "พื้นที่สีเขียว" ของจุดเกิดแต่ละฝั่ง ผู้เล่นเลือกปืนได้เฉพาะเมื่อยืนอยู่ในกรอบนี้
// ขนาดกรอบ = ขนาดของ Collider (หรือ Renderer ถ้าไม่มี Collider) ของ GameObject นี้
// ปรับขนาดพื้นที่สีเขียวใน Scene แล้วกรอบที่เลือกปืนได้จะเปลี่ยนตามทันที
public class SpawnZone : MonoBehaviour
{
    [Tooltip("0 = ผู้เล่นฝั่งแรก, 1 = ผู้เล่นฝั่งที่สอง (ต้องตรงกับ Slot ของ SpawnPoint ฝั่งนั้น)")]
    public int slot = 0;

    [Tooltip("ซ่อนพื้นที่สีเขียวตอนรอบเริ่มเล่น แล้วโชว์อีกครั้งตอนรอ/นับถอยหลังของรอบถัดไป")]
    [SerializeField] private bool hideWhenPlaying = true;

    private BoxCollider box;
    private Collider col;
    private Renderer rend;
    private Renderer[] renderers;
    private GameManager gm;
    private bool visible = true;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        col = GetComponent<Collider>();
        rend = GetComponent<Renderer>();
        if (rend == null) rend = GetComponentInChildren<Renderer>();
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Update()
    {
        if (!hideWhenPlaying) return;

        if (gm == null)
        {
            gm = FindFirstObjectByType<GameManager>();
            if (gm == null) return;
        }

        if (!gm.IsSpawned) return;

        // โชว์ตอนรอผู้เล่น/นับถอยหลัง ซ่อนตอนรอบเริ่มเล่น (ทำงานบนทุกเครื่องจากสถานะที่ Server ซิงก์มา)
        bool shouldShow = !gm.MatchStarted;
        if (shouldShow == visible) return;

        visible = shouldShow;
        foreach (Renderer r in renderers)
        {
            if (r != null) r.enabled = visible;
        }
    }

    // ตำแหน่งนี้อยู่ในกรอบหรือเปล่า (เช็กเฉพาะแนวราบ X/Z ไม่สนความสูง)
    public bool Contains(Vector3 worldPos)
    {
        // กรณีเป็น BoxCollider (เช่น Cube) เช็กในแกนของวัตถุ ถูกต้องแม้วัตถุหมุนหรือย่อขยาย
        if (box != null)
        {
            Vector3 local = box.transform.InverseTransformPoint(worldPos) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
        }

        // Collider ชนิดอื่น (เช่น Plane ที่เป็น MeshCollider) ใช้กรอบสี่เหลี่ยมที่ครอบวัตถุ
        if (col != null) return ContainsXZ(col.bounds, worldPos);

        // ไม่มี Collider ใช้ขนาดจาก Renderer
        if (rend != null) return ContainsXZ(rend.bounds, worldPos);

        return false;
    }

    private static bool ContainsXZ(Bounds b, Vector3 p)
    {
        return p.x >= b.min.x && p.x <= b.max.x &&
               p.z >= b.min.z && p.z <= b.max.z;
    }

    // วาดเส้นกรอบใน Scene ตอนเลือกวัตถุนี้ ไว้ตรวจว่าพื้นที่เลือกปืนอยู่ตรงไหน
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        var b = GetComponent<BoxCollider>();
        if (b != null)
        {
            Gizmos.matrix = b.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(b.center, b.size);
            return;
        }

        var c = GetComponent<Collider>();
        if (c != null) { Gizmos.DrawWireCube(c.bounds.center, c.bounds.size); return; }

        var r = GetComponentInChildren<Renderer>();
        if (r != null) Gizmos.DrawWireCube(r.bounds.center, r.bounds.size);
    }
}