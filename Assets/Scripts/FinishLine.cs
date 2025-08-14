using UnityEngine;

public class FinishLine : MonoBehaviour
{
    [Header("Finish Line Settings")]
    public ParticleSystem finishEffect; // 완주 이펙트
    public AudioClip finishSound; // 완주 사운드
    
    private void OnTriggerEnter2D(Collider2D other)
    {
        var player = other.GetComponent<NetworkPlayer>();
        if (player != null && player.Object.HasStateAuthority)
        {
            // 완주 처리는 NetworkPlayer의 OnCollisionEnter2D에서 처리함 여기서는 이펙트만 처리
            PlayFinishEffect();
        }
    }
    
    private void PlayFinishEffect()
    {
        if (finishEffect != null)
            finishEffect.Play();
            
        if (finishSound != null)
        {
            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
                audioSource.PlayOneShot(finishSound);
        }
    }
}