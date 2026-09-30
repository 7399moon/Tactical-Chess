using System;
using UnityEngine;

// 체스보드 상의 마우스 입력을 감지하고, Raycast를 통해 타일/기물 클릭 및 호버 상태 변경 이벤트를 발행하는 클래스.
public class BoardInputHandler : MonoBehaviour
{
    #region 이벤트
    public event Action<GameObject> OnObjectClicked;
    public event Action<Vector2Int> OnTileHoverChanged;
    #endregion

    #region 인스펙터 설정값
    [Header("References")]
    [SerializeField] private ChessBoard board;

    [Header("Raycast Settings")]
    [SerializeField] private LayerMask hoverableMask;
    #endregion

    #region 내부 상태 필드
    private Camera currentCamera;
    private Vector2Int currentHover = -Vector2Int.one;
    private GameObject currentHoveredPiece;
    #endregion

    #region 외부 공개 프로퍼티
    public Vector2Int CurrentHover => currentHover;
    #endregion

    #region 유니티 생명주기
    // 매 프레임 마우스 위치를 Raycast로 검사해 호버/클릭 이벤트를 발행
    private void Update()
    {
        // 카드 선택 UI가 활성화되어 있거나 게임 종료 시, 환경설정 패널이 열려 있을 때 보드 입력 전체 차단
        if ((CardSelectionManager.Instance != null && CardSelectionManager.Instance.IsSelecting) ||
            (GameEndManager.Instance != null && GameEndManager.Instance.IsGameOver) ||
            SettingsManager.IsPanelOpen)
        {
            ResetHoverStates();
            return;
        }

        // 매 프레임 현재 활성화된 메인 카메라를 다시 조회한다.
        // (팀별로 백/흑 카메라를 켜고 끄며 전환하므로, 한 번 캐싱하면 카메라 전환 후에도
        //  예전 카메라 기준으로 Raycast가 나가는 문제가 생길 수 있어 캐싱하지 않는다.)
        currentCamera = Camera.main;
        if (currentCamera == null) return;

        // 마우스 포인터 Raycast 검사
        Ray ray = currentCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f, hoverableMask))
        {
            ChessPieces hitPiece = hitInfo.transform.GetComponentInParent<ChessPieces>();

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
            }
        }
        else
        {
            ResetHoverStates();
        }
    }
    #endregion

    #region 호버 상태 관리
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
