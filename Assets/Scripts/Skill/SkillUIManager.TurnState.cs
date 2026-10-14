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
        foreach (var b in new[] { knightButton, bishopButton, rookButton, queenButton, kingButton })
            if (b != null) b.gameObject.SetActive(on);

        // 턴 종료 버튼은 스킬 모드와 우노 모드에서 쓴다 (우노: 1~4 카드로 최소 1회 이동한 뒤 남은 이동을 포기할 때)
        if (endTurnButton != null) endTurnButton.gameObject.SetActive(on || MatchSettings.IsUno);
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
        KingDoubleMovePiece = null;

        // 턴이 시작될 때 이전 턴의 스킬 범위 하이라이트(위협/쉴드 등)가 남아있지 않도록 안전망으로
        // 한 번 더 지운다(정상 흐름에서는 OnEndTurnButtonClicked()에서 이미 정리된다).
        ChessInteractionManager.Instance?.ClearCustomHighlights();

        // 2026-10-04 추가: 킹 지휘를 사용한 뒤, 지휘 대상 팀의 턴이 돌아왔을 때 "이번 턴에 지휘 대상을
        // N회 이동시켜야 한다"는 걸 화면 중앙 안내로 알려준다. (지휘 자체는 지휘를 사용한 즉시 그 턴
        // 안에서 2회 이동으로 끝나는 경우가 보통이지만, 섭정 등으로 턴이 유지되지 않고 넘어간 뒤 다시
        // 돌아오는 경우를 위한 안내)
        var psm = PieceSkillManager.Instance;
        if (psm != null && psm.IsCommandActiveForTeam(newTurnTeam))
        {
            CenterAnnouncer.Show($"지휘 효과: 지정한 기물을 이번 턴에 {psm.GetCommandMovesLeft(newTurnTeam)}회 이동시켜야 합니다.");
        }

        RefreshUIState();
    }

    // 기물이 이동했을 때 호출. 백/흑 어느 팀이 움직였든 동일하게 "이동 완료 -> 스킬 사용 가능 창"
    // 흐름을 거치도록 한다
    public void OnPieceMoved(ChessPieces piece)
    {
        // 우노 모드: 스킬 흐름(스킬 사용 창, 자동 턴 종료)을 거치지 않고 이동 횟수만 센다
        if (UnoTurnController.Active)
        {
            GameManager.Instance?.PieceMoved();
            return;
        }

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

    private void CheckAutoTurnEnd(ChessPieces movedPiece)
    {
        var psm = PieceSkillManager.Instance;
        int actingTeamForCommandCheck = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : movedPiece.team;
        bool isCommandTeamsTurn = psm != null && psm.IsCommandActiveForTeam(actingTeamForCommandCheck);
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

        ChessPieces knight = ResolveMovedOrFirst(ChessPieceType.WhiteKnight, actingTeam);
        ChessPieces rook = ResolveMovedOrFirst(ChessPieceType.WhiteRook, actingTeam);

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
        if (UnoTurnController.Active && !UnoTurnController.Instance.CanManualEndTurn) return; // 우노: 조건을 채워야 넘길 수 있다

        GameManager.Instance?.RequestEndTurn();
    }

    // CheckAutoTurnEnd() 등 이미 중계된 흐름(보드 클릭 RPC) 내부에서의 자동 종료 및
    // 킹 지휘 스킬 성공 시 호출된다. 호출 시점에는 이미 "현재 턴을 진행 중인 팀"의
    // 행동으로 확정된 상태이므로 팀을 구분해 다시 검사할 필요가 없다.
    public void OnEndTurnButtonClicked()
    {
        ActiveSkillType = PendingSkillType.None;

        ChessInteractionManager.Instance?.ClearCustomHighlights();

        GameManager.Instance?.PieceMoved();
    }
    #endregion
}
