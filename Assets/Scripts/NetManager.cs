using Fusion;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;
using Fusion.Sockets;
using TMPro;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine.SceneManagement;


public struct PlayerInputData : INetworkInput
{
    public bool jumpPressed;
    public bool dashPressed;
    public bool slideHeld;
}

public class NetManager : MonoBehaviour, INetworkRunnerCallbacks
{private int lastPlayerCount = 0;
    private RoomPlayerManager roomPlayerManager;
    public enum NetworkState
    {
        Disconnected,
        ConnectingToLobby,
        InLobby,
        CreatingSession,
        JoiningSession,
        InGameSession,
        Reconnecting
    }
    
    [Header("UI Events")] // ← 이 부분을 추가
    public System.Action<string, bool> OnRoomCreated;           // 방 생성됨 (방이름, 방장여부)
    public System.Action<PlayerRef, string> OnPlayerJoinedRoom; // 플레이어 입장
    public System.Action<PlayerRef> OnPlayerLeftRoom;           // 플레이어 
    
    [Header("Session Management")]
    public NetworkState currentState = NetworkState.Disconnected;
    private string targetSessionName;

// 상태 변경 메서드
    private void ChangeNetworkState(NetworkState newState)
    {
        NetworkState previousState = currentState;
        currentState = newState;
        Debug.Log($"Network State: {previousState} → {newState}");
    
        // UI 업데이트 이벤트 발생
        OnNetworkStateChanged?.Invoke(currentState);
    }

    public System.Action<NetworkState> OnNetworkStateChanged;
    
    [Header("Network Settings")]
    public NetworkRunner networkRunner;
    
    [Header("Game Settings")]
    public bool autoStartGame = false;
    public GameMode gameMode = GameMode.AutoHostOrClient; // ✅ Client → Host로 변경!
    
    [Header("Player Settings")]
    public NetworkPrefabRef playerPrefab; 
    
    [Header("Managers")]
    public RaceGameManager gameManager;
    
    // ✅ Cinemachine 카메라 참조 추가
    [Header("Camera")]
    public CinemachineCamera followCamera; // 또는 CinemachineVirtualCamera (버전에 따라)

    [Header("Game Settings")]
    public string fixedRoomName = "MainRoom"; // ✅ 고정 방 이름 추가
    
    [Header("UI")]
    public TextMeshProUGUI HostorClient; // 호스트인지

    public TextMeshProUGUI FPS;
    
    
    // ✅ State Authority 체크 추가
    private Dictionary<PlayerRef, NetworkObject> connectedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    
    
    private bool jumpInputThisFrame = false;
    private bool dashInputThisFrame = false;
    private bool slideInputHeld = false;

    private async void Start()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnUnitySceneLoaded;
        // ✅ Global Config 확인
        if (NetworkProjectConfig.Global == null)
        {
            Debug.LogError("❌ Network Project Config가 없습니다!");
            return;
        }

        networkRunner = this.GetComponent<NetworkRunner>();

        if (networkRunner == null)
            networkRunner = FindObjectOfType<NetworkRunner>();
        
        if (networkRunner != null)
        {
            networkRunner.AddCallbacks(this);
            
            if (autoStartGame)
            {
                await StartNetworkGame();
            }
        }
        
        // 기존 코드 이후에 추가
        DontDestroyOnLoad(this.gameObject); // NetworkManager 자체를 DontDestroy로 설정
    
        if (networkRunner != null)
        {
            DontDestroyOnLoad(networkRunner.gameObject); // NetworkRunner도 별도로 설정
        }
        
        // ✅ RoomPlayerManager 찾기
        roomPlayerManager = FindObjectOfType<RoomPlayerManager>();
        if (roomPlayerManager != null)
        {
            roomPlayerManager.OnPlayerListChanged += (player, name) =>
            {
                OnPlayerJoinedRoom?.Invoke(player, name);
            };
        }
    }
    
    // 세션 생성/참가 시 플레이어 추가
    private void AddPlayerToRoom(PlayerRef player, string playerName)
    {
        if (roomPlayerManager != null)
        {
            roomPlayerManager.RPC_AddPlayer(player, playerName);
        }
    }
    
    private void OnUnitySceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Debug.Log($"🎬 Unity 씬 로드 완료: {scene.name}");
        
        // 게임 씬인지 확인
        if (scene.name == "SampleScene")
        {
            Debug.Log("🎮 게임 씬 감지 - 설정 시작");
            
            // 약간의 딜레이 후 설정 (씬이 완전히 초기화되도록)
            StartCoroutine(SetupGameSceneDelayed());
        }
    }
    
    // ✅ 딜레이를 두고 게임 씬 설정
    private System.Collections.IEnumerator SetupGameSceneDelayed()
    {
        yield return new WaitForSeconds(0.2f); // 씬 초기화 대기
        
        Debug.Log("🔧 게임 씬 설정 시작");
        
        RefreshSceneComponents();
        
        if (networkRunner.IsServer)
        {
            SpawnAllActivePlayersInGameScene(networkRunner);
        }
        CleanupLobbyUI();
        Debug.Log("✅ 게임 씬 설정 완료");
    }
    
    // ✅ 현재 세션의 모든 플레이어 스폰
    private void SpawnAllActivePlayersInGameScene(NetworkRunner runner)
    {
        Debug.Log($"👥 세션 내 활성 플레이어 수: {connectedPlayers.Count}");
    
        foreach (var player in runner.ActivePlayers)
        {
            // 이미 스폰된 플레이어는 건너뛰기
            if (connectedPlayers.ContainsKey(player))
            {
                Debug.Log($"👤 플레이어 {player} 이미 스폰됨 - 건너뛰기");
                continue;
            }
        
            // 새로운 플레이어 스폰
            Vector3 spawnPosition = GetSpawnPosition(player);
            NetworkObject playerObject = runner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
        
            connectedPlayers[player] = playerObject;
            SetupSpawnedPlayer(playerObject, player);
        
            Debug.Log($"🎯 플레이어 {player} 게임 씬에서 스폰 완료 (위치: {spawnPosition})");
        }
    }
    
    // 로비 연결 메서드 추가
    public async System.Threading.Tasks.Task ConnectToLobby()
    {
        ChangeNetworkState(NetworkState.ConnectingToLobby);
    
        var result = await networkRunner.JoinSessionLobby(SessionLobby.Shared, "MainLobby");
    
        if (result.Ok)
        {
            ChangeNetworkState(NetworkState.InLobby);
            Debug.Log("✅ 로비 접속 성공");
        }
        else
        {
            ChangeNetworkState(NetworkState.Disconnected);
            Debug.LogError($"❌ 로비 접속 실패: {result.ShutdownReason}");
        }
    }
    
// ✅ RpcTargets.All을 사용하여 방장도 자신의 RPC를 받도록 수정
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SyncAllPlayers()
    {
        Debug.Log($"🔄 모든 플레이어 동기화 RPC 전송 중... (방장도 수신)");
    
        if (networkRunner != null && networkRunner.IsRunning)
        {
            foreach (var player in networkRunner.ActivePlayers)
            {
                string playerName = $"Player_{player.PlayerId}";
                bool isHost = networkRunner.IsServer && (player == networkRunner.LocalPlayer);
            
                Debug.Log($"🔔 동기화: {playerName} (Host: {isHost})");
            
                // ✅ 개별 RPC 대신 로컬 이벤트 직접 호출 (중복 방지)
                OnPlayerJoinedRoom?.Invoke(player, playerName);
            }
        }
    }


// ✅ 기존 RPC 메서드 개선
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_PlayerJoinedRoom(PlayerRef player, string playerName, bool isHost)
    {
        Debug.Log($"🔔 RPC_PlayerJoinedRoom 수신: {playerName} (PlayerRef: {player}, Host: {isHost})");
        Debug.Log($"🔔 로컬 플레이어: {networkRunner?.LocalPlayer}, 수신된 플레이어: {player}");
    
        // 강제로 모든 플레이어 정보를 UI에 전달 (자기 자신 포함)
        OnPlayerJoinedRoom?.Invoke(player, playerName);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_PlayerLeftRoom(PlayerRef player)
    {
        Debug.Log($"🔔 RPC_PlayerLeftRoom 수신: {player}");
        OnPlayerLeftRoom?.Invoke(player);
    }


    
// ✅ 중복 호출 방지를 위한 플래그 추가
private bool isSyncing = false;

public async System.Threading.Tasks.Task CreateNewSession(string sessionName, int maxPlayers = 4)
{
    ChangeNetworkState(NetworkState.CreatingSession);
    targetSessionName = sessionName;

    var result = await networkRunner.StartGame(new StartGameArgs()
    {
        GameMode = GameMode.Host,
        SessionName = sessionName,
        PlayerCount = maxPlayers,
        SessionProperties = new Dictionary<string, SessionProperty>
        {
            ["GameType"] = "MainGame",
            ["CreatedAt"] = DateTime.Now.ToString()
        }
    });

    if (result.Ok)
    {
        ChangeNetworkState(NetworkState.InGameSession);
        OnRoomCreated?.Invoke(sessionName, true);
        
        // ✅ 방장 생성 후 단일 동기화
        await System.Threading.Tasks.Task.Delay(500);
        
        Debug.Log($"🎯 방장으로서 플레이어 동기화 시작");
        SyncPlayersOnce();
        
        Debug.Log($"✅ 방 생성 성공 - 로비에서 대기 중");
    }
    else
    {
        ChangeNetworkState(NetworkState.InLobby);
        Debug.LogError($"❌ 세션 생성 실패: {result.ShutdownReason}");
    }
}

public async System.Threading.Tasks.Task JoinExistingSession(string sessionName)
{
    ChangeNetworkState(NetworkState.JoiningSession);
    targetSessionName = sessionName;

    var result = await networkRunner.StartGame(new StartGameArgs()
    {
        GameMode = GameMode.Client,
        SessionName = sessionName
    });

    if (result.Ok)
    {
        ChangeNetworkState(NetworkState.InGameSession);
        OnRoomCreated?.Invoke(sessionName, false);
        
        // ✅ 클라이언트 참가 후 동기화 요청
        await System.Threading.Tasks.Task.Delay(700);
        
        Debug.Log($"🎯 참가자로서 동기화 요청");
        RequestSyncOnce();
        
        Debug.Log($"✅ 방 참가 성공 - 로비에서 대기 중");
    }
    else
    {
        ChangeNetworkState(NetworkState.InLobby);
        Debug.LogError($"❌ 세션 참가 실패: {result.ShutdownReason}");
    }
}

// ✅ 중복 방지 동기화 메서드
private void SyncPlayersOnce()
{
    if (isSyncing) 
    {
        Debug.Log("⚠️ 이미 동기화 중 - 건너뛰기");
        return;
    }
    
    isSyncing = true;
    
    // LobbyManager에서 기존 목록 초기화 요청
    var lobbyManager = FindObjectOfType<LobbyManager>();
    if (lobbyManager != null)
    {
        lobbyManager.RefreshPlayerList();
    }
    
    // 현재 모든 플레이어 정보 전송
    if (networkRunner != null && networkRunner.IsRunning)
    {
        foreach (var player in networkRunner.ActivePlayers)
        {
            string playerName = $"Player_{player.PlayerId}";
            Debug.Log($"🔔 플레이어 동기화: {playerName}");
            OnPlayerJoinedRoom?.Invoke(player, playerName);
        }
    }
    
    // 동기화 완료 후 플래그 해제
    StartCoroutine(ResetSyncFlag());
}

private System.Collections.IEnumerator ResetSyncFlag()
{
    yield return new WaitForSeconds(1f);
    isSyncing = false;
    Debug.Log("🔓 동기화 플래그 해제");
}

private void RequestSyncOnce()
{
    if (isSyncing) return;
    
    isSyncing = true;
    
    // 클라이언트에서 방장에게 동기화 요청
    RPC_RequestPlayerSync();
    
    StartCoroutine(ResetSyncFlag());
}

[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
public void RPC_RequestPlayerSync()
{
    Debug.Log("🔄 클라이언트로부터 플레이어 동기화 요청 받음");
    
    if (!isSyncing)
    {
        SyncPlayersOnce();
    }
}




    
    // 방장이 "게임 시작" 버튼을 눌렀을 때 호출되는 메서드
    public void StartGameFromRoom()
    {
        if (networkRunner != null && networkRunner.IsServer)
        {
            Debug.Log("🎮 게임 시작! 게임 씬으로 전환");
            // 이제 여기서 씬 전환
            LoadSceneByIndex(1);
        }
        else
        {
            Debug.LogWarning("⚠️ 방장만 게임을 시작할 수 있습니다");
        }
    }
    
// ✅ 로비 UI 정리 메서드
    private void CleanupLobbyUI()
    {
        // LobbyManager 찾아서 UI 정리 요청
        var lobbyManager = FindObjectOfType<LobbyManager>();
        if (lobbyManager != null)
        {
            lobbyManager.HideAllUI();
        }
    
        Debug.Log("🧹 로비 UI 정리 완료");
    }
    
// ✅ 현재 세션의 모든 플레이어를 UI에 반영
    private void RefreshAllPlayersInSession()
    {
        if (networkRunner != null && networkRunner.IsRunning)
        {
            Debug.Log($"🔄 세션 내 활성 플레이어 UI 갱신");
        
            foreach (var player in networkRunner.ActivePlayers)
            {
                string playerName = $"Player_{player.PlayerId}";
                Debug.Log($"🔔 플레이어 UI 추가: {playerName}");
                OnPlayerJoinedRoom?.Invoke(player, playerName);
            }
        }
    }
    
    

    



    private async System.Threading.Tasks.Task StartNetworkGame()
    {
        
        // ✅ VSync 끄기 + FPS 제한 해제
        QualitySettings.vSyncCount = 0;          // VSync 끄기
        Application.targetFrameRate = -1;        // FPS 제한 해제
        
        var result = await networkRunner.StartGame(new StartGameArgs()
        {
            GameMode = gameMode,
            //SessionName = $"TestRoom_{UnityEngine.Random.Range(1000, 9999)}", // ✅ 랜덤 룸명
            SessionName = fixedRoomName, // ✅ 고정 방 이름 사용
            Scene = SceneRef.FromIndex(1),
            PlayerCount = 4,
            SceneManager = networkRunner.GetComponent<NetworkSceneManagerDefault>()
        });

 //       HostorClient.text = networkRunner.IsServer.ToString();
        
        if (result.Ok)
        {
            Debug.Log($"✅ 네트워크 세션 시작 성공!");
        }
        else
        {
            Debug.LogError($"❌ 네트워크 세션 시작 실패: {result.ShutdownReason}");
        }
    }
    
    // ✅ 스폰 위치 계산 (플레이어별 다른 위치)
    private Vector3 GetSpawnPosition(PlayerRef player)
    {
        // 플레이어 ID에 따라 다른 스폰 위치 반환
        int playerId = player.PlayerId;
        float offset = playerId * 2.0f;
        return new Vector3(offset, 0, 0);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"플레이어 {player} 접속됨");

        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // 게임 씬에서만 플레이어 스폰
        if (runner.IsServer && currentSceneName == "SampleScene")
        {
            Vector3 spawnPosition = GetSpawnPosition(player);
            NetworkObject playerObject = runner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
    
            connectedPlayers[player] = playerObject;
            SetupSpawnedPlayer(playerObject, player);
    
            Debug.Log($"🏠 게임 씬에서 플레이어 스폰 완료");
        }
        else
        {
            // ✅ 로비에서 플레이어 입장 시 RPC로 모든 클라이언트에 알림
            if (currentSceneName != "SampleScene")
            {
                string playerName = $"Player_{player.PlayerId}";
                bool isHost = runner.IsServer && (player == runner.LocalPlayer);
            
                Debug.Log($"🔔 플레이어 입장 RPC 전송: {playerName}");
                RPC_PlayerJoinedRoom(player, playerName, isHost);
            }
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"플레이어 {player} 퇴장");
    
        if (connectedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            if (playerObject != null)
                runner.Despawn(playerObject);
        
            connectedPlayers.Remove(player);
            Debug.Log($"현재 플레이어 수: {connectedPlayers.Count}");
        }
    
        // ✅ 퇴장도 RPC로 모든 클라이언트에 알림
        Debug.Log($"🔔 플레이어 퇴장 RPC 전송: {player}");
        RPC_PlayerLeftRoom(player);
    }


    
    
    
    private void Update()
    {
        // ✅ 간단한 FPS 계산 (매 프레임 업데이트)
        if (FPS != null)
        {
            float fps = 1.0f / Time.unscaledDeltaTime;
            FPS.text = $"FPS: {fps:F0}";
        }
        
        // 점프 입력 감지 (한 번 눌리면 다음 네트워크 틱까지 유지)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpInputThisFrame = true;
        }

        // 대시 입력 감지
        if (Input.GetKeyDown(KeyCode.C))
        {
            dashInputThisFrame = true;
        }

        // 슬라이드는 GetKey로 계속 체크
        slideInputHeld = Input.GetKey(KeyCode.X);
        
        if (networkRunner != null && networkRunner.IsRunning && 
            currentState == NetworkState.InGameSession)
        {
            int currentPlayerCount = networkRunner.ActivePlayers.Count();
        
            if (currentPlayerCount != lastPlayerCount)
            {
                Debug.Log($"🔄 플레이어 수 변화 감지: {lastPlayerCount} → {currentPlayerCount}");
            
                // 플레이어 수 증가 = 새로운 참가자
                if (currentPlayerCount > lastPlayerCount)
                {
                    Debug.Log("📈 새 플레이어 입장 감지 - 동기화 실행");
                    SyncPlayersOnce();
                }
                // 플레이어 수 감소 = 퇴장자 발생
                else if (currentPlayerCount < lastPlayerCount)
                {
                    Debug.Log("📉 플레이어 퇴장 감지 - UI 정리");
                    CleanupInvalidPlayers();
                }
            
                lastPlayerCount = currentPlayerCount;
            }
        }
    }
    
    // ✅ 유효하지 않은 플레이어 UI에서 제거
// ✅ LINQ 없이 구현한 버전
    private void CleanupInvalidPlayers()
    {
        var lobbyManager = FindObjectOfType<LobbyManager>();
        if (lobbyManager != null && networkRunner != null)
        {
            // 현재 활성 플레이어 목록 생성
            var validPlayers = new List<PlayerRef>();
            foreach (var player in networkRunner.ActivePlayers)
            {
                validPlayers.Add(player);
            }
        
            // UI에 있지만 실제로는 없는 플레이어들 찾기
            var invalidPlayers = new List<PlayerRef>();
            foreach (var kvp in lobbyManager.playerSlotMap)
            {
                PlayerRef uiPlayer = kvp.Key;
                bool isValid = false;
            
                // 유효한 플레이어 목록에 있는지 확인
                foreach (var validPlayer in validPlayers)
                {
                    if (uiPlayer == validPlayer)
                    {
                        isValid = true;
                        break;
                    }
                }
            
                if (!isValid)
                {
                    invalidPlayers.Add(uiPlayer);
                }
            }
        
            // 유효하지 않은 플레이어들 UI에서 제거
            foreach (var invalidPlayer in invalidPlayers)
            {
                Debug.Log($"🧹 유효하지 않은 플레이어 UI 제거: {invalidPlayer}");
                OnPlayerLeftRoom?.Invoke(invalidPlayer);
            }
        
            if (invalidPlayers.Count > 0)
            {
                Debug.Log($"✅ {invalidPlayers.Count}명의 퇴장한 플레이어 UI 정리 완료");
            }
        }
    }


    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new PlayerInputData
        {
            jumpPressed = jumpInputThisFrame,   // ✅ 캐시된 입력 사용
            dashPressed = dashInputThisFrame,   // ✅ 캐시된 입력 사용
            slideHeld = slideInputHeld          // ✅ 실시간 입력 사용
        };
        
        input.Set(data);

        // ✅ 입력 플래그 리셋 (한 번 전송 후 초기화)
        jumpInputThisFrame = false;
        dashInputThisFrame = false;

    #if UNITY_EDITOR
           
    #endif
        }


    private void SetupSpawnedPlayer(NetworkObject playerObject, PlayerRef player)
    {
        var networkPlayer = playerObject.GetComponent<NetworkPlayer>();
    
        // ✅ 로컬 플레이어만 처리
        if (player == networkRunner.LocalPlayer)
        {
            // GameManager에 등록
            if (gameManager != null)
            {
                gameManager.LocalPlayer = networkPlayer;
                Debug.Log($"✅ 로컬 플레이어 등록 완료");
            }
        
            // ✅ Cinemachine 카메라 타겟 설정
            SetupCameraTarget(playerObject.transform);
        }
    }
    
    private void SetupCameraTarget(Transform playerTransform)
    {
        if (followCamera != null)
        {
            // ✅ Cinemachine 3.0+ 버전용 (최신)
            followCamera.Follow = playerTransform;
            followCamera.LookAt = playerTransform;
        
            Debug.Log($"🎥 카메라 타겟 설정 완료: {playerTransform.name}");
        }
        else
        {
            // ✅ 카메라가 Inspector에 할당되지 않은 경우 자동 찾기
            followCamera = FindObjectOfType<CinemachineCamera>();
        
            if (followCamera != null)
            {
                followCamera.Follow = playerTransform;
                followCamera.LookAt = playerTransform;
                Debug.Log($"🎥 카메라 자동 찾기 및 타겟 설정 완료");
            }
            else
            {
                Debug.LogWarning("⚠️ Cinemachine 카메라를 찾을 수 없습니다!");
            }
        }
    }
    
    public void SetupLocalPlayerCamera(Transform playerTransform)
    {
        Debug.Log($"🎥 로컬 플레이어 카메라 설정 시도: {playerTransform.name}");
    
        if (gameManager != null)
        {
            var networkPlayer = playerTransform.GetComponent<NetworkPlayer>();
            if (networkPlayer != null)
            {
                gameManager.LocalPlayer = networkPlayer;
                Debug.Log($"✅ 로컬 플레이어 등록 완료");
            }
        }
    
        SetupCameraTarget(playerTransform);
    }


    // ✅ 불필요한 RPC 제거하고 기본 콜백들만 유지
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) 
    {
        Debug.Log("✅ 서버 연결됨");
    
        // ✅ 서버 연결 후 동기화 프로세스 시작
        StartCoroutine(WaitAndSyncPlayers());
    }

    private System.Collections.IEnumerator WaitAndSyncPlayers()
    {
        yield return new WaitForSeconds(1f); // 네트워크 안정화 대기
    
        Debug.Log("🔄 서버 연결 후 플레이어 동기화");
    
        if (networkRunner.IsServer)
        {
            // 방장이면 모든 플레이어 동기화
            RPC_SyncAllPlayers();
        }
        else
        {
            // 클라이언트면 동기화 요청
            RPC_RequestPlayerSync();
        }
    }

    
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) 
    {
        Debug.LogWarning($"❌ 서버 연결 끊김: {reason}");
    }
    
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) 
    {
        Debug.LogError($"❌ 연결 실패: {reason}");
    }
    
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    
    // 세션 목록 콜백 구현 필요
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        Debug.Log($"📋 세션 목록 업데이트: {sessionList.Count}개 세션");
    
        foreach (var session in sessionList)
        {
            Debug.Log($"세션: {session.Name}, 플레이어: {session.PlayerCount}/{session.MaxPlayers}");
        }
    
        // UI 업데이트를 위한 이벤트 발생
        OnSessionListChanged?.Invoke(sessionList);
    }

    public System.Action<List<SessionInfo>> OnSessionListChanged;
    
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey reliableKey, ArraySegment<byte> data) { }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey reliableKey, float progress) { }
    
    
// 씬 전환 메서드 수정
    public void LoadScene(string sceneName)
    {
        Debug.Log("LOAD SCENE");
        if (networkRunner != null && networkRunner.IsServer)
        {
            // SceneRef 생성
            SceneRef sceneRef = SceneRef.FromPath(sceneName);
        
            // LoadScene 사용 (SetActiveScene의 대안)
            networkRunner.LoadScene(sceneRef, LoadSceneMode.Single);
            Debug.Log($"🔄 씬 로딩 시작: {sceneName}");
        }
    }

// 빌드 인덱스로 씬 로딩
    public void LoadSceneByIndex(int sceneIndex)
    {
        if (networkRunner != null && networkRunner.IsServer)
        {
            SceneRef sceneRef = SceneRef.FromIndex(sceneIndex);
            networkRunner.LoadScene(sceneRef, LoadSceneMode.Single);
        }
    }


// 씬 로드 콜백 구현 강화
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("🔄 씬 로딩 시작");
        // 로딩 UI 표시 등
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("✅ 씬 로딩 완료");
        // 게임 상태 초기화 등
        
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Debug.Log($"🎬 현재 씬: {currentSceneName}");
    
        // 게임 씬에서만 플레이어 스폰 및 컴포넌트 재설정
        if (currentSceneName == "SampleScene")
        {
            SetupGameScene();
        }
    }
    
// ✅ 게임 씬 설정 메서드
private void SetupGameScene()
{
    // 새로운 씬에서 컴포넌트들 다시 찾기
    RefreshSceneComponents();
    
    // 모든 플레이어 스폰
    if (networkRunner.IsServer)
    {
        SpawnAllPlayers();
    }
    
    Debug.Log("🎮 게임 씬 설정 완료");
}

// ✅ 씬 컴포넌트 재탐색
private void RefreshSceneComponents()
{
    // GameManager 다시 찾기
    if (gameManager == null)
    {
        gameManager = FindObjectOfType<RaceGameManager>();
        Debug.Log($"🔍 GameManager 재탐색: {(gameManager != null ? "성공" : "실패")}");
    }
    
    // Cinemachine 카메라 다시 찾기
    if (followCamera == null)
    {
        followCamera = FindObjectOfType<CinemachineCamera>();
        Debug.Log($"🎥 카메라 재탐색: {(followCamera != null ? "성공" : "실패")}");
    }
    
    // UI 요소들 다시 찾기 (게임 씬의 UI)
    var gameUI = FindObjectOfType<Canvas>();
    if (gameUI != null)
    {
        // FPS, HostorClient 텍스트 등을 게임 씬에서 다시 찾기
        FPS = gameUI.GetComponentInChildren<TextMeshProUGUI>();
        Debug.Log("🖥️ 게임 UI 요소 재연결");
    }
}

// ✅ 모든 플레이어 스폰
private void SpawnAllPlayers()
{
    foreach (var player in networkRunner.ActivePlayers)
    {
        if (!connectedPlayers.ContainsKey(player))
        {
            Vector3 spawnPosition = GetSpawnPosition(player);
            NetworkObject playerObject = networkRunner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
            
            connectedPlayers[player] = playerObject;
            SetupSpawnedPlayer(playerObject, player);
            
            Debug.Log($"👤 플레이어 {player} 게임 씬에서 스폰 완료");
        }
    }
}

    
    private void OnDestroy()
    {
        
        if (networkRunner != null)
        {
            networkRunner.RemoveCallbacks(this);
        
            // 종료 시 세션 정리
            if (networkRunner.IsRunning)
            {
                _ = networkRunner.Shutdown(); // Fire and forget
            }
        }
    }

    // ✅ 유틸리티 메서드들
    public List<PlayerRef> GetConnectedPlayers() => new List<PlayerRef>(connectedPlayers.Keys);
    public int GetPlayerCount() => connectedPlayers.Count;
}
