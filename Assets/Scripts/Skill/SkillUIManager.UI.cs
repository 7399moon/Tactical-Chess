using UnityEngine;
using UnityEngine.UI;

// SkillUIManager의 버튼/텍스트/아웃라인 등 UI 표시 갱신 partial 파일.
public partial class SkillUIManager
{
    // 내 턴이 아닐 때 스킬 버튼을 흐리게(반투명) 표시하기 위한 알파값 (완전히 숨기지 않고 존재는 계속 보이게)
    private const float SkillButtonDimmedAlpha = 0.4f;
    private const float SkillButtonNormalAlpha = 1f;

    #region UI 표시 갱신
    // 현재 턴 및 게임 조건에 따라 스킬 버튼과 UI 텍스트 활성화 상태를 새로고침.
    // 이 패널은 "내 화면에는 내 팀의 스킬 UI만 보인다"를 구현하는 지점이라, 판정 기준은
    // 항상 이 클라이언트의 소유 팀(GameStartController.LocalTeam)이다. 로컬 테스트(LocalTeam == -1)
    // 에서는 팀 배정이 없으므로 기존처럼 백팀(0) 기준으로 동작한다.
    public void RefreshUIState()
    {
        int currentTurn = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : 0;
        int myTeam = GameStartController.LocalTeam >= 0 ? GameStartController.LocalTeam : 0;

        if (currentTurn != myTeam)
        {
            DisableAllButtons();
            SetSkillButtonsDimmed(true);
            return;
        }

        // 내 턴이면 스킬 버튼을 다시 원래 밝기로 복원 (쿨타임 등 개별 비활성화 사유는 interactable로만 표현)
        SetSkillButtonsDimmed(false);

        // 2026-10-03 수정: 나이트/비숍/룩은 팀당 2기(승급 시 더 늘어날 수 있음)라서 FindPiece가
        // 보드를 좌표 순으로 스캔해 "처음 찾은 한 개"만 반환하면, 실제로 이동/선택된 쪽이 아닌
        // 엉뚱한 인스턴스를 기준으로 쿨타임 텍스트와 버튼 활성화를 계산하는 문제가 있었다
        // (예: 비숍 스킬을 써도 쿨타임이 표시되지 않고 버튼이 계속 활성화 상태로 보이던 버그).
        // 킹은 팀당 1기뿐이라 FindPiece로도 항상 올바르다.
        ChessPieces knight = ResolveMovedOrFirst(ChessPieceType.WhiteKnight, myTeam);
        ChessPieces bishop = ResolvePreferredBishop(myTeam);
        ChessPieces rook = ResolveMovedOrFirst(ChessPieceType.WhiteRook, myTeam);
        ChessPieces king = FindPiece(ChessPieceType.WhiteKing, myTeam);

        UpdateCooldownText(knightCooldownText, knight);
        UpdateCooldownText(bishopCooldownText, bishop);
        UpdateCooldownText(rookCooldownText, rook);
        UpdateCooldownText(kingCooldownText, king);

        // 퀸 스택 표기 (유니크5 아우라 가속 보유 시 요구 스택이 6->5로 표시됨)
        int qStack = QueenSkill.Instance != null ? QueenSkill.Instance.GetStack(myTeam) : 0;
        int qRequired = QueenSkill.Instance != null ? QueenSkill.Instance.GetRequiredStack(myTeam) : 6;
        if (queenStackText != null) queenStackText.text = $"{qStack}/{qRequired}";

        // 버튼 상호작용 조건 설정
        bool isMovedKnight = HasMovedThisTurn && MovedPieceThisTurn == knight;
        if (knightButton)
            knightButton.interactable = !HasUsedSkillThisTurn && isMovedKnight && !PieceSkillManager.Instance.IsOnCooldown(knight);

        bool isMovedRook = HasMovedThisTurn && MovedPieceThisTurn == rook;
        if (rookButton)
            rookButton.interactable = !HasUsedSkillThisTurn && isMovedRook && !PieceSkillManager.Instance.IsOnCooldown(rook);

        if (bishopButton)
            bishopButton.interactable = !HasUsedSkillThisTurn && !HasMovedThisTurn && !PieceSkillManager.Instance.IsOnCooldown(bishop);

        if (queenButton)
            queenButton.interactable = (qStack >= qRequired);

        if (kingButton)
            kingButton.interactable = !HasUsedSkillThisTurn && !HasMovedThisTurn && !PieceSkillManager.Instance.IsOnCooldown(king);

        // 2026-10-05 수정 후 되돌림: 턴 종료 버튼은 "이번 턴엔 1회만 이동하고 전략적으로 넘긴다"는
        // 의도된 선택지라는 사용자 확인에 따라, 왕의 보폭/지휘 강제 이동 여부와 무관하게
        // HasMovedThisTurn 조건만으로 원래 동작으로 되돌린다.
        if (endTurnButton)
            endTurnButton.interactable = HasMovedThisTurn;

        UpdateSkillOverlays();
    }

    // 현재 활성화된 스킬에 맞춰 버튼 아웃라인(테두리 강조)을 갱신
    private void UpdateSkillOverlays()
    {
        SetOutlineActive(knightActiveOutline, ActiveSkillType == PendingSkillType.Knight);
        SetOutlineActive(bishopActiveOutline, ActiveSkillType == PendingSkillType.Bishop);
        SetOutlineActive(rookActiveOutline, ActiveSkillType == PendingSkillType.Rook);
        SetOutlineActive(queenActiveOutline, ActiveSkillType == PendingSkillType.Queen);
        SetOutlineActive(kingActiveOutline, ActiveSkillType == PendingSkillType.King);
    }

    // 아웃라인 컴포넌트 활성/비활성 설정
    private void SetOutlineActive(Outline outline, bool active)
    {
        if (outline != null)
            outline.enabled = active;
    }

    // 모든 스킬/턴종료 버튼을 비활성화 (내 턴이 아닐 때)
    private void DisableAllButtons()
    {
        if (knightButton) knightButton.interactable = false;
        if (bishopButton) bishopButton.interactable = false;
        if (rookButton) rookButton.interactable = false;
        if (queenButton) queenButton.interactable = false;
        if (kingButton) kingButton.interactable = false;
        if (endTurnButton) endTurnButton.interactable = false;
    }

    // 5개 스킬 버튼(나이트/비숍/룩/퀸/킹)을 내 턴이 아닐 때 반투명하게, 내 턴일 때 원래 밝기로 표시.
    // 완전히 숨기거나 제거하지 않고 CanvasGroup.alpha만 낮춰서 "지금은 쓸 수 없다"는 것을 보여준다.
    // (턴 종료 버튼은 사용자가 요청한 "스킬 버튼"에 해당하지 않으므로 제외)
    private void SetSkillButtonsDimmed(bool dimmed)
    {
        float alpha = dimmed ? SkillButtonDimmedAlpha : SkillButtonNormalAlpha;
        SetButtonAlpha(knightButton, alpha);
        SetButtonAlpha(bishopButton, alpha);
        SetButtonAlpha(rookButton, alpha);
        SetButtonAlpha(queenButton, alpha);
        SetButtonAlpha(kingButton, alpha);
    }

    // 버튼에 CanvasGroup이 없으면 추가해서 alpha를 적용 (Selectable의 자체 색상 트랜지션과 충돌하지 않도록
    // Graphic.color 대신 CanvasGroup으로 반투명 처리)
    private void SetButtonAlpha(Button button, float alpha)
    {
        if (button == null) return;

        if (!button.TryGetComponent<CanvasGroup>(out var canvasGroup))
            canvasGroup = button.gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha = alpha;
    }

    // 쿨타임 텍스트를 갱신 (쿨타임이 없으면 텍스트 자체를 비활성화)
    private void UpdateCooldownText(Text txt, ChessPieces piece)
    {
        if (txt == null) return;
        int cd = PieceSkillManager.Instance != null ? PieceSkillManager.Instance.GetCooldownRemaining(piece) : 0;
        txt.gameObject.SetActive(cd > 0);
        txt.text = cd > 0 ? $"{cd}" : string.Empty;
    }

    // 2026-10-03 추가: 팀당 2기 이상 있을 수 있는 타입(나이트/룩)에 대해, 이번 턴에 실제로
    // 이동한 기물이 있고 그 기물이 이 타입이라면 그 인스턴스를 그대로 반환한다. 그렇지 않으면
    // (아직 아무도 안 움직였거나 다른 종류가 움직였으면) 기존처럼 FindPiece로 첫 번째 인스턴스를
    // 임시로 보여준다 - 이 경우는 "어느 쪽이 맞다"고 특정할 수 없는 표시용 폴백일 뿐이고,
    // 실제 사용 가능 여부 판정(isMovedKnight/isMovedRook)은 항상 MovedPieceThisTurn과의 참조
    // 비교로 이루어지므로 이 폴백 값 자체가 오판정을 유발하지는 않는다.
    private ChessPieces ResolveMovedOrFirst(ChessPieceType whiteFormType, int team)
    {
        if (HasMovedThisTurn && MovedPieceThisTurn != null && MovedPieceThisTurn.team == team)
        {
            ChessPieceType movedResolvedType = ChessPieceTeamUtil.ResolveForTeam(whiteFormType, team);
            if (MovedPieceThisTurn.type == movedResolvedType)
                return MovedPieceThisTurn;
        }
        return FindPiece(whiteFormType, team);
    }

    // 2026-10-03 추가: 비숍 스킬 발동 대상을 고르는 ResolveBishopForSkill(SkillUIManager.SkillHandlers.cs)
    // 과 동일한 우선순위(선택되어 있던 아군 비숍 우선, 없으면 보드에서 첫 번째로 찾은 비숍)로
    // "표시용" 비숍을 고른다. 그래야 비숍이 2기 있을 때 실제로 스킬을 쓰게 될 비숍과 쿨타임
    // 표시/버튼 활성화 기준이 항상 일치한다.
    private ChessPieces ResolvePreferredBishop(int actingTeam)
    {
        ChessPieces currentSelected = ChessInteractionManager.Instance != null ? ChessInteractionManager.Instance.SelectedPiece : null;

        bool isOwnBishop = currentSelected != null &&
            (currentSelected.type == ChessPieceType.WhiteBishop || currentSelected.type == ChessPieceType.BlackBishop) &&
            currentSelected.team == actingTeam;

        return isOwnBishop ? currentSelected : FindPiece(ChessPieceType.WhiteBishop, actingTeam);
    }

    // 체스판 내에서 지정한 종류(백색 기준 타입)를 team 색상으로 환산해 검색
    private ChessPieces FindPiece(ChessPieceType whiteFormType, int team)
    {
        ChessBoard board = FindAnyObjectByType<ChessBoard>();
        if (board == null || board.Pieces == null) return null;

        ChessPieceType resolvedType = ChessPieceTeamUtil.ResolveForTeam(whiteFormType, team);
        ChessPieces[,] boardPieces = board.Pieces;
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                ChessPieces p = boardPieces[x, y];
                if (p != null && p.type == resolvedType && p.team == team)
                    return p;
            }
        }
        return null;
    }
    #endregion
}
