using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

// เก็บ Session ที่กำลังเล่นอยู่ เพื่อให้ออกจากห้อง (หรือลบห้องถ้าเป็น Host) ตอนจบเกมหรือหลุดการเชื่อมต่อ
// ถ้าไม่ออกจากห้อง ห้องจะค้าง และรอบถัดไปคนอื่นเข้าห้องนั้นไม่ได้
public static class SessionHolder
{
    public static ISession Current;

    public static async Task LeaveAsync()
    {
        ISession session = Current;
        Current = null;

        if (session == null) return;

        try
        {
            if (session.IsHost)
                await session.AsHost().DeleteAsync();   // Host: ลบห้องทิ้ง
            else
                await session.LeaveAsync();             // Client: ออกจากห้อง
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Session] ออกจากห้องไม่สำเร็จ: {e.Message}");
        }
    }
}