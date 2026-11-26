using UnityEngine;

public enum WeaponSlot { Primary, Secondary }
public enum WeaponFireType { Projectile, Raycast }

// สร้างไฟล์ปืนใน Project: คลิกขวา > Create > Game > Weapon Data
[CreateAssetMenu(menuName = "Game/Weapon Data", fileName = "NewWeapon")]
public class WeaponData : ScriptableObject
{
    [Header("Info")]
    public string displayName = "Weapon";
    public WeaponSlot slot = WeaponSlot.Primary;
    [Tooltip("โมเดลปืนที่จะแสดงในมือ (ไม่ใส่ก็ได้)")]
    public GameObject modelPrefab;
    [Tooltip("ปรับตำแหน่งโมเดลในมือ (Local Position)")]
    public Vector3 modelPositionOffset = Vector3.zero;
    [Tooltip("ปรับการหมุนโมเดล (องศา) ใช้แก้กรณีปืนหันผิดทิศ เช่น ลองใส่ Y = 90 หรือ -90")]
    public Vector3 modelRotationOffset = Vector3.zero;
    [Tooltip("ตัวคูณขนาดโมเดล (1,1,1 = ขนาดเดิมของ Prefab) ถ้าปืนใหญ่/เล็กเกินไปให้ปรับตรงนี้")]
    public Vector3 modelScale = Vector3.one;
    [Tooltip("จุดปากกระบอกในแกนของโมเดลปืน (ใช้เมื่อโมเดลไม่มีลูกชื่อ Muzzle) 0,0,0 = ไม่ใช้")]
    public Vector3 muzzleOffset = Vector3.zero;

    [Header("Fire")]
    public WeaponFireType fireType = WeaponFireType.Projectile;
    [Tooltip("true = กดค้างยิงต่อเนื่อง, false = กดหนึ่งครั้งต่อหนึ่งนัด")]
    public bool automatic = true;
    public int damage = 20;
    [Tooltip("เวลาระหว่างนัด (วินาที)")]
    public float fireInterval = 0.1f;
    [Tooltip("ความคลาดเคลื่อนของกระสุน (องศา) 0 = แม่นเป๊ะ")]
    public float spreadAngle = 1f;

    [Header("Projectile (ใช้เมื่อ Fire Type = Projectile)")]
    public GameObject projectilePrefab;
    [Tooltip("0 = ใช้ความเร็วจาก Prefab กระสุนเอง")]
    public float projectileSpeed = 0f;

    [Header("Raycast (ใช้เมื่อ Fire Type = Raycast)")]
    public float range = 300f;

    [Header("Ammo")]
    public int magazineSize = 30;
    public float reloadTime = 2.5f;

    [Header("Extra")]
    [Tooltip("เวลาหมุนลำกล้องก่อนเริ่มยิง (วินาที) ใช้กับ Gatling, 0 = ยิงได้ทันที")]
    public float spinUpTime = 0f;
    [Tooltip("ค่า FOV ตอนกดเมาส์ขวาเล็งซูม ใช้กับ AWM, 0 = ไม่มีโหมดซูม")]
    public float zoomFov = 0f;
}