using UnityEngine;

// วางไว้ที่ฐานของผู้เล่นแต่ละคน แล้วตั้ง slot ใน Inspector
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("0 = ผู้เล่นฝั่งแรก, 1 = ผู้เล่นฝั่งที่สอง")]
    public int slot = 0;
}