using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 우노 모드 화면: 왼쪽(버림 더미 / 카드 뽑기 / 덱), 아래(부채꼴 카드 패), 상대 닉네임 옆 카드 장수, 타임바 왼쪽 이동 점.
// 씬에는 이 컴포넌트가 붙은 빈 오브젝트 하나만 두고, 나머지 UI는 코드에서 만든다 (Hierarchy를 깔끔하게 유지).
// 우노 모드가 아니면 전체가 숨겨진다.
public class UnoUI : MonoBehaviour
{
    #region 인스펙터 설정값
    [Header("리소스")]
    [SerializeField] private Font font;
    [SerializeField] private Sprite cardBack;                 // UNO_Back

    [Header("참조")]
    [SerializeField] private RectTransform opponentPlate;     // 상대 닉네임 표시판 (Player Nameplates/Top Plate)
    [SerializeField] private RectTransform turnTimeBar;       // 타임 바 (이동 점을 이 왼쪽에 둔다)
    #endregion

    #region 크기/배치 상수 (1920x1080 기준)
    private static readonly Vector2 HandCardSize = new Vector2(150f, 234f);
    private static readonly Vector2 PileCardSize = new Vector2(130f, 203f);
    private const float FanRadius = 1000f;       // 부채꼴 반지름
    private const float FanStepDeg = 3.6f;       // 카드 사이 각도
    private const float FanMaxHalfDeg = 30f;     // 부채꼴 한쪽 최대 각도
    private const float CollapsedY = -150f;      // 접힌 상태: 카드 윗부분(약 84px)만 보인다
    private const float ExpandedY = -50f;        // 펼친 상태 (보드의 앞줄 기물을 너무 가리지 않게 카드 아랫부분은 화면 밖에 둔다)
    private const float HoverLift = 38f;         // 펼친 상태에서 마우스를 올린 카드가 더 올라오는 높이
    private const float SelectLift = 70f;        // 1번 클릭(선택)한 카드가 올라오는 높이
    private const int MaxPileObjects = 10;       // 화면에 표시하는 버림 더미 카드 수
    private const float FlyDuration = 0.32f;     // 카드가 더미로 날아가는 시간
    private static readonly Color Gold = new Color(1f, 0.82f, 0.25f, 1f);
    #endregion

    #region 런타임 오브젝트
    private RectTransform root;
    private RectTransform handRoot;
    private RectTransform pileRoot;
    private RectTransform deckRect;
    private Text deckCountText;
    private Button drawButton;
    private Text drawButtonText;
    private Text pendingText;
    private RectTransform dotsRoot;
    private readonly List<Image> dots = new List<Image>();
    private Text opponentCountText;
    private Sprite dotSprite;

    private readonly List<UnoCardView> views = new List<UnoCardView>();   // 내 손패 (UnoMatch 손패와 같은 순서)
    private readonly List<Image> pileViews = new List<Image>();            // 버림 더미 표시 오브젝트 (뒤가 최상단)
    private int pileTotal;                                                 // 버림 더미 실제 장수 (재섞기 감지용)
    #endregion

    #region 입력 상태
    private bool expanded;          // 패가 펼쳐져 있는가
    private int selected = -1;      // 1번 클릭으로 선택된 손패 위치
    private UnoCardView hovered;
    private bool wasCanAct;
    #endregion

    [SerializeField] private Sprite pawnIcon; // 부활 선택 패널의 폰 아이콘 (승급 카드 데이터에 폰이 없어 따로 받는다)

    private UnoTurnController Ctl => UnoTurnController.Instance;

    #region 생명주기
    private void Awake()
    {
        Build();
    }

    private void OnEnable()
    {
        var c = Ctl;
        if (c == null) return;
        c.OnMatchBegun += HandleMatchBegun;
        c.OnStateChanged += HandleStateChanged;
        c.OnCardPlayed += HandleCardPlayed;
        c.OnColorChosen += HandleColorChosen;
        c.OnReviveSelectStarted += HandleReviveStarted;
        c.OnReviveSelectEnded += HandleReviveEnded;
        c.OnCardsDrawn += HandleCardsDrawn;
        HandleMatchBegun(); // 이미 시작된 판이면(캔버스가 매치 시작 때 켜짐) 현재 상태를 바로 그린다
    }

    private void OnDisable()
    {
        var c = Ctl;
        if (c == null) return;
        c.OnMatchBegun -= HandleMatchBegun;
        c.OnStateChanged -= HandleStateChanged;
        c.OnCardPlayed -= HandleCardPlayed;
        c.OnColorChosen -= HandleColorChosen;
        c.OnReviveSelectStarted -= HandleReviveStarted;
        c.OnReviveSelectEnded -= HandleReviveEnded;
        c.OnCardsDrawn -= HandleCardsDrawn;
    }

    private void Update()
    {
        if (!UnoTurnController.Active || Ctl.AwaitingDeal) return;

        // 카드가 아닌 곳을 클릭하면 패를 다시 내린다
        if (expanded && Input.GetMouseButtonDown(0) && hovered == null)
        {
            expanded = false;
            selected = -1;
        }

        // 화면 주인의 팀이 바뀌는 로컬 테스트에서도 패가 맞게 보이도록 매 프레임 가볍게 확인한다
        bool canAct = Ctl.CanAct;
        if (canAct != wasCanAct)
        {
            wasCanAct = canAct;
            if (canAct) expanded = true;   // 내 턴이 되면 패가 자동으로 올라온다
            else { expanded = false; selected = -1; }
            RefreshStatic();
            RefreshDim();
        }

        UpdateHandTargets();
    }
    #endregion

    #region UI 만들기
    private void Build()
    {
        root = NewRect("Uno Root", transform);
        Stretch(root);
        dotSprite = MakeDotSprite();

        // 왼쪽: 버림 더미 / 카드 뽑기 버튼 / 덱
        pileRoot = NewRect("Discard Pile", root);
        SetAnchor(pileRoot, new Vector2(0f, 0.5f), new Vector2(150f, 270f), new Vector2(200f, 260f));

        var drawGo = new GameObject("Draw Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var drawRt = (RectTransform)drawGo.transform;
        drawRt.SetParent(root, false);
        SetAnchor(drawRt, new Vector2(0f, 0.5f), new Vector2(150f, 60f), new Vector2(230f, 78f));
        var drawImg = drawGo.GetComponent<Image>();
        drawImg.color = new Color(0.16f, 0.1f, 0.07f, 0.95f);
        var drawOutline = drawGo.AddComponent<Outline>();
        drawOutline.effectColor = new Color(0.75f, 0.6f, 0.3f, 1f);
        drawOutline.effectDistance = new Vector2(2f, -2f);
        drawButton = drawGo.GetComponent<Button>();
        drawButton.targetGraphic = drawImg;
        var colors = drawButton.colors;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
        drawButton.colors = colors;
        drawButton.onClick.AddListener(() => Ctl?.RequestDraw());
        drawButtonText = NewText("Label", drawRt, "카드 뽑기", 34, TextAnchor.MiddleCenter, Color.white);
        Stretch((RectTransform)drawButtonText.transform);

        var deckGo = new GameObject("Deck", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        deckRect = (RectTransform)deckGo.transform;
        deckRect.SetParent(root, false);
        SetAnchor(deckRect, new Vector2(0f, 0f), new Vector2(150f, 235f), PileCardSize);
        var deckImg = deckGo.GetComponent<Image>();
        deckImg.sprite = cardBack;
        deckImg.raycastTarget = false;
        deckCountText = NewText("Deck Count", deckRect, "", 40, TextAnchor.UpperCenter, Color.white);
        Stretch((RectTransform)deckCountText.transform);
        deckCountText.gameObject.AddComponent<Outline>().effectColor = Color.black;

        // 뽑아야 할 장수(+N)는 카드 뽑기 버튼 오른쪽에 표시한다
        pendingText = NewText("Pending Draw", root, "", 52, TextAnchor.MiddleLeft, Gold);
        SetAnchor((RectTransform)pendingText.transform, new Vector2(0f, 0.5f), new Vector2(330f, 60f), new Vector2(120f, 70f));
        pendingText.gameObject.AddComponent<Outline>().effectColor = Color.black;

        // 아래: 카드 패
        handRoot = NewRect("Hand", root);
        handRoot.anchorMin = handRoot.anchorMax = new Vector2(0.5f, 0f);
        handRoot.pivot = new Vector2(0.5f, 0f);
        handRoot.anchoredPosition = Vector2.zero;
        handRoot.sizeDelta = Vector2.zero;

        // 타임 바 왼쪽: 이동 횟수 점 (위에서 아래로, 최대 4개)
        dotsRoot = NewRect("Move Dots", root);
        dotsRoot.anchorMin = dotsRoot.anchorMax = new Vector2(1f, 1f);
        dotsRoot.pivot = new Vector2(0.5f, 0.5f);
        dotsRoot.sizeDelta = new Vector2(44f, 220f);
        Vector2 barPos = turnTimeBar != null ? turnTimeBar.anchoredPosition : new Vector2(-95f, -597f);
        float barWidth = turnTimeBar != null ? turnTimeBar.sizeDelta.x : 75f;
        dotsRoot.anchoredPosition = new Vector2(barPos.x - barWidth * 0.5f - 40f, barPos.y);
        for (int i = 0; i < 4; i++)
        {
            var dot = new GameObject("Dot " + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            dot.transform.SetParent(dotsRoot, false);
            var drt = (RectTransform)dot.transform;
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 1f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.sizeDelta = new Vector2(38f, 38f);
            drt.anchoredPosition = new Vector2(0f, -i * 54f);
            dot.sprite = dotSprite;
            dot.raycastTarget = false;
            dots.Add(dot);
        }

        // 상대 닉네임 오른쪽: 카드 아이콘 + 장수
        if (opponentPlate != null)
        {
            var icon = new GameObject("Uno Opponent Cards", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var irt = (RectTransform)icon.transform;
            irt.SetParent(opponentPlate, false);
            irt.anchorMin = irt.anchorMax = new Vector2(1f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(18f, 0f);
            irt.sizeDelta = new Vector2(34f, 52f);
            var img = icon.GetComponent<Image>();
            img.sprite = cardBack;
            img.raycastTarget = false;
            opponentCountText = NewText("Count", irt, "", 38, TextAnchor.MiddleLeft, Color.white);
            var crt = (RectTransform)opponentCountText.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
            crt.pivot = new Vector2(0f, 0.5f);
            crt.anchoredPosition = new Vector2(10f, 0f);
            crt.sizeDelta = new Vector2(110f, 52f);
            opponentCountText.gameObject.AddComponent<Outline>().effectColor = Color.black;
        }
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private Text NewText(string name, Transform parent, string text, int size, TextAnchor align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void SetAnchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // 이동 점용 원 스프라이트를 코드로 만든다 (기본 UI 스프라이트는 빌드에서 쓸 수 없다)
    private static Sprite MakeDotSprite()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float c = (N - 1) * 0.5f, r = N * 0.5f - 1.5f;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(r - d + 0.5f);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }
    #endregion

    #region 컨트롤러 이벤트
    private void HandleMatchBegun()
    {
        // 게임 종료(패 15장) 이벤트 구독 (판이 다시 시작돼도 중복되지 않게 한 번 풀고 건다)
        if (GameEndManager.Instance != null)
        {
            GameEndManager.Instance.OnWin -= HandleGameWin;
            GameEndManager.Instance.OnWin += HandleGameWin;
        }
        StopAllCoroutines();
        if (vignette != null) vignette.gameObject.SetActive(false);
        if (root != null) root.anchoredPosition = Vector2.zero;

        bool on = UnoTurnController.Active && !Ctl.AwaitingDeal; // 게스트는 호스트의 첫 배분이 올 때까지 그리지 않는다
        root.gameObject.SetActive(on);
        if (opponentPlate != null) SetOpponentVisible(on);
        if (!on) return;

        // 이전 판의 표시물을 모두 지우고 현재 상태로 다시 그린다
        for (int i = views.Count - 1; i >= 0; i--) if (views[i] != null) Destroy(views[i].gameObject);
        views.Clear();
        for (int i = pileViews.Count - 1; i >= 0; i--) if (pileViews[i] != null) Destroy(pileViews[i].gameObject);
        pileViews.Clear();
        pileTotal = 0;
        selected = -1;
        hovered = null;
        wasCanAct = Ctl.CanAct;
        expanded = wasCanAct;

        var history = Ctl.Match.Deck.DiscardHistory;
        AddPileCard(history[history.Count - 1], Ctl.Match.CurrentColor, instant: true);
        pileTotal = Ctl.Match.Deck.DiscardCount;

        SyncHand(animateNew: false);
        RefreshStatic();
        RefreshDim();
    }

    private void HandleStateChanged()
    {
        if (!UnoTurnController.Active) return;
        SyncHand(animateNew: false);
        RefreshStatic();
        RefreshDim();
    }

    private void HandleCardPlayed(int team, UnoCard card, int handIndex, UnoColor color)
    {
        if (!UnoTurnController.Active) return;
        SoundManager.Instance?.PlayShield(); // 카드 내기 효과음

        // 버림 더미가 재섞기로 줄었으면 더미 표시를 정리한다 (낸 카드 1장만 남는다)
        Vector3 from;
        Quaternion fromRot = Quaternion.identity;
        Vector2 fromSize = HandCardSize;
        if (team == Ctl.ViewTeam && handIndex >= 0 && handIndex < views.Count)
        {
            var v = views[handIndex];
            views.RemoveAt(handIndex);
            from = v.Rect.position;
            fromRot = v.Rect.rotation;
            Destroy(v.gameObject);
            expanded = false;
            selected = -1;
            hovered = null;
        }
        else
        {
            // 상대가 낸 카드는 상대 닉네임 표시판에서 날아온다
            from = opponentPlate != null ? opponentPlate.position : pileRoot.position;
            fromSize = PileCardSize * 0.6f;
        }

        Sprite sprite = FaceSprite(card, color);
        StartCoroutine(FlyToPile(sprite, from, fromRot, fromSize, card, color));

        SyncHand(animateNew: false);
        RefreshStatic();
        RefreshDim();
    }

    #region 패배 연출 (CARD OVERFLOW)
    private Image vignette;

    private void HandleGameWin(int winningTeam, GameEndManager.GameEndReason reason)
    {
        if (reason != GameEndManager.GameEndReason.HandOverflow || !UnoTurnController.Active) return;
        StartCoroutine(OverflowRoutine(1 - winningTeam == Ctl.ViewTeam));
    }

    // 15장 과부하: 화면 흔들림 -> 손패가 위로 튀어 흩어짐 -> 붉은 비네트 + "CARD OVERFLOW" (결과 화면은 GameOverUI가 약 2초 뒤에 띄운다)
    private IEnumerator OverflowRoutine(bool localLost)
    {
        if (vignette == null)
        {
            var go = new GameObject("Overflow Vignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            Stretch((RectTransform)go.transform);
            vignette = go.GetComponent<Image>();
            vignette.sprite = DefeatFx.VignetteSprite();
            vignette.raycastTarget = false;
        }
        vignette.transform.SetAsLastSibling();
        vignette.gameObject.SetActive(true);
        vignette.color = new Color(1f, 0.1f, 0.1f, 0f);

        var flying = new List<UnoCardView>();
        var vel = new List<Vector2>();
        var spin = new List<float>();
        if (localLost)
        {
            foreach (var v in views)
            {
                if (v == null) continue;
                v.Frozen = true;
                flying.Add(v);
                vel.Add(new Vector2(Random.Range(-900f, 900f), Random.Range(1500f, 2300f)));
                spin.Add(Random.Range(-540f, 540f));
            }
        }

        float t = 0f;
        const float Duration = 1.9f;
        while (t < Duration)
        {
            t += Time.unscaledDeltaTime;
            float dt = Time.unscaledDeltaTime;

            // 0~0.4초: 화면 흔들림
            root.anchoredPosition = t < 0.4f ? new Vector2(Random.Range(-14f, 14f), Random.Range(-14f, 14f)) * (1f - t / 0.4f) : Vector2.zero;

            // 0.3초부터 카드가 튀어 오르며 흩어진다
            if (t > 0.3f)
            {
                for (int i = 0; i < flying.Count; i++)
                {
                    if (flying[i] == null) continue;
                    vel[i] += new Vector2(0f, -2200f) * dt;
                    var rt = flying[i].Rect;
                    rt.anchoredPosition += vel[i] * dt;
                    rt.localRotation = Quaternion.Euler(0f, 0f, rt.localEulerAngles.z + spin[i] * dt);
                }
            }

            // 0.6초부터 붉은 비네트가 번진다
            float a = Mathf.Clamp01((t - 0.6f) / 0.6f) * (localLost ? 0.85f : 0.4f);
            vignette.color = new Color(1f, 0.1f, 0.1f, a);
            yield return null;
        }
        root.anchoredPosition = Vector2.zero;
    }

    #endregion

    #region 부활 기물 선택 패널
    private RectTransform revivePanel;
    private RectTransform reviveContent;
    private Button reviveConfirm;
    private int revivePicked = -1;
    private readonly List<(Outline ol, int idx)> reviveCards = new List<(Outline, int)>();

    private static string PieceName(ChessPieceType t)
    {
        switch (t)
        {
            case ChessPieceType.WhitePawn: case ChessPieceType.BlackPawn: return "폰";
            case ChessPieceType.WhiteKnight: case ChessPieceType.BlackKnight: return "나이트";
            case ChessPieceType.WhiteBishop: case ChessPieceType.BlackBishop: return "비숍";
            case ChessPieceType.WhiteRook: case ChessPieceType.BlackRook: return "룩";
            case ChessPieceType.WhiteQueen: case ChessPieceType.BlackQueen: return "퀸";
            default: return t.ToString();
        }
    }

    // 잡힌 기물을 좌우로 스크롤되는 카드 목록으로 보여 주고 하나를 고르게 한다 (내가 카드를 낸 클라이언트에서만 열린다)
    private void HandleReviveStarted(int team, List<int> capturedIndices)
    {
        if (team != Ctl.ViewTeam) return;
        if (revivePanel == null) BuildRevivePanel();

        for (int i = reviveContent.childCount - 1; i >= 0; i--) Destroy(reviveContent.GetChild(i).gameObject);
        reviveCards.Clear();
        revivePicked = -1;
        if (reviveConfirm != null) reviveConfirm.interactable = false;

        var captured = Ctl.GetCaptured(team);
        foreach (int idx in capturedIndices)
        {
            ChessPieceType type = captured[idx];
            int capturedIndex = idx;

            var go = new GameObject("Revive Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(reviveContent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.13f, 0.09f, 1f);
            var ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(0.75f, 0.6f, 0.3f, 1f);
            ol.effectDistance = new Vector2(3f, -3f);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 190f;
            le.minHeight = le.preferredHeight = 250f;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors; cb.highlightedColor = new Color(1.3f, 1.2f, 0.8f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); btn.colors = cb;
            btn.onClick.AddListener(() => PickRevive(capturedIndex));
            reviveCards.Add((ol, capturedIndex));

            Sprite icon = CardSelectionManager.Instance != null ? CardSelectionManager.Instance.GetPieceIcon(type) : null;
            if (icon == null && (type == ChessPieceType.WhitePawn || type == ChessPieceType.BlackPawn)) icon = pawnIcon;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(go.transform, false);
                var ir = (RectTransform)iconGo.transform;
                ir.anchorMin = new Vector2(0.1f, 0.3f); ir.anchorMax = new Vector2(0.9f, 0.92f);
                ir.offsetMin = ir.offsetMax = Vector2.zero;
                var iimg = iconGo.GetComponent<Image>();
                iimg.sprite = icon; iimg.preserveAspect = true; iimg.raycastTarget = false;
            }

            var label = NewText("Name", go.transform, PieceName(type), icon != null ? 34 : 48, icon != null ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter, Color.white);
            Stretch((RectTransform)label.transform);
            ((RectTransform)label.transform).offsetMin = new Vector2(0f, 12f);
            label.gameObject.AddComponent<Outline>().effectColor = Color.black;
        }

        revivePanel.gameObject.SetActive(true);
        var sr = revivePanel.GetComponentInChildren<ScrollRect>();
        if (sr != null) sr.horizontalNormalizedPosition = 0f;
    }

    // 부활 기물 카드를 눌렀다: 고르기 / 같은 카드를 다시 누르면 취소 / 다른 카드면 옮겨 고르기. 확정은 "확인" 버튼.
    private void PickRevive(int capturedIndex)
    {
        revivePicked = revivePicked == capturedIndex ? -1 : capturedIndex;
        foreach (var (ol, idx) in reviveCards)
        {
            if (ol == null) continue;
            bool on = idx == revivePicked;
            ol.effectColor = on ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0.75f, 0.6f, 0.3f, 1f);
            ol.effectDistance = on ? new Vector2(7f, -7f) : new Vector2(3f, -3f);
        }
        if (reviveConfirm != null) reviveConfirm.interactable = revivePicked >= 0;
    }

    private void ConfirmRevive()
    {
        if (revivePicked < 0) return;
        Ctl?.RequestRevivePiece(revivePicked);
    }

    private void HandleReviveEnded()
    {
        if (revivePanel != null) revivePanel.gameObject.SetActive(false);
    }

    private void BuildRevivePanel()
    {
        revivePanel = NewRect("Revive Panel", root);
        SetAnchor(revivePanel, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1100f, 480f));
        var bg = revivePanel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.07f, 0.05f, 0.94f);
        var bo = revivePanel.gameObject.AddComponent<Outline>();
        bo.effectColor = new Color(0.75f, 0.6f, 0.3f, 1f);
        bo.effectDistance = new Vector2(3f, -3f);

        var title = NewText("Title", revivePanel, "부활시킬 기물을 선택하세요", 40, TextAnchor.MiddleCenter, Gold);
        SetAnchor((RectTransform)title.transform, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(900f, 60f));
        title.gameObject.AddComponent<Outline>().effectColor = Color.black;

        var view = NewRect("Viewport", revivePanel);
        view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
        view.offsetMin = new Vector2(20f, 95f); view.offsetMax = new Vector2(-20f, -75f);
        view.gameObject.AddComponent<RectMask2D>();

        reviveContent = NewRect("Content", view);
        reviveContent.anchorMin = new Vector2(0f, 0f); reviveContent.anchorMax = new Vector2(0f, 1f);
        reviveContent.pivot = new Vector2(0f, 0.5f);
        reviveContent.offsetMin = reviveContent.offsetMax = Vector2.zero;
        var layout = reviveContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 24f;
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = reviveContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = revivePanel.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view;
        scroll.content = reviveContent;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        // 확인 버튼 (고른 기물을 확정)
        var confirmRt = NewRect("Confirm Button", revivePanel);
        SetAnchor(confirmRt, new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(280f, 70f));
        var cImg = confirmRt.gameObject.AddComponent<Image>();
        cImg.color = new Color(0.35f, 0.24f, 0.12f, 1f);
        confirmRt.gameObject.AddComponent<Outline>().effectColor = new Color(0.75f, 0.6f, 0.3f, 1f);
        reviveConfirm = confirmRt.gameObject.AddComponent<Button>();
        reviveConfirm.targetGraphic = cImg;
        var cc = reviveConfirm.colors; cc.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f); reviveConfirm.colors = cc;
        reviveConfirm.onClick.AddListener(ConfirmRevive);
        reviveConfirm.interactable = false;
        var cLabel = NewText("Label", confirmRt, "확인", 36, TextAnchor.MiddleCenter, Color.white);
        Stretch((RectTransform)cLabel.transform);

        revivePanel.gameObject.SetActive(false);
    }
    #endregion

    // 와일드 색이 정해지면 더미 맨 위 카드를 해당 색 카드로 바꾼다
    private void HandleColorChosen(UnoColor color)
    {
        if (!UnoTurnController.Active) return;
        var history = Ctl.Match.Deck.DiscardHistory;
        if (pileViews.Count > 0 && pileViews[pileViews.Count - 1] != null && history.Count > 0)
            pileViews[pileViews.Count - 1].sprite = FaceSprite(history[history.Count - 1], color);
        RefreshStatic();
        RefreshDim();
    }

    private void HandleCardsDrawn(int team, int count)
    {
        if (!UnoTurnController.Active) return;
        if (count > 0) SoundManager.Instance?.PlayAugmentCardAppear(); // 카드 뽑기 효과음
        SyncHand(animateNew: team == Ctl.ViewTeam);
        RefreshStatic();
        RefreshDim();
    }
    #endregion

    #region 손패 동기화
    // 화면의 카드 오브젝트를 UnoMatch의 내 손패와 맞춘다. 끝에 새 카드가 붙은 경우만 덱에서 날아오는 연출을 쓴다.
    private void SyncHand(bool animateNew)
    {
        var hand = Ctl.Match.GetHand(Ctl.ViewTeam);

        bool prefixSame = views.Count <= hand.Count;
        for (int i = 0; prefixSame && i < views.Count; i++)
            if (views[i].Card != hand[i]) prefixSame = false;

        if (!prefixSame)
        {
            // 순서/구성이 통째로 바뀜(리버스 교환, 0번 카드 등): 전부 새로 만든다
            for (int i = views.Count - 1; i >= 0; i--) Destroy(views[i].gameObject);
            views.Clear();
            animateNew = false;
        }

        for (int i = views.Count; i < hand.Count; i++)
        {
            UnoCard card = hand[i];
            Sprite sprite = Ctl.Database.Get(card).sprite;
            var view = UnoCardView.Create(this, handRoot, card, sprite, HandCardSize);
            views.Add(view);

            if (animateNew)
            {
                // 덱 위치에서 출발해 패의 자기 자리로 이동한다
                view.Rect.position = deckRect.position;
                view.TargetScale = 1f;
            }
            else
            {
                UpdateHandTargets();
                view.SnapTo(view.TargetPos, view.TargetRot, view.TargetScale);
            }
        }

        if (selected >= views.Count) selected = -1;
    }

    // 펼침/접힘, 호버, 선택 상태에 따라 각 카드의 목표 위치를 계산한다
    private void UpdateHandTargets()
    {
        int n = views.Count;
        if (n == 0) return;

        float step = n > 1 ? Mathf.Min(FanStepDeg, FanMaxHalfDeg * 2f / (n - 1)) : 0f;
        float baseY = expanded ? ExpandedY : CollapsedY;

        for (int i = 0; i < n; i++)
        {
            float angle = (i - (n - 1) * 0.5f) * step;        // 가운데 카드가 0도
            float rad = angle * Mathf.Deg2Rad;
            float x = Mathf.Sin(rad) * FanRadius;
            float y = (Mathf.Cos(rad) - 1f) * FanRadius + baseY;

            float scale = 1f;
            if (expanded)
            {
                if (i == selected) { y += SelectLift; scale = 1.12f; }
                else if (views[i] == hovered) { y += HoverLift; scale = 1.06f; }
            }

            var v = views[i];
            v.TargetPos = new Vector2(x, y);
            v.TargetRot = -angle;
            v.TargetScale = scale;
            v.Outline.enabled = expanded && i == selected;
            v.transform.SetSiblingIndex(i == selected || v == hovered ? n - 1 : i); // 올라온 카드가 앞에 보이게
        }
    }
    #endregion

    #region 입력 전달 (UnoCardView -> UnoUI)
    public void OnCardHover(UnoCardView view, bool enter)
    {
        if (enter) hovered = view;
        else if (hovered == view) hovered = null;
    }

    public void OnCardClicked(UnoCardView view)
    {
        if (!UnoTurnController.Active) return;
        int index = views.IndexOf(view);
        if (index < 0) return;

        // 접힌 패를 누르면 펼친다 (내 턴이 아니어도 손패는 볼 수 있다)
        if (!expanded)
        {
            expanded = true;
            return;
        }

        // 이동 단계, 상대 턴, 낼 수 없는 카드는 클릭해도 아무 일도 없다
        if (!Ctl.CanPlayHandCard(index)) return;

        if (selected == index)
        {
            selected = -1;
            Ctl.RequestPlay(index);   // 2번째 클릭: 카드를 낸다
        }
        else
        {
            selected = index;         // 1번째 클릭: 테두리 하이라이트
        }
    }
    #endregion

    #region 표시 갱신
    // 낼 수 있는 카드는 밝게, 낼 수 없는 카드는 어둡게 (내가 카드를 고르는 단계에서만)
    private void RefreshDim()
    {
        bool judge = Ctl.CanAct;
        for (int i = 0; i < views.Count; i++)
        {
            bool playable = !judge || Ctl.Match.CanPlay(Ctl.ViewTeam, i);
            views[i].Image.color = playable ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
        }
    }

    // 뽑기 버튼, 중첩 숫자, 덱 장수, 이동 점, 상대 카드 장수
    private void RefreshStatic()
    {
        var c = Ctl;
        var m = c.Match;

        drawButton.interactable = c.CanDraw;
        pendingText.text = m.PendingDraw > 0 ? "+" + m.PendingDraw : "";
        deckCountText.text = m.Deck.DrawPileCount.ToString();

        bool showDots = c.Phase == UnoPhase.ChessMove && c.MovesRequired > 0;
        for (int i = 0; i < dots.Count; i++)
        {
            bool visible = showDots && i < Mathf.Min(4, c.MovesRequired);
            dots[i].gameObject.SetActive(visible);
            if (visible) dots[i].color = i < c.MovesLeft ? Gold : new Color(0.2f, 0.2f, 0.2f, 0.85f); // 이동할 때마다 점이 어두워진다
        }

        if (opponentCountText != null)
            opponentCountText.text = "× " + m.GetHandCount(1 - c.ViewTeam);
    }

    private void SetOpponentVisible(bool on)
    {
        var t = opponentPlate.Find("Uno Opponent Cards");
        if (t != null) t.gameObject.SetActive(on);
    }
    #endregion

    #region 버림 더미 / 카드 이동 연출
    private Sprite FaceSprite(UnoCard card, UnoColor color)
    {
        var data = Ctl.Database.Get(card);
        return card.IsWild ? data.GetSprite(color) : data.sprite;
    }

    private void AddPileCard(UnoCard card, UnoColor color, bool instant)
    {
        var go = new GameObject("Pile Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(pileRoot, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = PileCardSize;
        rt.localRotation = Quaternion.Euler(0, 0, Random.Range(-20f, 20f)); // 쌓인 느낌을 위한 랜덤 기울기
        rt.anchoredPosition = new Vector2(Random.Range(-6f, 6f), Random.Range(-6f, 6f));
        var img = go.GetComponent<Image>();
        img.sprite = FaceSprite(card, color);
        img.raycastTarget = false;
        pileViews.Add(img);

        // 화면에는 최근 10장만 둔다 (가장 오래된 오브젝트부터 제거)
        while (pileViews.Count > MaxPileObjects)
        {
            Destroy(pileViews[0].gameObject);
            pileViews.RemoveAt(0);
        }
    }

    // 낸 카드가 출발 위치에서 버림 더미 위로 날아간다. 도착하면 더미에 실제 카드로 놓는다.
    private IEnumerator FlyToPile(Sprite sprite, Vector3 fromWorld, Quaternion fromRot, Vector2 fromSize, UnoCard card, UnoColor color)
    {
        var go = new GameObject("Flying Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(root, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = fromSize;
        rt.position = fromWorld;
        rt.rotation = fromRot;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        rt.SetAsLastSibling();

        Vector3 toWorld = pileRoot.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / FlyDuration;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f); // ease-out
            rt.position = Vector3.Lerp(fromWorld, toWorld, e);
            rt.rotation = Quaternion.Slerp(fromRot, Quaternion.identity, e);
            rt.sizeDelta = Vector2.Lerp(fromSize, PileCardSize, e);
            yield return null;
        }

        Destroy(go);
        if (!UnoTurnController.Active) yield break;

        // 재섞기로 더미가 줄었다면 낸 카드 1장만 남기고 정리한다
        if (Ctl.Match.Deck.DiscardCount < pileTotal)
        {
            for (int i = pileViews.Count - 1; i >= 0; i--) Destroy(pileViews[i].gameObject);
            pileViews.Clear();
        }
        pileTotal = Ctl.Match.Deck.DiscardCount;
        AddPileCard(card, card.IsWild ? Ctl.Match.CurrentColor : color, instant: false);
        RefreshStatic();
    }
    #endregion
}
