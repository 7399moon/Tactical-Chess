using UnityEngine;
using UnityEngine.UI;

// 로비 씬 UI 컨트롤러. 상태의 권한자는 호스트이며(LobbyState/MatchSettings), 게스트 화면은 호스트가 방송한
// 스냅샷을 읽기 전용으로 보여준다. 호스트만 규칙을 편집하고 "게임 시작"을 누를 수 있다.
public class LobbyController : MonoBehaviour
{
    public static LobbyController Instance { get; private set; }

    #region 인스펙터 설정값
    [Header("상단/플레이어")]
    [SerializeField] private Text roomCodeText;
    [SerializeField] private Text hostNameText;
    [SerializeField] private Text hostFactionText;
    [SerializeField] private Text guestNameText;
    [SerializeField] private Text guestFactionText;

    [Header("진영 선택")]
    [SerializeField] private Button whiteButton;
    [SerializeField] private Button blackButton;
    [SerializeField] private Button randomButton;

    [Header("게임 규칙 (호스트만 편집)")]
    [SerializeField] private Dropdown modeDropdown;     // 게임 모드: 기본/스킬/증강(+스킬)/우노 (인덱스 = MatchMode 값)
    [SerializeField] private GameObject[] augmentRows;  // 증강 모드에서만 보이는 행들 (최대 보유 증강, 증강 선택 시간)
    [SerializeField] private GameObject[] unoRows;      // 우노 모드에서만 보이는 행들 (시작 카드 장수, 최대 패 장수)
    [SerializeField] private Button unoStartMinus;     [SerializeField] private Button unoStartPlus;
    [SerializeField] private Text unoStartValueText;
    [SerializeField] private Button unoMaxMinus;       [SerializeField] private Button unoMaxPlus;
    [SerializeField] private Text unoMaxValueText;
    [SerializeField] private Button maxAugMinus;       [SerializeField] private Button maxAugPlus;
    [SerializeField] private Text maxAugValueText;
    [SerializeField] private Button augTimeToggle;     [SerializeField] private Text augTimeToggleLabel;
    [SerializeField] private Button turnToggle;        [SerializeField] private Text turnToggleLabel;
    [SerializeField] private InputField turnSecondsInput;
    [SerializeField] private Text rulesNoticeText;      // 게스트에게 "호스트만 변경 가능" 안내 / 스킬 OFF 안내

    [Header("시작 / 나가기")]
    [SerializeField] private Button startButton;
    [SerializeField] private Text startButtonLabel;
    [SerializeField] private Text hintText;
    [SerializeField] private Button leaveButton;

    [Header("토글 스프라이트 (ON/OFF 글자가 그림에 포함되어 있다)")]
    [SerializeField] private Sprite toggleOnSprite;
    [SerializeField] private Sprite toggleOffSprite;

    [Header("게임 시작 시 숨길 루트들 (Canvas, Camera, EventSystem 등)")]
    [SerializeField] private GameObject[] rootsToHide;
    #endregion

    // 버튼 상태 표현: 스프라이트는 하나(btn_normal)이고 Image.color 틴트로만 구분한다.
    // - 기본: 흰색(원본 색) / 선택됨: 금색 틴트 / 시작 가능: 연초록 틴트
    // - 비활성/눌림/마우스 올림은 Button.colors(ColorTint)가 처리한다.
    private static readonly Color Normal = Color.white;
    private static readonly Color Selected = new Color(1f, 0.78f, 0.3f, 1f);
    private static readonly Color StartReady = new Color(0.75f, 1f, 0.75f, 1f);

    private bool IsHost => BasicSpawner.Instance != null && BasicSpawner.Instance.IsHost;
    private bool starting;

    #region 생명주기
    private void Awake() { Instance = this; }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        LobbyState.OnChanged += Refresh;
        MatchSettings.OnChanged += Refresh;
    }

    private void OnDisable()
    {
        LobbyState.OnChanged -= Refresh;
        MatchSettings.OnChanged -= Refresh;
    }

    private void Start()
    {
        whiteButton.onClick.AddListener(() => OnPick(LobbyState.PickWhite));
        blackButton.onClick.AddListener(() => OnPick(LobbyState.PickBlack));
        randomButton.onClick.AddListener(() => OnPick(LobbyState.PickRandom));

        modeDropdown.ClearOptions();
        modeDropdown.AddOptions(new System.Collections.Generic.List<string> { "기본 모드", "스킬 모드", "증강(+스킬) 모드", "우노 모드" });
        modeDropdown.onValueChanged.AddListener(OnModeChanged);
        unoStartMinus.onClick.AddListener(() => Edit(() => MatchSettings.UnoStartHand--));
        unoStartPlus.onClick.AddListener(() => Edit(() => MatchSettings.UnoStartHand++));
        unoMaxMinus.onClick.AddListener(() => Edit(() => MatchSettings.UnoMaxHand--));
        unoMaxPlus.onClick.AddListener(() => Edit(() => MatchSettings.UnoMaxHand++));
        maxAugMinus.onClick.AddListener(() => Edit(() => MatchSettings.MaxAugments--));
        maxAugPlus.onClick.AddListener(() => Edit(() => MatchSettings.MaxAugments++));
        augTimeToggle.onClick.AddListener(() => Edit(() => MatchSettings.AugmentTimeLimit = !MatchSettings.AugmentTimeLimit));
        turnToggle.onClick.AddListener(() => Edit(() => MatchSettings.TurnTimeLimit = !MatchSettings.TurnTimeLimit));

        turnSecondsInput.contentType = InputField.ContentType.IntegerNumber;
        turnSecondsInput.characterLimit = 5;
        turnSecondsInput.onEndEdit.AddListener(OnTurnSecondsEdited);

        startButton.onClick.AddListener(OnStartClicked);
        leaveButton.onClick.AddListener(() => BasicSpawner.Instance?.GoToTitle());

        Refresh();
    }

    // 호스트가 로비에 들어온 직후 ChessNetworkSync가 스폰되면 첫 스냅샷이 필요하다 - 주기적으로 갱신해 UI 동기화를 보장한다.
    private float nextPoll;
    private void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.5f;
        Refresh();
    }
    #endregion

    #region 조작
    private void OnPick(int pick)
    {
        if (starting) return;
        int mine = IsHost ? LobbyState.HostPick : LobbyState.GuestPick;
        if (mine == pick) pick = LobbyState.PickNone; // 같은 버튼을 다시 누르면 선택 해제

        if (IsHost)
        {
            if (LobbyState.TrySetPick(true, pick)) ChessNetworkSync.Instance?.BroadcastLobby();
        }
        else
        {
            ChessNetworkSync.Instance?.RPC_LobbyRequestPick(pick);
        }
    }

    // 호스트 전용 규칙 편집: 값 변경 -> 의존성 정리 -> 방송
    private void Edit(System.Action change)
    {
        if (!IsHost || starting) return;
        change();
        MatchSettings.Normalize();
        Refresh();
        ChessNetworkSync.Instance?.BroadcastLobby();
    }

    // 드롭다운 선택 -> 호스트만 모드 변경. Refresh()가 값을 되돌리며 호출하는 SetValueWithoutNotify는 이벤트를 내지 않는다.
    private void OnModeChanged(int index)
    {
        if (!IsHost || starting) { Refresh(); return; }
        Edit(() => MatchSettings.Mode = (MatchMode)index);
    }

    private void OnTurnSecondsEdited(string text)
    {
        if (!IsHost) { Refresh(); return; }
        if (int.TryParse(text, out int v) && v > 0)
            Edit(() => MatchSettings.TurnSeconds = v);
        else
            turnSecondsInput.text = MatchSettings.TurnSeconds.ToString(); // 빈 값/0/음수: 이전 값 복원
    }

    private void OnStartClicked()
    {
        if (!IsHost || starting || !LobbyState.CanStart) return;
        if (ChessNetworkSync.Instance == null) return;

        starting = true;
        MatchSettings.Normalize();
        ChessNetworkSync.Instance.BroadcastLobby();
        ChessNetworkSync.Instance.RPC_LobbyStart(LobbyState.ResolveHostTeam(), UnityEngine.Random.Range(int.MinValue, int.MaxValue));
        BasicSpawner.Instance.LoadGameScene();
        Refresh();
    }
    #endregion

    #region 표시 갱신
    private static string PickLabel(int pick, bool present)
    {
        if (!present) return "";
        switch (pick)
        {
            case LobbyState.PickWhite: return "[백]";
            case LobbyState.PickBlack: return "[흑]";
            case LobbyState.PickRandom: return "[랜덤]";
            default: return "[선택 중]";
        }
    }

    private void Refresh()
    {
        if (this == null || startButton == null) return;
        bool host = IsHost;

        if (roomCodeText != null && BasicSpawner.Instance != null)
            roomCodeText.text = $"방 번호: {BasicSpawner.Instance.RoomCode}";

        hostNameText.text = string.IsNullOrEmpty(LobbyState.HostNick) ? "방장" : LobbyState.HostNick + " (방장)";
        hostFactionText.text = PickLabel(LobbyState.HostPick, true);
        guestNameText.text = LobbyState.GuestPresent ? LobbyState.GuestNick : "상대 대기 중...";
        guestFactionText.text = PickLabel(LobbyState.GuestPick, LobbyState.GuestPresent);

        // 진영 버튼: 상대가 고른 색은 비활성, 내 선택은 강조. 랜덤은 항상 활성.
        int mine = host ? LobbyState.HostPick : LobbyState.GuestPick;
        int other = host ? LobbyState.GuestPick : LobbyState.HostPick;
        bool canPick = !starting && (host || LobbyState.GuestPresent);
        SetPickButton(whiteButton, LobbyState.PickWhite, mine, other, canPick);
        SetPickButton(blackButton, LobbyState.PickBlack, mine, other, canPick);
        SetPickButton(randomButton, LobbyState.PickRandom, mine, other, canPick);

        // 규칙 표시 (게스트는 읽기 전용)
        bool edit = host && !starting;
        bool isAug = MatchSettings.Mode == MatchMode.AugmentSkill;
        bool isUno = MatchSettings.IsUno;
        modeDropdown.SetValueWithoutNotify((int)MatchSettings.Mode);
        modeDropdown.interactable = edit;
        // 선택된 모드의 옵션 행만 보인다. 씬에서는 기본 상태가 비활성(Hierarchy가 깔끔하도록)이고, 모드를 고르면 켜진다.
        SetRowsEnabled(augmentRows, isAug);
        SetRowsEnabled(unoRows, isUno);

        maxAugValueText.text = MatchSettings.MaxAugments.ToString();
        SetBtn(maxAugMinus, edit && isAug && MatchSettings.MaxAugments > MatchSettings.MinAugments);
        SetBtn(maxAugPlus, edit && isAug && MatchSettings.MaxAugments < MatchSettings.MaxAugmentsLimit);
        unoStartValueText.text = MatchSettings.UnoStartHand.ToString();
        SetBtn(unoStartMinus, edit && isUno && MatchSettings.UnoStartHand > MatchSettings.MinUnoStartHand);
        SetBtn(unoStartPlus, edit && isUno && MatchSettings.UnoStartHand < MatchSettings.MaxUnoStartHand);
        unoMaxValueText.text = MatchSettings.UnoMaxHand.ToString();
        SetBtn(unoMaxMinus, edit && isUno && MatchSettings.UnoMaxHand > MatchSettings.MinUnoMaxHand);
        SetBtn(unoMaxPlus, edit && isUno && MatchSettings.UnoMaxHand < MatchSettings.MaxUnoMaxHand);
        SetToggle(augTimeToggle, augTimeToggleLabel, MatchSettings.AugmentTimeLimit, edit && isAug);
        SetToggle(turnToggle, turnToggleLabel, MatchSettings.EffectiveTurnTimeLimit, edit && !isUno); // 우노 모드: 시간 제한 없음(고정)
        turnSecondsInput.interactable = edit && !isUno && MatchSettings.TurnTimeLimit;
        if (!turnSecondsInput.isFocused) turnSecondsInput.text = MatchSettings.TurnSeconds.ToString();

        if (rulesNoticeText != null)
            rulesNoticeText.text = ModeNotice(MatchSettings.Mode) + (host ? "" : "  (방장만 변경 가능)");

        // 시작 버튼: 호스트 화면에만 표시
        startButton.gameObject.SetActive(host);
        bool can = host && !starting && LobbyState.CanStart;
        startButton.interactable = can;
        startButton.image.color = can ? StartReady : Normal;
        if (startButtonLabel != null) startButtonLabel.text = starting ? "시작 중..." : "게임 시작";
        if (hintText != null)
            hintText.text = starting ? "게임을 불러오는 중..."
                : !LobbyState.GuestPresent ? (host ? "상대가 입장하길 기다리는 중입니다" : "")
                : (LobbyState.HostPick == LobbyState.PickNone || LobbyState.GuestPick == LobbyState.PickNone) ? "두 플레이어 모두 진영을 선택하면 시작할 수 있습니다"
                : (host ? "게임을 시작할 수 있습니다" : "방장이 게임을 시작하길 기다리는 중입니다");
    }

    private static string ModeNotice(MatchMode mode)
    {
        switch (mode)
        {
            case MatchMode.Default: return "기본 모드 : 기본 체스 규칙, 이동 후 턴 자동 종료";
            case MatchMode.Skill: return "스킬 모드 : 스킬 사용";
            case MatchMode.AugmentSkill: return "증강(+스킬) 모드 : 증강과 스킬 사용";
            default: return "우노 모드 : 우노 카드로 턴 행동 결정";
        }
    }

    private static void SetRowsEnabled(GameObject[] rows, bool on)
    {
        foreach (var go in rows)
            if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    private void SetPickButton(Button b, int pickValue, int mine, int other, bool canPick)
    {
        bool blockedByOther = pickValue != LobbyState.PickRandom && other == pickValue;
        bool interact = canPick && !blockedByOther;
        b.interactable = interact;
        b.image.color = mine == pickValue ? Selected : Normal;
    }

    private void SetToggle(Button b, Text label, bool on, bool interactable)
    {
        b.interactable = interactable;
        label.text = on ? "ON" : "OFF"; // 그림에 글자가 있으므로 라벨은 숨김 상태로 두되 값은 유지
        if (toggleOnSprite != null && toggleOffSprite != null)
            b.image.sprite = on ? toggleOnSprite : toggleOffSprite;
        b.image.color = Normal;
    }

    private void SetBtn(Button b, bool interactable)
    {
        b.interactable = interactable;
        b.image.color = Normal;
    }
    #endregion

    #region 게임 시작 시 숨김
    // GameStartController가 게임 시작 직전에 호출: 로비 UI/카메라/EventSystem을 끈다(씬은 언로드하지 않음).
    public static void HideLobbyScene()
    {
        if (Instance == null) return;
        foreach (var go in Instance.rootsToHide)
            if (go != null) go.SetActive(false);
    }
    #endregion
}
