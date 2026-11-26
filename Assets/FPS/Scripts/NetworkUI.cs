using System.Collections;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.UI;
using TMPro;

public class NetworkUI : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private Button joinButton;                 // ปุ่มเดียว ใครกดก่อนเป็น Host ใครกดหลังเป็น Client
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private TextMeshProUGUI statusText;        // ข้อความสถานะ (ไม่ใส่ก็ได้)

    [Header("Connection")]
    [SerializeField] private string address = "127.0.0.1";     // ใช้สำหรับทดสอบ Local ก่อน
    [SerializeField] private ushort port = 7777;
    [Tooltip("เวลารอการเชื่อมต่อต่อ 1 ครั้ง (มิลลิวินาที)")]
    [SerializeField] private int connectTimeoutMs = 1000;
    [SerializeField] private int maxConnectAttempts = 3;

    private NetworkVariable<int> playersNum =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone);

    private bool isJoining;

    private void Awake()
    {
        joinButton.onClick.AddListener(OnJoinClicked);
        SetStatus("");
    }

    private void Start()
    {
        // กลับมาหน้า Lobby ทั้งที่ยังเชื่อมต่ออยู่ (เช่นหลังจบเกม) ไม่ต้องแสดงปุ่มเข้าเกมอีก
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            joinButton.gameObject.SetActive(false);
    }

    private void OnJoinClicked()
    {
        var nm = NetworkManager.Singleton;
        if (isJoining || nm == null || nm.IsListening) return;

        StartCoroutine(JoinRoutine());
    }

    // หลักการ: เช็กว่าพอร์ตเกมถูกใช้อยู่ไหม
    //  - พอร์ตว่าง      = ยังไม่มีใครเป็น Host → เราเป็น Host
    //  - พอร์ตถูกใช้อยู่ = มี Host อยู่แล้ว      → เราเป็น Client
    // วิธีนี้ตัดสินได้ทันที ไม่ต้องรอเชื่อมต่อล้มเหลวก่อน
    private IEnumerator JoinRoutine()
    {
        isJoining = true;
        joinButton.gameObject.SetActive(false); // กดแล้วซ่อนปุ่มทันที กันกดซ้ำ

        var nm = NetworkManager.Singleton;
        var transport = nm.GetComponent<UnityTransport>();

        transport.SetConnectionData(address, port);
        transport.ConnectTimeoutMS = connectTimeoutMs;
        transport.MaxConnectAttempts = maxConnectAttempts;

        bool portFree = IsPortFree();
        Debug.Log($"[Join] พอร์ต {port} ว่าง = {portFree}");

        // ---------- ยังไม่มี Host → เป็น Host ----------
        if (portFree)
        {
            SetStatus("Starting host...");

            bool hostOk = nm.StartHost();
            Debug.Log($"[Join] StartHost = {hostOk}");

            if (hostOk)
            {
                SetStatus("");
                isJoining = false;
                yield break;
            }

            // เริ่ม Host ไม่ได้ (เช่นอีกคนตัดหน้าไปก่อนเสี้ยววินาที) → เคลียร์แล้วลองเป็น Client
            yield return StartCoroutine(ShutdownAndWait(nm));
        }

        // ---------- มี Host อยู่แล้ว → เป็น Client ----------
        SetStatus("Connecting...");
        Debug.Log("[Join] ลองเข้าเป็น Client");

        bool connected = false;
        if (nm.StartClient())
        {
            float timeout = (connectTimeoutMs * maxConnectAttempts) / 1000f + 2f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                elapsed += Time.unscaledDeltaTime;

                if (nm.IsConnectedClient) { connected = true; break; }

                // เชื่อมต่อไม่ติด NetworkManager จะปิดตัวเอง
                if (nm.ShutdownInProgress || (elapsed > 0.3f && !nm.IsListening)) break;

                yield return null;
            }
        }

        Debug.Log($"[Join] ผลการเป็น Client: {(connected ? "สำเร็จ" : "ไม่สำเร็จ")}");

        if (connected)
        {
            SetStatus("");
            isJoining = false;
            yield break;
        }

        // ---------- ล้มเหลว: คืนสถานะให้กดใหม่ได้ ----------
        yield return StartCoroutine(ShutdownAndWait(nm));

        SetStatus("Connection failed. Try again.");
        joinButton.gameObject.SetActive(true);
        isJoining = false;
    }

    // ทดสอบว่าพอร์ต UDP ว่างไหม ด้วยการลอง Bind ชั่วคราวแล้วปล่อยทันที
    private bool IsPortFree()
    {
        try
        {
            IPAddress ip = IPAddress.Any;
            if (IPAddress.TryParse(address, out IPAddress parsed) && IPAddress.IsLoopback(parsed))
                ip = parsed;

            using (var udp = new UdpClient(new IPEndPoint(ip, port)))
            {
                return true;
            }
        }
        catch (SocketException)
        {
            return false; // พอร์ตถูกใช้อยู่ = มี Host แล้ว
        }
    }

    // ปิดการเชื่อมต่อเดิมให้เรียบร้อย (รอสูงสุด 3 วินาที กันค้าง)
    private IEnumerator ShutdownAndWait(NetworkManager nm)
    {
        if (nm.IsListening || nm.IsClient || nm.IsServer)
            nm.Shutdown();

        float waited = 0f;
        while ((nm.ShutdownInProgress || nm.IsListening) && waited < 3f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    private void Update()
    {
        playerCountText.text = "Player: " + playersNum.Value.ToString();

        if (!IsServer) return;

        playersNum.Value = NetworkManager.Singleton.ConnectedClients.Count;
    }
}