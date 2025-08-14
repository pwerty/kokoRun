using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillCooldownUI : MonoBehaviour
{
    [Header("UI References")]
    public Image skillIcon; // 스킬 아이콘
    public Image cooldownOverlay; // 쿨다운 오버레이
    public TextMeshProUGUI cooldownText; // 쿨다운 텍스트
    
    private NetworkPlayer localPlayer;
    
    private void Start()
    {
        FindLocalPlayer();
    }
    
    private void Update()
    {
        if (localPlayer != null)
        {
            UpdateCooldownDisplay();
        }
        else
        {
            FindLocalPlayer();
        }
    }
    
    private void FindLocalPlayer()
    {
        var players = FindObjectsOfType<NetworkPlayer>();
        foreach (var player in players)
        {
            if (player.Object?.InputAuthority == player.Runner?.LocalPlayer)
            {
                localPlayer = player;
                break;
            }
        }
    }
    
    private void UpdateCooldownDisplay()
    {
        bool isOnCooldown = localPlayer.SkillCooldownTimer > 0f;
        bool isUsingSkill = localPlayer.IsUsingSkill;
        
        if (isOnCooldown)
        {
            // 쿨다운 중
            float cooldownRatio = localPlayer.SkillCooldownTimer / localPlayer.skillCooldown;
            cooldownOverlay.fillAmount = cooldownRatio;
            cooldownText.text = $"{localPlayer.SkillCooldownTimer:F1}";
            cooldownText.gameObject.SetActive(true);
            skillIcon.color = Color.gray;
        }
        else if (isUsingSkill)
        {
            // 스킬 사용 중
            cooldownOverlay.fillAmount = 0f;
            cooldownText.gameObject.SetActive(false);
            skillIcon.color = Color.yellow;
        }
        else
        {
            // 사용 가능
            cooldownOverlay.fillAmount = 0f;
            cooldownText.gameObject.SetActive(false);
            skillIcon.color = Color.white;
        }
    }
}