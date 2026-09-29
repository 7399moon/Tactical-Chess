using System.Collections;
using UnityEngine;

// 실제 기물의 물리/연출 이동, 포물선 애니메이션 및 특수 규칙(캐슬링, 앙파상, 프로모션, 긴급 교체) 처리를 담당하는 클래스.
public class PieceMovement : MonoBehaviour
{
    #region 인스펙터 설정값
    [Header("References")]
    [SerializeField] private ChessBoard board;
    [SerializeField] private PieceCapture pieceCapture;

    [Header("Animation Settings")]
    [SerializeField] private float moveDuration = 0.4f; // 이동 애니메이션 시간
    [SerializeField] private float arcHeight = 1.0f;     // 이동 포물선 높이
    #endregion

    #region 이동 핵심 로직
    // 지정한 기물을 target 좌표로 이동시키고 특수 수 및 이동 가능 조건 검사를 수행
    public void MovePiece(ChessPieces piece, Vector2Int target)
    {
        if (piece == null || board == null) return;

        ChessPieces targetPiece = board.GetPieceAt(target.x, target.y);

        // 1. 이동 및 공격 상태 검사 (스킬 및 상태 효과 판정)
        if (targetPiece != null && targetPiece.team != piece.team)
        {
            if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsShielded(targetPiece))
            {
                Debug.Log("대상 기물은 쉴드 상태여서 공격할 수 없습니다.");
                return;
            }
        }

        if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsImmobilized(piece))
        {
            Debug.Log("위협 상태로 인해 이동 불가능합니다.");
            return;
        }

        if (SkillUIManager.Instance != null && SkillUIManager.Instance.IsWarpPendingMove)
        {
            if (SkillUIManager.Instance.WarpBishopPiece != piece)
            {
                Debug.Log("워프 직후에는 워프한 비숍만 이동할 수 있습니다.");
                return;
            }
        }

        // 킹 지휘(commandedPiece/commandMovesLeft)는 팀별로 나뉘지 않는 단일 상태이지만, "지휘 대상만
        // 이동 가능"이라는 제약 자체는 지휘를 사용한 팀의 턴에만 걸려야 한다.
        // 2026-09-23 수정: 예전에는 팀 구분 없이 무조건 지휘 대상 기물 외의 모든 이동을 막아버렸다.
        // 섭정처럼 같은 턴 안에서 2회 이동이 곧바로 끝나는 경우가 아니라면(=일반적인 지휘 사용 시),
        // 지휘를 사용한 직후 턴이 상대에게 넘어가도 이 제약이 그대로 걸려 있어 상대가 자기 턴에
        // 아무 기물도 움직일 수 없이 완전히 멈춰버리는 심각한 버그였다. 이제 "지금이 지휘 대상 기물
        // 팀의 턴인가"를 함께 확인해 상대 턴에는 이 제약이 전혀 작동하지 않도록 한다.
        if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsCommandActive)
        {
            ChessPieces commandTarget = PieceSkillManager.Instance.CommandedPiece;
            bool isCommandTeamsTurn = commandTarget != null && GameManager.Instance != null
                && GameManager.Instance.CurrentTurn == commandTarget.team;

            if (isCommandTeamsTurn)
            {
                // 지휘 대상이 상대 턴 사이에 위협을 당하거나 아군 기물에 완전히 막히는 등으로 더 이상
                // 이동할 수 없는 상태가 되면, 지휘가 영원히 완료되지 못해 게임이 멈추므로 강제 종료한다.
                if (!PieceSkillManager.Instance.CommandTargetHasLegalMove())
                {
                    Debug.Log("[지휘] 지휘 대상 기물이 더 이상 이동할 수 없어 지휘 효과를 종료합니다.");
                    PieceSkillManager.Instance.ForceEndCommand();
                }
                else if (piece != commandTarget)
                {
                    Debug.LogWarning("지휘 스킬이 활성화된 상태에서는 지휘 대상 기물만 이동할 수 있습니다.");
                    return;
                }
            }
        }

        // 2. 이동 전 상태 데이터 계산
        int fromX = piece.currentX;
        int fromY = piece.currentY;

        bool isKing = IsKing(piece);
        bool isPawn = IsPawn(piece);
        bool isCastling = isKing && Mathf.Abs(target.x - fromX) == 2;
        bool isEnPassant = isPawn && target.x != fromX && board.GetPieceAt(target.x, target.y) == null;
        bool isDoubleStep = isPawn && Mathf.Abs(target.y - fromY) == 2;
        bool isCapture = (targetPiece != null && targetPiece.team != piece.team) || isEnPassant;

        // 3. 기물 캡처 연출 및 보드 데이터 제거
        HandleCapture(piece, target, fromY, isEnPassant);

        // 캡처가 아닌 일반 이동일 때만 이동 사운드 재생 (캡처는 PieceCapture.CapturePieceAt에서, 워프는 별도 경로라 여기를 타지 않음)
        if (!isCapture)
            SoundManager.Instance?.PlayMove();

        // 4. 보드 인덱스 배열 및 기물 정보 업데이트
        board.SetPieceAt(fromX, fromY, null);
        board.SetPieceAt(target.x, target.y, piece);

        piece.currentX = target.x;
        piece.currentY = target.y;
        piece.hasMoved = true;

        // 5. 지휘 스킬 횟수 차감
        // 2026-09-23 수정: IsCommandActive만 보고 차감하면, 지휘 대상이 아닌 다른 기물(특히 상대 팀이
        // 자기 턴에 정상적으로 두는 수)까지 지휘로 인한 이동으로 잘못 집계되어 지휘 잔여 횟수가
        // 엉뚱하게 소모되고 그 턴 종료 처리까지 건너뛰는 문제가 있었다. 실제 지휘 대상 기물이 움직인
        // 경우에만 차감하도록 제한한다.
        bool wasCommandMove = false;
        if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsCommandActive
            && piece == PieceSkillManager.Instance.CommandedPiece)
        {
            wasCommandMove = true;
            PieceSkillManager.Instance.OnCommandPieceMoved();
        }

        // 6. 이동 포물선 애니메이션 및 캐슬링 실행
        StartCoroutine(MovePieceAnimated(piece, board.GetTileCenter(target.x, target.y)));

        if (isCastling)
            MoveCastlingRook(fromX, fromY, target.x);

        UpdateEnPassantState(piece, fromX, fromY, isDoubleStep);

        // 7. 프로모션 검사 (발생 시 턴 진행 보류)
        if (CheckPromotion(piece, target))
        {
            return;
        }

        // 8. 지휘 첫 번째 이동 완료 시 연속 이동 허용 (턴 종료 대기)
        if (wasCommandMove && PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsCommandActive)
        {
            Debug.Log("지휘 연속 이동: 1회 추가 이동 가능");
            if (ChessInteractionManager.Instance != null)
            {
                ChessInteractionManager.Instance.DeselectPiece();
            }
            return;
        }

        // 8-1. 노말3 "왕의 보폭": ChessInteractionManager.ProcessKingDoubleMove가 이번 이동으로
        // 보너스 이동권을 부여했다면(IsKingDoubleMoveActive == true), 턴을 끝내지 않고 대기한다.
        if (SkillUIManager.Instance != null && SkillUIManager.Instance.IsKingDoubleMoveActive)
        {
            Debug.Log("[왕의 보폭] 추가 이동 대기 중: 턴을 유지합니다.");
            if (ChessInteractionManager.Instance != null)
            {
                ChessInteractionManager.Instance.DeselectPiece();
            }
            return;
        }

        // 9. 이동 후 UI 이벤트 통지 및 턴 상태 갱신
        if (SkillUIManager.Instance != null)
            SkillUIManager.Instance.OnPieceMoved(piece);
    }

    // 대상 위치의 적 기물 또는 앙파상 타겟 기물 캡처 위임
    private void HandleCapture(ChessPieces piece, Vector2Int target, int fromY, bool isEnPassant)
    {
        if (pieceCapture == null) return;

        if (isEnPassant)
        {
            ChessPieces capturedPawn = board.GetPieceAt(target.x, fromY);
            if (capturedPawn != null)
            {
                pieceCapture.CapturePieceAt(capturedPawn, piece);
                board.SetPieceAt(target.x, fromY, null);
            }
            return;
        }

        ChessPieces targetPiece = board.GetPieceAt(target.x, target.y);
        if (targetPiece != null && targetPiece.team != piece.team)
        {
            pieceCapture.CapturePieceAt(targetPiece, piece);
        }
    }
    #endregion

    #region 특수 규칙 (캐슬링, 앙파상, 프로모션)
    // 캐슬링
    private void MoveCastlingRook(int kingFromX, int fromY, int kingToX)
    {
        int direction = kingToX > kingFromX ? 1 : -1;
        int rookFromX = direction > 0 ? 7 : 0;
        int rookToX = kingToX - direction;

        ChessPieces rook = board.GetPieceAt(rookFromX, fromY);
        if (rook == null) return;

        board.SetPieceAt(rookFromX, fromY, null);
        board.SetPieceAt(rookToX, fromY, rook);

        rook.currentX = rookToX;
        rook.currentY = fromY;
        rook.hasMoved = true;

        StartCoroutine(MovePieceAnimated(rook, board.GetTileCenter(rookToX, fromY)));
    }

    // 앙 파상
    private void UpdateEnPassantState(ChessPieces piece, int fromX, int fromY, bool isDoubleStep)
    {
        if (GameManager.Instance == null) return;

        if (isDoubleStep)
        {
            int passedY = (fromY + piece.currentY) / 2;
            GameManager.Instance.SetEnPassantTarget(new Vector2Int(fromX, passedY));
        }
        else
        {
            GameManager.Instance.ClearEnPassantTarget();
        }
    }

    // 프로모션
    private bool CheckPromotion(ChessPieces movedPiece, Vector2Int targetPos)
    {
        bool isPromotion = false;

        if (movedPiece.type == ChessPieceType.WhitePawn && targetPos.y == 7)
            isPromotion = true;
        else if (movedPiece.type == ChessPieceType.BlackPawn && targetPos.y == 0)
            isPromotion = true;

        if (isPromotion)
        {
            if (CardSelectionManager.Instance != null)
                CardSelectionManager.Instance.ShowCardSelection(true, targetPos, movedPiece.team);

            return true;
        }

        return false;
    }
    #endregion

    #region 이동 애니메이션 코루틴 및 헬퍼
    // 기물을 시작 위치에서 목표 위치까지 포물선을 그리며 이동시키는 연출 코루틴
    private IEnumerator MovePieceAnimated(ChessPieces piece, Vector3 targetPosition)
    {
        Vector3 startPosition = piece.transform.position;
        float duration = Mathf.Max(0.0001f, moveDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);

            Vector3 flatPosition = Vector3.Lerp(startPosition, targetPosition, normalized);
            float height = Mathf.Sin(normalized * Mathf.PI) * arcHeight;

            piece.transform.position = flatPosition + Vector3.up * height;
            yield return null;
        }

        piece.transform.position = targetPosition;
    }

    // 기물 종류 판별용 유틸
    private static bool IsKing(ChessPieces piece) => piece.type == ChessPieceType.WhiteKing || piece.type == ChessPieceType.BlackKing;
    private static bool IsPawn(ChessPieces piece) => piece.type == ChessPieceType.WhitePawn || piece.type == ChessPieceType.BlackPawn;
    #endregion
}
