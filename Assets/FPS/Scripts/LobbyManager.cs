
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button startButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Scene")]
    [Tooltip("ชื่อฉากเกม ต้องใส่ใน Build Profiles และสะกดให้ตรง")]
    [SerializeField] private string gameSceneName = "Game";

    [Header("Matchmaking")]
    [Tooltip("เวลาค้นหาห้องก่อนสร้างห้องใหม่")]
    [SerializeField] private float matchmakingTimeoutSeconds = 5f;

    [Header("Timeout")]
    [SerializeField] private float networkTimeoutSeconds = 15f;

    private bool isStarting;

    private void Awake()
    {
        // แก้ไข: คืนเมาส์เมื่อเข้าหน้า Lobby
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartClicked);
        }
        else
        {
            Debug.LogError("[Lobby] Start Button ไม่ได้กำหนดใน Inspector");
        }

        SetStatus("");
    }

    private void OnDestroy()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(OnStartClicked);
        }
    }

    private async void OnStartClicked()
    {
        if (isStarting)
            return;

        isStarting = true;

        if (startButton != null)
        {
            startButton.gameObject.SetActive(false);
        }

        SetStatus("Waiting.....");

        await Task.Yield();

        try
        {
            await FindOrCreateRoomAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] เกิดข้อผิดพลาด: {e}");
            await FailAsync("Connection failed. Try again.");
        }
    }

    private async Task FindOrCreateRoomAsync()
    {
        NetworkManager nm = NetworkManager.Singleton;

        if (nm == null)
        {
            await FailAsync("NetworkManager not found.");
            return;
        }

        await WaitUntilStoppedAsync(nm);

        nm = NetworkManager.Singleton;

        if (nm == null)
        {
            await FailAsync("NetworkManager not found.");
            return;
        }

        if (nm.IsListening || nm.ShutdownInProgress)
        {
            await FailAsync("Network is still running. Try again.");
            return;
        }

        if (!ConfigureTransport(nm))
        {
            await FailAsync("Network configuration failed.");
            return;
        }

        SetStatus("Waiting.....");

        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var initOptions = new InitializationOptions()
                .SetProfile(Guid.NewGuid().ToString("N").Substring(0, 30));

            await UnityServices.InitializeAsync(initOptions);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        SetStatus("Waiting.....");

        var quickJoinOptions = new QuickJoinOptions
        {
            Filters = new List<FilterOption>
            {
                new FilterOption(
                    FilterField.AvailableSlots,
                    "1",
                    FilterOperation.GreaterOrEqual
                )
            },

            Timeout = TimeSpan.FromSeconds(
                Mathf.Max(1f, matchmakingTimeoutSeconds)
            ),

            CreateSession = true
        };

        var sessionOptions = new SessionOptions
        {
            MaxPlayers = 2
        }.WithRelayNetwork();

        ISession session =
            await MultiplayerService.Instance.MatchmakeSessionAsync(
                quickJoinOptions,
                sessionOptions
            );

        if (session == null)
        {
            await FailAsync("No session available. Try again.");
            return;
        }

        SessionHolder.Current = session;

        Debug.Log(
            $"[Lobby] Session joined: {session.Id}, " +
            $"Role: {(session.IsHost ? "Host" : "Client")}"
        );

        SetStatus("Waiting.....");

        float elapsed = 0f;

        while (nm != null &&
               !nm.IsListening &&
               elapsed < networkTimeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            await Task.Yield();

            nm = NetworkManager.Singleton;
        }

        nm = NetworkManager.Singleton;

        if (nm == null || !nm.IsListening)
        {
            await FailAsync("Connection failed. Try again.");
            return;
        }

        if (session.IsHost)
        {
            SetStatus("Starting game...");

            if (nm.SceneManager == null)
            {
                await FailAsync("Network Scene Manager not found.");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                await FailAsync(
                    $"Game scene '{gameSceneName}' not found."
                );
                return;
            }

            nm.SceneManager.LoadScene(
                gameSceneName,
                LoadSceneMode.Single
            );
        }
        else
        {
            SetStatus("Waiting.....");
        }
    }

    private bool ConfigureTransport(NetworkManager nm)
    {
        if (nm == null)
        {
            Debug.LogError("[Lobby] NetworkManager not found.");
            return false;
        }

        UnityTransport transport =
            nm.NetworkConfig.NetworkTransport as UnityTransport;

        if (transport == null)
        {
            Debug.LogError(
                "[Lobby] NetworkManager ไม่ได้ใช้ UnityTransport"
            );
            return false;
        }

        transport.UseWebSockets = true;

        Debug.Log(
            $"[Lobby] WebSockets enabled: {transport.UseWebSockets}"
        );

        return true;
    }

    private async Task WaitUntilStoppedAsync(NetworkManager nm)
    {
        float waited = 0f;

        while (nm != null &&
               (nm.ShutdownInProgress || nm.IsListening) &&
               waited < 3f)
        {
            waited += Time.unscaledDeltaTime;
            await Task.Yield();

            nm = NetworkManager.Singleton;
        }
    }

    private async Task FailAsync(string message)
    {
        try
        {
            await SessionHolder.LeaveAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[Lobby] Leave session failed: {e.Message}"
            );
        }

        NetworkManager nm = NetworkManager.Singleton;

        if (nm != null &&
            (nm.IsListening || nm.IsClient || nm.IsServer))
        {
            nm.Shutdown();
        }

        // แก้ไข: กรณีเชื่อมต่อไม่สำเร็จ เมาส์ต้องกลับมาใช้ได้
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetStatus(message);

        if (startButton != null)
        {
            startButton.gameObject.SetActive(true);
        }

        isStarting = false;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
