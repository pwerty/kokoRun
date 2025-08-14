using Fusion;
using UnityEngine;

// ✅ 새로운 NetworkBehaviour 클래스 생성
public class RoomPlayerManager : NetworkBehaviour
{
    private RoomPlayerManager roomPlayerManager;
    [Networked, Capacity(4)]
    public NetworkArray<PlayerRef> RoomPlayers => default;
    
    [Networked, Capacity(4)]
    public NetworkArray<byte> PlayerNames => default; // 플레이어 이름 해시값
    
    public System.Action<PlayerRef, string> OnPlayerListChanged;
    
    // 플레이어 추가
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddPlayer(PlayerRef player, string playerName)
    {
        // 빈 슬롯 찾아서 플레이어 추가
        for (int i = 0; i < RoomPlayers.Length; i++)
        {
            if (!RoomPlayers[i].IsRealPlayer)
            {
                RoomPlayers.Set(i, player);
                PlayerNames.Set(i, (byte)playerName.GetHashCode());
                
                Debug.Log($"✅ 플레이어 {playerName} 추가됨 (슬롯 {i})");
                break;
            }
        }
    }
    
    // 플레이어 제거
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RemovePlayer(PlayerRef player)
    {
        for (int i = 0; i < RoomPlayers.Length; i++)
        {
            if (RoomPlayers[i] == player)
            {
                RoomPlayers.Set(i, PlayerRef.None);
                PlayerNames.Set(i, 0);
                
                Debug.Log($"✅ 플레이어 {player} 제거됨 (슬롯 {i})");
                break;
            }
        }
    }
    
    // 네트워크 변수 변경 감지
    public void OnChanged()
    {
        // 플레이어 목록이 변경되면 UI 업데이트
        for (int i = 0; i < RoomPlayers.Length; i++)
        {
            if (RoomPlayers[i].IsRealPlayer)
            {
                string playerName = $"Player_{RoomPlayers[i].PlayerId}";
                OnPlayerListChanged?.Invoke(RoomPlayers[i], playerName);
            }
        }
    }
}
