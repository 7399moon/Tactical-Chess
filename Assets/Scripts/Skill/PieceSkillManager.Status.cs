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

    // 지휘로 이번 턴 추가 이동권을 받은 기물인지 확인
    public bool HasCommandExtraMove(ChessPieces piece) =>
        piece != null && commandExtraMoveThisTurn.TryGetValue(piece.team, out ChessPieces target) && target == piece;

    // 추가 이동권 소모 처리
    public void ConsumeCommandExtraMove(int team) => commandExtraMoveThisTurn.Remove(team);

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
    #endregion
}
