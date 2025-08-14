using Fusion;
using UnityEngine;

public class NetworkPlayer : NetworkBehaviour
{
    public float moveSpeed = 5f;
    public float jumpForce = 10f;
    public float extraFallGravity = 2.5f;
    public float maxFallSpeed = 15f;

    public float skillDashForce = 15f;
    public float skillDuration = 0.2f;
    public float skillCooldown = 3f;
    
    public float stunDuration = 0.1f;
    
    public Rigidbody2D rb;
    public BoxCollider2D collider;
    public Animator animator;
    public NetworkMecanimAnimator netAnimator;
    
    private int groundContactCount = 0;

    // ✅ 기존 네트워크 프로퍼티들
    [Networked] public bool IsDead { get; set; }
    [Networked] public bool IsUsingSkill { get; set; }
    [Networked] public float SkillTimer { get; set; }
    [Networked] public bool IsGrounded { get; set; }
    [Networked] public int JumpCount { get; set; }
    [Networked] public bool IsSliding { get; set; }
    [Networked] public float SkillCooldownTimer { get;set; }
    
    //경직 상태 동기화
    [Networked] public bool IsStunned { get;set; }
    [Networked] public float StunTimer { get;set; }

    // ✅ 완주 시스템 추가
    [Networked] public bool IsFinished { get; set; }
    [Networked] public float FinishTime { get; set; }
    [Networked] public int Rank { get; set; }
    [Networked] public NetworkString<_16> PlayerName { get; set; } // 플레이어 이름
    
    [Networked] public Vector2 AuthorityPosition { get; set; }
    [Networked] public Vector2 AuthorityVelocity { get; set; }
    
    private Vector2 lastServerPosition;
    private float lastReconcileTime;
    private bool isInputAuthority => Object.HasInputAuthority;

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (collider == null) collider = GetComponent<BoxCollider2D>();
        if (animator == null) animator = GetComponent<Animator>();
        if (netAnimator == null) netAnimator = GetComponent<NetworkMecanimAnimator>();

        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.None;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 1f;
    }

    public override void Spawned()
    {
        if (Object.InputAuthority == Runner.LocalPlayer)
        {
            var netManager = FindObjectOfType<NetManager>();
            if (netManager != null)
            {
                netManager.SetupLocalPlayerCamera(transform);
            }
        }

        if (Object.HasStateAuthority)
        {
            IsGrounded = false;
            JumpCount = 0;
            IsSliding = false;
            IsUsingSkill = false;
            SkillCooldownTimer = 0f;
            IsStunned = false;
            StunTimer = 0f;
            IsFinished = false;
            FinishTime = 0f;
            Rank = 0;
            PlayerName = $"Player_{Object.InputAuthority}"; // 기본 이름
            AuthorityPosition = transform.position;
            AuthorityVelocity = Vector2.zero;
        }

        if (animator != null && netAnimator != null)
        {
            netAnimator.Animator.SetBool("isRunning", true);
        }
        
        lastServerPosition = transform.position;
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority)
        {
            HandleStunTimer();
            bool canMove = RaceGameManager.IsGameStarted() && !IsFinished && !IsDead && !IsStunned;
            
            // ✅ 완주 또는 사망 상태가 아닐 때만 입력 처리
            if (canMove)
            {
                if (GetInput(out PlayerInputData input))
                {
                    HandleInput(input);
                }
                UpdateNetworkMovement();
            }else if (!IsFinished && !IsDead)
            {
                StopMovement();
            }
            
            AuthorityPosition = transform.position;
            AuthorityVelocity = rb.linearVelocity;
        }
    }

    private void HandleStunTimer()
    {
        if (IsStunned)
        {
            StunTimer -= Runner.DeltaTime;
        
            if (StunTimer <= 0f)
            {
                IsStunned = false;
            
                if (animator != null)
                {
                    animator.SetBool("isDamaged", false);
                }
            
                Debug.Log($"✅ {PlayerName} 경직 해제됨!");
            }
        }
    }

    private void HandleInput(PlayerInputData input)
    {
        if(IsStunned && IsDead) return;
        
        if (input.jumpPressed && JumpCount < 2 && !IsUsingSkill)
        {
            PerformJump();
        }

        if (input.dashPressed && !IsUsingSkill && SkillCooldownTimer <= 0f)
        {
            PerformDash();
        }
        else if (input.dashPressed && (IsUsingSkill || SkillCooldownTimer > 0f))
        {
            // ✅ 쿨다운 중이거나 사용 중일 때 시각적 피드백
            Debug.Log($"🚫 스킬 쿨다운 중! 남은 시간: {SkillCooldownTimer:F1}초");
        }
        
        if (input.slideHeld && !IsSliding && IsGrounded)
        {
            StartSlide();
        }
        else if (!input.slideHeld && IsSliding)
        {
            EndSlide();
        }
    }

    private void UpdateNetworkMovement()
    {
        if (IsDead || IsFinished) return; // ✅ 완주 시에도 움직임 정지
        
        //  게임 시작 여부 다시 한 번 확인
        if (!RaceGameManager.IsGameStarted()) 
        {
            StopMovement();
            return;
        }
        
        if (IsUsingSkill)
        {
            SkillTimer -= Runner.DeltaTime;
            if (SkillTimer <= 0f)
            {
                IsUsingSkill = false;
                netAnimator.Animator.SetBool("isSkill", false);
            
                // ✅ 스킬 종료 시 쿨다운 시작
                SkillCooldownTimer = skillCooldown;
                Debug.Log($"🔄 스킬 쿨다운 시작: {skillCooldown}초");
            }
        }
    
        // ✅ 스킬 쿨다운 처리
        if (SkillCooldownTimer > 0f)
        {
            SkillCooldownTimer -= Runner.DeltaTime;
            if (SkillCooldownTimer <= 0f)
            {
                Debug.Log("✅ 스킬 사용 가능!");
            }
        }

        Vector2 currentVel = rb.linearVelocity;
        float targetSpeedX = IsUsingSkill ? skillDashForce : moveSpeed;
    
        // ✅ 개선된 하강 처리
        if (!IsGrounded)
        {
            if (currentVel.y < 0f)
            {
                // 하강 중일 때 더 강한 중력 적용
                rb.AddForce(Vector2.down * extraFallGravity * rb.mass, ForceMode2D.Force);
            
                // ✅ 최대 하강 속도 제한 (너무 빨라지지 않도록)
                if (currentVel.y < -maxFallSpeed)
                {
                    currentVel.y = -maxFallSpeed;
                }
            }
            else if (currentVel.y > 0f && currentVel.y < 2f)
            {
                // ✅ 점프 정점 근처에서 빠르게 하강 시작
                rb.AddForce(Vector2.down * (extraFallGravity * 0.5f) * rb.mass, ForceMode2D.Force);
            }
        }
    
        rb.linearVelocity = new Vector2(targetSpeedX, currentVel.y);
    }
    
    private void StopMovement()
    {
        if (rb != null)
        {
            Vector2 currentVel = rb.linearVelocity;
            rb.linearVelocity = new Vector2(0f, currentVel.y); // X축 속도만 0으로
        }
    }

    public override void Render()
    {
        if (IsDead)
        {
            HandleDeathAnimation();
            return;
        }

        if (IsFinished)
        {
            HandleFinishAnimation();
            return;
        }

        UpdateAnimations();

        if (isInputAuthority && !Object.HasStateAuthority)
        {
            HandleClientPrediction();
        }
        else if (!isInputAuthority)
        {
            HandleRemotePlayerMovement();
        }
    }

    public void ApplyStun()
    {
        
        if (IsStunned || IsDead || IsFinished) return;

        IsStunned = true;
        animator.SetBool("isDamaged", true);    
        StunTimer = stunDuration;
    }
    
    private void HandleFinishAnimation()
    {
        // ✅ 완주 시 모든 애니메이션 정지
        if (animator != null)
        {
            animator.SetBool("isRunning", false);
            animator.SetBool("isJumping", false);
            animator.SetBool("isFalling", false);
            animator.SetBool("isSliding", false);
            animator.SetBool("isSkill", false);
            // 완주 애니메이션이 있다면
            // animator.SetBool("isFinished", true);
        }

        // ✅ 물리 정지
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }

    // ✅ 완주 처리 메서드
    public void Finish()
    {
        if (IsFinished || IsDead) return;

        Debug.Log($"🏁 플레이어 {PlayerName} 완주!");
        
        IsFinished = true;
        
        // ✅ 싱글톤 사용으로 간단해짐
        if (RaceGameManager.Instance != null)
        {
            FinishTime = RaceGameManager.Instance.GetElapsedTime();
            RaceGameManager.Instance.OnPlayerFinished(this);
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        IsUsingSkill = false;
        IsSliding = false;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!Object.HasStateAuthority) return;
        
        if (collision.gameObject.CompareTag("Ground"))
        {
            groundContactCount++;
            
            if (!IsGrounded)
            {
                SetGrounded(true);
                
                Vector2 vel = rb.linearVelocity;
                vel.y = 0f;
                rb.linearVelocity = vel;
            }
        }
        else if (collision.gameObject.CompareTag("Kill"))
        {
            Die();
        }
        else if (collision.gameObject.CompareTag("Finish")) // ✅ 골인 지점 추가
        {
            Finish();
        }
    }

    // 기존 메서드들 유지...
    private void HandleClientPrediction()
    {
        float positionDifference = Vector2.Distance(transform.position, AuthorityPosition);
        
        if (positionDifference > 0.5f)
        {
            transform.position = AuthorityPosition;
            rb.linearVelocity = AuthorityVelocity;
            lastReconcileTime = Time.time;
        }
        else if (positionDifference > 0.1f && Time.time - lastReconcileTime > 0.1f)
        {
            transform.position = Vector2.MoveTowards(transform.position, AuthorityPosition, Time.deltaTime * 2f);
        }
    }

    private void HandleRemotePlayerMovement()
    {
        float distance = Vector2.Distance(transform.position, AuthorityPosition);
        
        if (distance > 0.1f)
        {
            if (distance > 1f)
            {
                transform.position = AuthorityPosition;
            }
            else
            {
                transform.position = Vector2.MoveTowards(transform.position, AuthorityPosition, Time.deltaTime * 10f);
            }
        }
    }

    private void UpdateAnimations()
    {
        if (netAnimator?.Animator != null)
        {
            // ✅ 스턴 상태 처리
            if (IsStunned)
            {
                // 스턴 중에는 기본 애니메이션만 유지
                netAnimator.Animator.SetBool("isRunning", false);
                netAnimator.Animator.SetBool("isJumping", false);
                netAnimator.Animator.SetBool("isFalling", false);
                netAnimator.Animator.SetBool("isSliding", false);
                netAnimator.Animator.SetBool("isSkill", false);
                
                // ✅ 데미지 애니메이션 동기화
                if (animator != null)
                {
                    animator.SetBool("isDamaged", IsStunned);
                }
                return;
            }
            
            // ✅ 정상 상태 애니메이션
            Vector2 currentVel = rb.linearVelocity;
            netAnimator.Animator.SetBool("isRunning", Mathf.Abs(currentVel.x) > 0.1f);
            netAnimator.Animator.SetBool("isJumping", !IsGrounded && currentVel.y > 0);
            netAnimator.Animator.SetBool("isFalling", !IsGrounded && currentVel.y < -0.1f);
            netAnimator.Animator.SetBool("isSliding", IsSliding);
            netAnimator.Animator.SetBool("isSkill", IsUsingSkill);
            
            // ✅ 정상 상태에서는 데미지 애니메이션 해제
            if (animator != null )
            {
                animator.SetBool("isDamaged", false);
            }
        }
    }

    private void HandleDeathAnimation()
    {
        if (animator != null)
        {
            animator.SetBool("isDying", true);
            animator.SetBool("isRunning", false);
            animator.SetBool("isJumping", false);
            animator.SetBool("isSliding", false);
            animator.SetBool("isSkill", false);
        }
    }

    // 기존 메서드들...
    private void PerformJump()
    {
        if (JumpCount == 0)
            netAnimator.Animator.SetBool("isJumping", true);
        else if (JumpCount == 1)
            netAnimator.Animator.SetBool("isDoubleJumping", true);

        Vector2 vel = rb.linearVelocity;
        vel.y = jumpForce;
        rb.linearVelocity = vel;
        
        IsGrounded = false;
        JumpCount++;
    }

    private void PerformDash()
    {
        IsUsingSkill = true;
        SkillTimer = skillDuration;
        netAnimator.Animator.SetBool("isSkill", true);
        
        Vector2 vel = rb.linearVelocity;
        vel.x = skillDashForce;
        vel.y = Mathf.Max(vel.y, 0);
        rb.linearVelocity = vel;
    }

    private void StartSlide()
    {
        IsSliding = true;
        netAnimator.Animator.SetBool("isSliding", true);
    }

    private void EndSlide()
    {
        IsSliding = false;
        netAnimator.Animator.SetBool("isSliding", false);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (!Object.HasStateAuthority) return;
        
        if (collision.gameObject.CompareTag("Ground"))
        {
            groundContactCount--;
            
            if (groundContactCount <= 0)
            {
                groundContactCount = 0;
                
                if (IsGrounded)
                {
                    SetGrounded(false);
                }
            }
        }
    }
    
    private void SetGrounded(bool grounded)
    {
        IsGrounded = grounded;
        
        if (grounded)
        {
            netAnimator.Animator.SetBool("isFalling", false);
            netAnimator.Animator.SetBool("isJumping", false);
            netAnimator.Animator.SetBool("isDoubleJumping", false);
            JumpCount = 0;
        }
    }

    public void Die()
    {
        IsDead = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        IsUsingSkill = false;
        IsSliding = false;
        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isSliding", false);

        // 게임 매니저에 사망 알림
        RaceGameManager.Instance.OnPlayerDied(this);
    }
}
