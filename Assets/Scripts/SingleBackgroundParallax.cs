using UnityEngine;
using Unity.Cinemachine;

public class SingleBackgroundParallax : MonoBehaviour
{
    [Header("Settings")]
    public float parallaxSpeed = 0.3f; // 배경 이동 속도 (0~1)
    
    private CinemachineCamera cinemachineCamera;
    private Vector3 startPosition;
    private Vector3 lastCameraPosition;
    
    private void Start()
    {
        startPosition = transform.position;
        
        // ✅ 시네머신 카메라 찾기
        var netManager = FindObjectOfType<NetManager>();
        if (netManager?.followCamera != null)
        {
            cinemachineCamera = netManager.followCamera;
            lastCameraPosition = cinemachineCamera.transform.position;
        }
    }
    
    private void LateUpdate()
    {
        if (cinemachineCamera == null) return;
        
        // ✅ 카메라 이동량에 따른 배경 이동
        Vector3 cameraPos = cinemachineCamera.transform.position;
        Vector3 parallaxOffset = (cameraPos - lastCameraPosition) * parallaxSpeed;
        
        transform.position += parallaxOffset;
        lastCameraPosition = cameraPos;
    }
}