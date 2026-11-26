using TMPro;
using Unity.Netcode;
using UnityEngine;

// วางไว้ใน Scene (เช่น ที่ Canvas) ไม่ต้องใส่ที่ Player Prefab
public class GameUI : MonoBehaviour
{
    [Header("Texts (TextMeshPro)")]
    [SerializeField] private TMP_Text myKillText;       // Kill ที่เราทำได้
    [SerializeField] private TMP_Text enemyKillText;    // Kill ที่ศัตรูทำได้
    [SerializeField] private TMP_Text targetKillText;   // จำนวน Kill ที่ต้องทำให้ถึงเพื่อชนะ
    [SerializeField] private TMP_Text ammoText;         // กระสุนที่เหลือ
    [SerializeField] private TMP_Text resultText;       // ข้อความแพ้/ชนะ กลางจอ
    [SerializeField] private TMP_Text countdownText;    // ข้อความรอผู้เล่น / นับถอยหลังก่อนเริ่มเกม
    [SerializeField] private TMP_Text roundText;        // แสดงรอบที่เท่าไหร่ (ไม่ใส่ก็ได้)
    [SerializeField] private TMP_Text returnText;       // นับถอยหลังกลับ Lobby หลังจบเกม (ไม่ใส่ก็ได้)

    // {0} = จำนวน Kill ปัจจุบัน, {1} = จำนวน Kill ที่ต้องทำให้ถึง (ใส่หรือไม่ใส่ {1} ก็ได้)
    [Header("Messages")]
    [SerializeField] private string myKillFormat = "You: {0}";
    [SerializeField] private string enemyKillFormat = "Enemy: {0}";
    [SerializeField] private string targetKillFormat = "Kills to win: {0}";
    [SerializeField] private string ammoFormat = "{2}   {0} / {1}";   // {0} = เหลือ, {1} = ขนาดแมกกาซีน, {2} = ชื่อปืน
    [SerializeField] private string reloadingMessage = "Reloading...";
    [SerializeField] private string winMessage = "YOU WIN!";
    [SerializeField] private string loseMessage = "YOU LOSE";
    [SerializeField] private string waitingMessage = "Waiting for opponent...";
    [SerializeField] private string countdownFormat = "Game starts in {0}";   // {0} = วินาทีที่เหลือ
    [SerializeField] private string goMessage = "GO!";
    [SerializeField] private string roundFormat = "Round {0}";   // {0} = รอบที่
    [SerializeField] private string returnFormat = "Back to lobby in {0}";   // {0} = วินาทีที่เหลือ
    [SerializeField] private float goDuration = 1.5f;

    private GameManager gm;
    private MatchState lastState = MatchState.Waiting;
    private float goUntil;

    private void Start()
    {
        if (resultText != null) resultText.text = "";
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        if (gm == null)
        {
            gm = FindFirstObjectByType<GameManager>();
            if (gm == null) return;
        }

        if (!gm.IsSpawned) return;

        // หา Kill ของเรา (Owner) และของศัตรู
        int myKills = 0;
        int enemyKills = 0;
        PlayerWeapon myAmmo = null;

        foreach (NetworkObjectReference playerRef in gm.allPlayers)
        {
            if (!playerRef.TryGet(out NetworkObject obj, NetworkManager.Singleton)) continue;

            var ph = obj.GetComponent<PlayerHealth>();
            if (ph == null) continue;

            if (obj.IsOwner)
            {
                myKills = ph.Kills;
                myAmmo = obj.GetComponent<PlayerWeapon>();
            }
            else
            {
                enemyKills = ph.Kills;
            }
        }

        if (myKillText != null)
            myKillText.text = string.Format(myKillFormat, myKills, gm.KillsToWin);

        if (enemyKillText != null)
            enemyKillText.text = string.Format(enemyKillFormat, enemyKills, gm.KillsToWin);

        if (targetKillText != null)
            targetKillText.text = string.Format(targetKillFormat, gm.KillsToWin);

        if (ammoText != null && myAmmo != null)
        {
            ammoText.text = myAmmo.IsReloading
                ? reloadingMessage
                : string.Format(ammoFormat, myAmmo.CurrentAmmo, myAmmo.CurrentMagazineSize, myAmmo.CurrentWeaponName);
        }

        // รอผู้เล่น / นับถอยหลัง / GO!
        MatchState state = gm.State;
        if (state == MatchState.Playing && lastState != MatchState.Playing)
            goUntil = Time.time + goDuration;
        lastState = state;

        if (roundText != null)
            roundText.text = string.Format(roundFormat, gm.RoundNumber);

        if (returnText != null)
        {
            returnText.text = gm.IsReturningToLobby
                ? string.Format(returnFormat, Mathf.CeilToInt(gm.LobbyCountdownRemaining))
                : "";
        }

        if (countdownText != null)
        {
            if (state == MatchState.Waiting)
                countdownText.text = waitingMessage;
            else if (state == MatchState.Countdown)
                countdownText.text = string.Format(countdownFormat, Mathf.CeilToInt(gm.CountdownRemaining));
            else
                countdownText.text = Time.time < goUntil ? goMessage : "";
        }

        if (resultText != null)
        {
            if (gm.IsGameOver)
                resultText.text = gm.LocalPlayerWon ? winMessage : loseMessage;
            else
                resultText.text = "";
        }
    }
}