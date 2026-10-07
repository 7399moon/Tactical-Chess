using System.Collections.Generic;
using UnityEngine;

// ChessInteractionManager의 클릭 입력 분기 및 액티브 스킬 발동 처리 partial 파일.
// 보드 클릭 한 번이 "일반 이동"인지 "액티브 스킬 대상 지정"인지를 구분해 처리하고,
// 왕의 보폭(연속 이동), 긴급 교체 등 클릭에서 파생되는 부가 로직을 담당한다.
public partial class ChessInteractionManager
{
    #region 입력 및 스킬 처리
    // 클릭 이벤트 핸들러: 승급 대상 지정 / 액티브 스킬 모드 처리 / 일반 기물 선택-이동을 분기 수행.
    // 네트워크 대전 중에는 보드 클릭 RPC를 통해 양쪽 클라이언트에서 동일하게 호출되므로,
    // 이 메서드 내부의 모든 판단은 "현재 턴을 진행 중인 팀"(GameManager.CurrentTurn) 기준으로 이뤄져야 한다.
    private void HandleClick(GameObject hitObject)
    {
        if (hitObject == null) return;

        ChessPieces clickedPiece = hitObject.GetComponentInParent<ChessPieces>();

        // 우노 부활 카드: 부활 위치 선택 단계에서는 이 클릭을 부활 처리에만 쓴다
        if (UnoTurnController.Active && UnoTurnController.Instance.Phase == UnoPhase.ReviveSelectTile)
        {
            Vector2Int rt = clickedPiece != null ? new Vector2Int(clickedPiece.currentX, clickedPiece.currentY)
                : (board != null ? board.LookupTileIndex(hitObject) : -Vector2Int.one);
            if (rt != -Vector2Int.one) UnoTurnController.Instance.TryHandleReviveClick(rt.x, rt.y);
            return;
        }

        // 2026-09-21 수정(1-2): 팀별 pendingPromotions 딕셔너리로 바뀌면서, "이 클릭이 지금 대기 중인
        // 어떤 팀의 승급 요청 대상인가"를 클릭된 기물의 팀으로 직접 판별한다. 예전에는 팀 구분 없는
        // 단일 플래그(isWaitingForPromotionTarget)라서, 누군가의 승급이 대기 중이기만 하면 그와 무관한
        // 모든 클릭(다른 팀의 정상적인 이동/스킬 클릭 포함)까지 전부 여기서 삼켜버리는 문제가 있었다.
        if (clickedPiece != null && IsTeamAwaitingPromotionTarget(clickedPiece.team))
        {
            TryExecuteTargetPromotion(clickedPiece);
            return; // 승급 처리 시 일반 이동/스킬 로직 진행 안 함
        }

        Vector2Int tilePos = -Vector2Int.one;

        if (clickedPiece != null)
            tilePos = new Vector2Int(clickedPiece.currentX, clickedPiece.currentY);
        else if (board != null)
            tilePos = board.LookupTileIndex(hitObject);

        if (tilePos == -Vector2Int.one) return;

        int actingTeam = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : 0;

        // 1. 액티브 스킬 발동 모드 처리
        // (예전에는 "GameManager.CurrentTurn == 0"으로 고정되어 있어 흑팀 차례에는 스킬 클릭 분기 자체가
        //  동작하지 않았다. 이 클릭은 이미 "현재 턴 팀"의 것으로 검증되어 릴레이된 클릭이므로 팀 제한 없이 처리한다)
        if (SkillUIManager.Instance != null && SkillUIManager.Instance.ActiveSkillType != PendingSkillType.None)
        {
            if (GameManager.Instance != null)
            {
                PendingSkillType activeSkill = SkillUIManager.Instance.ActiveSkillType;
                bool skillSuccess = false;

                switch (activeSkill)
                {
                    case PendingSkillType.Knight:
                        ChessPieces knight = SkillUIManager.Instance.MovedPieceThisTurn;
                        if (knight != null && clickedPiece != null && clickedPiece.team != knight.team)
                        {
                            // 유니크1 [다중 위협]: 보유 시 대상을 2명 순차 클릭으로 지정.
                            // 2026-10-03 수정: 이미 선택해 둔 대상을 다시 클릭하면, 목표 인원(2명)을 다
                            // 채우지 못했어도 "지금까지 고른 인원만으로 바로 발동"으로 해석한다. 기존에는
                            // 사정거리 안에 적이 1명뿐이면 2번째 대상을 고를 수 없어 위협을 영영 쓸 수 없었다.
                            if (pendingMultiTargets.Contains(clickedPiece))
                            {
                                if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.TryUseThreat(knight, new List<ChessPieces>(pendingMultiTargets), bypassMinimumCount: true))
                                {
                                    Debug.Log("[Skill] 나이트 위협 발동 성공 (대상 재클릭으로 조기 발동)");
                                    skillSuccess = true;
                                }
                                pendingMultiTargets.Clear();
                                break;
                            }

                            int requiredCount = (AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(knight.team, "knight_threat_target_up")) ? 2 : 1;
                            pendingMultiTargets.Add(clickedPiece);

                            if (pendingMultiTargets.Count < requiredCount)
                            {
                                Debug.Log($"[Skill] 위협 대상 선택됨 ({pendingMultiTargets.Count}/{requiredCount}). 대상을 더 선택하거나, 선택한 대상을 다시 클릭하면 그 인원만으로 발동합니다.");
                                CenterAnnouncer.Show("위협 대상을 한 명 더 선택하거나, 선택한 대상을 다시 클릭하면 그 인원만으로 발동합니다.");
                                return; // 아직 대상 선택 중이므로 이번 클릭은 여기서 종료
                            }

                            if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.TryUseThreat(knight, new List<ChessPieces>(pendingMultiTargets)))
                            {
                                Debug.Log("[Skill] 나이트 위협 발동 성공");
                                skillSuccess = true;
                            }
                            pendingMultiTargets.Clear();
                        }
                        break;

                    case PendingSkillType.Rook:
                        ChessPieces rook = SkillUIManager.Instance.MovedPieceThisTurn;
                        if (rook != null && clickedPiece != null && clickedPiece.team == rook.team)
                        {
                            // 2026-10-04 수정: 나이트 위협과 동일하게, 이미 선택해 둔 대상을 다시 클릭하면
                            // 목표 인원(2명)을 다 채우지 못했어도 지금까지 고른 인원만으로 바로 발동한다.
                            if (pendingMultiTargets.Contains(clickedPiece))
                            {
                                if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.TryUseShield(rook, new List<ChessPieces>(pendingMultiTargets), bypassMinimumCount: true))
                                {
                                    Debug.Log("[Skill] 룩 쉴드 발동 성공 (대상 재클릭으로 조기 발동)");
                                    skillSuccess = true;
                                }
                                pendingMultiTargets.Clear();
                                break;
                            }

                            // 유니크3 [광역 수호]: 보유 시 대상을 2명 순차 클릭으로 지정
                            int requiredCount = (AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(rook.team, "rook_shield_target_up")) ? 2 : 1;
                            pendingMultiTargets.Add(clickedPiece);

                            if (pendingMultiTargets.Count < requiredCount)
                            {
                                Debug.Log($"[Skill] 쉴드 대상 선택됨 ({pendingMultiTargets.Count}/{requiredCount}). 대상을 더 선택하거나, 선택한 대상을 다시 클릭하면 그 인원만으로 발동합니다.");
                                CenterAnnouncer.Show("수호 대상을 한 명 더 선택하거나, 선택한 대상을 다시 클릭하면 그 인원만으로 발동합니다.");
                                return;
                            }

                            if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.TryUseShield(rook, new List<ChessPieces>(pendingMultiTargets)))
                            {
                                Debug.Log("[Skill] 룩 쉴드 발동 성공");
                                skillSuccess = true;
                            }
                            pendingMultiTargets.Clear();
                        }
                        break;

                    case PendingSkillType.Bishop:
                        ChessPieces bishop = SkillUIManager.Instance.SelectedBishop;
                        if (bishop != null)
                        {
                            List<Vector2Int> validWarpTiles = SkillUIManager.Instance.GetWarpTiles(bishop, board);
                            if (validWarpTiles != null && validWarpTiles.Contains(tilePos) && PieceSkillManager.Instance != null && PieceSkillManager.Instance.TryUseWarp(bishop, tilePos, board))
                            {
                                bishop.transform.position = board.GetTileCenter(tilePos.x, tilePos.y);

                                // 유니크2 [연속 워프]: 아직 두 번째 워프 기회가 남아 있다면 스킬 모드를 유지
                                if (PieceSkillManager.Instance.CanWarpAgain(bishop.team))
                                {
                                    Debug.Log("[Skill] 비숍 워프 발동 성공 (연속 워프: 1회 더 사용 가능)");
                                    if (tileHighlighter != null) tileHighlighter.ClearCustomHighlights();
                                    SkillUIManager.Instance.ContinueBishopWarp(bishop);
                                    DeselectPiece();
                                    return;
                                }

                                SkillUIManager.Instance.OnBishopWarpExecuted(bishop, tilePos);
                                Debug.Log("[Skill] 비숍 워프 발동 성공");
                                skillSuccess = true;
                            }
                        }
                        break;

                    case PendingSkillType.Queen:
                        // 2026-10-05 수정: 아군 기물만 승급 대상으로 선택 가능하도록 actingTeam 검증 추가
                        // (자세한 이유는 QueenSkill.TryPromoteTarget 주석 참고)
                        if (clickedPiece != null && QueenSkill.Instance != null && QueenSkill.Instance.TryPromoteTarget(clickedPiece, actingTeam))
                        {
                            Debug.Log("[Skill] 퀸 아우라 승급 성공");
                            skillSuccess = true;
                        }
                        break;

                    case PendingSkillType.King:
                        ChessPieceType kingType = ChessPieceTeamUtil.ResolveForTeam(ChessPieceType.WhiteKing, actingTeam);
                        ChessPieces king = FindPieceOnBoard(kingType, actingTeam);
                        if (clickedPiece != null && PieceSkillManager.Instance != null && PieceSkillManager.Instance.TryUseCommand(king, clickedPiece))
                        {
                            Debug.Log("[Skill] 킹 지휘 발동 성공");
                            skillSuccess = true;

                            // 레전더리5(섭정) 보유 여부는 AugmentManager에 직접 질의한다.
                            // (예전에는 SkillUIManager.KeepTurnOnKingCommand라는 팀 구분 없는 플랫 bool을 사용해서,
                            //  한쪽 팀만 섭정을 보유해도 다른 팀에게까지 "턴 유지" 효과가 새는 문제가 있었다)
                            bool keepTurn = AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(actingTeam, "king_regency");
                            if (!keepTurn)
                                SkillUIManager.Instance.OnEndTurnButtonClicked();
                        }
                        break;
                }

                if (skillSuccess)
                {
                    SkillUIManager.Instance.ClearPendingSkill();
                    if (tileHighlighter != null) tileHighlighter.ClearCustomHighlights();
                    DeselectPiece();
                    return;
                }
            }
        }

        // 2. 일반 이동 및 기물 선택/변경 처리
        if (SelectedPiece != null && availableMoves.Contains(tilePos))
        {
            ChessPieces targetPiece = board != null ? board.GetPieceAt(tilePos.x, tilePos.y) : null;

            // 긴급 교체 스킬 발동 조건: 이동 타깃에 아군 기물이 존재하는 경우
            if (targetPiece != null && targetPiece.team == SelectedPiece.team)
            {
                if (board != null)
                {
                    board.SwapPieces(new Vector2Int(SelectedPiece.currentX, SelectedPiece.currentY), tilePos);

                    AugmentManager.Instance?.ConsumeEmergencySwapUse(SelectedPiece.team);
                    if (AugmentManager.Instance != null)
                        SetRemainingSwapCount(SelectedPiece.team, AugmentManager.Instance.GetEmergencySwapUsesLeft(SelectedPiece.team));

                    DeselectPiece();
                }
            }
            else if (pieceMovement != null)
            {
                ChessPieces movedPiece = SelectedPiece;

                // 2026-10-05 수정: "왕의 보폭" 보너스 이동이 대기 중일 때는 그 킹 기물만 이동할 수
                // 있어야 하는데, 예전에는 아무 기물이나 이동하면 그 보너스를 가로채 소모해버리는
                // 버그가 있었다. 여기서 먼저 막아 아예 다른 기물의 이동 자체가 진행되지 않게 한다.
                if (SkillUIManager.Instance != null && SkillUIManager.Instance.IsKingDoubleMoveActive
                    && SkillUIManager.Instance.KingDoubleMovePiece != movedPiece)
                {
                    CenterAnnouncer.Show("왕의 보폭 효과가 활성화된 동안에는 킹만 이동할 수 있습니다.");
                    DeselectPiece();
                    return;
                }

                // 1. 이동 횟수 증가 및 왕의 보폭(노말3) 처리
                movedPiece.moveCountThisTurn++;
                ProcessKingDoubleMove(movedPiece);

                // 2. 기물 선택 해제
                DeselectPiece();

                // 3. PieceMovement로 이동 및 턴 종료 제어권 전달
                //    (턴을 끝낼지 말지는 SkillUIManager.IsKingDoubleMoveActive를 참고해 PieceMovement 내부에서 결정)
                pieceMovement.MovePiece(movedPiece, tilePos);
            }
        }
        else if (clickedPiece != null)
        {
            // 기존 선택 기물과 다른 기물 클릭 시 처리
            if (SelectedPiece == null || clickedPiece.team == SelectedPiece.team)
            {
                DeselectPiece();
                if (CanSelect(clickedPiece))
                    SelectPiece(clickedPiece);
            }
            else
            {
                DeselectPiece();
            }
        }
        else
        {
            DeselectPiece();
        }
    }

    // 팀의 긴급 교체 잔여 횟수를 조회
    public int GetRemainingSwapCount(int team)
    {
        if (swapRemainingCount != null && swapRemainingCount.ContainsKey(team))
        {
            return swapRemainingCount[team];
        }
        return 0;
    }

    // 팀의 긴급 교체 잔여 횟수를 갱신 (0 미만으로 내려가지 않도록 보정)
    public void SetRemainingSwapCount(int team, int count)
    {
        if (swapRemainingCount == null)
        {
            swapRemainingCount = new Dictionary<int, int>();
        }
        swapRemainingCount[team] = Mathf.Max(0, count);
    }

    // 노말3 "왕의 보폭": 킹이 이번 턴 처음 움직였다면 추가 이동 1회를 부여해 턴을 유지시킨다
    private void ProcessKingDoubleMove(ChessPieces movedPiece)
    {
        if (SkillUIManager.Instance == null) return;

        // 2026-10-05 수정: 호출 시점에는 이미 HandleClick에서 "보너스 대기 중이면 그 킹만 이동
        // 가능"함을 검증하고 들어온 상태이므로, 여기서 IsKingDoubleMoveActive가 true라면
        // movedPiece는 반드시 KingDoubleMovePiece와 같은 킹이다 - 안전하게 소모 처리한다.
        if (SkillUIManager.Instance.IsKingDoubleMoveActive)
        {
            SkillUIManager.Instance.ClearKingDoubleMove();
            return;
        }

        bool isKing = movedPiece.type == ChessPieceType.WhiteKing || movedPiece.type == ChessPieceType.BlackKing;
        if (!isKing) return;

        bool hasKingStride = AugmentManager.Instance != null &&
                             AugmentManager.Instance.HasAugment(movedPiece.team, "king_range_up");

        // 킹의 이번 턴 첫 이동이고 증강을 보유했다면 보너스 이동 1회 부여
        if (hasKingStride && movedPiece.moveCountThisTurn == 1)
        {
            Debug.Log("[왕의 보폭] 추가 이동 1회 가능! 턴이 유지됩니다.");
            CenterAnnouncer.Show("왕의 보폭 효과로 추가 이동 1회가 주어졌습니다.");
            SkillUIManager.Instance.SetKingDoubleMovePending(movedPiece);
        }
    }

    // 스킬 대기 상태에 따라 보드 하이라이트를 변경
    private void HandleSkillPendingChanged(PendingSkillType skillType)
    {
        tileHighlighter.ClearCustomHighlights();
        pendingMultiTargets.Clear();

        if (SkillUIManager.Instance == null) return;

        ChessPieces piece = null;
        if (skillType == PendingSkillType.Knight || skillType == PendingSkillType.Rook)
            piece = SkillUIManager.Instance.MovedPieceThisTurn;

        if (piece != null)
            tileHighlighter.HighlightCustomTiles(SkillUIManager.Instance.GetSurroundingTiles(piece, skillType));
    }
    #endregion

    #region 보조 유틸리티
    // 기물의 선택 외곽선(VFX)을 설정
    private void SetPieceOutline(ChessPieces piece, bool enabled)
    {
        if (!piece.TryGetComponent<PieceSelectionVFX>(out var vfx))
            vfx = piece.gameObject.AddComponent<PieceSelectionVFX>();

        vfx.SetOutline(enabled);
    }

    // 보드 위에서 특정 타입 및 팀의 기물 탐색
    private ChessPieces FindPieceOnBoard(ChessPieceType type, int team)
    {
        if (board == null || board.Pieces == null) return null;

        for (int x = 0; x < ChessBoard.TileCountX; x++)
        {
            for (int y = 0; y < ChessBoard.TileCountY; y++)
            {
                ChessPieces p = board.Pieces[x, y];
                if (p != null && p.type == type && p.team == team)
                    return p;
            }
        }
        return null;
    }
    #endregion
}
