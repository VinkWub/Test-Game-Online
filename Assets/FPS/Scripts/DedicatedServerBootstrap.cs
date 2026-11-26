using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

// วางไว้ในฉาก Lobby (ฉากแรกของ Build) ที่ GameObject ว่างๆ สักอัน
// ทำงานเฉพาะในบิลด์ Dedicated Server (Linux Server / Windows Server) ที่มี define UNITY_SERVER
// บิลด์ปกติ (Editor / WebGL / Desktop) สคริปต์นี้ไม่ทำอะไร
//
// หน้าที่: เปิด Server แล้วโหลดฉากเกมรอผู้เล่นทั้งสองเข้ามา
// พอจบแมตช์ GameManager จะปิด NetworkManager แล้วโหลดฉาก Lobby กลับมา สคริปต์นี้ก็เริ่มทำงานรอบใหม่เอง
public class DedicatedServerBootstrap : MonoBehaviour
{
    [Tooltip("พอร์ตที่ Server ฟังอยู่ (ถ้าใส่ -port 9000 ตอนรันโปรแกรม จะใช้ค่านั้นแทน)")]
    [SerializeField] private ushort port = 7777;

    [Tooltip("ชื่อฉากเกม (ต้องใส่ใน Build Settings และสะกดให้ตรง)")]
    [SerializeField] private string gameSceneName = "Game";

#if UNITY_SERVER
    private IEnumerator Start()
    {
        Application.targetFrameRate = 60;   // บิลด์ headless ไม่มี VSync กัน CPU เต็ม

        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogError("[Server] ไม่พบ NetworkManager ในซีน Lobby");
            yield break;
        }

        // กลับมาจากแมตช์ก่อนหน้า: รอให้การปิดการเชื่อมต่อเสร็จก่อน
        float waited = 0f;
        while ((nm.ShutdownInProgress || nm.IsListening) && waited < 5f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        ushort listenPort = ReadPortFromArgs(port);

        var transport = nm.GetComponent<UnityTransport>();
        transport.UseWebSockets = true;      // เบราว์เซอร์คุยได้แค่ WebSocket
        transport.UseEncryption = false;     // TLS (wss) ให้ Caddy/nginx ทำแทนที่หน้าบ้าน
        transport.SetConnectionData("0.0.0.0", listenPort, "0.0.0.0");

        if (!nm.StartServer())
        {
            Debug.LogError($"[Server] เปิด Server ที่พอร์ต {listenPort} ไม่ได้");
            yield break;
        }

        Debug.Log($"[Server] listening (WebSocket) on port {listenPort}");

        yield return null;   // รอ 1 เฟรมให้ระบบพร้อม

        // Server อยู่ในฉากเกมรอไว้เลย ผู้เล่นที่เชื่อมต่อเข้ามาจะถูกพาเข้าฉากเกมให้อัตโนมัติ
        nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    private static ushort ReadPortFromArgs(ushort fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-port" && ushort.TryParse(args[i + 1], out ushort p))
                return p;
        }
        return fallback;
    }
#endif
}