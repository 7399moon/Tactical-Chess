using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
    private enum CardSelectionMode { Promotion, Augment, PieceChoice, ColorChoice }

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

    [Header("선택 확인")]
    [SerializeField] private Button confirmButton;                          // 고른 카드를 확정하는 "확인" 버튼 (내리기 버튼 왼쪽)

    [Header("네트워크 대기 표시 (선택 사항)")]
    [SerializeField] private GameObject waitingStatusIndicator; // "상대방 선택 대기 중" 안내 오브젝트 - 없으면 표시만 생략

    [Header("증강 리롤")]
    [SerializeField] private Button rerollButton; // 증강 선택 화면(왼쪽 아래)에서만 보이는 리롤 버튼 - 없으면 리롤 기능 생략
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
    private CardUI pickedCard;                            // 고른(아직 확인 전) 카드

    // 우노 와일드 색상 선택(ColorChoice) 전용
    private System.Action<UnoColor> colorChoiceCallback;

    // 이진 선택(PieceChoice) 전용
    private PromotionOptionData[] pieceChoiceOptions;
    private System.Action<PromotablePieceType> pieceChoiceCallback;

    // 제시된 증강 및 중복 방지 기록
    private List<AugmentData> currentOfferedAugments;
    private readonly HashSet<string> usedAugmentIds = new HashSet<string>();

    // 2026-10-09 수정: GetCheckpointRarity()가 매번 GameManager.Instance.TurnCount를 직접 읽었는데,
    // TriggerAugmentCheckpoint()는 GameManager.EndTurn()의 turnCount++보다 "먼저" 호출되지만, 그
    // 직후 turnCount++가 곧바로 실행되어 사용자가 실제로 화면을 보고 있는 시점에는 이미 turnCount가
    // 1 증가해 있다. 최초 카드 표시(PopulateAugmentCards)는 TriggerAugmentCheckpoint 호출 "도중"에
    // 끝나므로 증가 전 값으로 계산되지만, 리롤 버튼(RerollAugmentCards)은 그보다 한참 뒤(사용자가
    // 버튼을 누른 시점)에 같은 GetCheckpointRarity()를 다시 호출해 "증가된" turnCount로 계산해버려서
    // 서로 다른(그리고 리롤할 때마다는 동일하게 고정된) 등급이 나오는 버그가 있었다(신고: "골드
    // 증강이었는데 리롤하면 브론즈로 바뀌고 이후 계속 브론즈만 나옴"). 체크포인트 시작 시점의
    // turnCount를 한 번만 캐싱해두고, 같은 체크포인트 동안의 모든 등급 계산(최초 표시 + 리롤 + 다음
    // 팀의 최초 표시)이 항상 이 고정값을 쓰도록 한다.
    private int checkpointSeedTurnCount;

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

    // 승급 카드 데이터의 아이콘 (나이트/비숍/룩/퀸). 폰 등 없는 종류는 null
    public Sprite GetPieceIcon(ChessPieceType type)
    {
        if (promotionOptions == null) return null;
        string n = type.ToString();
        foreach (var o in promotionOptions)
            if (o != null && o.icon != null && n.EndsWith(o.pieceType.ToString())) return o.icon;
        return null;
    }
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

        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmPressed);
            confirmButton.interactable = false;
        }

        if (rerollButton != null)
            rerollButton.onClick.AddListener(RerollAugmentCards);
        SetRerollButtonVisible(false);

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
        ResetPickedCard();
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

        // 리롤 버튼은 증강 선택(프로모션이 아닐 때)에서만 보인다.
        SetRerollButtonVisible(!promotion);

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
            card.SetupCard(OnCardClicked);
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
        ResetPickedCard();
        currentMode = CardSelectionMode.PieceChoice;
        SetRerollButtonVisible(false);

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
                card.SetupCard(OnCardClicked);
                card.SetInteractable(true);
            }
        }
    }
    #endregion

    // 우노 와일드 색상 선택: 기존 4장 카드 선택 UI를 재사용해 카드 대신 색상 4개(빨강/노랑/초록/파랑)를 보여준다.
    // 선택 결과는 이 클라이언트에서만 필요하므로(이후 카드 내기 RPC에 색이 실려 간다) 별도 RPC 없이 콜백으로 돌려준다.
    // colorSprites: Red, Yellow, Green, Blue 순서의 아이콘 (UnoCardData.chosenColorSprites)
    public void ShowColorChoice(Sprite[] colorSprites, System.Action<UnoColor> onChosen)
    {
        if (IsSelecting) return;

        IsSelecting = true;
        isClosing = false;
        ResetPickedCard();
        currentMode = CardSelectionMode.ColorChoice;
        colorChoiceCallback = onChosen;
        SetRerollButtonVisible(false);

        GameManager.Instance?.PauseTimer();

        if (cardSelectionPanel != null)
            cardSelectionPanel.SetActive(true);

        string[] names = { "빨강", "노랑", "초록", "파랑" };
        for (int i = 0; i < cardList.Length; i++)
        {
            CardUI card = cardList[i];
            if (card == null) continue;

            bool hasColor = i < 4;
            card.transform.localPosition = originalPositions[i];
            card.gameObject.SetActive(hasColor);
            if (!hasColor) continue;

            Sprite icon = colorSprites != null && i < colorSprites.Length ? colorSprites[i] : null;
            card.SetSpriteOnly(icon);
            card.transform.localScale = Vector3.one * 0.7f; // 카드 크기를 줄여 카드 사이에 간격을 둔다
            card.SetupCard(OnCardClicked);
            card.SetInteractable(true);
        }
    }

    #region 증강 체크포인트
    // GameManager.EndTurn()이 10턴마다 호출한다. 백/흑 두 팀을 모두 대기열에 넣고 각자 제안하되,
    // 이미 팀별 상한(maxAugmentCount)에 도달한 팀은 애초에 대상에서 제외한다 - 양쪽 다 상한이면
    // 대기열이 비어 체크포인트 자체가 열리지 않는다.
    public void TriggerAugmentCheckpoint()
    {
        if (!MatchSettings.AugmentEnabled) return; // 증강 시스템 OFF

        // 이번 체크포인트 동안(리롤/다음 팀 표시 포함) 등급 계산에 항상 쓸 turnCount를 지금(아직
        // GameManager.EndTurn()이 turnCount++를 실행하기 전) 캐싱해둔다 - checkpointSeedTurnCount
        // 필드 선언부 주석 참고.
        checkpointSeedTurnCount = GameManager.Instance != null ? GameManager.Instance.TurnCount : 0;

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
        for (int i = 0; i < pendingAugmentTeams.Count; i++)
        {
            ShowCardSelection(promotion: false, team: pendingAugmentTeams[i]);
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

    // 증강 선택 화면에서만 리롤 버튼을 보여준다(프로모션/이진 선택/색상 선택에는 없음).
    // 2026-10-09 수정: 리롤은 체크포인트(화면)당 1회로 제한해야 하므로, 화면을 "새로" 보여줄 때마다
    // (= 매번 ShowCardSelection으로 새 카드 4장이 올라올 때) interactable도 함께 true로 리셋한다 -
    // 실제 1회 제한(사용 후 false로 바꾸는 쪽)은 RerollAugmentCards()에서 처리한다.
    private void SetRerollButtonVisible(bool value)
    {
        if (rerollButton == null) return;

        rerollButton.gameObject.SetActive(value);
        if (value)
            rerollButton.interactable = true;
    }
    #endregion
}
