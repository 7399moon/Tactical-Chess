using System.Collections.Generic;
using UnityEngine;

// SkillUIManager의 스킬 버튼 클릭 처리 및 비숍 워프 범위 계산 partial 파일.
public partial class SkillUIManager
{
    #region 스킬 버튼 및 범위 처리
    // 좌측 스킬 버튼(나이트/비숍/룩/퀸/킹) 클릭 시 스킬 대기 모드를 토글.
    // 이 클릭은 로컬 UI 이벤트라서(보드 클릭과 달리) 좌표 릴레이를 타지 않으므로,
    // 네트워크 대전 중에는 실제 상태 변경(ApplySkillModeToggle)을 직접 수행하지 않고
    // RPC로 전파해 양쪽 클라이언트가 동일하게 반영하도록 한다. 그래야 이후 상대 화면에서도
    // "지금 상대가 무슨 스킬 대기 모드인지"를 알고 보드 클릭 릴레이를 올바르게 해석할 수 있다.
    public void OnSkillButtonClicked(PendingSkillType type)
    {
        int currentTurn = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : -1;
        int myTeam = GameStartController.LocalTeam >= 0 ? GameStartController.LocalTeam : currentTurn;
        if (currentTurn != myTeam) return;

        SoundManager.Instance?.PlaySkillButtonClick();

        if (GameStartController.LocalTeam >= 0 && ChessNetworkSync.Instance != null)
        {
            ChessNetworkSync.Instance.RPC_RelaySkillModeToggle(currentTurn, (int)type);
        }
        else
        {
            ApplySkillModeToggle(currentTurn, type);
        }
    }

    // 실제로 스킬 대기 모드를 전환하는 처리. RPC(또는 로컬 테스트 시 직접 호출)를 통해
    // 양쪽 클라이언트에서 항상 동일한 team 값을 기준으로 동일하게 실행되므로 내부에서는
    // GameStartController.LocalTeam을 참조하지 않는다(참조하면 상대 화면에서 잘못된 팀 기준으로 해석됨).
    public void ApplySkillModeToggle(int team, PendingSkillType type)
    {
        if (ActiveSkillType == type)
        {
            ActiveSkillType = PendingSkillType.None; // 토글 취소
            CancelSkillMode();
        }
        else
        {
            ActiveSkillType = type;

            if (type == PendingSkillType.Bishop)
            {
                lastCheckedSelection = null;
                ResolveBishopForSkill(team);
            }
        }

        Debug.Log($"[SkillUI] 선택된 스킬 모드 변경 완료: {ActiveSkillType}");
        OnSkillPendingChanged?.Invoke(ActiveSkillType);
        RefreshUIState();
    }

    // 비숍 스킬 모드 진입 시 대상 비숍을 결정(선택되어 있던 아군 비숍 우선, 없으면 보드에서 자동 탐색).
    // actingTeam을 기준으로만 계산하므로 양쪽 클라이언트가 항상 동일한 결과를 얻는다.
    private void ResolveBishopForSkill(int actingTeam)
    {
        ChessPieces currentSelected = ChessInteractionManager.Instance != null ? ChessInteractionManager.Instance.SelectedPiece : null;

        bool isOwnBishop = currentSelected != null &&
            (currentSelected.type == ChessPieceType.WhiteBishop || currentSelected.type == ChessPieceType.BlackBishop) &&
            currentSelected.team == actingTeam;

        // 2026-10-06 수정: FindPiece(첫 번째로 찾은 비숍, 즉 항상 "왼쪽" 비숍)로 폴백하면 그 비숍이
        // 쿨타임 중이거나 워프 가능한 칸이 없어도 그대로 선택되어 버려, 실제로는 사용 가능한 오른쪽
        // 비숍이 있어도 선택될 수 없는 버그가 있었다(SkillUIManager.UI.cs의 FindUsableBishop 주석 참고).
        if (!isOwnBishop)
            currentSelected = FindUsableBishop(actingTeam);

        if (currentSelected == null)
        {
            Debug.Log("선택된 비숍이 없습니다.");
            CancelSkillMode();
            return;
        }

        selectedBishop = currentSelected;
        ShowBishopWarpRange(selectedBishop);
    }

    // 비숍의 워프 가능 범위를 계산해 보드에 하이라이트로 표시
    public void ShowBishopWarpRange(ChessPieces bishop)
    {
        ChessBoard board = FindAnyObjectByType<ChessBoard>();
        if (ChessInteractionManager.Instance == null || board == null || bishop == null) return;

        List<Vector2Int> validWarpTiles = GetWarpTiles(bishop, board);
        ChessInteractionManager.Instance.HighlightCustomTiles(validWarpTiles);
    }

    // 비숍이 워프 가능한 타일 목록을 계산 (증강 보유 시 아군/적이 있는 칸도 포함)
    // 2026-10-03 수정: 워프 직후에는 IsWarpPendingMove 제약으로 "방금 워프한 비숍"만 이동할 수 있는데,
    // 워프 도착 칸에서 그 비숍이 둘 수 있는 합법적인 수가 하나도 없으면(사면이 기물에 막히는 등) 어떤
    // 기물도 움직일 수 없는 상태로 게임이 멈춰버렸다. 그런 칸은 애초에 워프 후보에서 제외한다.
    public List<Vector2Int> GetWarpTiles(ChessPieces bishop, ChessBoard board)
    {
        List<Vector2Int> validWarpTiles = new List<Vector2Int>();
        if (bishop == null || board == null) return validWarpTiles;

        // 2026-10-05 수정: 위협(이동 불가) 상태인 비숍은 PieceSkillManager.TryUseWarp에서 이제 워프
        // 자체를 거부하므로, 하이라이트도 애초에 아무 칸도 보여주지 않아야 플레이어가 혼란스럽지 않다.
        if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsImmobilized(bishop))
            return validWarpTiles;

        bool ceasefireActive = GameManager.Instance != null && GameManager.Instance.armisticeTurns > 0;

        // 레어4(워프 스왑)/레전더리2(차원 암살) 보유 시 아군/적이 있는 칸도 워프 후보로 노출
        bool canSwap = AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(bishop.team, "bishop_warp_swap");
        // 2026-10-05 수정: 차원 암살도 휴전 협정 중에는 TryUseWarp에서 거부되므로, 그 상태에서는
        // 적이 있는 칸을 워프 후보로 노출하지 않는다.
        bool canAttack = AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(bishop.team, "bishop_warp_attack") && !ceasefireActive;

        for (int i = 0; i < WarpDirections.Length; i++)
        {
            int targetX = bishop.currentX + WarpDirections[i].x;
            int targetY = bishop.currentY + WarpDirections[i].y;

            if (targetX < 0 || targetX >= ChessBoard.TileCountX || targetY < 0 || targetY >= ChessBoard.TileCountY)
                continue;

            ChessPieces occupant = board.GetPieceAt(targetX, targetY);
            // 2026-10-05 수정: 스왑 대상 아군이 위협 상태면 TryUseWarp에서 거부되므로 후보에서도 제외.
            bool occupantImmobilized = PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsImmobilized(occupant);
            bool isSwapCase = occupant != null && occupant.team == bishop.team && canSwap && !occupantImmobilized;
            bool isAttackCase = occupant != null && occupant.team != bishop.team && canAttack;
            bool valid = occupant == null || isSwapCase || isAttackCase;

            if (valid && !WouldHaveLegalMoveAfterWarp(bishop, new Vector2Int(targetX, targetY), occupant, isSwapCase, board))
                valid = false;

            if (valid)
                validWarpTiles.Add(new Vector2Int(targetX, targetY));
        }
        return validWarpTiles;
    }

    // targetPos로 워프를 가정하고, 그 위치에서 bishop이 이후 최소 1개의 합법적인 일반 이동을 가질 수
    // 있는지 시뮬레이션한다(보드를 실제로 가상 이동시켰다가 끝나면 원상 복구). 스왑이 아닌 경우(빈 칸
    // 이동/차원 암살 처치)는 occupant가 대상 칸에서 사라지는 결과가 동일하므로 같은 분기로 처리한다.
    private bool WouldHaveLegalMoveAfterWarp(ChessPieces bishop, Vector2Int targetPos, ChessPieces occupant, bool isSwapCase, ChessBoard board)
    {
        if (bishop == null || board == null) return false;

        int fromX = bishop.currentX;
        int fromY = bishop.currentY;
        Vector2Int? enPassantTarget = GameManager.Instance != null ? GameManager.Instance.EnPassantTarget : null;

        // 가상 워프 적용
        board.SetPieceAt(fromX, fromY, null);
        board.SetPieceAt(targetPos.x, targetPos.y, bishop);
        bishop.currentX = targetPos.x;
        bishop.currentY = targetPos.y;

        if (isSwapCase && occupant != null)
        {
            board.SetPieceAt(fromX, fromY, occupant);
            occupant.currentX = fromX;
            occupant.currentY = fromY;
        }

        bool hasMove = ChessRules.GetLegalMoves(board.Pieces, bishop, enPassantTarget).Count > 0;

        // 원상 복구
        bishop.currentX = fromX;
        bishop.currentY = fromY;
        board.SetPieceAt(targetPos.x, targetPos.y, occupant);
        board.SetPieceAt(fromX, fromY, bishop);

        if (isSwapCase && occupant != null)
        {
            occupant.currentX = targetPos.x;
            occupant.currentY = targetPos.y;
        }

        return hasMove;
    }

    // 유니크2(연속 워프): 방금 워프한 비숍으로 한 번 더 워프할 수 있도록 스킬 모드를 재진입시킴
    public void ContinueBishopWarp(ChessPieces bishop)
    {
        ActiveSkillType = PendingSkillType.Bishop;
        selectedBishop = bishop;
        lastCheckedSelection = null; // Update()의 재감지 로직이 selectedBishop을 덮어쓰지 않도록 초기화

        // 주의: 이 이벤트 핸들러(ChessInteractionManager.HandleSkillPendingChanged)가 커스텀 하이라이트를 지우므로
        // 워프 범위 하이라이트는 반드시 이벤트 호출 "이후"에 표시해야 한다.
        OnSkillPendingChanged?.Invoke(PendingSkillType.Bishop);
        ShowBishopWarpRange(bishop);
        RefreshUIState();
    }

    // 기물 주변 인접 타일 반환 (나이트 위협/룩 쉴드 스킬의 증강 범위 보너스를 반영)
    // 2026-10-06 수정: 예전에는 항상 고정된 8방향 인접 타일만 반환해서, 레어1(위협 확장)/레어6(쉴드
    // 확장) 증강으로 TryUseThreat/TryUseShield의 실제 판정 범위(range = 1 + bonus, 체비셰프 거리)가
    // 넓어져도 화면에는 여전히 기본 1칸 범위만 하이라이트되는 표시 버그가 있었다(스킬 자체는 확장된
    // 범위로 정상 작동하지만, 플레이어는 넓어진 범위를 볼 수 없어 혼란/오사용을 유발). 실제 판정과
    // 동일한 공식으로 범위를 계산해 하이라이트에 반영한다.
    public List<Vector2Int> GetSurroundingTiles(ChessPieces piece, PendingSkillType skillType = PendingSkillType.None)
    {
        List<Vector2Int> tiles = new List<Vector2Int>();
        if (piece == null) return tiles;

        int range = 1;
        if (AugmentManager.Instance != null)
        {
            if (skillType == PendingSkillType.Knight)
                range += AugmentManager.Instance.GetKnightThreatRangeBonus(piece.team);
            else if (skillType == PendingSkillType.Rook)
                range += AugmentManager.Instance.GetRookShieldRangeBonus(piece.team);
        }

        // 2026-10-09 수정: 레어8(자체 방벽, rook_self_shield) 보유 시 TryUseShield가 이미 자기 자신을
        // 쉴드 대상으로 허용하고 있었지만(allowSelfTarget), 이 하이라이트 계산은 항상 자기 자신의
        // 칸(dx=0,dy=0)을 건너뛰어 실제로 사용 가능한 범위인데도 화면엔 표시되지 않는 문제가 있었다.
        bool includeSelfTile = skillType == PendingSkillType.Rook
            && AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(piece.team, "rook_self_shield");

        for (int dx = -range; dx <= range; dx++)
        {
            for (int dy = -range; dy <= range; dy++)
            {
                if (dx == 0 && dy == 0 && !includeSelfTile) continue;

                int x = piece.currentX + dx;
                int y = piece.currentY + dy;

                if (x >= 0 && x < ChessBoard.TileCountX && y >= 0 && y < ChessBoard.TileCountY)
                    tiles.Add(new Vector2Int(x, y));
            }
        }

        return tiles;
    }

    // 스킬 대기 모드를 취소하고 커스텀 하이라이트를 정리
    public void CancelSkillMode()
    {
        selectedBishop = null;
        lastCheckedSelection = null;
        ChessInteractionManager.Instance?.ClearCustomHighlights();
    }

    // 비숍 워프 실행 완료 후 호출: 스킬 모드를 끄고 "워프 직후 정규 이동 대기" 상태로 전환
    public void OnBishopWarpExecuted(ChessPieces bishop, Vector2Int targetPos)
    {
        ActiveSkillType = PendingSkillType.None;
        CancelSkillMode();
        SetWarpPending(bishop);
    }

    // 대기 중인 스킬 모드를 완전히 해제
    public void ClearPendingSkill()
    {
        ActiveSkillType = PendingSkillType.None;
        CancelSkillMode();
        OnSkillPendingChanged?.Invoke(PendingSkillType.None);
        RefreshUIState();
    }

    // 워프 직후 정규 이동 대기 상태로 설정
    public void SetWarpPending(ChessPieces bishop)
    {
        IsWarpPendingMove = true;
        WarpBishopPiece = bishop;
        RefreshUIState();
    }

    // 2026-10-05 추가: "왕의 보폭" 보너스 이동을 킹이 이번 턴 처음 움직였을 때 부여(예약)한다.
    // 이 보너스는 반드시 같은 킹이 소모해야 하므로 대상 기물 참조(KingDoubleMovePiece)도 함께 기록한다.
    public void SetKingDoubleMovePending(ChessPieces king)
    {
        IsKingDoubleMoveActive = true;
        KingDoubleMovePiece = king;
    }

    // 왕의 보폭 보너스 이동 소모/취소 처리 (실제 소모는 그 킹이 다시 움직였을 때만 일어나야 한다 -
    // ChessInteractionManager.ProcessKingDoubleMove가 호출 전에 이미 피호출 기물이 KingDoubleMovePiece와
    // 같은지 검증한다)
    public void ClearKingDoubleMove()
    {
        IsKingDoubleMoveActive = false;
        KingDoubleMovePiece = null;
    }
    #endregion
}
