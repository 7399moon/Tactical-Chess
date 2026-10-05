using UnityEngine;

// PieceSkillManager의 상태(쿨타임/부동/쉴드/지휘 추가 이동) 조회 및 수정 API partial 파일.
public partial class PieceSkillManager
{
    #region 상태 조회 및 수정
    // 위협(이동 불가) 상태 여부
    public bool IsImmobilized(ChessPieces piece) => piece != null && immobilized.ContainsKey(piece);
    // 쉴드(피격 무효) 상태 여부
    public bool IsShielded(ChessPieces piece) => piece != null && shielded.ContainsKey(piece);

    // 남은 쿨타임 조회
    public int GetCooldownRemaining(ChessPieces piece) =>
        (piece != null && cooldowns.TryGetValue(piece, out int remaining)) ? remaining : 0;

    // 쿨타임이 남아있는지 여부
    public bool IsOnCooldown(ChessPieces piece) => GetCooldownRemaining(piece) > 0;

    // 증강(사냥꾼의 본능, 워프 가속 등) 효과로 쿨타임을 즉시 감소
    public void ReduceCooldown(ChessPieces piece, int amount)
    {
        if (piece == null || amount <= 0) return;

        int reduced = Mathf.Max(0, GetCooldownRemaining(piece) - amount);

        if (reduced > 0) cooldowns[piece] = reduced;
        else cooldowns.Remove(piece);
    }

    // 증강(섭정 등)에 의한 킹 지휘 기본 쿨타임 수치 변경. 반드시 보유한 팀(team)에만 적용되며,
    // 다른 팀의 kingCommandCooldownBase에는 영향을 주지 않는다(2026-09-21 수정 1-3).
    public void SetKingCommandCooldownBase(int team, int newCooldown)
    {
        if (team != 0 && team != 1) return;
        kingCommandCooldownBase[team] = newCooldown;
    }

    // 지정한 팀의 현재 킹 지휘 기본 쿨타임 수치 조회 (검증/디버그 및 UI 표시용)
    public int GetKingCommandCooldownBase(int team) =>
        (team == 0 || team == 1) ? kingCommandCooldownBase[team] : kingCommandCooldownDefault;

    // 해당 팀이 augmentId를 가지고 있는지 짧게 확인하기 위한 헬퍼
    private bool HasAugment(int team, string augmentId) =>
        AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(team, augmentId);

    // 2026-10-05 추가: 승급(프로모션)으로 기존 기물이 파괴되고 새 인스턴스로 교체될 때 호출하는 API.
    // 사용자 피드백 반영 - 위협/쉴드/지휘 대상 지정 같은 "상태 효과"는 승급되면서 다른 기물이 된
    // 것이므로 그대로 해제되는 게 맞지만(여기서 명시적으로 즉시 정리), "쿨타임"은 기물의 생존
    // 여부와 무관하게 유지되어야 하므로 이 메서드에서는 건드리지 않는다(쿨타임 이전은
    // GetCooldownRemaining/SetCooldownDirectly를 호출부(ChessBoard.PromotePieceAt)에서 직접 처리).
    public void ClearPieceState(ChessPieces piece, int team)
    {
        if (piece == null) return;

        if (immobilized.Remove(piece))
        {
            piece.IsThreatenedTarget = false;
            RemoveThreatVfx(piece);
        }

        if (shielded.Remove(piece))
            RemoveShieldVfx(piece);

        if (pendingCommandTarget.TryGetValue(team, out ChessPieces pendingTarget) && pendingTarget == piece)
            pendingCommandTarget.Remove(team);

        if (commandedPieceByTeam.TryGetValue(team, out ChessPieces activeTarget) && activeTarget == piece)
            ClearCommandState(team);
    }

    // 2026-10-05 추가: 승급된 새 기물에 승급 전 기물의 남은 쿨타임을 그대로 옮겨 적용하기 위한 API
    // (쿨타임이 걸린 스킬을 쓰자마자 승급해서 쿨타임을 리셋하는 악용을 막기 위함이기도 하다).
    // value가 0 이하이면 쿨타임이 없는 상태이므로 딕셔너리 항목 자체를 남기지 않는다.
    public void SetCooldownDirectly(ChessPieces piece, int value)
    {
        if (piece == null) return;

        if (value > 0) cooldowns[piece] = value;
        else cooldowns.Remove(piece);
    }
    #endregion
}
