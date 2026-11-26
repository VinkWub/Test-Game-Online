using UnityEngine;
using Unity.Netcode;

public class ClienPlayerMovement : NetworkBehaviour
{
    // true ตอนเมนูเลือกปืนเปิดอยู่ (WeaponSelectUI เป็นคนตั้งค่า)
    // ใช้กันไม่ให้สคริปต์นี้ล็อกเมาส์กลับ ตอนผู้เล่นคลิกปุ่มในเมนู
    public static bool MenuOpen;

    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float rotateSpeed = 500f;
    [SerializeField] private Transform cameraTarget;
    [Tooltip("รัศมีตัวละคร (เมตร) ใช้เช็กการชนกำแพงล่องหนก่อนเริ่มเกม")]
    [SerializeField] private float playerRadius = 0.4f;

    private Animator animator;

    // การจัดตำแหน่งตอนเกิดย้ายไปอยู่ที่ PlayerHealth แล้ว (ตาม SpawnPoint slot)
    // จึงตัด mySpawnIndex และโค้ดจัดตำแหน่งออกจากที่นี่
    public override void OnNetworkSpawn()
    {
        MenuOpen = false;

        if (!IsOwner)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (!IsOwner)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // คลิกเพื่อล็อกเมาส์กลับ (ยกเว้นตอนเมนูเลือกปืนเปิดอยู่)
        if (Input.GetMouseButtonDown(0) &&
            Cursor.lockState == CursorLockMode.None &&
            !MenuOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            animator.SetFloat("Speed", 0f);
            return;
        }

        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 cameraForward = cameraTarget.forward;
        Vector3 cameraRight = cameraTarget.right;

        cameraForward.y = 0f;
        cameraRight.y = 0f;

        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 movementDirection =
            (cameraRight * horizontal + cameraForward * vertical).normalized;

        // เดินโดยเคารพกำแพงล่องหนก่อนเริ่มเกม (StartBarrier) ถ้าชนจะไถลไปตามแนวกำแพง
        Vector3 step = movementDirection * moveSpeed * Time.deltaTime;
        Vector3 pos = transform.position;
        Vector3 stepX = new Vector3(step.x, 0f, 0f);
        Vector3 stepZ = new Vector3(0f, 0f, step.z);

        if (StartBarrier.IsBlocked(pos, playerRadius))
            pos += step;                                            // อยู่ในกำแพงอยู่แล้ว ให้เดินออกได้ ไม่ติดค้าง
        else if (!StartBarrier.IsBlocked(pos + step, playerRadius))
            pos += step;
        else if (!StartBarrier.IsBlocked(pos + stepX, playerRadius))
            pos += stepX;
        else if (!StartBarrier.IsBlocked(pos + stepZ, playerRadius))
            pos += stepZ;

        transform.position = pos;

        // หัน Model ตามทิศทางกล้องแบบ Valorant
        Vector3 cameraDirection = cameraTarget.forward;
        cameraDirection.y = 0f;
        cameraDirection.Normalize();

        if (cameraDirection != Vector3.zero)
        {
            Quaternion cameraRotation = Quaternion.LookRotation(
                cameraDirection,
                Vector3.up
            );

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                cameraRotation,
                rotateSpeed * Time.deltaTime
            );
        }

        animator.SetFloat("Speed", movementDirection.magnitude);
    }
}