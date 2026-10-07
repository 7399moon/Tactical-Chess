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
    [SerializeField] private Text unoText;                 // [추가] 우노 모드 탭 본문(기존 Text는 숨기고 구조화된 내용으로 대체)
    [SerializeField] private UnoCardDatabase unoDatabase;  // [추가] 카드 설명 행의 카드 이미지용
    [SerializeField] private Text augmentHeader;
    [SerializeField] private RectTransform augmentContent; // GridLayoutGroup(4열) 부모
    [SerializeField] private AugmentDatabase augmentDatabase;
    [SerializeField] private Font font;
    [SerializeField] private Sprite[] rarityFrames = new Sprite[4]; // [변경] 0 노말, 1 레어, 2 유니크, 3 레전더리 카드 프레임
    [SerializeField] private Sprite[] pieceIcons = new Sprite[6];   // [변경] 0 폰, 1 나이트, 2 비숍, 3 룩, 4 퀸, 5 킹 (룰/스킬 설명용 아이콘)
    [SerializeField] private Sprite[] skillIcons = new Sprite[6];   // [변경] 0 나이트, 1 비숍, 2 룩, 3 킹, 4 퀸 스킬 버튼, 5 턴 종료 버튼
    #endregion

    // 탭 버튼은 btn_normal 하나를 쓰고 선택 여부는 틴트로만 구분한다.
    private static readonly Color TabNormal = Color.white;
    private static readonly Color TabSelected = new Color(1f, 0.78f, 0.3f, 1f);
    private bool augmentsBuilt;

    #region 튜토리얼 본문 (구조화)
    // [변경] 룰/스킬 페이지를 긴 텍스트 한 덩어리 대신 "섹션 제목 + 아이콘 행" 구조로 만든다.
    private const int P_PAWN = 0, P_KNIGHT = 1, P_BISHOP = 2, P_ROOK = 3, P_QUEEN = 4, P_KING = 5;
    private const string TitleHex = "#FFC84D";   // 제목(금색)
    private const string TagHex = "#9FD4FF";     // 쿨타임 표기(하늘색)

    private void BuildRulesPage()
    {
        var c = PrepareScrollContent(rulesText);
        AddSection(c, "게임 진행");
        AddBullet(c, "백이 먼저 시작하고, 한 턴에 기물 하나를 한 번 움직입니다.");
        AddBullet(c, "상대 킹을 체크메이트로 몰면 승리합니다. 킹이 직접 잡혀도 게임이 끝납니다.");
        AddBullet(c, "움직일 수 있는 수가 없는데 체크 상태가 아니면 스테일메이트로 무승부입니다.");
        AddBullet(c, "방장이 턴 시간 제한을 켜 두었다면, 시간이 끝났을 때 턴이 자동으로 넘어갑니다.");

        AddSection(c, "기물 이동");
        AddRow(c, new[] { pieceIcons[P_PAWN] }, "폰", null, "앞으로 1칸(첫 이동은 2칸), 대각선 앞의 적을 잡습니다.", 84);
        AddRow(c, new[] { pieceIcons[P_KNIGHT] }, "나이트", null, "L자로 이동하며 다른 기물을 뛰어넘을 수 있습니다.", 84);
        AddRow(c, new[] { pieceIcons[P_BISHOP] }, "비숍", null, "대각선으로 원하는 만큼 이동합니다.", 84);
        AddRow(c, new[] { pieceIcons[P_ROOK] }, "룩", null, "가로·세로로 원하는 만큼 이동합니다.", 84);
        AddRow(c, new[] { pieceIcons[P_QUEEN] }, "퀸", null, "가로·세로·대각선으로 원하는 만큼 이동합니다.", 84);
        AddRow(c, new[] { pieceIcons[P_KING] }, "킹", null, "모든 방향으로 1칸 이동하며, 체크 상태가 되는 칸으로는 갈 수 없습니다.", 84);

        AddSection(c, "특수 규칙");
        AddRow(c, new[] { pieceIcons[P_KING], pieceIcons[P_ROOK] }, "캐슬링", null, "킹과 룩이 아직 움직이지 않았고 둘 사이가 비어 있으면 함께 자리를 바꿀 수 있습니다.", 84);
        AddRow(c, new[] { pieceIcons[P_PAWN] }, "앙파상", null, "상대 폰이 2칸 전진해 내 폰 옆에 섰다면, 바로 다음 턴에 그 뒤 칸으로 대각선 이동하며 잡을 수 있습니다.", 84);
        AddRow(c, new[] { pieceIcons[P_PAWN], pieceIcons[P_QUEEN] }, "프로모션", null, "폰이 상대편 끝줄에 도착하면 카드 중에서 승급할 기물을 고릅니다.", 84);
    }

    private void BuildSkillsPage()
    {
        var c = PrepareScrollContent(skillsText);
        AddBullet(c, "이동 전후로 조건이 맞으면 화면 왼쪽의 스킬 버튼이 활성화됩니다. 한 턴에 스킬은 한 번만 쓸 수 있고, 사용 후에는 쿨타임(턴)이 돌아야 다시 쓸 수 있습니다. (증강으로 효과가 바뀔 수 있습니다)", false);

        AddSection(c, "기물 스킬");
        AddRow(c, new[] { skillIcons[0] }, "나이트 · 위협", "쿨타임 4턴", "나이트를 움직인 턴에 사용합니다. 주변 1칸 안의 적 기물을 지정하면, 그 기물은 다음 턴까지 움직일 수 없습니다.", 120);
        AddRow(c, new[] { skillIcons[1] }, "비숍 · 워프", "쿨타임 4턴", "기물을 움직이기 전에 사용합니다. 비숍이 상하좌우로 인접한 빈 칸 1곳으로 즉시 워프하며, 워프한 뒤에는 이어서 일반 이동을 할 수 있습니다.", 120);
        AddRow(c, new[] { skillIcons[2] }, "룩 · 쉴드", "쿨타임 6턴", "룩을 움직인 턴에 사용합니다. 주변 1칸 안의 아군 기물에 1턴 동안 공격을 막아 주는 보호막을 씌웁니다.", 120);
        AddRow(c, new[] { skillIcons[3] }, "킹 · 지휘", "쿨타임 8턴", "기물을 움직이기 전에 사용합니다. 아군 기물 하나를 지정하면 그 기물이 연속으로 2번 움직일 수 있습니다.", 120);
        AddRow(c, new[] { skillIcons[4] }, "퀸 · 아우라", "패시브", "퀸이 살아 있는 동안 우리 팀이 적 기물을 잡을 때마다 스택이 쌓입니다. 6스택이 되면 퀸 버튼으로 아군 기물 하나를 한 단계 승급시킬 수 있습니다. (폰 → 나이트 → 비숍 → 룩 → 퀸) 퀸이 모두 사라지면 스택이 초기화됩니다.", 120);

        AddSection(c, "턴 종료");
        AddRow(c, new[] { skillIcons[5] }, "턴 종료 버튼", null, "이동을 마친 뒤 더 쓸 수 있는 스킬이 없으면 턴이 자동으로 끝납니다. 스킬을 쓸 수 있는 상황에서 그냥 넘기려면 '턴 종료' 버튼을 누르세요.", 120);

        AddBullet(c, "※ 방장이 스킬 시스템을 끄면 스킬 버튼과 퀸 아우라가 모두 사라지고, 이동만 하면 턴이 자동으로 끝납니다.", false);
    }
    #endregion


    #region 우노 모드 페이지 [추가]
    // 우노 카드 스프라이트를 데이터베이스에서 찾는다 (없으면 null → 이미지 없이 표시)
    private Sprite UnoSprite(UnoColor color, UnoKind kind, int number = 0)
    {
        if (unoDatabase == null) return null;
        var d = unoDatabase.allCards.FirstOrDefault(x => x != null && x.kind == kind && x.color == color
                                                         && (kind != UnoKind.Number || x.number == number));
        return d != null ? d.sprite : null;
    }

    private void BuildUnoPage()
    {
        if (unoText == null) return;
        var c = PrepareScrollContent(unoText);
        const float CardIcon = 130f;

        AddSection(c, "기본 플레이");
        AddBullet(c, "체스 기물을 직접 고르는 대신 카드를 내고, 그 카드의 효과대로 기물을 움직이는 모드입니다. 백이 먼저 시작하며, 체크·체크메이트·캐슬링·앙파상·프로모션 같은 체스 규칙은 그대로 적용됩니다.");
        AddBullet(c, "내 턴에 버림 더미의 맨 위 카드와 색, 숫자, 종류 중 하나가 같은 카드(또는 와일드)를 낼 수 있습니다. 시작 카드가 와일드이면 아무 카드나 낼 수 있습니다.");
        AddBullet(c, "낼 수 있는 카드가 없으면 카드 1장을 뽑고 턴이 끝납니다. 뽑은 카드는 다음 턴부터 쓸 수 있습니다.");
        AddBullet(c, "화면 아래 카드 패는 언제든 클릭해서 올리고 내릴 수 있습니다. 카드를 내는 것은 내 턴에만 가능하며, 카드를 한 번 누르면 선택, 한 번 더 누르면 냅니다.");
        AddBullet(c, "우노 모드에서는 턴 시간 제한이 적용되지 않습니다. 시작 손패 장수와 최대 패 장수는 방장이 정한 설정을 따릅니다.");

        AddSection(c, "승리 조건");
        AddBullet(c, "상대에게 체크메이트를 하면 승리합니다. (움직일 수 있는 수가 없고 체크 상태가 아니면 스테일메이트로 무승부)");
        AddBullet(c, "카드를 내서 내 손패가 0장이 되면 효과 처리 없이 즉시 승리합니다.");
        AddBullet(c, "패가 최대 장수(기본 15장)가 되면 패배합니다. 상대의 패가 최대 장수가 되면 승리입니다.");

        AddSection(c, "카드 효과");
        AddRow(c, new[] { UnoSprite(UnoColor.Red, UnoKind.Number, 1), UnoSprite(UnoColor.Blue, UnoKind.Number, 4) }, "숫자 1~4 · 이동", null,
            "서로 다른 기물을 숫자만큼 한 번씩 움직입니다. 같은 기물은 한 카드에서 두 번 움직일 수 없고, 최소 1번은 움직여야 합니다. 움직일 수 있는 기물이 부족하면 '턴 종료' 버튼으로 넘길 수 있습니다. 체크 상태라면 첫 이동은 체크를 벗어나는 데 써야 합니다.", CardIcon);
        AddRow(c, new[] { UnoSprite(UnoColor.Green, UnoKind.Number, 5), UnoSprite(UnoColor.Yellow, UnoKind.Number, 8) }, "숫자 5~8 · 부활", null,
            "잡힌 내 기물 중 하나를 골라 내 진영 첫 두 줄의 빈칸에 되살립니다. 세로 줄 n번째 또는 n-4번째(5→5·1줄, 6→6·2줄, 7→7·3줄, 8→8·4줄)에서 고를 수 있고, 폰은 2랭크에만 부활합니다. 부활한 턴에는 기물을 움직이지 않습니다. 잡힌 기물이 없거나 둘 칸이 없으면 효과 없이 턴이 넘어갑니다.", CardIcon);
        AddRow(c, new[] { UnoSprite(UnoColor.Red, UnoKind.Number, 0) }, "숫자 0 · 새로고침", null,
            "내 손패를 모두 덱에 섞고 같은 장수만큼 새로 뽑은 뒤, 기물을 1회 움직입니다.", CardIcon);
        AddRow(c, new[] { UnoSprite(UnoColor.Blue, UnoKind.Skip) }, "스킵", null,
            "기물을 1회 움직인 뒤 상대의 턴을 통째로 건너뛰고, 내가 다시 카드를 냅니다.", CardIcon);
        AddRow(c, new[] { UnoSprite(UnoColor.Yellow, UnoKind.Draw2) }, "+2", null,
            "상대에게 +2를 부과하고 기물을 1회 움직입니다. 상대는 +2 또는 +4로 반격할 수 있습니다.", CardIcon);
        AddRow(c, new[] { UnoSprite(UnoColor.Green, UnoKind.Reverse) }, "리버스", null,
            "나와 상대의 손패를 통째로 맞바꾸고 기물을 1회 움직입니다.", CardIcon);
        AddRow(c, new[] { UnoSprite(UnoColor.Wild, UnoKind.Wild) }, "와일드", null,
            "언제든 낼 수 있습니다. 다음 색을 고르고 기물을 1회 움직입니다.", CardIcon);
        AddRow(c, new[] { UnoSprite(UnoColor.Wild, UnoKind.WildDraw4) }, "와일드 +4", null,
            "언제든 낼 수 있습니다. 상대에게 +4를 부과하고 다음 색을 고른 뒤 기물을 1회 움직입니다. 상대는 +4로만 반격할 수 있습니다.", CardIcon);

        AddSection(c, "+2 / +4 중첩");
        AddBullet(c, "+2나 +4를 맞은 쪽은 반격해 장수를 쌓을 수 있습니다. (+2 → +2/+4 가능, +4 → +4만 가능) 반격한 카드도 기물을 1회 움직입니다.");
        AddBullet(c, "한 번 +4가 들어가면 이후에는 +4로만 반격할 수 있습니다. 반격하지 못하면 쌓인 장수만큼 카드를 모두 뽑고 턴이 상대에게 넘어갑니다.");

        AddSection(c, "UNO 경쟁");
        AddBullet(c, "카드를 내서 손패가 1장이 되면, 효과 처리 전에 UNO 경쟁이 시작됩니다. (리버스로 1장이 된 경우는 교환 직후)");
        AddBullet(c, "화면에 빛나는 빈 칸을 먼저 누르는 쪽이 이깁니다. 손패 주인이 먼저 누르면 UNO 선언 성공, 상대가 먼저 누르면 손패 주인이 2장을 뽑습니다.");

        AddSection(c, "덱");
        AddBullet(c, "덱이 모자라면 버림 더미의 맨 위 카드 1장만 남기고 나머지를 섞어 덱에 다시 넣습니다.");
    }
    #endregion

    #region 설명 페이지 레이아웃 헬퍼
    // 기존 단일 Text 대신 VerticalLayoutGroup 콘텐츠를 ScrollRect에 연결한다. (기존 Text 오브젝트는 숨김)
    private RectTransform PrepareScrollContent(Text oldText)
    {
        var scroll = oldText.GetComponentInParent<ScrollRect>(true);
        oldText.gameObject.SetActive(false);

        var go = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(scroll.viewport, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero;

        var v = go.GetComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(8, 8, 6, 24); v.spacing = 12;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = rt;
        return rt;
    }

    // 섹션 제목 + 금색 구분선
    private void AddSection(RectTransform parent, string title)
    {
        var box = new GameObject("Section " + title, typeof(RectTransform), typeof(VerticalLayoutGroup));
        box.transform.SetParent(parent, false);
        var v = box.GetComponent<VerticalLayoutGroup>();
        v.spacing = 4; v.padding = new RectOffset(0, 0, 14, 0);
        v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

        var t = NewText(box.transform, "Title", title, 36, FontStyle.Bold, new Color(1f, 0.78f, 0.3f), TextAnchor.MiddleLeft);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.gameObject.AddComponent<LayoutElement>().minHeight = 48;

        var line = NewImage(box.transform, "Line", new Color(1f, 0.78f, 0.3f, 0.55f));
        line.gameObject.AddComponent<LayoutElement>().minHeight = 3;
    }

    // 아이콘 없는 설명 한 줄 (글머리표 옵션)
    private void AddBullet(RectTransform parent, string text, bool bullet = true)
    {
        var t = NewText(parent, "Bullet", bullet ? "• " + text : text, 27, FontStyle.Normal, new Color(0.92f, 0.92f, 0.95f), TextAnchor.UpperLeft);
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.lineSpacing = 1.15f;
    }

    // 왼쪽 아이콘(1~2개) + 오른쪽 제목/태그/설명 한 행
    private void AddRow(RectTransform parent, Sprite[] icons, string title, string tag, string desc, float iconSize)
    {
        var row = new GameObject("Row " + title, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        var bg = row.GetComponent<Image>(); bg.color = new Color(1f, 1f, 1f, 0.06f); bg.raycastTarget = false;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(16, 20, 10, 10); h.spacing = 18; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;

        foreach (var sp in icons)
        {
            var ic = NewImage(row.transform, "Icon", Color.white);
            ic.sprite = sp; ic.preserveAspect = true; ic.enabled = sp != null;
            var le = ic.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = iconSize; le.preferredHeight = iconSize; le.minWidth = iconSize; le.minHeight = iconSize;
        }

        var col = new GameObject("Text Column", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        col.transform.SetParent(row.transform, false);
        var cv = col.GetComponent<VerticalLayoutGroup>();
        cv.spacing = 4; cv.childControlWidth = cv.childControlHeight = true; cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        cv.childAlignment = TextAnchor.MiddleLeft;
        var cle = col.GetComponent<LayoutElement>(); cle.flexibleWidth = 1; cle.minWidth = 100;

        string head = $"<color={TitleHex}>{title}</color>" + (string.IsNullOrEmpty(tag) ? "" : $"   <size=24><color={TagHex}>{tag}</color></size>");
        var tt = NewText(col.transform, "Title", head, 32, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
        tt.supportRichText = true; tt.horizontalOverflow = HorizontalWrapMode.Overflow;
        var td = NewText(col.transform, "Description", desc, 26, FontStyle.Normal, new Color(0.92f, 0.92f, 0.95f), TextAnchor.UpperLeft);
        td.horizontalOverflow = HorizontalWrapMode.Wrap; td.verticalOverflow = VerticalWrapMode.Overflow; td.lineSpacing = 1.12f;
    }
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
        BuildRulesPage();   // [변경] 아이콘 + 제목 + 설명 행 구조로 생성
        BuildSkillsPage();
        BuildUnoPage();     // [추가]
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
            augmentHeader.text = $"총 {list.Count}종 · 10턴마다 두 플레이어가 각자 증강 카드 1장을 고릅니다 (등급: 노말(동) < 레어(은) < 유니크(금) < 레전더리(청))";

        // [변경] 카드 프레임(464x760 비율)에 맞춰 셀 크기와 열 수를 조정 (5열)
        const float W = 270f, H = 442f;
        var grid = augmentContent.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(W, H);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter; // [변경] 가운데 정렬
        }

        foreach (var a in list)
        {
            var card = new GameObject(a.displayName, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(augmentContent, false);
            var frame = card.GetComponent<Image>();
            int fi = Mathf.Clamp((int)a.rarity, 0, 3);
            frame.sprite = rarityFrames != null && fi < rarityFrames.Length ? rarityFrames[fi] : null;
            frame.color = frame.sprite != null ? Color.white : new Color(0.12f, 0.12f, 0.16f, 1f);
            frame.raycastTarget = false;

            // [변경] 프레임의 영역 비율: 아트 중심 0.32 / 이름 바 0.62 / 설명 0.71~0.93
            var icon = NewImage(card.transform, "Icon", Color.white);
            icon.sprite = a.icon; icon.preserveAspect = true; icon.enabled = a.icon != null;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 1);
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.rectTransform.sizeDelta = new Vector2(170, 170); icon.rectTransform.anchoredPosition = new Vector2(0, -H * 0.32f);

            var name = NewText(card.transform, "Name", a.displayName, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 14; name.resizeTextMaxSize = 24;
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0.5f, 1);
            name.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            name.rectTransform.sizeDelta = new Vector2(W * 0.8f, 40); name.rectTransform.anchoredPosition = new Vector2(0, -H * 0.62f);

            var desc = NewText(card.transform, "Description", a.description, 20, FontStyle.Normal, new Color(0.92f, 0.9f, 0.85f), TextAnchor.UpperCenter);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap; desc.verticalOverflow = VerticalWrapMode.Truncate;
            desc.resizeTextForBestFit = true; desc.resizeTextMinSize = 12; desc.resizeTextMaxSize = 20;
            desc.rectTransform.anchorMin = new Vector2(0, 0); desc.rectTransform.anchorMax = new Vector2(1, 1);
            desc.rectTransform.offsetMin = new Vector2(W * 0.1f, H * 0.075f); desc.rectTransform.offsetMax = new Vector2(-W * 0.1f, -H * 0.72f);
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
