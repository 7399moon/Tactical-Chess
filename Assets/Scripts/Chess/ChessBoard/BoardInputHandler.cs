using System;
using UnityEngine;

// 체스보드 상의 마우스 입력을 감지하고, Raycast를 통해 타일/기물 클릭 및 호버 상태 변경 이벤트를 발행하는 클래스.
// 기물 드래그 이동(누른 채로 마우스를 옮겨 이동 가능한 칸에 놓는 방식) 입력도 함께 감지해 이벤트로 발행한다.
public class BoardInputHandler : MonoBehaviour
{
    #region 이벤트
    public event Action<GameObject> OnObjectClicked;
    public event Action<Vector2Int> OnTileHoverChanged;

    // 드래그 중 매 프레임 발행: 드래그 대상 기물, 들어 올려진 월드 좌표, 현재 가리키고 있는 보드 칸.
    // 실제로 이 기물이 드래그(이동) 가능한 상태인지는 구독 측(ChessInteractionManager)이
    // SelectedPiece 비교로 판단한다 - 여기서는 입력 사실만 전달한다.
    public event Action<ChessPieces, Vector3, Vector2Int> OnPieceDragUpdate;
    // 드래그를 놓았을 때(마우스 버튼을 뗐을 때) 1회 발행: 드래그 대상 기물, 놓은 시점의 보드 칸(-1,-1이면 보드 밖/무효).
    public event Action<ChessPieces, Vector2Int> OnPieceDragEnd;
    #endregion

    #region 인스펙터 설정값
    [Header("References")]
    [SerializeField] private ChessBoard board;

    [Header("Raycast Settings")]
    [SerializeField] private LayerMask hoverableMask;

    [Header("Drag Settings")]
    // 드래그 중 기물이 보드 위로 떠 있는 높이. 가장 큰 기물(킹, 바닥 기준 높이 약 3.48)보다는 높아야
    // 드래그 중인 기물이 다른 기물들의 머리 위를 그냥 지나가며, 다른 기물 메쉬와 겹쳐 부딪히는 것처럼
    // 보이지 않는다. 다만 필요 이상으로 높이면 기물이 과하게 붕 뜬 느낌이 들어, 킹 높이보다 살짝만
    // 더 높은 값(약간의 여유)으로 설정한다.
    [SerializeField] private float dragLiftHeight = 3.6f;
    #endregion

    #region 내부 상태 필드
    private Camera currentCamera;
    private Vector2Int currentHover = -Vector2Int.one;
    private GameObject currentHoveredPiece;

    // 마우스가 같은 오브젝트 위에 머무는 동안 GetComponentInParent를 매 프레임 반복하지 않도록 캐싱
    private Transform cachedHitTransform;
    private ChessPieces cachedHitPiece;

    // 현재 드래그 중인 기물(없으면 null) 및 마지막으로 유효했던 호버 칸(마우스를 뗀 프레임에 레이캐스트가
    // 보드 밖으로 벗어나 있어도 직전 칸 정보를 사용할 수 있도록 캐싱)
    private ChessPieces dragPiece;
    private Vector2Int lastDragHoverTile = -Vector2Int.one;
    // 드래그 중인 기물 자신의 콜라이더. 드래그 시작 시 비활성화해두지 않으면, 기물이 마우스를 따라
    // 떠오른 뒤에는 Raycast가 (목표 타일이 아니라) 바로 이 기물 자신에게 매 프레임 다시 맞아버려서
    // "지금 가리키는 칸"이 항상 원래 있던 칸으로 고정되는 문제가 있었다 - 그 결과 이동 가능 타일 위로
    // 옮겨도 초록색으로 바뀌지 않고, 손을 떼도 항상 "이동 불가" 판정으로 원위치 복귀만 되었다.
    private Collider dragPieceCollider;
    #endregion

    #region 외부 공개 프로퍼티
    public Vector2Int CurrentHover => currentHover;
    #endregion

    #region 유니티 생명주기
    // 매 프레임 마우스 위치를 Raycast로 검사해 호버/클릭/드래그 이벤트를 발행
    private void Update()
    {
        // 카드 선택 UI가 활성화되어 있거나 게임 종료 시, 환경설정 패널이 열려 있을 때 보드 입력 전체 차단
        if ((CardSelectionManager.Instance != null && CardSelectionManager.Instance.IsSelecting) ||
            (GameEndManager.Instance != null && GameEndManager.Instance.IsGameOver) ||
            SettingsManager.IsPanelOpen ||
            // 우노 모드: 아래 카드 패 등 UI 위에서의 클릭이 뒤의 보드 입력으로 새지 않게 막는다
            (UnoTurnController.Active && UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
        {
            ResetHoverStates();
            CancelDrag();
            return;
        }

        // 매 프레임 현재 활성화된 메인 카메라를 다시 조회한다.
        // (팀별로 백/흑 카메라를 켜고 끄며 전환하므로, 한 번 캐싱하면 카메라 전환 후에도
        //  예전 카메라 기준으로 Raycast가 나가는 문제가 생길 수 있어 캐싱하지 않는다.)
        currentCamera = Camera.main;
        if (currentCamera == null) return;

        // 액티브 스킬 발동 모드 중에는 드래그 이동을 사용하지 않는다(스킬 대상 지정은 기존 클릭 방식 그대로 유지).
        bool skillActive = SkillUIManager.Instance != null && SkillUIManager.Instance.ActiveSkillType != PendingSkillType.None;
        if (skillActive)
            CancelDrag();

        // 마우스 포인터 Raycast 검사
        Ray ray = currentCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f, hoverableMask))
        {
            ChessPieces hitPiece = ResolveHitPiece(hitInfo.transform);

            // 1. 기물 위에 마우스가 있는 경우
            if (hitPiece != null)
            {
                HandlePieceHover(hitPiece.gameObject);
                ClearTileMouseHover();
            }
            // 2. 일반 타일 위에 마우스가 있는 경우
            else
            {
                HandlePieceHover(null);
                Vector2Int hitPosition = board.LookupTileIndex(hitInfo.transform.gameObject);
                UpdateTileMouseHover(hitPosition);
            }

            // 마우스 좌클릭 시 이벤트 발생
            if (Input.GetMouseButtonDown(0))
            {
                OnObjectClicked?.Invoke(hitInfo.transform.gameObject);

                // 기물을 누른 경우 드래그 후보로 기록(스킬 모드 중에는 추적하지 않음).
                // 실제로 이 기물을 들어 올려도 되는지(내 턴/내 기물/이동 가능 여부)는
                // ChessInteractionManager가 SelectedPiece 비교로 판단한다.
                if (!skillActive && hitPiece != null)
                {
                    dragPiece = hitPiece;

                    // 드래그 중에는 이 기물 자신의 콜라이더를 꺼서, 이후 프레임의 Raycast가
                    // (마우스를 따라다니는) 기물 자신이 아니라 실제로 가리키는 타일/다른 기물에
                    // 맞도록 한다.
                    dragPieceCollider = hitPiece.GetComponent<Collider>();
                    if (dragPieceCollider != null)
                        dragPieceCollider.enabled = false;
                }
            }

            // 드래그 중 매 프레임 기물을 마우스 위치로 들어 올려 따라가게 하고, 가리키는 칸을 전달
            if (!skillActive && dragPiece != null && Input.GetMouseButton(0))
            {
                Vector2Int hoveredTile = hitPiece != null
                    ? new Vector2Int(hitPiece.currentX, hitPiece.currentY)
                    : board.LookupTileIndex(hitInfo.transform.gameObject);

                lastDragHoverTile = hoveredTile;

                Vector3 liftedPoint = new Vector3(hitInfo.point.x, board.YOffset + dragLiftHeight, hitInfo.point.z);
                OnPieceDragUpdate?.Invoke(dragPiece, liftedPoint, hoveredTile);
            }
        }
        else
        {
            ResetHoverStates();
        }

        // 마우스 버튼을 뗀 순간 드래그 종료 처리(이번 프레임 레이캐스트 적중 여부와 무관하게 항상 검사)
        if (Input.GetMouseButtonUp(0) && dragPiece != null)
        {
            OnPieceDragEnd?.Invoke(dragPiece, lastDragHoverTile);
            RestoreDragPieceCollider();
            dragPiece = null;
            lastDragHoverTile = -Vector2Int.one;
        }
    }
    #endregion

    #region 드래그 상태 관리
    // 드래그 중인 기물이 있다면 "놓인 칸 없음(-1,-1)"으로 즉시 종료 이벤트를 발행해 원래 자리로 복귀시킨다.
    // (환경설정 패널이 열리거나 스킬 모드로 전환되는 등 드래그를 더 이상 진행할 수 없는 상황에 사용)
    private void CancelDrag()
    {
        if (dragPiece == null) return;
        OnPieceDragEnd?.Invoke(dragPiece, -Vector2Int.one);
        RestoreDragPieceCollider();
        dragPiece = null;
        lastDragHoverTile = -Vector2Int.one;
    }

    // 드래그 시작 시 꺼두었던 콜라이더를 다시 켠다(드래그 종료/취소 경로 모두에서 호출).
    private void RestoreDragPieceCollider()
    {
        if (dragPieceCollider != null)
            dragPieceCollider.enabled = true;
        dragPieceCollider = null;
    }
    #endregion

    #region 호버 상태 관리
    // 맞은 Transform이 바뀌었을 때만 부모 기물 컴포넌트를 다시 조회한다.
    private ChessPieces ResolveHitPiece(Transform hit)
    {
        if (hit != cachedHitTransform)
        {
            cachedHitTransform = hit;
            cachedHitPiece = hit.GetComponentInParent<ChessPieces>();
        }
        return cachedHitPiece;
    }

    // 마우스 호버 상태인 타일 위치를 갱신하고 타일 하이라이트를 업데이트
    private void UpdateTileMouseHover(Vector2Int hitPosition)
    {
        if (hitPosition == -Vector2Int.one)
        {
            ClearTileMouseHover();
            return;
        }

        if (currentHover == hitPosition) return;

        Vector2Int oldHover = currentHover;
        currentHover = hitPosition;

        if (oldHover != -Vector2Int.one)
            BoardTileHighlighter.Instance?.RefreshTileColor(oldHover, currentHover);

        if (currentHover != -Vector2Int.one)
            BoardTileHighlighter.Instance?.RefreshTileColor(currentHover, currentHover);

        OnTileHoverChanged?.Invoke(currentHover);
    }

    // 타일 호버 상태를 해제하고 하이라이트를 복원
    private void ClearTileMouseHover()
    {
        if (currentHover == -Vector2Int.one) return;

        Vector2Int oldHover = currentHover;
        currentHover = -Vector2Int.one;

        BoardTileHighlighter.Instance?.RefreshTileColor(oldHover, currentHover);
        OnTileHoverChanged?.Invoke(currentHover);
    }

    // 기물 호버 상태 변경
    private void HandlePieceHover(GameObject pieceObj)
    {
        if (currentHoveredPiece == pieceObj) return;
        currentHoveredPiece = pieceObj;
    }

    // 모든 호버 상태(기물 및 타일)를 한 번에 초기화
    private void ResetHoverStates()
    {
        HandlePieceHover(null);
        ClearTileMouseHover();
    }
    #endregion
}
