using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// UI에서 선택된 대기 중인 스킬 종류
public enum PendingSkillType { None, Knight, Bishop, Rook, Queen, King }

// 스킬 UI 버튼, 쿨타임/스택 표기, 하이라이트 범위 연산 및 턴 종료 제어를 총괄하는 클래스.
// 파일이 길어져 기능별로 partial class로 분할되어 있다.
//  - SkillUIManager.cs               : 핵심 필드, 초기화, 유니티 생명주기
//  - SkillUIManager.TurnState.cs     : 턴 시작/종료 및 스킬 사용 상태 관리
//  - SkillUIManager.SkillHandlers.cs : 스킬 버튼 클릭, 비숍 워프 범위 계산 등 입력 처리
//  - SkillUIManager.UI.cs            : 버튼/텍스트/아웃라인 등 UI 표시 갱신
//
// 주의: ActiveSkillType/HasMovedThisTurn/MovedPieceThisTurn 등 이번 턴 상태 필드들은 "현재 턴을 진행 중인 팀"의
// 상태를 나타내는 단일(플랫) 필드다. 한 턴에는 한 팀만 행동할 수 있고 턴 시작마다 전부 초기화되므로,
// 백/흑을 구분하는 별도의 팀별 딕셔너리 없이도 양쪽 클라이언트에서 항상 동일하게 유지된다.
public partial class SkillUIManager : MonoBehaviour
{
    public static SkillUIManager Instance { get; private set; }

    #region 인스펙터 UI 버튼 및 텍스트
    [Header("Left 6 Circle Buttons")]
    [SerializeField] private Button knightButton;
    [SerializeField] private Text knightCooldownText;

    [SerializeField] private Button bishopButton;
    [SerializeField] private Text bishopCooldownText;

    [SerializeField] private Button rookButton;
    [SerializeField] private Text rookCooldownText;

    [SerializeField] private Button queenButton;
    [SerializeField] private Text queenStackText;

    [SerializeField] private Button kingButton;
    [SerializeField] private Text kingCooldownText;

    [SerializeField] private Button endTurnButton;

    [Header("Skill Active Outlines")]
    [SerializeField] private Outline knightActiveOutline;
    [SerializeField] private Outline bishopActiveOutline;
    [SerializeField] private Outline rookActiveOutline;
    [SerializeField] private Outline queenActiveOutline;
    [SerializeField] private Outline kingActiveOutline;
    #endregion

    #region 이벤트 및 상태 플래그
    public event Action<PendingSkillType> OnSkillPendingChanged;

    public PendingSkillType ActiveSkillType { get; private set; } = PendingSkillType.None;
    public bool HasUsedSkillThisTurn { get; private set; } = false; // 이번 턴 스킬 사용 여부 (퀸 제외)
    public bool HasMovedThisTurn { get; private set; } = false;     // 이번 턴 기물 이동 완료 여부
    public ChessPieces MovedPieceThisTurn { get; private set; } = null;
    public bool IsWarpPendingMove { get; private set; } = false;     // 워프 직후 정규 이동 대기 상태
    public ChessPieces WarpBishopPiece { get; private set; } = null;
    public bool IsKingDoubleMoveActive { get; set; } = false;
    #endregion

    #region 내부 상태 필드
    private ChessPieces selectedBishop = null;
    private ChessPieces lastCheckedSelection = null; // 비숍 스킬 모드에서 보드 선택 변화 감지 캐시

    public ChessPieces SelectedBishop => selectedBishop;

    // 워프 방향 (상/하/좌/우 1칸)
    private static readonly Vector2Int[] WarpDirections = new Vector2Int[]
    {
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0)
    };

    // 주변 8방향 인접 오프셋
    private static readonly Vector2Int[] SurroundingOffsets = new Vector2Int[]
    {
        new Vector2Int(-1, -1), new Vector2Int(-1, 0), new Vector2Int(-1, 1),
        new Vector2Int(0, -1),                          new Vector2Int(0, 1),
        new Vector2Int(1, -1),  new Vector2Int(1, 0),   new Vector2Int(1, 1)
    };
    #endregion

    #region 유니티 생명주기
    // 싱글턴 등록, 버튼 클릭 이벤트 바인딩, 아웃라인 초기 비활성화
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // UI 버튼 이벤트 바인딩
        knightButton?.onClick.AddListener(() => OnSkillButtonClicked(PendingSkillType.Knight));
        bishopButton?.onClick.AddListener(() => OnSkillButtonClicked(PendingSkillType.Bishop));
        rookButton?.onClick.AddListener(() => OnSkillButtonClicked(PendingSkillType.Rook));
        queenButton?.onClick.AddListener(() => OnSkillButtonClicked(PendingSkillType.Queen));
        kingButton?.onClick.AddListener(() => OnSkillButtonClicked(PendingSkillType.King));
        // 턴 종료 버튼은 네트워크 대전 중 상대에게도 동일하게 전파되어야 하므로
        // RPC 중계를 거치는 OnEndTurnButtonPressed로 바인딩한다.
        endTurnButton?.onClick.AddListener(OnEndTurnButtonPressed);

        // 초기 아웃라인 비활성화
        SetOutlineActive(knightActiveOutline, false);
        SetOutlineActive(bishopActiveOutline, false);
        SetOutlineActive(rookActiveOutline, false);
        SetOutlineActive(queenActiveOutline, false);
        SetOutlineActive(kingActiveOutline, false);
    }

    // 턴 시작 이벤트 구독 및 초기 UI 갱신
    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnTurnStarted += HandleTurnStarted;

        RefreshUIState();
    }

    // 비숍 스킬 모드일 때 보드 선택 변화를 감지해 워프 범위 하이라이트를 갱신
    private void Update()
    {
        if (ActiveSkillType != PendingSkillType.Bishop || ChessInteractionManager.Instance == null) return;

        ChessPieces currentSelection = ChessInteractionManager.Instance.SelectedPiece;
        if (currentSelection == lastCheckedSelection) return;
        lastCheckedSelection = currentSelection;

        // "현재 턴을 진행 중인 팀"의 비숍 선택 유무 검사.
        // (팀 0 고정이 아니라 CurrentTurn 기준으로 판정해야 흑팀 차례에도 동일하게 동작한다)
        int actingTeam = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : 0;
        bool isOwnBishop = currentSelection != null &&
            (currentSelection.type == ChessPieceType.WhiteBishop || currentSelection.type == ChessPieceType.BlackBishop) &&
            currentSelection.team == actingTeam;

        if (isOwnBishop)
        {
            selectedBishop = currentSelection;
            ShowBishopWarpRange(selectedBishop);
        }
        else
        {
            selectedBishop = null;
            ChessInteractionManager.Instance?.ClearCustomHighlights();
        }
    }

    // 파괴 시 이벤트 구독 해제
    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnTurnStarted -= HandleTurnStarted;
    }
    #endregion
}
