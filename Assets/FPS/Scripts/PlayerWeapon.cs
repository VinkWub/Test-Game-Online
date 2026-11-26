using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// แทน ShootFireball + PlayerAmmo เดิม แปะที่ Player Prefab (ตัวเดียวกับ PlayerHealth)
public class PlayerWeapon : NetworkBehaviour
{
    [Header("Weapons (ลำดับในลิสต์ = id ของปืน)")]
    [SerializeField] private WeaponData[] weapons;

    [Header("References")]
    [SerializeField] private Transform shootTransform;          // ปากกระบอก
    [SerializeField] private Transform weaponHolder;            // ที่วางโมเดลปืน (ไม่ใส่ก็ได้)
    [SerializeField] private Camera aimCamera;                  // ว่าง = ใช้ Camera.main
    [SerializeField] private float aimMaxDistance = 200f;
    [Tooltip("ระยะต่ำสุดของจุดเล็ง (เมตร) กันทิศกระสุนแกว่งเวลาเป้าอยู่ใกล้มาก เช่นก้มมองพื้นหรือชนกำแพง")]
    [SerializeField] private float aimMinDistance = 10f;
    [Tooltip("Server ยอมให้จุดเกิดกระสุนห่างจากตัวผู้เล่นได้ไม่เกินกี่เมตร (กันโกง)")]
    [SerializeField] private float maxMuzzleDistance = 3f;
    [Tooltip("ความสูงจากเท้าของจุดที่ใช้เช็กว่าปากกระบอกทะลุกำแพงหรือไม่ (ประมาณระดับอกตัวละคร เมตร)")]
    [SerializeField] private float bodyCheckHeight = 1.2f;

    [Header("Aim Follow (ปืนหมุนตามกล้อง)")]
    [Tooltip("ให้ Weapon Holder (และปืน/แขนที่เป็นลูกของมัน) หมุนไปทางที่เล็งอยู่ ทั้งเงยและก้ม")]
    [SerializeField] private bool aimHolderFollowsCamera = true;
    [Tooltip("ความเร็วที่ปืนของเราเองหมุนตามกล้อง (ยิ่งมากยิ่งไว ยิ่งน้อยยิ่งนุ่ม)")]
    [SerializeField] private float ownerAimSmoothSpeed = 40f;
    [Tooltip("ความเร็วที่ปืนของผู้เล่นคนอื่นหมุนตาม (ยิ่งมากยิ่งไว)")]
    [SerializeField] private float aimSmoothSpeed = 15f;
    [Tooltip("ส่งทิศเล็งให้คนอื่นกี่วินาทีต่อครั้ง")]
    [SerializeField] private float aimSyncInterval = 0.05f;

    [Header("Settings")]
    [Tooltip("รัศมีเขตจุดเกิดที่เปลี่ยนปืนได้ (เมตร)")]
    [SerializeField] private float spawnZoneRadius = 6f;
    [SerializeField] private bool autoReloadWhenEmpty = true;
    [SerializeField] private KeyCode reloadKey = KeyCode.R;

    // ---------- สถานะที่ Server เป็นคนเขียน ทุกคนอ่านได้ ----------
    private NetworkVariable<int> primaryId = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> secondaryId = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> currentSlot = new NetworkVariable<int>(   // 0 = ปืนหลัก, 1 = ปืนพก
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> primaryAmmo = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> secondaryAmmo = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> isReloading = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ทิศเล็งในแกนของตัวละคร (Owner เป็นคนเขียน เพื่อให้ทุกเครื่องเห็นปืนเงย/ก้มตาม)
    private NetworkVariable<Vector3> netLocalAimDir = new NetworkVariable<Vector3>(
        Vector3.forward, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private Vector3 smoothedLocalAimDir = Vector3.forward;
    private Vector3 lastSentAimDir = Vector3.forward;
    private float nextAimSyncTime;

    private PlayerHealth health;
    private GameManager gm;
    private Coroutine reloadRoutine;
    private readonly List<int> primaryIds = new List<int>();

    private float lastShotTimeServer;
    private float nextFireTimeClient;
    private float spinTimer;
    private float defaultFov = -1f;
    private bool scopeActive;
    private GameObject currentModel;
    private Transform currentMuzzle;   // จุดปากกระบอกของปืนที่ถืออยู่

    // ---------- ข้อมูลให้ UI / สคริปต์อื่นอ่าน ----------
    private WeaponData Current => GetWeapon(currentSlot.Value == 0 ? primaryId.Value : secondaryId.Value);
    public int CurrentAmmo => currentSlot.Value == 0 ? primaryAmmo.Value : secondaryAmmo.Value;
    public int CurrentMagazineSize => Current != null ? Current.magazineSize : 0;
    public string CurrentWeaponName => Current != null ? Current.displayName : "-";
    public bool IsReloading => isReloading.Value;
    public bool CanShootClient => Current != null && !isReloading.Value && CurrentAmmo > 0;
    public IReadOnlyList<int> PrimaryWeaponIds => primaryIds;

    // เปลี่ยนปืนได้เฉพาะช่วงรอผู้เล่น/นับถอยหลังก่อนเริ่มรอบ และต้องอยู่ในโซนเขียวของตัวเอง
    // พอรอบเริ่มเล่นแล้ว (กำแพงเปิด) เลือกปืนไม่ได้ จนกว่ารอบถัดไปจะเริ่มนับถอยหลังอีกครั้ง
    public bool CanChangeLoadout
    {
        get
        {
            if (health == null) return false;

            if (gm == null) gm = FindFirstObjectByType<GameManager>();
            if (gm != null && gm.MatchStarted) return false;

            return health.IsInSpawnZone(spawnZoneRadius);
        }
    }

    public WeaponData GetWeaponData(int id) => GetWeapon(id);

    private WeaponData GetWeapon(int id)
    {
        if (weapons == null || id < 0 || id >= weapons.Length) return null;
        return weapons[id];
    }

    private void Awake()
    {
        health = GetComponentInParent<PlayerHealth>();

        primaryIds.Clear();
        if (weapons != null)
        {
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] != null && weapons[i].slot == WeaponSlot.Primary)
                    primaryIds.Add(i);
            }
        }
    }

    private int FindFirstWeapon(WeaponSlot slot)
    {
        if (weapons == null) return -1;
        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] != null && weapons[i].slot == slot) return i;
        }
        return -1;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            gm = FindFirstObjectByType<GameManager>();

            // ปืนเริ่มต้น: ปืนหลักกระบอกแรกในลิสต์ + ปืนพกกระบอกแรก
            primaryId.Value = FindFirstWeapon(WeaponSlot.Primary);
            secondaryId.Value = FindFirstWeapon(WeaponSlot.Secondary);
            currentSlot.Value = 0;
            FillAllAmmoServer();
        }

        primaryId.OnValueChanged += OnWeaponStateChanged;
        secondaryId.OnValueChanged += OnWeaponStateChanged;
        currentSlot.OnValueChanged += OnWeaponStateChanged;

        RefreshModel();
    }

    public override void OnNetworkDespawn()
    {
        primaryId.OnValueChanged -= OnWeaponStateChanged;
        secondaryId.OnValueChanged -= OnWeaponStateChanged;
        currentSlot.OnValueChanged -= OnWeaponStateChanged;
    }

    private void OnWeaponStateChanged(int previous, int next)
    {
        RefreshModel();
    }

    // โมเดลปืนในมือ (ทำเฉพาะภาพ ไม่ใช่ NetworkObject แต่ละเครื่องสร้างเอง)
    private void RefreshModel()
    {
        currentMuzzle = null;

        if (weaponHolder == null) return;

        if (currentModel != null) Destroy(currentModel);

        WeaponData w = Current;
        if (w == null || w.modelPrefab == null) return;

        currentModel = Instantiate(w.modelPrefab, weaponHolder);
        // ใช้ค่าชดเชยจากไฟล์ปืน เพื่อแก้โมเดลที่หันผิดทิศ/ตำแหน่งไม่ตรงมือ
        currentModel.transform.localPosition = w.modelPositionOffset;
        currentModel.transform.localRotation = Quaternion.Euler(w.modelRotationOffset);
        // Model Scale เป็นตัวคูณจากขนาดเดิมของ Prefab (1,1,1 = ขนาดเดิม)
        // ถ้าเป็น 0,0,0 (ไฟล์ปืนเก่าที่ยังไม่เคยตั้งค่า) ให้ถือเป็น 1,1,1 กันปืนหายไป
        Vector3 mul = w.modelScale == Vector3.zero ? Vector3.one : w.modelScale;
        currentModel.transform.localScale = Vector3.Scale(w.modelPrefab.transform.localScale, mul);

        // หาจุดปากกระบอก: 1) ลูกชื่อ "Muzzle" ในโมเดล  2) Muzzle Offset ในไฟล์ปืน
        currentMuzzle = FindMuzzle(currentModel.transform);
        if (currentMuzzle == null && w.muzzleOffset != Vector3.zero)
        {
            var go = new GameObject("Muzzle (auto)");
            go.transform.SetParent(currentModel.transform, false);
            go.transform.localPosition = w.muzzleOffset;
            currentMuzzle = go.transform;
        }

        // Log ช่วยตรวจ (เฉพาะผู้เล่นเครื่องนี้) ลบทิ้งได้เมื่อใช้งานได้แล้ว
        if (IsOwner)
        {
            if (currentMuzzle != null)
                Debug.Log($"[Weapon] {w.displayName}: ใช้จุดปากกระบอก = {currentMuzzle.name}");
            else
                Debug.LogWarning($"[Weapon] {w.displayName}: ไม่พบจุดปากกระบอก ใช้ Shoot Transform แทน");
        }
    }

    private static Transform FindMuzzle(Transform root)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);

        // 1) ชื่อตรงตัวว่า "Muzzle" ก่อน (ไม่สนตัวพิมพ์เล็กใหญ่)
        foreach (Transform t in all)
        {
            if (t != root && t.name.Equals("muzzle", System.StringComparison.OrdinalIgnoreCase))
                return t;
        }

        // 2) ชื่อมีคำว่า muzzle แต่ต้องไม่ใช่ชิ้นส่วนโมเดล (เช่น muzzlebreak_low_0 ที่มี Mesh)
        foreach (Transform t in all)
        {
            if (t != root && t.name.ToLower().Contains("muzzle") && t.GetComponent<Renderer>() == null)
                return t;
        }

        return null;
    }

    // จุดที่กระสุนออก: ปากกระบอกของปืนที่ถืออยู่ ถ้าไม่มีใช้ shootTransform เดิม
    private Transform Muzzle => currentMuzzle != null ? currentMuzzle : shootTransform;

    // ฝั่ง Server: ใช้ตำแหน่งที่ Client ส่งมา ถ้าไกลจากตัวผู้เล่นผิดปกติให้ใช้ shootTransform แทน
    private Vector3 ValidateMuzzle(Vector3 muzzlePos)
    {
        Vector3 rootPos = NetworkObject.transform.position;
        if ((muzzlePos - rootPos).sqrMagnitude > maxMuzzleDistance * maxMuzzleDistance)
            return shootTransform.position;
        return muzzlePos;
    }

    // ฝั่ง Server: มีกำแพง/วัตถุคั่นระหว่างลำตัวผู้เล่นกับปากกระบอกหรือไม่
    private bool IsMuzzleBlocked(Vector3 origin)
    {
        Vector3 body = NetworkObject.transform.position + Vector3.up * bodyCheckHeight;
        Vector3 d = origin - body;
        float dist = d.magnitude;
        if (dist < 0.01f) return false;

        RaycastHit[] hits = Physics.RaycastAll(
            body, d / dist, dist, ~0, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit h in hits)
        {
            if (h.collider.GetComponentInParent<NetworkObject>() == NetworkObject) continue;   // ตัวเราเอง
            if (h.collider.GetComponentInParent<PlayerHealth>() != null) continue;             // ผู้เล่นอีกคน ไม่นับเป็นกำแพง
            return true;
        }

        return false;
    }

    // หมุน Weapon Holder ให้ชี้ไปทางที่เล็ง (ทำหลัง Animator เพื่อไม่ให้ถูกทับ)
    private void LateUpdate()
    {
        if (!aimHolderFollowsCamera || weaponHolder == null) return;

        Transform root = NetworkObject.transform;

        if (IsOwner)
        {
            // ใช้ทิศมองของกล้องโดยตรง (ไม่เอาจุดที่ชนมาคำนวณ) ปืนจะนิ่ง ไม่กระตุกตอนก้มหรือชนกำแพง
            Camera cam = aimCamera != null ? aimCamera : Camera.main;
            Vector3 camDir = cam != null ? cam.transform.forward : root.forward;
            Vector3 localDir = root.InverseTransformDirection(camDir).normalized;

            // หมุนตามแบบนุ่มๆ กันสะดุด
            smoothedLocalAimDir = Vector3.Slerp(
                smoothedLocalAimDir, localDir, Mathf.Clamp01(ownerAimSmoothSpeed * Time.deltaTime));

            // ส่งให้เครื่องอื่นเป็นช่วงๆ และเฉพาะเมื่อทิศเปลี่ยนจริง ลดปริมาณข้อมูล
            if (Time.time >= nextAimSyncTime &&
                (localDir - lastSentAimDir).sqrMagnitude > 0.0001f)
            {
                netLocalAimDir.Value = localDir;
                lastSentAimDir = localDir;
                nextAimSyncTime = Time.time + aimSyncInterval;
            }
        }
        else
        {
            // ผู้เล่นคนอื่น: ค่อยๆ หมุนตามทิศที่ได้รับ ให้ดูนุ่ม
            smoothedLocalAimDir = Vector3.Slerp(
                smoothedLocalAimDir, netLocalAimDir.Value, aimSmoothSpeed * Time.deltaTime);
        }

        if (smoothedLocalAimDir.sqrMagnitude < 0.0001f) return;

        weaponHolder.rotation = root.rotation * Quaternion.LookRotation(smoothedLocalAimDir, Vector3.up);
    }

    // ======================= ฝั่ง Owner (รับ Input) =======================

    private void Update()
    {
        if (!IsOwner) return;

        // เมาส์ไม่ล็อก (เปิดเมนูอยู่ / กด Esc) ไม่รับคำสั่งยิง
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            spinTimer = 0f;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha1)) RequestSwitchSlotServerRpc(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) RequestSwitchSlotServerRpc(1);
        if (Input.GetKeyDown(reloadKey)) RequestReloadServerRpc();

        WeaponData w = Current;
        if (w == null) return;

        HandleScope(w);
        HandleFire(w);
    }

    private void HandleFire(WeaponData w)
    {
        bool holding = Input.GetKey(KeyCode.Mouse0);

        // Gatling: ต้องกดค้างให้ลำกล้องหมุนเต็มที่ก่อนถึงจะเริ่มยิง
        if (w.spinUpTime > 0f)
        {
            if (holding) spinTimer += Time.deltaTime;
            else spinTimer = Mathf.Max(0f, spinTimer - Time.deltaTime * 2f);
        }
        else
        {
            spinTimer = 0f;
        }

        bool wantsFire = w.automatic ? holding : Input.GetKeyDown(KeyCode.Mouse0);
        if (!wantsFire) return;
        if (spinTimer < w.spinUpTime) return;
        if (Time.time < nextFireTimeClient) return;
        if (!CanShootClient) return;

        nextFireTimeClient = Time.time + w.fireInterval;
        FireServerRpc(GetAimDirection(), Muzzle.position);
    }

    // เล็งซูม (กดเมาส์ขวาค้าง) เฉพาะปืนที่ตั้ง Zoom Fov ไว้ เช่น AWM
    private void HandleScope(WeaponData w)
    {
        Camera cam = aimCamera != null ? aimCamera : Camera.main;
        if (cam == null) return;

        if (defaultFov < 0f) defaultFov = cam.fieldOfView;

        if (w.zoomFov > 0f)
        {
            bool zoom = Input.GetMouseButton(1) && !isReloading.Value;
            float target = zoom ? w.zoomFov : defaultFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, 15f * Time.deltaTime);
            scopeActive = true;
        }
        else if (scopeActive)
        {
            // เปลี่ยนไปปืนที่ไม่มีซูม ค่อยๆ คืน FOV เดิม
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, defaultFov, 15f * Time.deltaTime);
            if (Mathf.Abs(cam.fieldOfView - defaultFov) < 0.1f)
            {
                cam.fieldOfView = defaultFov;
                scopeActive = false;
            }
        }
    }

    // ทิศกระสุน: จากปากกระบอก ไปยังจุดที่กลางจอ (Crosshair) ชี้อยู่
    private Vector3 GetAimDirection()
    {
        Camera cam = aimCamera != null ? aimCamera : Camera.main;
        if (cam == null) return shootTransform.forward;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, aimMaxDistance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() == NetworkObject) continue;

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
            }
        }

        // เป้าที่ใกล้เกินไปทำให้ทิศกระสุนไม่นิ่ง จึงใช้ระยะจุดเล็งอย่างน้อย aimMinDistance
        float dist = nearest == float.MaxValue ? aimMaxDistance : nearest;
        dist = Mathf.Clamp(dist, aimMinDistance, aimMaxDistance);
        Vector3 targetPoint = ray.GetPoint(dist);

        Vector3 dir = targetPoint - Muzzle.position;
        if (dir.sqrMagnitude < 0.01f || Vector3.Dot(dir, ray.direction) < 0f)
            return ray.direction;

        return dir.normalized;
    }

    // ให้ UI เลือกปืนหลักเรียก (Server จะตรวจสิทธิ์อีกครั้ง)
    public void RequestSelectPrimary(int weaponId)
    {
        if (!IsOwner) return;
        SelectPrimaryServerRpc(weaponId);
    }

    // ======================= ฝั่ง Server =======================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void FireServerRpc(Vector3 aimDirection, Vector3 muzzlePos)
    {
        WeaponData w = Current;
        if (w == null) return;
        if (health != null && health.IsDead) return;
        if (gm != null && gm.IsGameOver) return;
        if (gm != null && !gm.MatchStarted) return;   // ยังไม่เริ่มเกม ห้ามยิง
        if (isReloading.Value) return;

        // กันยิงถี่เกินอัตราของปืนนั้น (เผื่อเน็ตกระตุก 10%)
        if (Time.time - lastShotTimeServer < w.fireInterval * 0.9f) return;

        if (CurrentAmmo <= 0)
        {
            if (autoReloadWhenEmpty) StartReloadServer();
            return;
        }

        ConsumeAmmoServer();
        lastShotTimeServer = Time.time;

        if (aimDirection.sqrMagnitude < 0.0001f) aimDirection = shootTransform.forward;
        aimDirection.Normalize();

        Vector3 origin = ValidateMuzzle(muzzlePos);

        // ยืนชิดกำแพง ปากกระบอกอาจยื่นทะลุไปอีกฝั่ง ทำให้กระสุนเกิดหลังกำแพงแล้วยิงทะลุ
        // ถ้าระหว่างตัวผู้เล่นกับปากกระบอกมีสิ่งกีดขวางคั่นอยู่ นัดนี้ถือว่าชนกำแพงทันที (เสียกระสุนแต่ไม่เกิดกระสุน)
        if (IsMuzzleBlocked(origin)) return;

        if (w.fireType == WeaponFireType.Raycast)
            FireRaycastServer(w, aimDirection, origin);
        else
            FireProjectileServer(w, aimDirection, origin);

        if (CurrentAmmo <= 0 && autoReloadWhenEmpty)
            StartReloadServer();
    }

    private void FireProjectileServer(WeaponData w, Vector3 aim, Vector3 origin)
    {
        if (w.projectilePrefab == null)
        {
            Debug.LogWarning($"{w.displayName}: ยังไม่ได้ใส่ Projectile Prefab");
            return;
        }

        Vector3 dir = ApplySpread(aim, w.spreadAngle);
        GameObject go = Instantiate(
            w.projectilePrefab,
            origin,
            Quaternion.LookRotation(dir, Vector3.up)
        );

        var proj = go.GetComponent<MoveProjectile>();
        proj.shooterClientId = OwnerClientId;
        proj.damage = w.damage;
        proj.speedOverride = w.projectileSpeed;

        go.GetComponent<NetworkObject>().Spawn(true);
    }

    // Raycast (เช่น AWM): โดนทันที ตัวแรกที่ชน (ที่ไม่ใช่ตัวเอง) คือเป้าหมาย
    private void FireRaycastServer(WeaponData w, Vector3 aim, Vector3 origin)
    {
        Vector3 dir = ApplySpread(aim, w.spreadAngle);

        RaycastHit[] hits = Physics.RaycastAll(
            origin, dir, w.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() == NetworkObject) continue;

            var target = hit.collider.GetComponentInParent<PlayerHealth>();
            if (target != null)
                target.ApplyDamageServer(w.damage, OwnerClientId);

            break; // ชนอะไรก็ตามที่ไม่ใช่ตัวเอง กระสุนหยุดตรงนั้น
        }
    }

    private static Vector3 ApplySpread(Vector3 dir, float angle)
    {
        if (angle <= 0f) return dir;

        Quaternion spread = Quaternion.Euler(
            Random.Range(-angle, angle),
            Random.Range(-angle, angle),
            0f
        );
        return (Quaternion.LookRotation(dir, Vector3.up) * spread) * Vector3.forward;
    }

    private void ConsumeAmmoServer()
    {
        if (currentSlot.Value == 0) primaryAmmo.Value = Mathf.Max(0, primaryAmmo.Value - 1);
        else secondaryAmmo.Value = Mathf.Max(0, secondaryAmmo.Value - 1);
    }

    // ---------- รีโหลด ----------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestReloadServerRpc()
    {
        StartReloadServer();
    }

    private void StartReloadServer()
    {
        if (!IsServer) return;

        WeaponData w = Current;
        if (w == null) return;
        if (health != null && health.IsDead) return;
        if (isReloading.Value) return;
        if (CurrentAmmo >= w.magazineSize) return;

        reloadRoutine = StartCoroutine(ReloadCoroutine());
    }

    private IEnumerator ReloadCoroutine()
    {
        WeaponData w = Current;
        int slot = currentSlot.Value;

        isReloading.Value = true;
        yield return new WaitForSeconds(w.reloadTime);

        if (slot == 0) primaryAmmo.Value = w.magazineSize;
        else secondaryAmmo.Value = w.magazineSize;

        isReloading.Value = false;
        reloadRoutine = null;
    }

    private void CancelReloadServer()
    {
        if (reloadRoutine != null)
        {
            StopCoroutine(reloadRoutine);
            reloadRoutine = null;
        }
        isReloading.Value = false;
    }

    // ---------- สลับปืน / เลือกปืน ----------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestSwitchSlotServerRpc(int slot)
    {
        if (slot != 0 && slot != 1) return;
        if (slot == currentSlot.Value) return;
        if (health != null && health.IsDead) return;

        CancelReloadServer();
        currentSlot.Value = slot;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SelectPrimaryServerRpc(int weaponId)
    {
        WeaponData w = GetWeapon(weaponId);
        if (w == null || w.slot != WeaponSlot.Primary) return;
        if (weaponId == primaryId.Value) return;

        // Server ตรวจสิทธิ์: ต้องเป็นช่วงรอ/นับถอยหลัง และอยู่ในโซนเขียวของตัวเองเท่านั้น
        if (!CanChangeLoadout)
            return;

        CancelReloadServer();
        primaryId.Value = weaponId;
        primaryAmmo.Value = w.magazineSize;
        currentSlot.Value = 0;
    }

    private void FillAllAmmoServer()
    {
        WeaponData p = GetWeapon(primaryId.Value);
        WeaponData s = GetWeapon(secondaryId.Value);
        primaryAmmo.Value = p != null ? p.magazineSize : 0;
        secondaryAmmo.Value = s != null ? s.magazineSize : 0;
    }

    // เรียกจาก PlayerHealth.RespawnServer: กระสุนเต็มทุกกระบอก ยกเลิกรีโหลดที่ค้างอยู่
    public void ResetAmmoServer()
    {
        if (!IsServer) return;

        CancelReloadServer();
        FillAllAmmoServer();
    }
}