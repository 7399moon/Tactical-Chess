using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// 시작 화면 튜토리얼: 상단 탭 3개(체스 기본 룰 / 스킬 설명 / 증강 설명)로 아래 내용이 바뀐다.
// 증강 탭은 AugmentDatabase에서 카드를 자동 생성하는 세로 스크롤(한 줄 4장)이라 증강이 추가/변경돼도 별도 수정이 필요 없다.
public class TutorialPanelUI : MonoBehaviour
{
    #region 인스펙터 설정값
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject panel;

    [SerializeField] private Button[] tabButtons;      // 0: 룰, 1: 스킬, 2: 증강
    [SerializeField] private GameObject[] pages;       // 탭별 페이지 루트
    [SerializeField] private Text rulesText;
    [SerializeField] private Text skillsText;
    [SerializeField] private Text augmentHeader;
    [SerializeField] private RectTransform augmentContent; // GridLayoutGroup(4열) 부모
    [SerializeField] private AugmentDatabase augmentDatabase;
    [SerializeField] private Font font;
    #endregion

    private static readonly Color TabNormal = new Color(0.22f, 0.22f, 0.27f, 1f);
    private static readonly Color TabSelected = new Color(0.95f, 0.75f, 0.2f, 1f);
    private bool augmentsBuilt;

    #region 튜토리얼 본문
    private const string RulesBody =
"■ 게임 진행\n" +
"• 백이 먼저 시작하고, 한 턴에 기물 하나를 한 번 움직입니다.\n" +
"• 상대 킹을 체크메이트로 몰면 승리합니다. 킹이 직접 잡혀도 게임이 끝납니다.\n" +
"• 움직일 수 있는 수가 없는데 체크 상태가 아니면 스테일메이트로 무승부입니다.\n" +
"• 방장이 턴 시간 제한을 켜 두었다면, 시간이 끝났을 때 턴이 자동으로 넘어갑니다.\n\n" +
"■ 기물 이동\n" +
"• 폰: 앞으로 1칸(첫 이동은 2칸), 대각선 앞의 적을 잡습니다.\n" +
"• 나이트: L자로 이동하며 다른 기물을 뛰어넘을 수 있습니다.\n" +
"• 비숍: 대각선으로 원하는 만큼 이동합니다.\n" +
"• 룩: 가로·세로로 원하는 만큼 이동합니다.\n" +
"• 퀸: 가로·세로·대각선으로 원하는 만큼 이동합니다.\n" +
"• 킹: 모든 방향으로 1칸 이동하며, 체크 상태가 되는 칸으로는 갈 수 없습니다.\n\n" +
"■ 특수 규칙\n" +
"• 캐슬링: 킹과 룩이 아직 움직이지 않았고 둘 사이가 비어 있으면 함께 자리를 바꿀 수 있습니다.\n" +
"• 앙파상: 상대 폰이 2칸 전진해 내 폰 옆에 섰다면, 바로 다음 턴에 그 뒤 칸으로 대각선 이동하며 잡을 수 있습니다.\n" +
"• 프로모션: 폰이 상대편 끝줄에 도착하면 카드 중에서 승급할 기물을 고릅니다.";

    private const string SkillsBody =
"이동 전후로 조건이 맞으면 화면 왼쪽의 스킬 버튼이 활성화됩니다. 한 턴에 스킬은 한 번만 쓸 수 있고, 사용 후에는 쿨타임(턴)이 돌아야 다시 쓸 수 있습니다. (증강으로 효과가 바뀔 수 있습니다)\n\n" +
"■ 나이트 - 위협 (쿨타임 4턴)\n" +
"나이트를 움직인 턴에 사용합니다. 주변 1칸 안의 적 기물을 지정하면, 그 기물은 다음 턴까지 움직일 수 없습니다.\n\n" +
"■ 비숍 - 워프 (쿨타임 4턴)\n" +
"기물을 움직이기 전에 사용합니다. 비숍이 상하좌우로 인접한 빈 칸 1곳으로 즉시 워프하며, 워프한 뒤에는 이어서 일반 이동을 할 수 있습니다.\n\n" +
"■ 룩 - 쉴드 (쿨타임 6턴)\n" +
"룩을 움직인 턴에 사용합니다. 주변 1칸 안의 아군 기물에 1턴 동안 공격을 막아 주는 보호막을 씌웁니다.\n\n" +
"■ 킹 - 지휘 (쿨타임 8턴)\n" +
"기물을 움직이기 전에 사용합니다. 아군 기물 하나를 지정하면 그 기물이 연속으로 2번 움직일 수 있습니다.\n\n" +
"■ 퀸 - 아우라 (패시브)\n" +
"퀸이 살아 있는 동안 우리 팀이 적 기물을 잡을 때마다 스택이 쌓입니다. 6스택이 되면 퀸 버튼으로 아군 기물 하나를 한 단계 승급시킬 수 있습니다. (폰 → 나이트 → 비숍 → 룩 → 퀸) 퀸이 모두 사라지면 스택이 초기화됩니다.\n\n" +
"■ 턴 종료\n" +
"이동을 마친 뒤 더 쓸 수 있는 스킬이 없으면 턴이 자동으로 끝납니다. 스킬을 쓸 수 있는 상황에서 그냥 넘기려면 '턴 종료' 버튼을 누르세요.\n\n" +
"※ 방장이 스킬 시스템을 끄면 스킬 버튼과 퀸 아우라가 모두 사라지고, 이동만 하면 턴이 자동으로 끝납니다.";
    #endregion

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
        openButton.onClick.AddListener(Open);
        closeButton.onClick.AddListener(Close);
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int idx = i;
            tabButtons[i].onClick.AddListener(() => ShowTab(idx));
        }
        rulesText.text = RulesBody;
        skillsText.text = SkillsBody;
    }

    public void Open()
    {
        panel.SetActive(true);
        ShowTab(0);
    }

    public void Close() => panel.SetActive(false);

    private void ShowTab(int index)
    {
        for (int i = 0; i < pages.Length; i++)
            pages[i].SetActive(i == index);
        for (int i = 0; i < tabButtons.Length; i++)
            tabButtons[i].image.color = i == index ? TabSelected : TabNormal;

        if (index == 2) BuildAugmentCards();
    }

    #region 증강 카드 생성
    private static Color RarityColor(AugmentRarity r)
    {
        switch (r)
        {
            case AugmentRarity.Rare: return new Color(0.2f, 0.42f, 0.75f, 1f);
            case AugmentRarity.Unique: return new Color(0.5f, 0.28f, 0.72f, 1f);
            case AugmentRarity.Legendary: return new Color(0.82f, 0.6f, 0.15f, 1f);
            default: return new Color(0.4f, 0.4f, 0.45f, 1f);
        }
    }

    private static string RarityName(AugmentRarity r)
    {
        switch (r)
        {
            case AugmentRarity.Rare: return "레어";
            case AugmentRarity.Unique: return "유니크";
            case AugmentRarity.Legendary: return "레전더리";
            default: return "노말";
        }
    }

    private void BuildAugmentCards()
    {
        if (augmentsBuilt || augmentDatabase == null) return;
        augmentsBuilt = true;

        var list = augmentDatabase.allAugments.Where(a => a != null)
            .OrderBy(a => a.rarity).ThenBy(a => a.displayName).ToList();
        if (augmentHeader != null)
            augmentHeader.text = $"총 {list.Count}종 · 10턴마다 두 플레이어가 각자 증강 카드 1장을 고릅니다 (등급: 노말 < 레어 < 유니크 < 레전더리)";

        foreach (var a in list)
        {
            var card = new GameObject(a.displayName, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(augmentContent, false);
            card.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 1f);

            var bar = NewImage(card.transform, "Rarity Bar", RarityColor(a.rarity));
            bar.rectTransform.anchorMin = new Vector2(0, 1); bar.rectTransform.anchorMax = new Vector2(1, 1);
            bar.rectTransform.pivot = new Vector2(0.5f, 1); bar.rectTransform.sizeDelta = new Vector2(0, 40);
            var rar = NewText(bar.transform, "Rarity", RarityName(a.rarity), 24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Stretch(rar.rectTransform);

            var icon = NewImage(card.transform, "Icon", Color.white);
            icon.sprite = a.icon; icon.preserveAspect = true; icon.enabled = a.icon != null;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 1);
            icon.rectTransform.pivot = new Vector2(0.5f, 1);
            icon.rectTransform.sizeDelta = new Vector2(150, 150); icon.rectTransform.anchoredPosition = new Vector2(0, -50);

            var name = NewText(card.transform, "Name", a.displayName, 30, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0.5f, 1);
            name.rectTransform.pivot = new Vector2(0.5f, 1);
            name.rectTransform.sizeDelta = new Vector2(330, 44); name.rectTransform.anchoredPosition = new Vector2(0, -210);

            var desc = NewText(card.transform, "Description", a.description, 24, FontStyle.Normal, new Color(0.88f, 0.88f, 0.92f), TextAnchor.UpperCenter);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap; desc.verticalOverflow = VerticalWrapMode.Truncate;
            desc.resizeTextForBestFit = true; desc.resizeTextMinSize = 16; desc.resizeTextMaxSize = 24;
            desc.rectTransform.anchorMin = new Vector2(0, 0); desc.rectTransform.anchorMax = new Vector2(1, 1);
            desc.rectTransform.offsetMin = new Vector2(16, 12); desc.rectTransform.offsetMax = new Vector2(-16, -262);
        }
    }

    private Image NewImage(Transform parent, string name, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
        return img;
    }

    private Text NewText(Transform parent, string name, string s, int size, FontStyle style, Color c, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font; t.text = s; t.fontSize = size; t.fontStyle = style; t.color = c; t.alignment = anchor;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
    #endregion
}
