using System;
using UnityEngine;

public class Player : MonoBehaviour
{
    public bool isDead = false;
    public float moveSpeed = 5f;
    public float jumpForce = 10f;
    public float extraFallGravity = 2.5f;

    public float skillDashForce = 15f;
    public float skillDuration = 0.2f;
    private bool isUsingSkill = false;
    private float skillTimer = 0f;

    public Rigidbody2D rb;
    public BoxCollider2D collider;
    public Animator animator;
    public LayerMask groundLayer;

    private bool isGrounded = true;
    public int maxJumpCount = 2;
    private int jumpCount = 0;
    private bool isSliding = false;

    // 튜닝 변수
    [Tooltip("ray 길이")]
    public float rayLen ;

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (collider == null) collider = GetComponent<BoxCollider2D>();
        if (animator == null) animator = GetComponent<Animator>();

        // 물리 세팅 추천
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void OnEnable() => animator.SetBool("isRunning", true);

    void Update()
    {
        if (isDead) return;

        // 점프 (더블점프 포함)
        if (Input.GetKeyDown(KeyCode.Space) && jumpCount < maxJumpCount)
        {
            if (jumpCount == 0)
                animator.SetBool("isJumping", true);
            else if (jumpCount == 1)
                animator.SetBool("isDoubleJumping", true);

            Vector2 v = rb.linearVelocity;
            v.y = 0;
            rb.linearVelocity = v;
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            isGrounded = false;
            jumpCount++;
        }

        // 스킬(대쉬)
        if (Input.GetKeyDown(KeyCode.C) && !isUsingSkill)
        {
            isUsingSkill = true;
            skillTimer = skillDuration;
            animator.SetBool("isSkill", true);
            Vector2 v = rb.linearVelocity;
            v.x = skillDashForce;
            v.y = 0; // 대쉬 시 수직 속도 초기화(원하면 제거)
            rb.linearVelocity = v;
        }

        // 슬라이드
        if (Input.GetKey(KeyCode.X))
        {
            if (!isSliding)
            {
                isSliding = true;
                animator.SetBool("isSliding", true);
            }
        }
        else if (isSliding)
        {
            isSliding = false;
            animator.SetBool("isSliding", false);
        }

        // 낙하 애니메이션(간단 체크)
        if (!isGrounded && rb.linearVelocity.y < -0.1f)
            animator.SetBool("isFalling", true);
    }

    void FixedUpdate()
    {
        if (isDead) return;

        // 가로 속도 (스킬 우선)
        if (isUsingSkill)
        {
            skillTimer -= Time.fixedDeltaTime;
            if (skillTimer <= 0f)
            {
                isUsingSkill = false;
                animator.SetBool("isSkill", false);
                // 스킬 끝나면 일반 이동 속도로 회복
            }
        }

        // 현재 물리 속도 취득
        Vector2 vel = rb.linearVelocity;
        vel.x = isUsingSkill ? skillDashForce : moveSpeed;

        // 중력 가속 보정 (빠르게 떨어질 때)
        if (vel.y < 0f)
        {
            vel.y += Physics2D.gravity.y * (extraFallGravity - 1f) * Time.fixedDeltaTime;
        }

        // 실제 속도 적용 (수평은 덮어쓰기, 수직은 보정된 값 적용)
        rb.linearVelocity = vel;
        
        
        Vector2 footPos = (Vector2)transform.position + collider.offset - new Vector2(0, collider.size.y / 2f);

        RaycastHit2D hit = Physics2D.Raycast(footPos, Vector2.down, rayLen, groundLayer);
        Debug.DrawRay(footPos, Vector2.down * rayLen, Color.red);

        if (hit.collider != null)
        {
            if(hit.collider.gameObject.tag == "Kill")
            {
                Die();
            }else if (!isGrounded&&rb.linearVelocity.y < -0.1f)
            {
                // 땅에 닿았으니 착지
                Debug.Log("땅에 닿음");
                transform.position = new Vector2(transform.position.x, hit.point.y + collider.size.y*3);
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                animator.SetBool("isFalling", false);
                animator.SetBool("isJumping", false);
                animator.SetBool("isDoubleJumping", false);
                isGrounded = true;
                jumpCount = 0;   
            }
        }

        
        
        
    }

    public void Die()
    {
        animator.SetBool("isDying", true);
        isDead = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        isUsingSkill = false;
        isSliding = false;

        animator.SetBool("isRunning", false);
        animator.SetBool("isJumping", false);
        animator.SetBool("isSliding", false);
    }
}
