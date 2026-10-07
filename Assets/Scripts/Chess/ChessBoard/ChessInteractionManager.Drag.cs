using UnityEngine;

// ChessInteractionManager의 기물 드래그 이동(마우스로 집어서 옮기는 방식) 처리 partial 파일.
// BoardInputHandler가 매 프레임 발행하는 드래그 이벤트를 받아 기물을 시각적으로 들어 올려 마우스를
// 따라가게 하고, 드래그 중 가리키는 칸을 초록색으로 강조하며, 손을 떼면 그 칸이 이동 가능한 칸일 때만
// 실제 이동을 실행한다. 스킬 모드 중에는 BoardInputHandler가 애초에 드래그 이벤트를 발행하지 않으므로
// 이 파일은 관여하지 않고, 기존 클릭 기반 스킬 발동(ChessInteractionManager.Skills.cs)이 그대로 동작한다.
public partial class ChessInteractionManager
{
    #region 인스펙터 설정값 (드래그 상승 애니메이션)
    // 기물을 처음 집어 든 순간부터 공중으로 완전히 떠오르기까지 걸리는 시간(초).
    // 0이면 기존처럼 즉시(한 프레임 만에) 목표 높이로 순간이동한다.
    [Header("Drag Rise Animation")]
    [SerializeField] private float riseDuration = 0.3f;
    #endregion

    #region 드래그 상태
    private Vector2Int? dragHighlightedTile; // 현재 초록색으로 강조 중인 칸(없으면 null)

    // 상승 애니메이션 진행 중인 기물과 그 타이밍 정보. 드래그 대상이 바뀌면(새로 집어 들면) 초기화되어
    // 매번 보드 높이에서부터 다시 부드럽게 떠오른다.
    private ChessPieces risingPiece;
    private float riseStartTime;
    private float riseStartY;
    #endregion

    #region 유니티 생명주기 연결 (Start/OnDestroy에서 구독/해제)
    private void SubscribeDragEvents()
    {
        if (inputHandler != null)
        {
            inputHandler.OnPieceDragUpdate += HandleDragUpdate;
            inputHandler.OnPieceDragEnd += HandleDragEnd;
        }
    }

    private void UnsubscribeDragEvents()
    {
        if (inputHandler != null)
        {
            inputHandler.OnPieceDragUpdate -= HandleDragUpdate;
            inputHandler.OnPieceDragEnd -= HandleDragEnd;
        }
    }
    #endregion

    #region 드래그 이벤트 처리
    // 드래그 중 매 프레임 호출: 기물을 마우스 위치로 이동시키고, 이동 가능한 칸 위에 있을 때만 초록색 강조
    private void HandleDragUpdate(ChessPieces piece, Vector3 worldPoint, Vector2Int hoveredTile)
    {
        if (piece == null) return;

        // 이 기물이 실제로 선택(이동 가능 판정 통과)된 상태가 아니면 드래그를 무시하고 제자리 유지
        if (piece != SelectedPiece)
        {
            piece.transform.position = board.GetTileCenter(piece.currentX, piece.currentY);
            ClearDragHighlight();
            ResetRiseState();
            return;
        }

        // 드래그가 막 시작된 경우(새로 집어 든 기물) 상승 애니메이션 타이머를 그 시점의 높이로 초기화
        if (piece != risingPiece)
        {
            risingPiece = piece;
            riseStartTime = Time.time;
            riseStartY = piece.transform.position.y;
        }

        // 수평(X, Z) 위치는 기존처럼 마우스를 즉시 따라가되, 높이(Y)만 riseDuration에 걸쳐
        // 부드럽게(ease-in-out) 목표 높이까지 떠오르도록 한다.
        float t = riseDuration > 0f ? Mathf.Clamp01((Time.time - riseStartTime) / riseDuration) : 1f;
        float easedT = t * t * (3f - 2f * t); // smoothstep
        float currentY = Mathf.Lerp(riseStartY, worldPoint.y, easedT);

        piece.transform.position = new Vector3(worldPoint.x, currentY, worldPoint.z);

        bool isLegalHoverTile = hoveredTile != -Vector2Int.one && availableMoves.Contains(hoveredTile);
        Vector2Int? newHighlight = isLegalHoverTile ? hoveredTile : (Vector2Int?)null;

        if (newHighlight != dragHighlightedTile)
        {
            tileHighlighter?.SetDragHoverTile(newHighlight);
            dragHighlightedTile = newHighlight;
        }
    }

    // 손을 뗐을 때 호출: 이동 가능한 칸 위였다면 기존 클릭-이동 경로(네트워크 중계 포함)를 그대로 재사용해 이동 실행,
    // 아니었다면 원래 있던 칸으로 복귀
    private void HandleDragEnd(ChessPieces piece, Vector2Int releaseTile)
    {
        ClearDragHighlight();
        ResetRiseState();

        if (piece == null) return;

        if (piece != SelectedPiece)
        {
            piece.transform.position = board.GetTileCenter(piece.currentX, piece.currentY);
            return;
        }

        bool isLegalRelease = releaseTile != -Vector2Int.one && availableMoves.Contains(releaseTile);

        if (!isLegalRelease)
        {
            piece.transform.position = board.GetTileCenter(piece.currentX, piece.currentY);
            DeselectPiece();
            return;
        }

        // 드래그로 이미 목표 칸 근처까지 옮겨온 위치를 그대로 유지한다 - 이동 실행 시
        // PieceMovement의 이동 애니메이션이 "현재(드래그로 옮겨온) 위치"에서 목표 칸까지 짧게
        // 이어지므로 포물선이 원래 칸에서 다시 시작되는 부자연스러운 연출이 생기지 않는다.
        GameObject releaseObject = board.GetPieceAt(releaseTile.x, releaseTile.y) != null
            ? board.GetPieceAt(releaseTile.x, releaseTile.y).gameObject
            : board.GetTileObject(releaseTile.x, releaseTile.y);

        OnLocalBoardClicked(releaseObject);
    }

    private void ClearDragHighlight()
    {
        if (dragHighlightedTile == null) return;
        tileHighlighter?.SetDragHoverTile(null);
        dragHighlightedTile = null;
    }

    // 다음 드래그 시작 시 처음부터 다시 부드럽게 떠오르도록 상승 애니메이션 상태를 초기화
    private void ResetRiseState()
    {
        risingPiece = null;
    }
    #endregion
}
