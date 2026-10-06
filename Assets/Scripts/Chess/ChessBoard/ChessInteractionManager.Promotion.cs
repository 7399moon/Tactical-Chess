using System.Collections.Generic;
using UnityEngine;

// ChessInteractionManager의 증강 즉시 승급 처리 partial 파일.
// "카드 선택 완료 -> 승급시킬 아군 기물을 보드에서 직접 클릭" 흐름의 대기 상태와 유효성 검사를 담당한다.
//
// 2026-09-21 수정(1-2): 예전에는 이 대기 상태가 팀 구분 없는 단일 필드(isWaitingForPromotionTarget/
// promotionTeam)였다. 같은 증강 체크포인트에서 White/Black이 동시에 서로 다른 즉시 승급형 증강을
// 뽑는 것도 가능한데(예: 둘 다 "전격 승급" 계열을 고르는 경우), 그 경우 두 번째 StartTargetPromotionMode
// 호출이 첫 번째 팀의 대기 상태를 그냥 덮어써 버려 그 팀은 영원히 승급 대상을 클릭할 수 없게 되는
// 잠재 버그가 있었다. 팀별 Dictionary로 바꿔 두 팀이 동시에 각자의 승급 대상 클릭을 대기할 수 있게 했다.
public partial class ChessInteractionManager
{
    #region 승급 대상 클릭 대기 상태
    // 하나의 대기 중인 승급 요청 정보 (팀별로 최대 1개)
    private struct PendingPromotionRequest // 값 타입: 힙 할당/GC 없이 Dictionary에 저장
    {
        public ChessPieceType targetType;             // 승급 결과로 바뀔 기물 타입
        public ChessPieceType[] requiredSourceTypes;   // 승급 가능한 원본 기물 종류 제한 (null이면 제한 없음)
    }

    // 팀(0=White, 1=Black)별 승급 대상 클릭 대기 상태. 이 상태는 증강 선택 RPC(RpcTargets.All)를 통해
    // 양쪽 클라이언트에서 항상 동일하게 설정되는 "공유 게임 상태"이다(로컬 UI가 아님).
    private readonly Dictionary<int, PendingPromotionRequest> pendingPromotions = new Dictionary<int, PendingPromotionRequest>();

    // 승급 대상 클릭이 실제로 완료(성공)됐을 때 발행되는 이벤트(인자: 완료된 팀).
    // CardSelectionManager가 이를 구독해, 증강 체크포인트의 "턴 진행" 재개를 이 시점까지 미룬다
    // (증강 선택 - 이진 선택(있다면) - 효과 발동/보드 상호작용(있다면) - 턴 진행 순서 보장).
    public event System.Action<int> OnPromotionTargetResolved;

    // 카드 선택 완료 후 승급시킬 기물 클릭 대기 상태 시작
    // validSourceTypes를 비워두면 제한 없음(킹 제외 전체), 하나 이상 지정하면 해당 기물 종류(백/흑 구분 없이)만 허용
    public void StartTargetPromotionMode(int team, ChessPieceType targetType, params ChessPieceType[] validSourceTypes)
    {
        pendingPromotions[team] = new PendingPromotionRequest
        {
            targetType = targetType,
            requiredSourceTypes = (validSourceTypes != null && validSourceTypes.Length > 0) ? validSourceTypes : null
        };

        Debug.Log($"[Augment] 승급시킬 아군 기물을 클릭하세요. (목표: {targetType})");
    }

    // 지정된 팀이 현재 승급 대상 클릭을 대기 중인지 여부.
    // 1-2 버그 수정의 핵심: 이 창이 열려 있는 동안은 그 팀의 보드 클릭이 CurrentTurn과 무관하게
    // 허용되어야 한다(OnLocalBoardClicked 참고).
    public bool IsTeamAwaitingPromotionTarget(int team) => pendingPromotions.ContainsKey(team);

    // 클릭된 기물에 대해 승급 유효성을 검사하고 승급을 실행
    private void TryExecuteTargetPromotion(ChessPieces clickedPiece)
    {
        if (clickedPiece == null) return;
        if (!pendingPromotions.TryGetValue(clickedPiece.team, out PendingPromotionRequest request)) return;

        // 유효성 검사: 이미 clickedPiece.team으로 조회했으므로 팀은 항상 일치하며, 킹이나 동일 기물 등
        // 승급 제외 대상만 추가로 판정한다.
        if (IsValidPromotionTarget(clickedPiece, request))
        {
            int team = clickedPiece.team;
            ChessBoard.Instance.PromotePieceAt(clickedPiece.currentX, clickedPiece.currentY, request.targetType, team);
            pendingPromotions.Remove(team);
            DeselectPiece();

            Debug.Log($"[Augment] {clickedPiece.name} 기물이 {request.targetType}(으)로 성공적으로 승급되었습니다.");

            // 보드 상호작용(승급 대상 지정)까지 완전히 끝났음을 알린다 - CardSelectionManager가
            // 이 시점에야 비로소 이 팀의 증강 체크포인트를 "완료"로 처리하고 턴/타이머를 재개한다.
            OnPromotionTargetResolved?.Invoke(team);
        }
        else
        {
            Debug.LogWarning("승급시킬 수 없는 대상입니다. 아군 기물을 다시 선택하세요.");
        }
    }

    // 기물 타입을 백/흑 구분 없는 종류(폰/나이트/비숍/룩/퀸/킹)로 환산하기 위한 분류
    private enum PromotionSourceCategory { Pawn, Knight, Bishop, Rook, Queen, King, Other }

    // ChessPieceType(백/흑 구분 있음)을 색상 무관 카테고리로 변환
    private static PromotionSourceCategory GetPromotionCategory(ChessPieceType type)
    {
        switch (type)
        {
            case ChessPieceType.WhitePawn:
            case ChessPieceType.BlackPawn:
                return PromotionSourceCategory.Pawn;
            case ChessPieceType.WhiteKnight:
            case ChessPieceType.BlackKnight:
                return PromotionSourceCategory.Knight;
            case ChessPieceType.WhiteBishop:
            case ChessPieceType.BlackBishop:
                return PromotionSourceCategory.Bishop;
            case ChessPieceType.WhiteRook:
            case ChessPieceType.BlackRook:
                return PromotionSourceCategory.Rook;
            case ChessPieceType.WhiteQueen:
            case ChessPieceType.BlackQueen:
                return PromotionSourceCategory.Queen;
            case ChessPieceType.WhiteKing:
            case ChessPieceType.BlackKing:
                return PromotionSourceCategory.King;
            default:
                return PromotionSourceCategory.Other;
        }
    }

    // 클릭한 기물이 이번 승급 요청의 유효한 대상인지 검사 (허용 종류 / 킹 제외 / 동일 타입 제외)
    private bool IsValidPromotionTarget(ChessPieces piece, PendingPromotionRequest request)
    {
        if (piece == null) return false;

        // 1. 특정 소스 기물 제한이 설정되어 있다면 해당 종류(백/흑 무관)와 일치해야 함 (예: 폰 전용, 나이트/비숍 전용, 룩 전용 등)
        if (request.requiredSourceTypes != null && request.requiredSourceTypes.Length > 0)
        {
            bool matches = false;
            PromotionSourceCategory pieceCategory = GetPromotionCategory(piece.type);

            for (int i = 0; i < request.requiredSourceTypes.Length; i++)
            {
                if (pieceCategory == GetPromotionCategory(request.requiredSourceTypes[i]))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches) return false;
        }

        // 2. 킹은 승급 대상에서 제외
        if (piece.type == ChessPieceType.WhiteKing || piece.type == ChessPieceType.BlackKing) return false;

        // 3. 이미 변경하려는 동일 타입 기물인 경우 제외
        if (piece.type == request.targetType) return false;

        return true;
    }
    #endregion
}
