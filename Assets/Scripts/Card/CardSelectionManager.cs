using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum AugmentRarityMask
{
    None = 0,
    Normal = 1 << 0, // 1
    Rare = 1 << 1, // 2
    Unique = 1 << 2, // 4
    Legendary = 1 << 3, // 8
    All = ~0      // 전체 선택
}

/// <summary>
/// 카드 선택 UI(프로모션, 증강 선택, 기물 선택)의 연출, 데이터 바인딩 및 결과를 제어하는 매니저 클래스.
///
/// 복구 노트(2026-09-23): 이 파일은 한때 더 오래된(팀 분리/네트워크 동기화 재설계 이전) 단일 버전으로
/// 디스크에서 되돌아가 있었고, 그 결과 아래 두 partial 파일과 필드/메서드가 중복 선언되어(CS0111)
/// 컴파일이 전면 실패하는 상태였다. 이 파일을 다시 partial로 선언하고, 실제 로직이 있는 아래 두
/// partial 파일과 겹치지 않는 부분(필드 선언/초기화/외부 진입점)만 남기도록 재구성했다:
/// - CardSelectionManager.cs (이 파일): 필드/초기화 및 외부에서 호출하는 진입점(ShowCardSelection,
///   ShowPieceChoice, TriggerAugmentCheckpoint)
/// - CardSelectionManager.Data.cs: 카드 데이터 바인딩 및 증강 뽑기(풀링)
/// - CardSelectionManager.Selection.cs: 카드 클릭 이후 처리(연출/결과 반영/네트워크 동기화)
/// </summary>
public partial class CardSelectionManager : MonoBehaviour
{
    public static CardSelectionManager Instance { get; private set; }

    // 카드 선택 화면의 용도를 구분하는 열거형
    private enum CardSelectionMode { Promotion, Augment, PieceChoice }

    #region Serialized Fields
    [Header("UI Panels & Prefabs")]
    [SerializeField] private GameObject cardSelectionPanel;
    [SerializeField] private CardUI[] cardList;                             // 고정 배치된 4개 카드

    [Header("Card Data")]
    [SerializeField] private AugmentDatabase augmentDatabase;               // 증강 데이터베이스 풀
    [SerializeField] private List<PromotionOptionData> promotionOptions;    // 프로모션용 카드 데이터 (4종)

    [Header("Promotion SO Data References")]
    [SerializeField] private PromotionOptionData knightPromotionData;
    [SerializeField] private PromotionOptionData bishopPromotionData;

    [Header("Augment Limit Settings")]
    // 팀당 최대 증강 수는 로비 설정(MatchSettings.MaxAugments)을 따른다 (기본 6개).
    private static int maxAugmentCount => MatchSettings.MaxAugments;

    [Header("Animation Settings")]
    [SerializeField] private float animDuration = 0.6f;
    [SerializeField] private float moveUpDistance = 1200f;
    [SerializeField] private float moveDownDistance = 1200f;

    [Header("References")]
    [SerializeField] private ChessBoard chessBoard; // ChessBoard 참조

    [Header("Augment Rarity Filter Settings")]
    [SerializeField] private AugmentRarityMask allowedRarities = AugmentRarityMask.All; // 인스펙터에서 필터링할 등급 선택

    [Header("네트워크 대기 표시 (선택 사항)")]
    [SerializeField] private GameObject waitingStatusIndicator; // "상대방 선택 대기 중" 안내 오브젝트 - 없으면 표시만 생략
    #endregion

    #region Private Fields
    // 애니메이션 관련 변수
    private Vector3[] originalPositions;
    private Vector3[] startPositions;
    private Vector3[] targetPositions;

    // 선택 상태 및 데이터
    private CardSelectionMode currentMode;
    private Vector2Int pendingPromotionTile;
    private int pendingPromotionTeam;
    private int pendingPieceChoiceTeam;
    private CardUI currentSelectedCard;

    // 이진 선택(PieceChoice) 전용
    private PromotionOptionData[] pieceChoiceOptions;
    private System.Action<PromotablePieceType> pieceChoiceCallback;

    // 제시된 증강 및 중복 방지 기록
    private List<AugmentData> currentOfferedAugments;
    private readonly HashSet<string> usedAugmentIds = new HashSet<string>();

    // 증강 체크포인트 진행 상태(TriggerAugmentCheckpoint 참고): 이번 체크포인트에서 아직 선택을
    // 완료하지 못한 팀 목록. 로컬 테스트 모드에서는 순차 진행 순서로도 쓰인다.
    private List<int> pendingAugmentTeams = new List<int>();
    // 증강 선택이 이진 선택(ShowPieceChoice)으로 이어져 그 이진 선택까지는 아직 끝나지 않은 팀.
    private readonly HashSet<int> teamsAwaitingPieceChoice = new HashSet<int>();
    // 증강 선택이 보드 타겟 클릭(즉시 승급 등)으로 이어져 그 보드 상호작용까지는 아직 끝나지 않은 팀.
    private readonly HashSet<int> teamsAwaitingBoardTarget = new HashSet<int>();
    #endregion

    #region Properties
    public bool IsSelecting { get; private set; }
    private bool isClosing;
    #endregion

    public PromotionOptionData KnightPromotionData => knightPromotionData;
    public PromotionOptionData BishopPromotionData => bishopPromotionData;

    #region Unity Lifecycle & Initialization
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        CacheCardPositions();

        if (cardSelectionPanel != null)
            cardSelectionPanel.SetActive(false);

        SetWaitingStatus(false);

        ResetUsedAugments();
    }

    // ChessInteractionManager.OnPromotionTargetResolved 구독(증강 효과로 시작된 "승급 대상 보드
    // 클릭" 완료를 알림 - HandlePromotionTargetResolved 참고). Awake 시점에는 다른 싱글턴의
    // Awake()가 아직 실행되지 않았을 수 있으므로, 씬의 모든 Awake가 끝난 뒤 호출되는 Start에서 구독한다.
    private void Start()
    {
        if (ChessInteractionManager.Instance != null)
            ChessInteractionManager.Instance.OnPromotionTargetResolved += HandlePromotionTargetResolved;
    }

    private void OnDestroy()
    {
        if (ChessInteractionManager.Instance != null)
            ChessInteractionManager.Instance.OnPromotionTargetResolved -= HandlePromotionTargetResolved;

        if (Instance == this)
            Instance = null;
    }

    // 카드 UI들의 기본 위치 캐싱
    private void CacheCardPositions()
    {
        int count = cardList.Length;
        originalPositions = new Vector3[count];
        startPositions = new Vector3[count];
        targetPositions = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            if (cardList[i] != null)
                originalPositions[i] = cardList[i].transform.localPosition;
        }
    }
    #endregion

    #region Public Display Methods
    // 프로모션 카드 창(팀 무관), 또는 로컬 테스트 모드의 증강 선택 카드 창을 호출한다.
    // 네트워크 대전 중 증강 선택은 TriggerAugmentCheckpoint()/HandleAugmentTeamDone()이 호출한다.
    //
    // 2026-09-18 재설계: "게임 상태(양쪽 동일)"와 "화면 표시(자기 팀만)"를 분리 - 이 화면에 관여하는
    // 팀(team)이 이 클라이언트의 로컬 팀이 아니면(네트워크 대전 중) 패널을 띄우지 않는다
    // (IsInteractiveForTeam 참고). 프로모션도 동일 원칙 적용: 승급하는 팀의 클라이언트에서만 보인다.
    public void ShowCardSelection(bool promotion = false, Vector2Int promotionTile = default, int team = 0)
    {
        if (IsSelecting) return;
        if (!promotion && !MatchSettings.AugmentEnabled) return;
        if (!promotion && AugmentManager.Instance != null && AugmentManager.Instance.GetAcquiredCount(team) >= maxAugmentCount) return;
        if (!IsInteractiveForTeam(team)) return;

        IsSelecting = true;
        isClosing = false;
        currentMode = promotion ? CardSelectionMode.Promotion : CardSelectionMode.Augment;
        pendingPromotionTile = promotionTile;
        pendingPromotionTeam = team;

        // 게임 진행 및 타이머 일시정지
        GameManager.Instance?.PauseTimer();

        // 패널 활성화
        if (cardSelectionPanel != null)
            cardSelectionPanel.SetActive(true);

        // 증강 카드 등장 사운드 (프로모션 카드 창에는 재생하지 않음)
        if (!promotion)
            SoundManager.Instance?.PlayAugmentCardAppear();

        if (promotion)
            PopulatePromotionCards();
        else
            PopulateAugmentCards();

        // 모든 카드 초기화
        for (int i = 0; i < cardList.Length; i++)
        {
            CardUI card = cardList[i];
            if (card == null) continue;

            card.transform.localPosition = originalPositions[i];
            card.gameObject.SetActive(true);
            card.SetupCard(OnCardSelected);
            card.SetInteractable(true);
        }
    }

    // 기물 2개 중 하나를 고르는 이진 선택 창 호출.
    // team: 이 선택 결과가 귀속될 팀 - 네트워크 대전 중에는 이 팀의 클라이언트에서만 패널이 보인다.
    // 콜백(onChosen) 등록 자체는 team과 무관하게 항상 수행한다 - ApplyPieceChoice가 양쪽 클라이언트
    // 에서 항상 호출되어 실제 게임 상태(승급 등)를 반영해야 하기 때문이다.
    public void ShowPieceChoice(int team, PromotionOptionData optionA, PromotionOptionData optionB, System.Action<PromotablePieceType> onChosen)
    {
        pendingPieceChoiceTeam = team;
        pieceChoiceCallback = onChosen;

        // 이 이진 선택이 증강 체크포인트 도중 시작된 것이라면, 그 팀의 체크포인트가 아직
        // "완전히" 끝나지 않았음을 기록해둔다(FinalizeAugmentTeamIfReady/ApplyPieceChoice 참고).
        if (pendingAugmentTeams.Contains(team))
            teamsAwaitingPieceChoice.Add(team);

        if (!IsInteractiveForTeam(team)) return;
        if (IsSelecting) return;

        IsSelecting = true;
        isClosing = false;
        currentMode = CardSelectionMode.PieceChoice;

        GameManager.Instance?.PauseTimer();

        if (cardSelectionPanel != null)
            cardSelectionPanel.SetActive(true);

        PopulatePieceChoiceCards(optionA, optionB);

        for (int i = 0; i < cardList.Length; i++)
        {
            CardUI card = cardList[i];
            if (card == null) continue;

            bool isActiveSlot = (i == 1 || i == 2); // 가운데 2칸만 사용(중앙 정렬)
            card.transform.localPosition = originalPositions[i];
            card.gameObject.SetActive(isActiveSlot);

            if (isActiveSlot)
            {
                card.SetupCard(OnCardSelected);
                card.SetInteractable(true);
            }
        }
    }
    #endregion

    #region 증강 체크포인트
    // GameManager.EndTurn()이 10턴마다 호출한다. 백/흑 두 팀을 모두 대기열에 넣고 각자 제안하되,
    // 이미 팀별 상한(maxAugmentCount)에 도달한 팀은 애초에 대상에서 제외한다 - 양쪽 다 상한이면
    // 대기열이 비어 체크포인트 자체가 열리지 않는다.
    public void TriggerAugmentCheckpoint()
    {
        if (!MatchSettings.AugmentEnabled) return; // 증강 시스템 OFF
        teamsAwaitingPieceChoice.Clear();
        teamsAwaitingBoardTarget.Clear();

        pendingAugmentTeams = new List<int>();
        for (int team = 0; team <= 1; team++)
        {
            if (AugmentManager.Instance != null && AugmentManager.Instance.GetAcquiredCount(team) >= maxAugmentCount)
                continue;
            pendingAugmentTeams.Add(team);
        }

        if (pendingAugmentTeams.Count == 0) return;

        GameManager.Instance?.PauseTimer();

        if (GameStartController.LocalTeam < 0)
        {
            // 로컬 테스트 모드(네트워크 대전 아님): 기존처럼 한 팀씩 순차적으로 제안한다.
            ShowCardSelection(promotion: false, team: pendingAugmentTeams[0]);
            return;
        }

        // 네트워크 대전 모드: 이 메서드는 양쪽 클라이언트에서 각자 독립적으로 호출된다(EndTurn이
        // RPC로 양쪽에서 동일하게 실행되므로). 대상 팀 전체에 대해 ShowCardSelection을 호출하지만,
        // 실제로 패널이 뜨는 것은 IsInteractiveForTeam(team)이 true인 자기 팀 화면뿐이다 - 즉 각
        // 클라이언트는 자기 팀 오프너만 독립적으로 열게 된다(동시 진행, 순차 아님).
        foreach (int team in pendingAugmentTeams)
        {
            ShowCardSelection(promotion: false, team: team);
        }
    }

    // 지금 이 클라이언트가 해당 팀의 카드 선택 화면을 실제로 띄워야 하는지 여부.
    // 로컬 테스트 모드(네트워크 대전 아님, LocalTeam == -1)에서는 항상 화면을 띄운다.
    // 네트워크 대전 중에는 그 팀이 이 클라이언트의 로컬 팀일 때만 띄운다 - 게임 상태 반영
    // (AddAugment/즉시 효과 등)은 team과 무관하게 항상 실행되지만, 화면 표시는 자기 팀만
    // 보는 것이 원칙이다(2026-09-18 재설계 참고).
    private bool IsInteractiveForTeam(int team)
    {
        return GameStartController.LocalTeam < 0 || GameStartController.LocalTeam == team;
    }

    // "상대방 선택 대기 중" 안내(인스펙터에 연결되어 있다면)를 토글한다. 연결되어 있지 않으면
    // 아무 것도 하지 않는다(선택 사항 - 없어도 체크 표시(CardUI.SetSelected)만으로 대기 상태를 알 수 있음).
    private void SetWaitingStatus(bool value)
    {
        if (waitingStatusIndicator != null)
            waitingStatusIndicator.SetActive(value);
    }
    #endregion
}
