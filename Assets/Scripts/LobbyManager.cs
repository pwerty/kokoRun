using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

public class LobbyManager : MonoBehaviour
{
    [Header("Lobby Buttons")] // ← 새로 추가
    public Button createRoomButton;      // 방 만들기
    public Button joinRandomRoomButton;  // 아무 방 들어가기
    
    [Header("Room UI Panels")]
    public GameObject lobbyPanel;      // 로비 UI
    public GameObject roomPanel;       // 방 UI
    
    [Header("Room Info")]
    public Button startGameButton;
    public Button leaveRoomButton;
    
    [Header("Player List")]
    public Transform playerListParent;
    public GameObject playerItemPrefab;

    [Header("Player Slots UI")]
    public GameObject[] playerSlots = new GameObject[4];     // Player1~4 Panel
    public TextMeshProUGUI[] playerNameTexts = new TextMeshProUGUI[4]; // 각 슬롯의 이름 텍스트

    // 플레이어 슬롯 관리
    public Dictionary<PlayerRef, int> playerSlotMap = new Dictionary<PlayerRef, int>(); // 플레이어 → 슬롯 번호
    private bool[] slotOccupied = new bool[4]; // 슬롯 사용 여부
    
    public NetManager netManager;
    private Dictionary<PlayerRef, GameObject> playerUIItems = new Dictionary<PlayerRef, GameObject>();
    
    void Start()
    {
        // 버튼 이벤트 연결
        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameClicked);
        
        if (leaveRoomButton != null)
            leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
        
        // ✅ 로비 버튼들 이벤트 연결 추가
        if (createRoomButton != null)
            createRoomButton.onClick.AddListener(OnCreateRoomClicked);
        
        if (joinRandomRoomButton != null)
            joinRandomRoomButton.onClick.AddListener(OnJoinRandomRoomClicked);
        
        // NetManager 이벤트 구독
        if (netManager != null)
        {
            netManager.OnPlayerJoinedRoom += OnPlayerJoinedRoom;
            netManager.OnPlayerLeftRoom += OnPlayerLeftRoom;
            netManager.OnRoomCreated += OnRoomCreated;
        }
        
        // 초기 상태: 방 UI 숨김
        ShowRoomUI(false);
	 InitializePlayerSlots();
    }

    // ✅ 플레이어 슬롯 초기화
    private void InitializePlayerSlots()
    {
        for (int i = 0; i < playerSlots.Length; i++)
        {
            SetPlayerSlot(i, "", false, false); // 빈 슬롯으로 설정
            slotOccupied[i] = false;
        }
        
        Debug.Log("🎮 플레이어 슬롯 초기화 완료");
    }
    
    // ✅ 모든 UI 숨기기 메서드 추가
    public void HideAllUI()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (roomPanel != null) roomPanel.SetActive(false);
        
        // 플레이어 목록도 정리
    ClearAllPlayerSlots();
        
        Debug.Log("🚫 모든 로비 UI 숨김 처리");
    }

// ✅ 모든 플레이어 슬롯 정리
private void ClearAllPlayerSlots()
{
    playerSlotMap.Clear();
    
    for (int i = 0; i < slotOccupied.Length; i++)
    {
        slotOccupied[i] = false;
        SetPlayerSlot(i, "", false, false);
    }
    
    Debug.Log("🧹 플레이어 슬롯 모두 정리 완료");
}
    
    // ✅ 방 만들기 버튼 클릭
    private void OnCreateRoomClicked()
    {
        string roomName;
//        roomName = $"Room_{UnityEngine.Random.Range(1000, 9999)}";
        roomName = "Room_1000";
        
        Debug.Log($"🎮 방 생성 요청: {roomName}");
        _ = netManager.CreateNewSession(roomName);
        ShowRoomUI(true);
    }
    
    // ✅ 아무 방 들어가기 버튼 클릭
    private void OnJoinRandomRoomClicked()
    {
        // 첫 번째 사용 가능한 세션에 참가하거나 고정 방에 참가
        string targetRoom = "Room_1000";
        
        Debug.Log($"🚪 방 참가 요청: {targetRoom}");
        _ = netManager.JoinExistingSession(targetRoom);
    }
    
    // ✅ 방 나가기 버튼 수정 (타이틀 씬으로)
    private void OnLeaveRoomClicked()
    {
        if (netManager != null)
        {
            _ = LeaveToTitle();
        }
    }
    

    // ✅ 타이틀 씬으로 완전 복귀
    private async System.Threading.Tasks.Task LeaveToTitle()
    {
        // ✅ 퇴장하기 전에 다른 플레이어들에게 알림
        if (netManager != null && netManager.networkRunner.IsRunning)
        {
        
            // RPC로 다른 플레이어들에게 퇴장 알림
            netManager.RPC_PlayerLeftRoom(netManager.networkRunner.LocalPlayer);
        
            // 잠시 대기하여 RPC가 전송되도록 함
            await System.Threading.Tasks.Task.Delay(200);
        }
    
        // 세션 종료
        if (netManager != null && netManager.networkRunner.IsRunning)
        {
            await netManager.networkRunner.Shutdown();
        }

        ShowRoomUI(false);
        ClearPlayerList();
        // 타이틀 씬 로드
        UnityEngine.SceneManagement.SceneManager.LoadScene("Title"); // 또는 타이틀 씬
    }

    
    // 방 UI 표시/숨김
    public void ShowRoomUI(bool show)
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(!show);
        if (roomPanel != null) roomPanel.SetActive(show);
    }
    
    // 방 생성 시 호출
    private void OnRoomCreated(string roomName, bool isHost)
    {
        ShowRoomUI(true);
        
        // 방장만 게임 시작 버튼 활성화
        if (startGameButton != null)
            startGameButton.interactable = isHost;
        
        Debug.Log($"✅ 방 UI 활성화: {roomName} ({(isHost ? "방장" : "참가자")})");
    }
    
// 플레이어 입장 시 슬롯 할당
    private void OnPlayerJoinedRoom(PlayerRef player, string playerName)
    {
        Debug.Log($"👤 [LobbyManager] 플레이어 입장: {playerName} (PlayerRef: {player})");
    
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    
        // 게임 씬이 아닐 때만 처리
        if (currentSceneName != "SampleScene")
        {
            // ✅ 중복 체크를 더 엄격하게
            if (!playerSlotMap.ContainsKey(player))
            {
                int availableSlot = FindAvailableSlot();
            
                if (availableSlot != -1)
                {
                    playerSlotMap[player] = availableSlot;
                    slotOccupied[availableSlot] = true;
                
                    bool isLocalPlayer = (player == netManager.networkRunner.LocalPlayer);
                    bool isHost = netManager.networkRunner.IsServer && isLocalPlayer;
                
                    SetPlayerSlot(availableSlot, playerName, isLocalPlayer, isHost);
                
                    Debug.Log($"✅ 새 플레이어 {playerName} → 슬롯 {availableSlot} 할당");
                }
            }
            else
            {
                Debug.Log($"🔄 이미 존재하는 플레이어 무시: {playerName}");
            }
        }
    
        UpdatePlayerCount();
    }

// ✅ 빈 슬롯 찾기
private int FindAvailableSlot()
{
    for (int i = 0; i < slotOccupied.Length; i++)
    {
        if (!slotOccupied[i])
        {
            return i;
        }
    }
    return -1; // 빈 슬롯 없음
}

// ✅ 플레이어 목록 갱신 전에 기존 데이터 완전 초기화
public void RefreshPlayerList()
{
    Debug.Log("🧹 플레이어 목록 완전 초기화 시작");
    
    // 기존 슬롯 맵과 UI 완전 초기화
    playerSlotMap.Clear();
    
    for (int i = 0; i < slotOccupied.Length; i++)
    {
        slotOccupied[i] = false;
        SetPlayerSlot(i, "", false, false); // 빈 슬롯으로 설정
    }
    
    Debug.Log("✅ 플레이어 목록 초기화 완료");
}

// ✅ 플레이어 슬롯 UI 설정
private void SetPlayerSlot(int slotIndex, string playerName, bool isLocalPlayer, bool isHost)
{
    if (slotIndex < 0 || slotIndex >= playerSlots.Length) return;
    
    // 슬롯 활성화/비활성화
    if (playerSlots[slotIndex] != null)
    {
        playerSlots[slotIndex].SetActive(!string.IsNullOrEmpty(playerName));
    }
    
    // 플레이어 이름 설정
    if (playerNameTexts[slotIndex] != null)
    {
        if (string.IsNullOrEmpty(playerName))
        {
            playerNameTexts[slotIndex].text = "빈 슬롯";
            playerNameTexts[slotIndex].color = Color.gray;
        }
        else
        {
            string displayName = playerName;
            
            // 로컬 플레이어 표시
            if (isLocalPlayer)
            {
                displayName += " (나)";
                playerNameTexts[slotIndex].color = Color.green;
            }
            // 방장 표시
            else if (isHost)
            {
                displayName += " (방장)";
                playerNameTexts[slotIndex].color = Color.yellow;
            }
            else
            {
                playerNameTexts[slotIndex].color = Color.white;
            }
            
            playerNameTexts[slotIndex].text = displayName;
        }
    }
    
    // 상태 아이콘 설정 (선택사항)

}

    
    // 플레이어 퇴장 시 슬롯 해제
private void OnPlayerLeftRoom(PlayerRef player)
{
    Debug.Log($"👋 플레이어 퇴장: PlayerRef {player}");
    
    if (playerSlotMap.TryGetValue(player, out int slotIndex))
    {
        // 슬롯 해제
        slotOccupied[slotIndex] = false;
        playerSlotMap.Remove(player);
        
        // UI 업데이트 (빈 슬롯으로)
        SetPlayerSlot(slotIndex, "", false, false);
        
        Debug.Log($"✅ 슬롯 {slotIndex} 해제됨");
    }
    else
    {
        Debug.LogWarning($"⚠️ 퇴장한 플레이어의 슬롯을 찾을 수 없음: {player}");
    }
    
    UpdatePlayerCount();
}

    
    // 플레이어 목록 아이템 생성
    private void CreatePlayerListItem(PlayerRef player, string playerName)
    {
        if (playerItemPrefab != null && playerListParent != null)
        {
            GameObject playerItem = Instantiate(playerItemPrefab, playerListParent);
            
            // 플레이어 이름 표시
            var nameText = playerItem.GetComponentInChildren<TextMeshProUGUI>();
            if (nameText != null)
            {
                nameText.text = playerName;
                
                // 로컬 플레이어 표시
                if (player == netManager.networkRunner.LocalPlayer)
                {
                    nameText.text += " (나)";
                    nameText.color = Color.green;
                }
            }
            
            playerUIItems[player] = playerItem;
        }
    }
    
    // 플레이어 목록 아이템 제거
    private void RemovePlayerListItem(PlayerRef player)
    {
        if (playerUIItems.TryGetValue(player, out GameObject item))
        {
            Destroy(item);
            playerUIItems.Remove(player);
        }
    }
    
// 플레이어 수 업데이트
private void UpdatePlayerCount()
{
    int currentPlayerCount = playerSlotMap.Count;
    
  /*  if (roomStatusText != null)
    {
        string baseStatus = netManager.networkRunner.IsServer ? "방장" : "참가자";
        roomStatusText.text = $"{baseStatus} ({currentPlayerCount}/4)";
    }*/
    
    Debug.Log($"📊 현재 플레이어 수: {currentPlayerCount}/4");
}

    
    // 게임 시작 버튼 클릭
    private void OnStartGameClicked()
    {
            if (netManager != null && netManager.networkRunner.IsServer)
            {
                // ✅ 새로 만든 메서드 호출
                netManager.StartGameFromRoom();
            }
        
    }
    
    // 플레이어 목록 초기화
    private void ClearPlayerList()
    {
        foreach (var item in playerUIItems.Values)
        {
            if (item != null) Destroy(item);
        }
        playerUIItems.Clear();
    }
    
    private void OnDestroy()
    {
        // 이벤트 구독 해제
        if (netManager != null)
        {
            netManager.OnPlayerJoinedRoom -= OnPlayerJoinedRoom;
            netManager.OnPlayerLeftRoom -= OnPlayerLeftRoom;
            netManager.OnRoomCreated -= OnRoomCreated;
        }
        
        HideAllUI();
    }
    
    public void OnDebugSyncClicked()
    {
        Debug.Log("🔧 수동 동기화 요청");
    
        if (netManager != null)
        {
            if (netManager.networkRunner.IsServer)
            {
                netManager.RPC_SyncAllPlayers();
            }
            else
            {
                netManager.RPC_RequestPlayerSync();
            }
        }
    }
}
