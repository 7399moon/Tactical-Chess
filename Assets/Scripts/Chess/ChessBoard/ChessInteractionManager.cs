using System.Collections.Generic;
using UnityEngine;

// 체스 기물 선택, 이동 하이라이트, 액티브 스킬 실행 및 턴 상태 연동을 종합 제어하는 오케스트레이터 클래스.
// 파일이 길어져 기능별로 partial class로 분할되어 있다.
//  - ChessInteractionManager.cs            : 핵심 필드, 초기화, 기물 선택/해제 로직
//  - ChessInteractionManager.Skills.cs      : 클릭 입력 분기 및 액티브 스킬 발동 처리
//  - ChessInteractionManager.Promotion.cs   : 증강으로 인한 "즉시 승급 대상 클릭" 대기 상태 처리
//  - ChessInteractionManager.CheckState.cs  : 체크 상태 갱신 및 하이라이트 외부 중계 메서드
//  - ChessInteractionManager.Drag.cs        : 기물 드래그 이동(마우스로 집어서 옮기는 방식) 처리
public partial class ChessInteractionManager : MonoBehaviour
{
    public static ChessInteractionManager Instance { get; private set; }

    #region 인스펙터 설정값
    [Header("References")]
    [SerializeField] private ChessBoard board;
    [SerializeField] private PieceMovement pieceMovement;
    [SerializeField] private BoardInputHandler inputHandler;
    [SerializeField] private BoardTileHighlighter tileHighlighter;
    #endregion

    #region 내부 상태 필드
    private List<Vector2Int> availableMoves = new List<Vector2Int>(); // 현재 선택된 기물의 이동 가능 타일 목록

    private Dictionary<int, int> swapRemainingCount = new Dictionary<int, int> { { 0, 2 }, { 1, 2 } }; // 팀별 긴급 교체 잔여 횟수

    // 다중 위협(나이트)/광역 수호(룩)처럼 여러 명을 순차 클릭으로 지정해야 하는 스킬의 임시 대상 목록
    private readonly List<ChessPieces> pendingMultiTargets = new List<ChessPieces>();

    public ChessPieces SelectedPiece { get; private set; } // 현재 선택된 기물
    #endregion

    #region 유니티 생명주기 및 이벤트 연결
    // 싱글턴 인스턴스 등록
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 하이라이터 초기화 및 각종 매니저 이벤트 구독
    private void Start()
    {
        if (tileHighlighter != null && board != null)
            tileHighlighter.Initialize(board);

        if (inputHandler != null)
            inputHandler.OnObjectClicked += OnLocalBoardClicked;

        SubscribeDragEvents();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTurnTimeout += ForceDeselect;
            GameManager.Instance.OnTurnStarted += HandleTurnStarted;
        }

        if (SkillUIManager.Instance != null)
        {
            SkillUIManager.Instance.OnSkillPendingChanged += HandleSkillPendingChanged;
        }
    }

    // 파괴 시 구독했던 이벤트를 모두 해제
    private void OnDestroy()
    {
        if (inputHandler != null)
            inputHandler.OnObjectClicked -= OnLocalBoardClicked;

        UnsubscribeDragEvents();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTurnTimeout -= ForceDeselect;
            GameManager.Instance.OnTurnStarted -= HandleTurnStarted;
        }

        if (SkillUIManager.Instance != null)
        {
            SkillUIManager.Instance.OnSkillPendingChanged -= HandleSkillPendingChanged;
        }
    }

    // 턴 변경 등으로 선택 유지 조건이 깨지면 매 프레임 검사해 자동 선택 해제
    private void Update()
    {
        // 턴 변경에 따른 선택 해제 예외 처리
        if (SelectedPiece != null && !CanSelect(SelectedPiece))
            DeselectPiece();
    }
    #endregion

    #region 네트워크 동기화 (보드 클릭 중계)
    // 로컬 플레이어의 보드 클릭을 처리한다.
    // 네트워크 대전 중(팀이 배정된 상태)이면 즉시 처리하지 않고 좌표만 상대에게 중계하여,
    // 양쪽 클라이언트가 동일한 좌표로 HandleClick을 실행해 결과가 어긋나지 않도록 한다.
    private void OnLocalBoardClicked(GameObject hitObject)
    {
        if (hitObject == null) return;

        // 우노 UNO 경쟁: 턴과 무관하게 양쪽이 빛나는 칸을 누를 수 있다 (순서는 RPC 도착 순서)
        if (UnoTurnController.RaceActive)
        {
            if (ResolveBoardCoords(hitObject) == UnoTurnController.RaceTile)
                UnoTurnController.Instance.RequestRaceClick();
            return;
        }

        // 네트워크 대전 중이 아니면(로컬 테스트 등) 기존처럼 즉시 처리
        if (GameStartController.LocalTeam < 0)
        {
            HandleClick(hitObject);
            return;
        }

        bool isMyTurn = GameManager.Instance != null && GameManager.Instance.CurrentTurn == GameStartController.LocalTeam;

        // 2026-09-21 수정(1-2): 증강 효과로 인한 "승급 대상 지정" 등 보드 상호작용은 원래 턴 순서와
        // 무관하게 곧바로(그 턴이 아니어도) 완료할 수 있어야 한다 - 그렇지 않으면 상대 턴 중에 이
        // 창이 열려 있을 경우 영원히 완료할 수 없다. 내 팀이 지금 이런 "증강 효과 발동" 창을 대기
        // 중이라면(IsTeamAwaitingPromotionTarget), 내 턴이 아니어도 클릭을 허용해 상대에게 중계한다.
        // 이 창이 아니고 내 턴도 아니면 기존처럼 클릭 자체를 무시한다(다른 팀/다른 상황에는 영향 없음).
        bool isMyAugmentEffectWindow = IsTeamAwaitingPromotionTarget(GameStartController.LocalTeam);

        if (!isMyTurn && !isMyAugmentEffectWindow)
            return;

        Vector2Int coords = ResolveBoardCoords(hitObject);
        if (coords == -Vector2Int.one) return;

        ChessNetworkSync.Instance?.RPC_RelayBoardClick(coords.x, coords.y);
    }

    // 클릭된 오브젝트(기물 또는 타일)를 보드 좌표로 변환
    private Vector2Int ResolveBoardCoords(GameObject hitObject)
    {
        ChessPieces piece = hitObject.GetComponentInParent<ChessPieces>();
        if (piece != null) return new Vector2Int(piece.currentX, piece.currentY);
        if (board != null) return board.LookupTileIndex(hitObject);
        return -Vector2Int.one;
    }

    // ChessNetworkSync의 RPC를 통해 전달된 보드 좌표를 로컬 오브젝트로 복원해 실제 클릭 처리를 실행한다.
    public void HandleRelayedClick(int x, int y)
    {
        if (board == null) return;

        ChessPieces piece = board.GetPieceAt(x, y);
        GameObject target = piece != null ? piece.gameObject : board.GetTileObject(x, y);
        if (target != null)
            HandleClick(target);
    }
    #endregion

    #region 기물 선택 검증 및 처리
    // 강제로 기물 선택을 해제
    public void ForceDeselect() => DeselectPiece();

    // 해당 기물을 현재 선택할 수 있는지 조건(팀 소유권, 위협 스탯, 턴 권한, 행동 여부)을 검사.
    //
    // 중요: 이 메서드는 두 군데에서 호출된다.
    //  1) Update()의 매 프레임 자동 선택 해제 검사 - 로컬 클라이언트 자신의 화면 상태 정리용
    //  2) HandleClick() 내부 - 보드 클릭 RPC로 릴레이되어 "양쪽 클라이언트 모두 동일하게" 재현되는 로직
    // 두 경우 모두 실제로 검사해야 하는 것은 "이 기물이 지금 행동 중인 팀(GameManager.CurrentTurn) 소속인가"이지,
    // "이 기물이 내(로컬) 팀 소속인가(GameStartController.LocalTeam)"가 아니다. 예전에는 LocalTeam으로 검사했는데,
    // 상대 팀의 클릭이 릴레이되어 재현될 때 내 화면에서는 그 기물이 "내 팀이 아니다"로 판정되어
    // 잘못 선택 해제되거나 선택 자체가 거부되는 문제가 있었다(특히 흑팀 차례일 때 백팀 클라이언트 화면에서).
    private bool CanSelect(ChessPieces piece)
    {
        if (piece == null) return false;

        // 우노 모드: 카드를 낸 뒤에만, 이번 턴에 아직 안 움직인 기물만 고를 수 있다
        if (UnoTurnController.Active && !UnoTurnController.Instance.CanSelectPiece(piece))
            return false;

        int actingTeam = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : piece.team;

        // 네트워크 대전 중에는 현재 턴을 진행 중인 팀의 기물만 선택 가능
        if (GameStartController.LocalTeam >= 0 && piece.team != actingTeam)
            return false;

        if (GameManager.Instance != null && !GameManager.Instance.CanMovePiece(piece))
            return false;

        if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsImmobilized(piece))
        {
            Debug.Log($"[{piece.name}] 기물은 위협 상태(이동 불가)입니다.");
            CenterAnnouncer.Show("위협 효과로 인해 이번 턴에는 이동할 수 없습니다.");
            return false;
        }

        if (piece.team == actingTeam && SkillUIManager.Instance != null && SkillUIManager.Instance.HasMovedThisTurn)
        {
            // 2026-10-05 수정: 왕의 보폭 보너스 이동 대기 중인 킹, 또는 지휘로 2회 이동이 보장된 기물은
            // "이번 턴에 이미 움직였다"는 이유로 재선택이 막혀서는 안 된다 - 그렇지 않으면 왕의 보폭/지휘가
            // 안내만 뜨고 실제로는 절대 발동할 수 없는 죽은 기능이 된다(해당 기물 재선택이 전면 차단되므로).
            bool isPendingKingStride = SkillUIManager.Instance.IsKingDoubleMoveActive
                && SkillUIManager.Instance.KingDoubleMovePiece == piece;
            bool isPendingCommandMove = PieceSkillManager.Instance != null
                && PieceSkillManager.Instance.IsCommandActiveForTeam(actingTeam)
                && PieceSkillManager.Instance.GetCommandedPiece(actingTeam) == piece;

            if (!isPendingKingStride && !isPendingCommandMove)
                return false;
        }

        return true;
    }

    // 지정된 기물을 선택하고 합법적인 이동 가능 타일을 하이라이트
    private void SelectPiece(ChessPieces piece)
    {
        if (piece == null || board == null) return;
        SelectedPiece = piece;

        if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsImmobilized(piece))
        {
            Debug.Log($"[{piece.name}] 기물은 위협(이동 불가) 상태입니다.");
            return;
        }

        Vector2Int? enPassantTarget = GameManager.Instance?.EnPassantTarget;
        availableMoves.Clear();
        availableMoves.AddRange(ChessRules.GetLegalMoves(board.Pieces, piece, enPassantTarget));

        for (int i = 0; i < availableMoves.Count; i++)
        {
            Vector2Int move = availableMoves[i];
            ChessPieces target = board.GetPieceAt(move.x, move.y);
            bool hasEnemy = target != null && target.team != piece.team;

            if (tileHighlighter != null)
            {
                tileHighlighter.SetHighlightState(move.x, move.y,
                    hasEnemy ? BoardTileHighlighter.TileHighlightState.Capture : BoardTileHighlighter.TileHighlightState.Move);

                if (inputHandler != null)
                    tileHighlighter.RefreshTileColor(move, inputHandler.CurrentHover);
            }
        }
        SetPieceOutline(piece, true);
    }

    // 현재 선택된 기물의 하이라이트를 제거하고 선택 상태를 해제
    public void DeselectPiece()
    {
        for (int i = 0; i < availableMoves.Count; i++)
        {
            Vector2Int move = availableMoves[i];
            tileHighlighter.SetHighlightState(move.x, move.y, BoardTileHighlighter.TileHighlightState.None);
            tileHighlighter.RefreshTileColor(move, inputHandler.CurrentHover);
        }

        availableMoves.Clear();

        if (SelectedPiece != null)
            SetPieceOutline(SelectedPiece, false);

        SelectedPiece = null;
    }

    // 새 매치를 시작할 때 이전 매치에서 남아있을 수 있는 선택/하이라이트/스킬 대상 지정 상태를 초기화한다.
    public void ResetInteractionState()
    {
        DeselectPiece();
        pendingMultiTargets.Clear();
        swapRemainingCount[0] = 2;
        swapRemainingCount[1] = 2;
        pendingPromotions.Clear(); // 이전 매치에서 남아있을 수 있는 승급 대상 클릭 대기 상태도 초기화
        wasCheckActive = false; // 체크 진입 사운드 판정용 상태도 새 매치 기준으로 초기화

        if (tileHighlighter != null)
            tileHighlighter.ClearCustomHighlights();
    }
    #endregion
}
