using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// วางไว้ใน Scene (เช่น ที่ Canvas) ไม่ต้องใส่ที่ Player Prefab
public class WeaponSelectUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel;                // แผงเมนูเลือกปืน (ปิดไว้ตอนเริ่ม)
    [Tooltip("เรียงตามปืนหลักใน PlayerWeapon: AK, M4A1, Gatling, AWM ...")]
    [SerializeField] private Button[] weaponButtons;
    [SerializeField] private TMP_Text hintText;               // ข้อความ "กด B เพื่อเลือกปืน" (ไม่ใส่ก็ได้)

    [Header("Settings")]
    [SerializeField] private KeyCode toggleKey = KeyCode.B;
    [SerializeField] private string hintMessage = "Press B : Choose weapon";

    private PlayerWeapon myWeapon;
    private bool buttonsReady;
    private bool isOpen;

    private void Start()
    {
        if (panel != null) panel.SetActive(false);
        if (hintText != null) hintText.text = "";
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        // หา PlayerWeapon ของผู้เล่นเครื่องนี้
        if (myWeapon == null)
        {
            var playerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (playerObj != null) myWeapon = playerObj.GetComponent<PlayerWeapon>();
            if (myWeapon == null) return;
        }

        if (!buttonsReady) SetupButtons();

        bool canChange = myWeapon.CanChangeLoadout;

        // ออกจากเขตจุดเกิด (และไม่ได้ตายอยู่) → ปิดเมนูอัตโนมัติ
        if (isOpen && !canChange) Close();

        if (hintText != null)
            hintText.text = (canChange && !isOpen) ? hintMessage : "";

        if (Input.GetKeyDown(toggleKey))
        {
            if (isOpen) Close();
            else if (canChange) Open();
        }
    }

    private void SetupButtons()
    {
        buttonsReady = true;

        var ids = myWeapon.PrimaryWeaponIds;
        for (int i = 0; i < weaponButtons.Length; i++)
        {
            Button btn = weaponButtons[i];
            if (btn == null) continue;

            if (i >= ids.Count)
            {
                btn.gameObject.SetActive(false); // ปุ่มเกินจำนวนปืนหลัก
                continue;
            }

            int weaponId = ids[i];

            var label = btn.GetComponentInChildren<TMP_Text>();
            var data = myWeapon.GetWeaponData(weaponId);
            if (label != null && data != null) label.text = data.displayName;

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                myWeapon.RequestSelectPrimary(weaponId);
                Close();
            });
        }
    }

    private void Open()
    {
        isOpen = true;
        if (panel != null) panel.SetActive(true);

        ClienPlayerMovement.MenuOpen = true;   // กันสคริปต์เดินล็อกเมาส์กลับทันทีที่คลิกปุ่ม
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Close()
    {
        isOpen = false;
        if (panel != null) panel.SetActive(false);

        ClienPlayerMovement.MenuOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}