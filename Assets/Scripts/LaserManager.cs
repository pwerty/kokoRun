using Fusion;
using UnityEngine;

public class LaserManager : NetworkBehaviour
{
    [Header("Laser Prefab")]
    public GameObject laserPrefab; // 하나의 레이저 프리팹
    
    [Header("Spawn Points")]
    public Transform[] laserSpawnPoints = new Transform[7]; // 7개의 Y축 지점
    
    [Header("Timing Settings")]
    public float warningDuration = 1.3f; // 경고 레이저 지속 시간
    public float laserDuration = 1.3f; // 실제 레이저 지속 시간
    public float spawnInterval = 5f; // 레이저 사이클 간격 (경고+레이저+쿨다운)
    
    [Header("Warning Settings")]
    public Color warningColor = Color.red; // 경고 레이저 색상
    public Color normalColor = Color.white; // 실제 레이저 색상
    
    // 네트워크 동기화
    [Networked] public float NextLaserTime { get; set; }
    [Networked] public bool IsWarningActive { get; set; }
    [Networked] public int CurrentLaserPoint { get; set; }
    
    private static LaserManager _instance;
    public static LaserManager Instance => _instance;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            NextLaserTime = (float)Runner.SimulationTime + 2f; // 게임 시작 후 2초 대기
            IsWarningActive = false;
            CurrentLaserPoint = -1;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        
        // 게임이 시작되고 끝나지 않았을 때만 레이저 시스템 작동
        if (RaceGameManager.IsGameStarted() && !RaceGameManager.IsGameEnded())
        {
            if (Runner.SimulationTime >= NextLaserTime && !IsWarningActive)
            {
                StartLaserSequence();
            }
        }
    }

    private void StartLaserSequence()
    {
        // 랜덤한 스폰 포인트 선택
        CurrentLaserPoint = Random.Range(0, laserSpawnPoints.Length);
        
        if (CurrentLaserPoint >= 0 && CurrentLaserPoint < laserSpawnPoints.Length)
        {
            StartCoroutine(LaserSequenceCoroutine());
        }
    }

    private System.Collections.IEnumerator LaserSequenceCoroutine()
    {
        IsWarningActive = true;
        Transform spawnPoint = laserSpawnPoints[CurrentLaserPoint];
        
        // 1단계: 경고 레이저 스폰
        var warningLaser = Runner.Spawn(laserPrefab, spawnPoint.position, Quaternion.identity);
        var warningComponent = warningLaser.GetComponent<NetworkLaser>();
        
        if (warningComponent != null)
        {
            warningComponent.InitializeAsWarning(warningDuration, warningColor);
        }
        
        Debug.Log($"⚠️ 경고 레이저 생성: 지점 {CurrentLaserPoint}");
        
        // 경고 시간만큼 대기
        yield return new WaitForSeconds(warningDuration);
        
        // 경고 레이저 제거
        if (warningLaser != null )
        {
            Runner.Despawn(warningLaser);
        }
        
        // 2단계: 실제 레이저 스폰
        var actualLaser = Runner.Spawn(laserPrefab, spawnPoint.position, Quaternion.identity);
        var laserComponent = actualLaser.GetComponent<NetworkLaser>();
        
        if (laserComponent != null)
        {
            laserComponent.InitializeAsActual(laserDuration, normalColor);
        }
        
        
        // 레이저 지속 시간만큼 대기
        yield return new WaitForSeconds(laserDuration);
        
        // 실제 레이저는 자동으로 사라짐 (NetworkLaser에서 처리)
        
        // 3단계: 쿨다운 및 다음 레이저 예약
        IsWarningActive = false;
        NextLaserTime = (float)Runner.SimulationTime + spawnInterval;
        
        Debug.Log($"🔄 레이저 사이클 완료, 다음 레이저까지: {spawnInterval}초");
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    // ✅ 디버깅용 - Inspector에서 수동 테스트
    [ContextMenu("Test Warning Laser")]
    private void TestWarningLaser()
    {
        if (Application.isPlaying && Object.HasStateAuthority)
        {
            StartLaserSequence();
        }
    }
}
