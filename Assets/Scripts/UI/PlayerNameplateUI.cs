using UnityEngine;
using UnityEngine.UI;

// 게임 씬 화면 중앙 상단 / 하단에 두 플레이어의 닉네임을 표시한다.
// - 하단: 이 클라이언트의 플레이어(나), 상단: 상대. (흑팀은 카메라가 회전해 흑 기물이 아래에 보이므로
//   "진영 고정 위치"가 아니라 "내 기준 위/아래"로 배치한다)
// - 닉네임 옆 원형 표시는 진영 색(백 = 흰색, 흑 = 검은색)을 나타낸다.
// - 현재 턴인 플레이어의 표시판은 강조색으로 바뀐다.
// - 닉네임을 아직 모르거나 네트워크 대전이 아니면 "백팀" / "흑팀"으로 표시한다.
public class PlayerNameplateUI : MonoBehaviour
{
    #region 인스펙터 설정값
    [Header("하단 (나)")]
    [SerializeField] private Image bottomPlate;
    [SerializeField] private Image bottomChip;
    [SerializeField] private Text bottomLabel;

    [Header("상단 (상대)")]
    [SerializeField] private Image topPlate;
    [SerializeField] private Image topChip;
    [SerializeField] private Text topLabel;

    [Header("진영 아이콘 (킹) / 아이콘 뒤 원형 배경")]
    [SerializeField] private Sprite whiteKingSprite;
    [SerializeField] private Sprite blackKingSprite;
    [SerializeField] private Image bottomBadge;
    [SerializeField] private Image topBadge;

    [Header("색상 (패널 스프라이트에 곱해지는 틴트)")]
    [SerializeField] private Color plateNormalColor = new Color(1f, 1f, 1f, 0.92f);
    [SerializeField] private Color plateTurnColor = new Color(1f, 0.78f, 0.3f, 1f);
    [SerializeField] private Color whiteBadgeColor = new Color(0.22f, 0.22f, 0.25f, 1f);   // 백 킹(흰색)은 어두운 배경 위에
    [SerializeField] private Color blackBadgeColor = new Color(0.92f, 0.92f, 0.9f, 1f);    // 흑 킹(검정)은 밝은 배경 위에
    #endregion

    #region 내부 상태
    // 마지막으로 그린 상태(값이 바뀔 때만 다시 그린다)
    private int shownLocalTeam = -2;
    private int shownTurn = -2;
    private string shownWhiteName;
    private string shownBlackName;
    #endregion

    #region 유니티 생명주기
    private void OnEnable()
    {
        PlayerProfile.OnTeamNicknamesChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        PlayerProfile.OnTeamNicknamesChanged -= Refresh;
    }

    // 로컬 팀 배정, 턴 전환은 별도 이벤트가 없거나 구독 시점이 불확실하므로 값이 바뀌었는지만 가볍게 확인한다.
    private void Update()
    {
        int localTeam = GetLocalTeam();
        int turn = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : -1;

        if (localTeam != shownLocalTeam || turn != shownTurn)
            Refresh();
    }
    #endregion

    #region 표시 갱신
    private static int GetLocalTeam()
    {
        return GameStartController.LocalTeam < 0 ? 0 : GameStartController.LocalTeam;
    }

    private void Refresh()
    {
        int localTeam = GetLocalTeam();
        int opponentTeam = 1 - localTeam;
        int turn = GameManager.Instance != null ? GameManager.Instance.CurrentTurn : -1;

        shownLocalTeam = localTeam;
        shownTurn = turn;

        Apply(bottomPlate, bottomChip, bottomBadge, bottomLabel, localTeam, turn, isMine: true);
        Apply(topPlate, topChip, topBadge, topLabel, opponentTeam, turn, isMine: false);
    }

    private void Apply(Image plate, Image chip, Image badge, Text label, int team, int currentTurn, bool isMine)
    {
        string name = PlayerProfile.GetTeamNickname(team);
        if (string.IsNullOrEmpty(name))
            name = team == 0 ? "백팀" : "흑팀";
        if (isMine) name += " (나)";

        if (label != null) label.text = name;
        if (chip != null) chip.sprite = team == 0 ? whiteKingSprite : blackKingSprite;
        if (badge != null) badge.color = team == 0 ? whiteBadgeColor : blackBadgeColor;
        if (plate != null) plate.color = (team == currentTurn) ? plateTurnColor : plateNormalColor;
    }
    #endregion
}
