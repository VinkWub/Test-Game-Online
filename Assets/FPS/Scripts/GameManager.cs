
using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

// สถานะของแมตช์: รอผู้เล่น → นับถอยหลัง → เริ่มเล่น
public enum MatchState { Waiting = 0, Countdown = 1, Playing = 2 }

public class GameManager : NetworkBehaviour
{
    public NetworkList<NetworkObjectReference> allPlayers = new();

    [Header("Scene / Lobby Settings")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private float returnToLobbyDelay = 10f;

    [Header("Match Start Settings")]
    [SerializeField] private int playersToStart = 2;
    [SerializeField] private float startCountdown = 10f;

    [Header("Kill / Win Settings")]
    [SerializeField] private int killsToWin = 5;

    [Header("Player Respawn Settings")]
    [SerializeField] private float playerRespawnDelay = 5f;

    [Header("HP Box Settings")]
    public GameObject hpBox;
    [SerializeField] private Transform hpBoxSpawnPoint;
    [SerializeField] private float respawnDelay = 5f;

    private NetworkObject currentHPBox;
    private bool isWaitingForRespawn = false;
    private bool isRoundResetting = false;

    private NetworkVariable<bool> gameOver = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<ulong> winnerClientId = new NetworkVariable<ulong>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsGameOver => gameOver.Value;

    private NetworkVariable<int> matchState = new NetworkVariable<int>(
        (int)MatchState.Waiting,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<double> countdownEndTime = new NetworkVariable<double>(
        0.0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public MatchState State => (MatchState)matchState.Value;
    public bool MatchStarted => matchState.Value == (int)MatchState.Playing;

    private NetworkVariable<int> roundNumber = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int RoundNumber => roundNumber.Value;

    private NetworkVariable<bool> returningToLobby = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<double> lobbyReturnEndTime = new NetworkVariable<double>(
        0.0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsReturningToLobby => returningToLobby.Value;

    public float LobbyCountdownRemaining =>
        Mathf.Max(0f, (float)(lobbyReturnEndTime.Value - NetworkManager.ServerTime.Time));

    public float CountdownRemaining =>
        Mathf.Max(0f, (float)(countdownEndTime.Value - NetworkManager.ServerTime.Time));

    public int KillsToWin => killsToWin;

    public bool LocalPlayerWon =>
        NetworkManager.Singleton != null &&
        winnerClientId.Value == NetworkManager.Singleton.LocalClientId;

    private bool leavingToLobby;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SpawnHPBox();

            NetworkManager.SceneManager.OnLoadEventCompleted += OnGameSceneLoaded;
            NetworkManager.SceneManager.OnSynchronizeComplete += OnClientSynchronized;

            if (IsHost)
                StartCoroutine(SpawnHostPlayerRoutine());
        }
    }

    private IEnumerator SpawnHostPlayerRoutine()
    {
        yield return null;
        yield return null;
        SpawnPlayerIfMissing(NetworkManager.LocalClientId);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (NetworkManager != null && NetworkManager.SceneManager != null)
            {
                NetworkManager.SceneManager.OnLoadEventCompleted -= OnGameSceneLoaded;
                NetworkManager.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
            }
        }
        else if (!leavingToLobby)
        {
            leavingToLobby = true;
            _ = SessionHolder.LeaveAsync();
            SceneManager.LoadScene(lobbySceneName);
        }
    }

    private void OnGameSceneLoaded(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;

        foreach (ulong clientId in clientsCompleted)
        {
            SpawnPlayerIfMissing(clientId);
        }
    }

    private void OnClientSynchronized(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[Game] Client {clientId} ซิงก์ฉากเสร็จ");

        SpawnPlayerIfMissing(clientId);
    }

    private void SpawnPlayerIfMissing(ulong clientId)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("GameManager: ยังไม่ได้ใส่ Player Prefab ในช่อง Player Prefab");
            return;
        }

        if (NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId) != null)
            return;

        Debug.Log($"[Game] สร้างตัวละครให้ Client {clientId}");

        GameObject go = Instantiate(playerPrefab);
        go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;

        int count = NetworkManager.ConnectedClients.Count;

        switch (State)
        {
            case MatchState.Waiting:
                if (count >= playersToStart)
                {
                    countdownEndTime.Value =
                        NetworkManager.ServerTime.Time + startCountdown;

                    matchState.Value = (int)MatchState.Countdown;
                }
                break;

            case MatchState.Countdown:
                if (count < playersToStart)
                {
                    matchState.Value = (int)MatchState.Waiting;
                }
                else if (NetworkManager.ServerTime.Time >= countdownEndTime.Value)
                {
                    StartMatch();
                }
                break;
        }
    }

    private void StartMatch()
    {
        matchState.Value = (int)MatchState.Playing;
    }

    private void RespawnAllPlayers()
    {
        foreach (NetworkObjectReference playerRef in allPlayers)
        {
            if (playerRef.TryGet(out NetworkObject playerObj, NetworkManager.Singleton))
            {
                var playerHealth = playerObj.GetComponent<PlayerHealth>();

                if (playerHealth != null)
                    playerHealth.RespawnServer();
            }
        }
    }

    private void SpawnHPBox()
    {
        if (!IsServer || hpBox == null || hpBoxSpawnPoint == null)
            return;

        if (currentHPBox != null)
            return;

        GameObject box = Instantiate(
            hpBox,
            hpBoxSpawnPoint.position,
            hpBoxSpawnPoint.rotation
        );

        currentHPBox = box.GetComponent<NetworkObject>();

        if (currentHPBox == null)
        {
            Debug.LogError("HP Box ต้องมี NetworkObject!");
            Destroy(box);
            return;
        }

        currentHPBox.Spawn(true);
    }

    public void OnHPBoxCollected()
    {
        if (!IsServer || currentHPBox == null || isWaitingForRespawn)
            return;

        currentHPBox.Despawn(true);
        currentHPBox = null;

        StartCoroutine(RespawnHPBoxCoroutine());
    }

    private IEnumerator RespawnHPBoxCoroutine()
    {
        isWaitingForRespawn = true;

        yield return new WaitForSeconds(respawnDelay);

        isWaitingForRespawn = false;
        SpawnHPBox();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddPlayerServerRpc(NetworkObjectReference playerRef)
    {
        if (!allPlayers.Contains(playerRef))
        {
            allPlayers.Add(playerRef);
            print("allPlayer");
        }
    }

    public void HandlePlayerDeath(NetworkObjectReference victimRef, ulong killerClientId)
    {
        if (!IsServer || gameOver.Value || !MatchStarted)
            return;

        if (victimRef.TryGet(out NetworkObject victimObj, NetworkManager.Singleton)
            && victimObj.OwnerClientId != killerClientId)
        {
            bool hasWinner = AddKill(killerClientId);

            if (hasWinner)
                return;
        }

        if (isRoundResetting)
            return;

        StartCoroutine(RoundResetCoroutine());
    }

    private bool AddKill(ulong killerClientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(killerClientId, out var client)
            || client.PlayerObject == null)
            return false;

        var killer = client.PlayerObject.GetComponent<PlayerHealth>();

        if (killer == null)
            return false;

        killer.AddKillServer();
        print($"Client {killerClientId} Kill = {killer.Kills}/{killsToWin}");

        if (killer.Kills >= killsToWin)
        {
            winnerClientId.Value = killerClientId;
            gameOver.Value = true;

            print($"Client {killerClientId} ชนะ!");

            StartCoroutine(ReturnToLobbyCoroutine());
            return true;
        }

        return false;
    }

    private IEnumerator ReturnToLobbyCoroutine()
    {
        lobbyReturnEndTime.Value =
            NetworkManager.ServerTime.Time + returnToLobbyDelay;

        returningToLobby.Value = true;

        yield return new WaitForSeconds(returnToLobbyDelay);

        ReturnToLobbyClientRpc();

        yield return new WaitForSeconds(0.5f);

        LeaveToLobby();
    }

    [Rpc(SendTo.NotServer)]
    private void ReturnToLobbyClientRpc()
    {
        LeaveToLobby();
    }

    private void LeaveToLobby()
    {
        if (leavingToLobby)
            return;

        leavingToLobby = true;

        // แก้ไข: คืนเมาส์ก่อนออกจากฉากเกม
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        _ = SessionHolder.LeaveAsync();

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(lobbySceneName);
    }

    private IEnumerator RoundResetCoroutine()
    {
        isRoundResetting = true;

        yield return new WaitForSeconds(playerRespawnDelay);

        if (!gameOver.Value)
        {
            RespawnAllPlayers();

            roundNumber.Value += 1;

            countdownEndTime.Value =
                NetworkManager.ServerTime.Time + startCountdown;

            matchState.Value = (int)MatchState.Countdown;
        }

        isRoundResetting = false;
    }
}
