using Fusion;
using UnityEngine;

public class NetworkLaser : NetworkBehaviour
{
    [Header("Laser Components")]
    public SpriteRenderer spriteRenderer;
    public Collider2D laserCollider;
    
    [Header("Visual Effects")]
    public GameObject hitEffect;
    public AudioClip hitSound;
    
    // 네트워크 동기화
    [Networked] public float LifeTimer { get; set; }
    [Networked] public bool IsDestroyed { get; set; }
    [Networked] public bool IsWarningLaser { get; set; }
    [Networked] public Color LaserColor { get; set; }

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
            
        if (laserCollider == null)
            laserCollider = GetComponent<Collider2D>();
            
        // 레이저는 움직이지 않으므로 Rigidbody2D 불필요
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            IsDestroyed = false;
        }
        
        UpdateLaserAppearance();
    }

    // ✅ 경고 레이저로 초기화
    public void InitializeAsWarning(float duration, Color color)
    {
        if (!Object.HasStateAuthority) return;
        
        IsWarningLaser = true;
        LifeTimer = duration;
        LaserColor = color;
        
        // 경고 레이저는 충돌 없음
        if (laserCollider != null)
            laserCollider.enabled = false;
            
        UpdateLaserAppearance();
        
        Debug.Log($"⚠️ 경고 레이저 초기화: {duration}초");
    }

    // ✅ 실제 레이저로 초기화
    public void InitializeAsActual(float duration, Color color)
    {
        if (!Object.HasStateAuthority) return;
        
        IsWarningLaser = false;
        LifeTimer = duration;
        LaserColor = color;
        
        // 실제 레이저는 충돌 활성화
        if (laserCollider != null)
            laserCollider.enabled = true;
            
        UpdateLaserAppearance();
        
        Debug.Log($"⚡ 실제 레이저 초기화: {duration}초");
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || IsDestroyed) return;
        
        // 수명 체크
        LifeTimer -= Runner.DeltaTime;
        if (LifeTimer <= 0f)
        {
            DestroyLaser();
            return;
        }
    }

    // ✅ 플레이어와 충돌 (실제 레이저만)
    private void OnTriggerEnter2D(Collider2D other)
    {
    
        if (!Object.HasStateAuthority || IsDestroyed || IsWarningLaser) return;
    
        // ✅ 부모까지 포함해서 NetworkPlayer 찾기
        var player = other.GetComponentInParent<NetworkPlayer>();
    
        if (player != null)
        {
            Debug.Log($"✅ NetworkPlayer 발견: {player.PlayerName}");
            player.ApplyStun();
            PlayHitEffectRPC(other.transform.position);
        }
       
    }

// ✅ 오브젝트의 전체 경로를 추적하는 헬퍼 메서드
    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;
    
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
    
        return path;
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void PlayHitEffectRPC(Vector3 position)
    {
        // 히트 이펙트 재생
        if (hitEffect != null)
        {
            var effect = Instantiate(hitEffect, position, Quaternion.identity);
            hitEffect.transform.parent = transform;
            hitEffect.SetActive(true);
            Destroy(effect.gameObject, 2f);
        }
        
        // 히트 사운드 재생
        if (hitSound != null)
        {
            AudioSource.PlayClipAtPoint(hitSound, position);
        }
    }

    private void UpdateLaserAppearance()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = LaserColor;
            
            // 레이저 투명도 조정 (선택사항)
            if (IsWarningLaser)
            {
                var color = spriteRenderer.color;
                color.a = 0.7f; // 경고 레이저는 약간 투명하게
                spriteRenderer.color = color;
            }
        }
    }

    public override void Render()
    {
        // 클라이언트에서도 외관 업데이트
        UpdateLaserAppearance();
        
        // 삭제 예정이면 시각적으로 숨기기
        if (IsDestroyed)
        {
            gameObject.SetActive(false);
        }
    }

    private void DestroyLaser()
    {
        if (IsDestroyed) return;
        
        IsDestroyed = true;
        
        // 충돌 비활성화
        if (laserCollider != null)
            laserCollider.enabled = false;
            
        // 즉시 삭제
        if (Object != null && Object.HasStateAuthority)
        {
            Runner.Despawn(Object);
        }
    }
}
