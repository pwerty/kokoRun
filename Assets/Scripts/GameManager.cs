using Fusion;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using TMPro;

public class RaceGameManager : NetworkBehaviour
{
    [Header("Game Settings")]
    public float raceStartDelay = 3f;
    
    [Header("UI References")]
    public GameObject leaderboardUI;
    public Transform leaderboardContent;
    public GameObject leaderboardItemPrefab;
    public TextMeshProUGUI countdownText;
    
    [Header("Game Over")]
    public Animator gameOverAnimator;
    
    // 싱글톤 구현
    private static RaceGameManager _instance;
    public static RaceGameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<RaceGameManager>();
                
                if (_instance == null)
                {
                    Debug.LogError("❌ RaceGameManager가 씬에 없습니다!");
                }
            }
            return _instance;
        }
    }
    
    // ✅ 네트워크 동기화에 카운트다운 상태 추가
    [Networked] public bool GameStarted { get; set; }
    [Networked] public bool GameEnded { get; set; }
    [Networked] public float GameStartTime { get; set; }
    [Networked] public int FinishedPlayerCount { get; set; }
    [Networked] public int DeadPlayerCount { get; set; }
    
    // ✅ 카운트다운 상태를 네트워크로 동기화
    [Networked] public NetworkString<_16> CurrentCountdownText { get; set; }
    [Networked] public bool ShowCountdown { get; set; }

    public NetworkPlayer LocalPlayer { get; set; }
    
    private List<NetworkPlayer> allPlayers = new List<NetworkPlayer>();
    private List<PlayerResult> playerResults = new List<PlayerResult>();
    
    [System.Serializable]
    public class PlayerResult
    {
        public NetworkPlayer player;
        public float finishTime;
        public bool isDead;
        public int rank;
        
        public PlayerResult(NetworkPlayer player, float finishTime, bool isDead)
        {
            this.player = player;
            this.finishTime = finishTime;
            this.isDead = isDead;
            this.rank = 0;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("⚠️ RaceGameManager 중복 인스턴스 제거");
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
    }

    public override void Spawned()
    {
        if (_instance == null || _instance == this)
        {
            _instance = this;
        }
        
        if (Object.HasStateAuthority)
        {
            // ✅ 초기 카운트다운 상태 설정
            CurrentCountdownText = "";
            ShowCountdown = false;
            StartCoroutine(StartRaceSequence());
        }
        
        if (leaderboardUI != null)
            leaderboardUI.SetActive(false);
    }

    // ✅ 모든 클라이언트에서 매 프레임 UI 업데이트
    private void Update()
    {
        UpdateCountdownUI();
    }

    private void UpdateCountdownUI()
    {
        if (countdownText != null)
        {
            countdownText.text = CurrentCountdownText.ToString();
            countdownText.gameObject.SetActive(ShowCountdown);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public static bool IsGameStarted()
    {
        return Instance != null && Instance.GameStarted;
    }
    
    public static bool IsGameEnded()
    {
        return Instance != null && Instance.GameEnded;
    }
    
    public static float GetCurrentElapsedTime()
    {
        return Instance?.GetElapsedTime() ?? 0f;
    }

    // ✅ Networked 프로퍼티를 사용한 카운트다운
    private System.Collections.IEnumerator StartRaceSequence()
    {
        yield return new WaitForSeconds(1f);
        
        RefreshPlayerList();
        
        // ✅ 카운트다운 표시 시작
        ShowCountdown = true;
        
        for (int i = 3; i > 0; i--)
        {
            CurrentCountdownText = i.ToString();
            Debug.Log($"🔢 카운트다운: {i}");
            yield return new WaitForSeconds(1f);
        }
        
        CurrentCountdownText = "GO!";
        Debug.Log("🚀 GO!");
        yield return new WaitForSeconds(0.5f);
        
        // ✅ 게임 시작 및 카운트다운 숨기기
        GameStarted = true;
        GameStartTime = (float)Runner.SimulationTime;
        ShowCountdown = false;
        CurrentCountdownText = "";
        
        Debug.Log("🏁 레이스 시작!");
    }

    public void OnPlayerFinished(NetworkPlayer player)
    {
        if (!Object.HasStateAuthority || GameEnded) return;
        
        FinishedPlayerCount++;
        player.Rank = FinishedPlayerCount;
        
        Debug.Log($"🏆 {player.PlayerName} - {FinishedPlayerCount}등 (시간: {player.FinishTime:F2}초)");
        
        CheckGameEnd();
    }

    public void OnPlayerDied(NetworkPlayer player)
    {
        if (!Object.HasStateAuthority || GameEnded) return;
        
        DeadPlayerCount++;
        if (gameOverAnimator != null)
        {
            gameOverAnimator.SetBool("isGameOver", true);
        }
        Debug.Log($"💀 {player.PlayerName} 사망");
        
        CheckGameEnd();
    }

    private void CheckGameEnd()
    {
        RefreshPlayerList();
        int totalPlayers = allPlayers.Count;
        
        if (FinishedPlayerCount + DeadPlayerCount >= totalPlayers)
        {
            EndGame();
        }
    }

    private void EndGame()
    {
        if (GameEnded) return;
        
        GameEnded = true;
        Debug.Log("🏁 게임 종료!");
        
        CalculateFinalRankings();
        ShowLeaderboardRPC();
    }

    private void CalculateFinalRankings()
    {
        RefreshPlayerList();
        
        int nextRank = FinishedPlayerCount + 1;
        
        foreach (var player in allPlayers)
        {
            if (player.IsDead && player.Rank == 0)
            {
                player.Rank = nextRank;
                nextRank++;
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void ShowLeaderboardRPC()
    {
        ShowLeaderboard();
    }

    private void ShowLeaderboard()
    {
        if (leaderboardUI == null) return;
        
        RefreshPlayerList();
        
        foreach (Transform child in leaderboardContent)
        {
            Destroy(child.gameObject);
        }
        
        var sortedPlayers = allPlayers.OrderBy(p => p.Rank).ToList();
        
        foreach (var player in sortedPlayers)
        {
            CreateLeaderboardItem(player);
        }
        
        leaderboardUI.SetActive(true);
    }

    private void CreateLeaderboardItem(NetworkPlayer player)
    {
        if (leaderboardItemPrefab == null || leaderboardContent == null) return;
        
        GameObject item = Instantiate(leaderboardItemPrefab, leaderboardContent);
        
        var rankText = item.transform.Find("RankText")?.GetComponent<TextMeshProUGUI>();
        var nameText = item.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        var timeText = item.transform.Find("TimeText")?.GetComponent<TextMeshProUGUI>();
        
        if (rankText != null)
            rankText.text = $"{player.Rank}위";
            
        if (nameText != null)
            nameText.text = player.PlayerName.ToString();
            
        if (timeText != null)
        {
            if (player.IsDead)
                timeText.text = "사망";
            else if (player.IsFinished)
                timeText.text = $"{player.FinishTime:F2}초";
            else
                timeText.text = "미완주";
        }
    }

    private void RefreshPlayerList()
    {
        allPlayers.Clear();
        allPlayers.AddRange(FindObjectsOfType<NetworkPlayer>());
    }

    public float GetElapsedTime()
    {
        if (!GameStarted) return 0f;
        return (float)Runner.SimulationTime - GameStartTime;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RestartGameRPC()
    {
        if (Object.HasStateAuthority)
        {
            RestartGame();
        }
    }

    private void RestartGame()
    {
        // ✅ 게임 상태 초기화 시 카운트다운도 초기화
        GameStarted = false;
        GameEnded = false;
        FinishedPlayerCount = 0;
        DeadPlayerCount = 0;
        ShowCountdown = false;
        CurrentCountdownText = "";
        
        RefreshPlayerList();
        foreach (var player in allPlayers)
        {
            if (player.Object.HasStateAuthority)
            {
                player.IsDead = false;
                player.IsFinished = false;
                player.FinishTime = 0f;
                player.Rank = 0;
                
                player.transform.position = Vector3.zero;
                player.rb.bodyType = RigidbodyType2D.Dynamic;
                player.rb.linearVelocity = Vector2.zero;
            }
        }
        
        HideLeaderboardRPC();
        StartCoroutine(StartRaceSequence());
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void HideLeaderboardRPC()
    {
        if (leaderboardUI != null)
            leaderboardUI.SetActive(false);
    }
}
