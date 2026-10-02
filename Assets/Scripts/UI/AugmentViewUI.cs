using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// "보유 증강 확인" 패널: 버튼을 누르면 현재 White/Black 두 팀이 각각 보유한 증강 카드를
// 기존 증강 카드 UI와 동일한 시각 스타일(CardUI)로 보여주는 읽기 전용 정보창.
//
// 네트워크 대전 관련 참고: 이 패널은 순수한 로컬 조회 UI라서 별도의 RPC/네트워크 동기화가 필요 없다.
// AugmentManager의 팀별 보유 증강 목록(acquiredAugments)은 이미 기존 카드 선택 RPC 릴레이 설계에 의해
// 양쪽 클라이언트에서 항상 동일하게 유지되므로, 이 패널은 그 상태를 그대로 읽기만 하면 된다.
public class AugmentViewUI : MonoBehaviour
{
    #region 인스펙터 설정값
    [Header("Panel References")]
    [SerializeField] private GameObject panelRoot;                 // 패널 전체 루트(Overlay + Panel Background 포함)
    [SerializeField] private RectTransform cardContent;             // ScrollRect의 Content (카드가 배치되는 부모)
    [SerializeField] private CardUI heldCardPrefab;                 // 보유 증강 1개를 표시할 카드 프리팹
    [SerializeField] private GameObject emptyPlaceholder;           // 보유한 증강이 0개일 때 표시할 안내 텍스트

    [Header("Data")]
    [SerializeField] private AugmentDatabase augmentDatabase;       // augmentId -> AugmentData(아이콘/이름/설명) 조회용

    [Header("Team Toggle Buttons")]
    [SerializeField] private Button whiteTeamButton;
    [SerializeField] private Button blackTeamButton;
    [SerializeField] private Image whiteTeamButtonImage;
    [SerializeField] private Image blackTeamButtonImage;
    [SerializeField] private Color selectedTeamButtonColor = new Color(1f, 0.8f, 0f, 1f);   // 현재 선택된 팀 버튼 강조색
    [SerializeField] private Color whiteTeamDefaultColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] private Color blackTeamDefaultColor = new Color(0.15f, 0.15f, 0.15f, 1f);

    [Header("Open / Close Buttons")]
    [SerializeField] private Button openButton;   // HUD 상의 "보유 증강 확인" 버튼 (Canvas 어디에 있든 무방)
    [SerializeField] private Button closeButton;  // 패널 내부의 닫기 버튼
    #endregion

    #region 내부 상태
    private int currentTeam;
    private readonly List<GameObject> spawnedCards = new List<GameObject>();
    #endregion

    #region 유니티 생명주기
    // onClick 리스너를 전부 코드에서 직접 등록한다(에디터에서 UnityEvent를 수동으로 연결할 필요 없음).
    private void Awake()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (whiteTeamButton != null) whiteTeamButton.onClick.AddListener(ShowWhiteTeam);
        if (blackTeamButton != null) blackTeamButton.onClick.AddListener(ShowBlackTeam);
    }
    #endregion

    // 로비 규칙(증강 ON/OFF)을 UI에 반영: OFF면 보유 증강 버튼을 숨기고 열려 있던 패널도 닫는다.
    public void ApplyMatchSettings()
    {
        if (!MatchSettings.AugmentEnabled) Close();
        if (openButton != null) openButton.gameObject.SetActive(MatchSettings.AugmentEnabled);
    }

    #region 공개 API (버튼에서 호출)
    // "보유 증강 확인" 버튼에서 호출: 패널을 열고, 로컬 플레이어 자신의 팀을 기본으로 보여준다.
    // 로컬 테스트(LocalTeam == -1) 중이면 White(0)를 기본값으로 사용한다.
    public void Open()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        int defaultTeam = GameStartController.LocalTeam >= 0 ? GameStartController.LocalTeam : 0;
        ShowTeam(defaultTeam);
    }

    // 닫기 버튼에서 호출.
    public void Close()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    // 상단 "백팀" 버튼에서 호출.
    public void ShowWhiteTeam() => ShowTeam(0);

    // 상단 "흑팀" 버튼에서 호출.
    public void ShowBlackTeam() => ShowTeam(1);
    #endregion

    #region 표시 갱신
    private void ShowTeam(int team)
    {
        currentTeam = team;
        UpdateTeamButtonHighlight();
        RefreshCards();
    }

    // 선택된 팀 버튼을 강조색으로, 나머지는 기본색으로 되돌린다.
    private void UpdateTeamButtonHighlight()
    {
        if (whiteTeamButtonImage != null)
            whiteTeamButtonImage.color = (currentTeam == 0) ? selectedTeamButtonColor : whiteTeamDefaultColor;

        if (blackTeamButtonImage != null)
            blackTeamButtonImage.color = (currentTeam == 1) ? selectedTeamButtonColor : blackTeamDefaultColor;
    }

    // 현재 선택된 팀이 보유한 증강 목록을 읽어와 카드로 나열한다.
    private void RefreshCards()
    {
        ClearSpawnedCards();

        IReadOnlyCollection<string> ids = AugmentManager.Instance != null
            ? AugmentManager.Instance.GetAcquiredAugmentIds(currentTeam)
            : null;

        bool hasAny = ids != null && ids.Count > 0;
        if (emptyPlaceholder != null)
            emptyPlaceholder.SetActive(!hasAny);

        if (!hasAny || heldCardPrefab == null || cardContent == null)
            return;

        foreach (string augmentId in ids)
        {
            AugmentData data = augmentDatabase != null ? augmentDatabase.GetById(augmentId) : null;
            if (data == null) continue;

            CardUI card = Instantiate(heldCardPrefab, cardContent);
            card.gameObject.SetActive(true);
            card.SetVisual(data.icon, data.displayName, data.description, data.rarity); // [변경] 등급 전달
            card.SetInteractable(false); // 읽기 전용 표시: 클릭/호버 반응 없음
            spawnedCards.Add(card.gameObject);
        }
    }

    private void ClearSpawnedCards()
    {
        foreach (GameObject go in spawnedCards)
        {
            if (go != null)
                Destroy(go);
        }
        spawnedCards.Clear();
    }
    #endregion
}
