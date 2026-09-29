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

        if (!isOwnBishop)
            currentSelected = FindPiece(ChessPieceType.WhiteBishop, actingTeam);

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
    public List<Vector2Int> GetWarpTiles(ChessPieces bishop, ChessBoard board)
    {
        List<Vector2Int> validWarpTiles = new List<Vector2Int>();
        if (bishop == null || board == null) return validWarpTiles;

        // 레어4(워프 스왑)/레전더리2(차원 암살) 보유 시 아군/적이 있는 칸도 워프 후보로 노출
        bool canSwap = AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(bishop.team, "bishop_warp_swap");
        bool canAttack = AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(bishop.team, "bishop_warp_attack");

        for (int i = 0; i < WarpDirections.Length; i++)
        {
            int targetX = bishop.currentX + WarpDirections[i].x;
            int targetY = bishop.currentY + WarpDirections[i].y;

            if (targetX < 0 || targetX >= ChessBoard.TileCountX || targetY < 0 || targetY >= ChessBoard.TileCountY)
                continue;

            ChessPieces occupant = board.GetPieceAt(targetX, targetY);
            bool valid = occupant == null
                || (occupant.team == bishop.team && canSwap)
                || (occupant.team != bishop.team && canAttack);

            if (valid)
                validWarpTiles.Add(new Vector2Int(targetX, targetY));
        }
        return validWarpTiles;
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

    // 기물 주변 8방향 인접 타일 반환
    public List<Vector2Int> GetSurroundingTiles(ChessPieces piece)
    {
        List<Vector2Int> tiles = new List<Vector2Int>();
        if (piece == null) return tiles;

        for (int i = 0; i < SurroundingOffsets.Length; i++)
        {
            int x = piece.currentX + SurroundingOffsets[i].x;
            int y = piece.currentY + SurroundingOffsets[i].y;

            if (x >= 0 && x < ChessBoard.TileCountX && y >= 0 && y < ChessBoard.TileCountY)
                tiles.Add(new Vector2Int(x, y));
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
    #endregion
}
