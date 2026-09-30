using UnityEngine;

// SkillUIManager의 턴 시작/종료 및 스킬-이동 상태 관리 partial 파일.
public partial class SkillUIManager
{
    #region 턴 및 상태 관리
    // 로비 규칙(스킬 ON/OFF)을 UI에 반영: OFF면 스킬 버튼 5개와 턴 종료 버튼을 숨긴다.
    // GameStartController.StartMatch()에서 매 판 시작 시 호출된다.
    public void ApplyMatchSettings()
    {
        bool on = MatchSettings.SkillEnabled;
        foreach (var b in new[] { knightButton, bishopButton, rookButton, queenButton, kingButton, endTurnButton })
            if (b != null) b.gameObject.SetActive(on);
    }

    public void SetSkillUsedThisTurn()
    {
        HasUsedSkillThisTurn = true;
        RefreshUIState();
    }

    private void HandleTurnStarted(int newTurnTeam)
    {
        HasMovedThisTurn = false;
        MovedPieceThisTurn = null;
        IsWarpPendingMove = false;
        WarpBishopPiece = null;
        ActiveSkillType = PendingSkillType.None;
        lastCheckedSelection = null;
        HasUsedSkillThisTurn = false;

        IsKingDoubleMoveActive = false;
        RefreshUIState();
    }

    // 기물이 이동했을 때 호출. 백/흑 어느 팀이 움직였든 동일하게 "이동 완료 -> 스킬 사용 가능 창"
    // 흐름을 거치도록 한다 (예전에는 팀 0(백)만 이 흐름을 타고, 그 외 팀은 스킬 기회 없이 곧장
    // 턴이 종료되어 흑팀은 스킬을 전혀 쓸 수 없는 문제가 있었다).
    public void OnPieceMoved(ChessPieces piece)
    {
        HasMovedThisTurn = true;
        MovedPieceThisTurn = piece;

        if (IsWarpPendingMove)
        {
            IsWarpPendingMove = false;
            WarpBishopPiece = null;
        }

        RefreshUIState();

        if (piece == null)
        {
            GameManager.Instance?.PieceMoved();
            return;
        }

        CheckAutoTurnEnd(piece);
    }

    // 중요: 이 판정은 절대 버튼의 interactable(표시 상태)을 읽어서는 안 된다.
    // RefreshUIState()의 버튼 상태는 "이 클라이언트 화면에 보이는 내 팀 스킬 UI"를 위한 것이라
    // GameStartController.LocalTeam 기준으로 계산되는데(내 턴이 아니면 전부 강제로 false), 이 메서드는
    // OnPieceMoved를 통해 양쪽 클라이언트 모두에서 "실제로 방금 행동한 팀(=현재 턴 팀)"에 대해
    // 동일하게 호출된다. 예전에 여기서 knightButton.interactable 등을 그대로 읽었을 때는,
    // 상대 턴을 구경만 하는 클라이언트 화면에서는 그 버튼들이 항상 dimmed(비활성)라서
    // "사용 가능한 스킬 없음"으로 잘못 판정해 관전 클라이언트가 상대 턴을 제멋대로 조기 종료시키는
    // 네트워크 디싱크가 발생했다. 그래서 화면 표시와 무관하게 현재 턴 팀 기준으로 직접 재계산한다.
    private void CheckAutoTurnEnd(ChessPieces movedPiece)
    {
        // 2026-09-23 수정: IsCommandActive만 보고 무조건 자동 종료를 막으면, 지휘를 사용한 팀이 아닌
        // 상대 팀이 자기 턴에 정상적으로 이동을 마쳐도 이 턴이 자동으로 끝나지 않는 문제가 있었다.
        // (지휘 대상 팀의 턴일 때만 "지휘가 끝날 때까지 자동 종료 보류"가 적용되어야 한다)
        var psm = PieceSkillManager.Instance;
        bool isCommandTeamsTurn = psm != null && psm.IsCommandActive && psm.CommandedPiece != null
            && GameManager.Instance != null && GameManager.Instance.CurrentTurn == psm.CommandedPiece.team;
        if (isCommandTeamsTurn)
            return;

        if (IsKingDoubleMoveActive)
            return;

        if (!HasAnyUsableSkillForActingTeam())
        {
            Debug.Log("사용 가능한 스킬 버튼이 없어 자동으로 턴을 종료합니다.");
            OnEndTurnButtonClicked();
        }
    }

    // "현재 턴을 진행 중인 팀"이 실제로 사용 가능한 스킬(나이트/룩/퀸)이 있는지를,
    // 화면 표시(dimming)와 무관하게 계산한다. FindPiece는 SkillUIManager.UI.cs에 정의되어 있다.
    private bool HasAnyUsableSkillForActingTeam()
    {
        if (!MatchSettings.SkillEnabled) return false; // 스킬 시스템 OFF: 이동만 하면 턴이 자동 종료된다
        int actingTeam = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : 0;

        ChessPieces knight = FindPiece(ChessPieceType.WhiteKnight, actingTeam);
        ChessPieces rook = FindPiece(ChessPieceType.WhiteRook, actingTeam);

        bool isMovedKnight = HasMovedThisTurn && MovedPieceThisTurn == knight;
        bool canUseKnight = !HasUsedSkillThisTurn && isMovedKnight
            && PieceSkillManager.Instance != null && !PieceSkillManager.Instance.IsOnCooldown(knight);

        bool isMovedRook = HasMovedThisTurn && MovedPieceThisTurn == rook;
        bool canUseRook = !HasUsedSkillThisTurn && isMovedRook
            && PieceSkillManager.Instance != null && !PieceSkillManager.Instance.IsOnCooldown(rook);

        int qStack = QueenSkill.Instance != null ? QueenSkill.Instance.GetStack(actingTeam) : 0;
        int qRequired = QueenSkill.Instance != null ? QueenSkill.Instance.GetRequiredStack(actingTeam) : 6;
        bool canUseQueen = qStack >= qRequired;

        return canUseKnight || canUseRook || canUseQueen;
    }

    // 실제 "턴 종료" UI 버튼의 onClick에 바인딩되는 진입점.
    // 네트워크 대전 중에는 이 클릭 자체를 상대에게도 동일하게 전파해야 하므로
    // RequestEndTurn()(RPC 중계)을 거치고, 로컬 전용일 때는 기존처럼 즉시 종료한다.
    // 내 턴이 아니면(=버튼이 반투명 상태여도 로직상 안전하게) 클릭을 무시한다.
    public void OnEndTurnButtonPressed()
    {
        int currentTurn = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : -1;
        int myTeam = GameStartController.LocalTeam >= 0 ? GameStartController.LocalTeam : currentTurn;
        if (currentTurn != myTeam) return;

        GameManager.Instance?.RequestEndTurn();
    }

    // CheckAutoTurnEnd() 등 이미 중계된 흐름(보드 클릭 RPC) 내부에서의 자동 종료 및
    // 킹 지휘 스킬 성공 시 호출된다. 호출 시점에는 이미 "현재 턴을 진행 중인 팀"의
    // 행동으로 확정된 상태이므로 팀을 구분해 다시 검사할 필요가 없다.
    public void OnEndTurnButtonClicked()
    {
        ActiveSkillType = PendingSkillType.None;
        GameManager.Instance?.PieceMoved();
    }
    #endregion
}
