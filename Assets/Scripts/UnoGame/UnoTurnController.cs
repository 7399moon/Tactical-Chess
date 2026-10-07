using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 우노 한 턴 안에서의 진행 단계. "지금 턴을 진행 중인 팀" 기준이며 모든 클라이언트에서 같은 값을 가진다.
public enum UnoPhase
{
    Inactive,     // 우노 모드가 아님 (또는 게임 시작 전)
    SelectCard,   // 카드를 내거나 뽑아야 하는 단계
    ChooseColor,  // 와일드를 냈고 색상을 고르는 단계 (색이 정해질 때까지 카드는 검은색)
    UnoRace,      // 손패가 1장이 되어 UNO 경쟁 중 (카드 효과 처리 전)
    ReviveSelectPiece, // 부활 카드: 부활시킬 기물을 고르는 단계
    ReviveSelectTile,  // 부활 카드: 노란 타일 중 부활 위치를 고르는 단계
    ChessMove     // 카드를 냈고, 요구된 횟수만큼 체스 이동을 하는 단계
}

// 우노 모드의 "턴 흐름"을 담당하는 컨트롤러: 카드 사용/뽑기 -> 체스 이동 횟수 -> 턴 종료.
//
// 네트워크 구조 (7단계: 호스트 권한 + 비공개 정보):
//  - 진짜 UnoMatch(덱, 양쪽 손패)는 호스트만 가진다. 덱은 호스트만 아는 난수로 섞는다.
//  - 게스트는 명령(카드 내기/뽑기/색 선택/UNO 클릭/부활 선택)만 호스트에게 보내고, 호스트가 검증·적용한 결과를
//    이벤트로 돌려받아 같은 코드로 재생한다. 게스트의 UnoMatch는 상대 손패와 뽑을 카드 더미가 "비공개 카드"이고,
//    자기 손패에 들어온 카드의 정체만 이벤트(비밀 목록)로 받는다. 상대가 낸 카드는 낼 때 공개된다.
//  - 체스 이동/기물 클릭은 기존 릴레이(양쪽 동일 실행) 그대로다.
//
// 3단계 범위: 카드 사용 -> 이동 횟수(1~4 = 숫자, 그 외 1회, 5~8 부활 = 0회) -> 이동 점 -> 턴 종료 버튼 조건 ->
// 뽑기 후 턴 종료 -> 15장 패배 / 손패 비움 승리. 0/스킵/리버스 등 카드 "효과"와 색상 선택 UI는 4단계에서 붙이며,
// 그동안 와일드 색상은 손패에서 가장 많은 색으로 자동 선택한다.
public class UnoTurnController : MonoBehaviour
{
    public static UnoTurnController Instance { get; private set; }
    public static bool Active => Instance != null && Instance.active;

    [SerializeField] private UnoCardDatabase database;

    #region 상태
    private bool active;
    private bool cardActionDone;                       // 이번 턴에 카드를 냈거나 뽑았는가
    private bool skipOpponent;                         // 스킵 카드: 이동을 마치면 상대 턴을 건너뛴다
    private int lastActingTeam;                        // 직전 턴을 진행한 팀 (스킵이면 현재 팀과 같을 수 있다)
    private UnoCard pendingWildCard;                    // 색 선택을 기다리는 와일드 카드
    public event Action<UnoColor> OnColorChosen;
    private bool colorChoiceOpen;                      // 와일드 색상 선택 창이 열려 있는가
    private readonly HashSet<int> movedTiles = new HashSet<int>(); // 이번 턴에 이동한 기물이 서 있는 칸(x * 8 + y)
    private readonly List<UnoCard> scratch = new List<UnoCard>();

    #region 네트워크 역할 (7단계)
    public enum NetRole { Local, Host, Guest }
    public const int K_Play = 0, K_Color = 1, K_Draw = 2, K_Race = 3, K_Revive = 4;

    // 테스트용: 한 프로세스에서 호스트/게스트 경로를 직접 돌려 볼 때 지정한다 (null이면 실제 접속 상태로 판단)
    public static NetRole? SimRole;
    public static Action<int, int, int, int> SimCommandSink;                   // 게스트 -> 호스트 명령이 가는 곳 (테스트)
    public static Action<int, int, int, int, int, string> SimEventSink;        // 호스트 -> 게스트 이벤트가 가는 곳 (테스트)
    public static Action<int, string, int> SimDealSink;                        // 호스트 -> 게스트 첫 배분 (테스트)

    private NetRole role = NetRole.Local;
    private bool awaitingDeal;                                // 게스트: 호스트의 첫 배분을 기다리는 중
    private readonly List<UnoCard> secretOut = new List<UnoCard>();   // 호스트: 이번 명령 처리 중 게스트에게 알려 줄 카드들
    private Queue<UnoCard> secretIn = new Queue<UnoCard>();          // 게스트: 이번 이벤트에 실려 온 카드들
    private readonly List<(int kind, int team, int a, int b, int code, string secrets)> earlyEvents
        = new List<(int, int, int, int, int, string)>();            // 게스트: 배분 전에 도착한 이벤트
    private static int pendingDealMatch = int.MinValue;              // 배분이 컨트롤러보다 먼저 도착했을 때 보관
    private static string pendingDealHand;
    private static int pendingDealStart;

    public NetRole Role => role;
    public bool AwaitingDeal => awaitingDeal;
    private int OwnTeam => GameStartController.LocalTeam;
    private int GuestTeam => role == NetRole.Host ? 1 - GameStartController.LocalTeam : GameStartController.LocalTeam;

    private NetRole DetectRole()
    {
        if (SimRole.HasValue) return SimRole.Value;
        if (GameStartController.LocalTeam < 0 || ChessNetworkSync.Instance == null) return NetRole.Local;
        var runner = ChessNetworkSync.Instance.Runner;
        return runner != null && runner.IsServer ? NetRole.Host : NetRole.Guest;
    }
    #endregion

    public UnoMatch Match { get; private set; }
    public UnoCardDatabase Database => database;
    public UnoPhase Phase { get; private set; } = UnoPhase.Inactive;
    public int MovesRequired { get; private set; }
    public int MovesDone { get; private set; }
    public bool MinMoveRule { get; private set; }      // 1~4 카드: 최소 1회 이동해야 하고, 그 뒤에는 턴 종료 버튼으로 넘길 수 있다
    #endregion

    #region 이벤트 (UI가 구독)
    public event Action OnMatchBegun;                               // 새 판 시작 (손패/더미 전체 다시 그리기)
    public event Action OnStateChanged;                             // 단계/이동 횟수/중첩 등 표시 값이 바뀜
    public event Action<int, UnoCard, int, UnoColor> OnCardPlayed;  // (낸 팀, 카드, 손패에서의 위치, 현재 색)
    public event Action<int, int> OnCardsDrawn;                     // (뽑은 팀, 뽑은 장수)
    #endregion

    #region 조회
    // 이 화면의 주인 팀. 네트워크 대전이면 내 팀, 로컬 테스트(팀 미배정)에서는 지금 턴인 팀을 보여준다.
    public int ViewTeam => GameStartController.LocalTeam >= 0
        ? GameStartController.LocalTeam
        : (GameManager.Instance != null ? GameManager.Instance.CurrentTurn : 0);

    private int CurrentTeam => GameManager.Instance != null ? GameManager.Instance.CurrentTurn : 0;
    private bool GameOver => GameEndManager.Instance != null && GameEndManager.Instance.IsGameOver;

    public bool IsViewerTurn => active && CurrentTeam == ViewTeam;

    // 지금 내(화면 주인) 턴이고 카드를 내거나 뽑을 수 있는 단계인가
    public bool CanAct => active && !GameOver && Phase == UnoPhase.SelectCard && IsViewerTurn;

    // 카드 뽑기 버튼 활성 조건: 낼 수 있는 카드가 없을 때 (중첩에 반격할 수 없는 경우 포함)
    public bool CanDraw => CanAct && !Match.HasPlayableCard(ViewTeam);

    public bool CanPlayHandCard(int handIndex) => CanAct && Match.CanPlay(ViewTeam, handIndex);

    // "턴 종료" 버튼: 1~4 카드로 최소 1회 이동했고 아직 횟수가 남았을 때만 눌러 넘길 수 있다
    // 또는 더 움직일 수 있는 기물이 하나도 없을 때 (남은 이동 횟수는 사라진다)
    public bool CanManualEndTurn => active && !GameOver && Phase == UnoPhase.ChessMove && IsViewerTurn
        && MovesDone < MovesRequired
        && ((MinMoveRule && MovesDone >= 1) || !HasMovablePieceLeft());

    // 이번 턴에 아직 움직이지 않았고 합법 수가 있는 기물이 남아 있는가
    private bool HasMovablePieceLeft()
    {
        var board = ChessBoard.Instance;
        if (board == null || board.Pieces == null) return true; // 판단할 수 없으면 막지 않는다
        Vector2Int? ep = GameManager.Instance != null ? GameManager.Instance.EnPassantTarget : null;
        int team = CurrentTeam;
        for (int x = 0; x < ChessBoard.TileCountX; x++)
        {
            for (int y = 0; y < ChessBoard.TileCountY; y++)
            {
                var piece = board.Pieces[x, y];
                if (piece == null || piece.team != team) continue;
                if (movedTiles.Contains(x * 8 + y)) continue;
                if (ChessRules.GetLegalMoves(board.Pieces, piece, ep).Count > 0) return true;
            }
        }
        return false;
    }

    // GameManager.EndTurn이 호출: 스킵 카드의 "상대 턴 건너뛰기"를 수동 턴 종료에도 적용하고 소모한다
    public bool ConsumeSkip()
    {
        bool skip = skipOpponent;
        skipOpponent = false;
        return skip;
    }

    public int MovesLeft => Mathf.Max(0, MovesRequired - MovesDone);
    #endregion

    #region 생명주기
    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnTurnStarted += HandleTurnStarted;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnTurnStarted -= HandleTurnStarted;
    }

    // GameStartController.StartMatch가 GameManager.ResetForNewMatch보다 "먼저" 호출한다.
    // (그래야 첫 턴 시작 이벤트를 받을 때 이미 덱과 손패가 준비되어 있다)
    public void BeginMatch()
    {
        // GameManager가 이 컨트롤러보다 늦게 만들어졌을 수 있어 구독을 한 번 더 보장한다 (중복 구독 방지 후 등록)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTurnStarted -= HandleTurnStarted;
            GameManager.Instance.OnTurnStarted += HandleTurnStarted;
        }

        active = MatchSettings.IsUno && database != null;
        cardActionDone = false;
        skipOpponent = false;
        StopAllCoroutines();
        raceCounter = 0;
        raceNext = null;
        ClearRaceGlow();
        capturedPieces[0].Clear();
        capturedPieces[1].Clear();
        reviveTiles.Clear();
        colorChoiceOpen = false;
        lastActingTeam = 0;
        movedTiles.Clear();
        MovesRequired = MovesDone = 0;
        MinMoveRule = false;

        if (!active)
        {
            Match = null;
            Phase = UnoPhase.Inactive;
            OnMatchBegun?.Invoke();
            return;
        }

        role = DetectRole();
        lastReshuffleCount = 0;
        secretOut.Clear();
        secretIn.Clear();
        earlyEvents.Clear();
        awaitingDeal = false;

        if (role == NetRole.Guest)
        {
            // 게스트: 카드 정체를 모르는 빈 뷰로 시작하고, 호스트의 첫 배분이 오면 채운다
            int total = database.BuildDeck().Count;
            Match = UnoMatch.CreateHiddenView(Mathf.Max(0, total - 2 * MatchSettings.UnoStartHand - 1),
                                              MatchSettings.UnoStartHand, MatchSettings.UnoMaxHand);
            awaitingDeal = true;
            Phase = UnoPhase.Inactive;
            if (pendingDealMatch == MatchSession.AugmentSeed) ReceiveDeal(pendingDealMatch, pendingDealHand, pendingDealStart);
            return;
        }

        // 호스트: 게스트가 공개된 시드로 덱을 계산할 수 없게 호스트만 아는 난수를 쓴다. 로컬(한 화면 테스트)은 시드 고정.
        var rng = role == NetRole.Host
            ? new System.Random(Guid.NewGuid().GetHashCode())
            : new System.Random(MatchSession.AugmentSeed ^ 0x1F2E3D4C);
        Match = new UnoMatch(database.BuildDeck(), rng, MatchSettings.UnoStartHand, MatchSettings.UnoMaxHand);
        Phase = UnoPhase.SelectCard;
        OnMatchBegun?.Invoke();

        if (role == NetRole.Host)
        {
            string hand = UnoCardCodec.Pack(Match.GetHand(GuestTeam));
            int start = Match.TopCard.Encode();
            if (SimDealSink != null) SimDealSink(MatchSession.AugmentSeed, hand, start);
            else ChessNetworkSync.Instance?.RPC_UnoDeal(MatchSession.AugmentSeed, hand, start);
        }
    }

    // 호스트의 첫 배분 수신 (RPC가 컨트롤러보다 먼저 도착할 수 있어 정적 진입점을 둔다)
    public static void ReceiveDeal(int matchId, string guestHand, int startCard)
    {
        pendingDealMatch = matchId;
        pendingDealHand = guestHand;
        pendingDealStart = startCard;
        var c = Instance;
        if (c == null || !c.awaitingDeal || matchId != MatchSession.AugmentSeed) return;

        c.awaitingDeal = false;
        pendingDealMatch = int.MinValue;
        var hand = UnoCardCodec.Unpack(guestHand);
        c.Match.ApplyDeal(c.OwnTeam, hand, UnoCard.Decode((ushort)startCard));
        c.Phase = UnoPhase.SelectCard;
        c.OnMatchBegun?.Invoke();
        c.HandleTurnStarted(c.CurrentTeam);

        // 배분 전에 먼저 도착한 이벤트가 있으면 순서대로 재생한다
        var queued = new List<(int kind, int team, int a, int b, int code, string secrets)>(c.earlyEvents);
        c.earlyEvents.Clear();
        foreach (var e in queued) c.ReceiveEvent(e.kind, e.team, e.a, e.b, e.code, e.secrets);
    }

    private void HandleTurnStarted(int team)
    {
        if (!active || awaitingDeal) return;

        // 부활 선택 도중 턴이 넘어간 경우(시간 초과 등) 선택 UI와 하이라이트를 정리한다
        reviveTiles.Clear();
        OnReviveSelectEnded?.Invoke();

        // 직전 턴의 팀이 손패를 모두 비웠으면 승리 (마지막 카드의 이동까지 마친 뒤 턴이 넘어온 것)
        int previous = lastActingTeam;
        if (cardActionDone && Match.GetHandCount(previous) == 0 && !GameOver)
        {
            GameEndManager.Instance?.NotifyUnoWin(previous, GameEndManager.GameEndReason.HandEmpty);
            return;
        }

        cardActionDone = false;
        skipOpponent = false;
        lastActingTeam = team;
        movedTiles.Clear();
        MovesRequired = MovesDone = 0;
        MinMoveRule = false;
        Phase = UnoPhase.SelectCard;
        OnStateChanged?.Invoke();
    }
    #endregion

    #region 카드 내기 / 뽑기 (요청 -> RPC -> 모든 클라이언트에 적용)
    // UI가 호출: 내 손패의 handIndex번 카드를 낸다 (와일드 색상은 지금은 자동 선택)
    public void RequestPlay(int handIndex)
    {
        if (colorChoiceOpen || !CanPlayHandCard(handIndex)) return;
        if (CardSelectionManager.Instance != null && CardSelectionManager.Instance.IsSelecting) return;

        int team = ViewTeam;
        UnoCard card = Match.GetHand(team)[handIndex];

        SendPlay(team, handIndex, -1);
    }

    private void SendColor(int team, int color) => Send(K_Color, team, color, 0);
    private void SendPlay(int team, int handIndex, int color) => Send(K_Play, team, handIndex, color);

    // 내 행동을 실행한다: 로컬/호스트는 바로 적용(호스트는 결과를 게스트에게 알림), 게스트는 호스트에게 명령만 보낸다
    private void Send(int kind, int team, int a, int b)
    {
        if (role != NetRole.Guest) { Execute(kind, team, a, b); return; }

        if (SimCommandSink != null) SimCommandSink(kind, team, a, b);
        else ChessNetworkSync.Instance?.RPC_UnoCmd(kind, team, a, b);
    }

    private void Execute(int kind, int team, int a, int b)
    {
        switch (kind)
        {
            case K_Play: ApplyPlay(team, a, b); break;
            case K_Color: ApplyColor(team, a); break;
            case K_Draw: ApplyDraw(team); break;
            case K_Race: ApplyRaceClick(team); break;
            case K_Revive: ApplyRevivePiece(team, a); break;
        }
    }

    // 호스트: 게스트의 명령 수신. 보낸 쪽이 자기 팀 행동만 요청하도록 검증하고, 나머지 규칙 검증은 Apply*가 한다.
    public void OnCommandFromGuest(int kind, int team, int a, int b)
    {
        if (role != NetRole.Host || !active || awaitingDeal) return;
        if (team != GuestTeam) return;
        Execute(kind, team, a, b);
    }

    // 호스트: Apply를 실행하고, 성공했다면 결과를 게스트에게 알린다 (게스트 손패에 들어간 카드는 secrets로)
    private void HostRun(int kind, int team, int a, int b, Func<bool> core)
    {
        if (role != NetRole.Host) { core(); return; }

        int code = 0;
        if (kind == K_Play && team >= 0 && team < 2 && a >= 0 && a < Match.GetHandCount(team))
            code = Match.GetHand(team)[a].Encode(); // 낸 카드는 공개되므로 코드로 알려 준다

        secretOut.Clear();
        bool ok = core();
        string secrets = UnoCardCodec.Pack(secretOut);
        secretOut.Clear();
        if (!ok) return;

        if (SimEventSink != null) SimEventSink(kind, team, a, b, code, secrets);
        else ChessNetworkSync.Instance?.RPC_UnoEvt(kind, team, a, b, code, secrets);
    }

    // 게스트: 호스트의 결과 이벤트 재생
    public void ReceiveEvent(int kind, int team, int a, int b, int code, string secrets)
    {
        if (role != NetRole.Guest || !active) return;
        if (awaitingDeal) { earlyEvents.Add((kind, team, a, b, code, secrets)); return; }

        secretIn = new Queue<UnoCard>(UnoCardCodec.Unpack(secrets));
        if (kind == K_Play && team != OwnTeam)
            Match.ReplaceCard(team, a, UnoCard.Decode((ushort)code)); // 상대가 낸 카드의 정체를 이제 안다

        switch (kind)
        {
            case K_Play: ApplyPlayCore(team, a, b); break;
            case K_Color: ApplyColorCore(team, a); break;
            case K_Draw: ApplyDrawCore(team); break;
            case K_Race: ApplyRaceClickCore(team); break;
            case K_Revive: ApplyRevivePieceCore(team, a); break;
        }

        if (secretIn.Count > 0) Debug.LogWarning($"[Uno] 처리하지 않은 비밀 카드 {secretIn.Count}장 (kind={kind})");
        secretIn.Clear();
    }

    #region 뽑은/바뀐 카드의 정체 처리 (호스트: 게스트 몫을 secrets에 담기, 게스트: 채워 넣기)
    private UnoCard TakeSecret()
    {
        if (secretIn.Count > 0) return secretIn.Dequeue();
        Debug.LogWarning("[Uno] 비밀 카드가 모자랍니다");
        return UnoCard.Hidden;
    }

    // team이 방금 count장을 뽑았다
    private int lastReshuffleCount;

    private void AfterDraw(int team, int count)
    {
        if (Match.Deck.ReshuffleCount != lastReshuffleCount)
        {
            lastReshuffleCount = Match.Deck.ReshuffleCount;
            CenterAnnouncer.Show("덱이 부족해 버림 더미를 섞어 덱에 추가했습니다");
        }
        if (count <= 0) return;
        var hand = Match.GetHand(team);
        if (role == NetRole.Host)
        {
            if (team == GuestTeam)
                for (int i = hand.Count - count; i < hand.Count; i++) secretOut.Add(hand[i]);
        }
        else if (role == NetRole.Guest)
        {
            var cards = new List<UnoCard>(count);
            for (int i = 0; i < count; i++) cards.Add(team == OwnTeam ? TakeSecret() : UnoCard.Hidden);
            Match.ReplaceTail(team, cards);
        }
    }

    // team의 손패가 통째로 새로 구성됐다 (0 카드)
    private void AfterHandRebuilt(int team)
    {
        var hand = Match.GetHand(team);
        if (role == NetRole.Host)
        {
            if (team == GuestTeam) for (int i = 0; i < hand.Count; i++) secretOut.Add(hand[i]);
        }
        else if (role == NetRole.Guest)
        {
            var cards = new List<UnoCard>(hand.Count);
            for (int i = 0; i < hand.Count; i++) cards.Add(team == OwnTeam ? TakeSecret() : UnoCard.Hidden);
            Match.ReplaceHand(team, cards);
        }
    }

    // 두 손패를 맞바꿨다 (리버스)
    private void AfterSwap()
    {
        if (role == NetRole.Host)
        {
            var hand = Match.GetHand(GuestTeam);
            for (int i = 0; i < hand.Count; i++) secretOut.Add(hand[i]);
        }
        else if (role == NetRole.Guest)
        {
            int own = OwnTeam, other = 1 - OwnTeam;
            var mine = new List<UnoCard>();
            for (int i = 0; i < Match.GetHandCount(own); i++) mine.Add(TakeSecret());
            var theirs = new List<UnoCard>();
            for (int i = 0; i < Match.GetHandCount(other); i++) theirs.Add(UnoCard.Hidden);
            Match.ReplaceHand(own, mine);
            Match.ReplaceHand(other, theirs);
        }
    }
    #endregion

    // UI가 호출: 카드를 한 장 뽑는다 (중첩이 쌓여 있으면 쌓인 장수를 모두 뽑는다)
    public void RequestDraw()
    {
        if (!CanDraw) return;

        Send(K_Draw, ViewTeam, 0, 0);
    }

    // 모든 클라이언트에서 같은 순서로 실행되는 "카드 내기" 적용. 잘못된 요청은 조용히 무시한다.
    public void ApplyPlay(int team, int handIndex, int colorValue)
        => HostRun(K_Play, team, handIndex, colorValue, () => ApplyPlayCore(team, handIndex, colorValue));

    private bool ApplyPlayCore(int team, int handIndex, int colorValue)
    {
        if (!active || GameOver) return false;
        if (team != CurrentTeam || Phase != UnoPhase.SelectCard) return false;

        bool colorGiven = colorValue >= (int)UnoColor.Red && colorValue <= (int)UnoColor.Blue;
        UnoColor color = colorGiven ? (UnoColor)colorValue : UnoColor.Red; // 색 미정이면 임시 값으로 낸 뒤 Wild(검정)로 되돌린다

        UnoPlayResult result = Match.PlayCard(team, handIndex, color);
        if (!result.Success) return false;

        cardActionDone = true;
        UnoCard played = result.Card;
        bool wildNeedsColor = played.IsWild && !colorGiven;

        // 카드를 내서 손패가 0장이 됐다: 효과/이동/색 선택/UNO 경쟁 없이 그 자리에서 승리
        if (result.EmptiedHand)
        {
            if (wildNeedsColor) Match.SetCurrentColor(UnoColor.Wild);
            OnCardPlayed?.Invoke(team, played, handIndex, played.IsWild && wildNeedsColor ? UnoColor.Wild : Match.CurrentColor);
            GameEndManager.Instance?.NotifyUnoWin(team, GameEndManager.GameEndReason.HandEmpty);
            return true;
        }

        if (wildNeedsColor)
        {
            // 카드를 낸 뒤 색을 고른다: 고르기 전까지는 검은색 와일드 카드로 보인다
            Match.SetCurrentColor(UnoColor.Wild);
            pendingWildCard = played;
        }

        OnCardPlayed?.Invoke(team, played, handIndex, wildNeedsColor ? UnoColor.Wild : Match.CurrentColor);

        Action next = wildNeedsColor ? (Action)(() => EnterChooseColor(team, played)) : () => ContinueAfterPlay(team, played);

        // 카드를 내서 손패가 1장이 되면 효과 처리 전에 UNO 경쟁을 먼저 한다
        if (result.TriggersUnoRace) BeginRace(team, next);
        else next();
        return true;
    }

    private void EnterChooseColor(int team, UnoCard card)
    {
        Phase = UnoPhase.ChooseColor;
        OnStateChanged?.Invoke();
        SkillUIManager.Instance?.RefreshUIState();
        if (team == ViewTeam) StartCoroutine(OpenColorPicker(team, card));
    }

    #region UNO 경쟁
    private int raceOwner;
    private Vector2Int raceTile;
    private int raceCounter;
    private Action raceNext;
    private RaceTileGlow raceGlow;

    private void ClearRaceGlow()
    {
        if (raceGlow != null) Destroy(raceGlow.gameObject);
        raceGlow = null;
    }

    public static bool RaceActive => Active && Instance.Phase == UnoPhase.UnoRace;
    public static Vector2Int RaceTile => Instance != null ? Instance.raceTile : -Vector2Int.one;

    // owner의 손패가 1장이 되었다: 보드의 한 칸이 빛나고, 먼저 누르는 쪽이 이긴다 (시드로 정해 양쪽 화면이 같은 칸을 본다)
    private void BeginRace(int owner, Action next)
    {
        raceOwner = owner;
        raceNext = next;
        var rng = new System.Random(MatchSession.AugmentSeed ^ 0x2468ACE ^ (++raceCounter * 7919));
        raceTile = new Vector2Int(rng.Next(ChessBoard.TileCountX), rng.Next(ChessBoard.TileCountY));

        Phase = UnoPhase.UnoRace;
        GameManager.Instance?.PauseTimer();
        ClearRaceGlow();
        var tileObj = ChessBoard.Instance != null ? ChessBoard.Instance.GetTileObject(raceTile.x, raceTile.y) : null;
        if (tileObj != null) raceGlow = RaceTileGlow.Create(tileObj);
        SoundManager.Instance?.PlayWarp();
        CenterAnnouncer.Show("UNO! 빛나는 칸을 먼저 누르세요");
        OnStateChanged?.Invoke();
        SkillUIManager.Instance?.RefreshUIState();
    }

    // 로컬에서 빛나는 칸을 눌렀을 때 ChessInteractionManager가 호출 (네트워크 중에는 RPC로 순서가 정해진다)
    public void RequestRaceClick()
    {
        if (Phase != UnoPhase.UnoRace) return;

        // 호스트는 게스트 클릭이 호스트에 도착하는 데 걸리는 시간(왕복 지연의 절반)만큼 늦춰서 적용해 양쪽의 차이를 줄인다
        float delay = role == NetRole.Host ? HostRaceDelay() : 0f;
        if (delay > 0.005f) StartCoroutine(DelayedRaceClick(ViewTeam, delay));
        else Send(K_Race, ViewTeam, 0, 0);
    }

    private IEnumerator DelayedRaceClick(int team, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (Phase == UnoPhase.UnoRace) Send(K_Race, team, 0, 0);
    }

    // 게스트 -> 호스트 단방향 지연 추정치 (초). 시뮬레이션/로컬에서는 0
    private float HostRaceDelay()
    {
        if (SimRole.HasValue || ChessNetworkSync.Instance == null) return 0f;
        var runner = ChessNetworkSync.Instance.Runner;
        if (runner == null) return 0f;
        foreach (var p in runner.ActivePlayers)
        {
            if (p == runner.LocalPlayer) continue;
            return Mathf.Clamp((float)runner.GetPlayerRtt(p) * 0.5f, 0f, 0.25f);
        }
        return 0f;
    }

    // 모든 클라이언트에서 같은 순서로 실행: 가장 먼저 도착한 클릭이 승자
    public void ApplyRaceClick(int team) => HostRun(K_Race, team, 0, 0, () => ApplyRaceClickCore(team));

    private bool ApplyRaceClickCore(int team)
    {
        if (!active || GameOver || Phase != UnoPhase.UnoRace) return false;

        ClearRaceGlow();
        GameManager.Instance?.ResumeTimer();
        Action next = raceNext;
        raceNext = null;

        if (team == raceOwner)
        {
            CenterAnnouncer.Show("UNO 선언 성공!");
        }
        else
        {
            CenterAnnouncer.Show($"UNO 실패! {(raceOwner == 0 ? "백팀" : "흑팀")}이 {UnoRules.UnoRacePenalty}장을 뽑습니다");
            UnoDrawResult draw = Match.Draw(raceOwner, UnoRules.UnoRacePenalty);
            AfterDraw(raceOwner, draw.Drawn);
            OnCardsDrawn?.Invoke(raceOwner, draw.Drawn);
            if (draw.Lost)
            {
                GameEndManager.Instance?.NotifyUnoWin(1 - raceOwner, GameEndManager.GameEndReason.HandOverflow);
                return true;
            }
        }

        OnStateChanged?.Invoke();
        next?.Invoke();
        return true;
    }
    #endregion

    // 카드를 낸 팀이 색을 고르는 창을 연다 (카드가 더미에 날아가 앉을 시간을 잠깐 준다)
    private IEnumerator OpenColorPicker(int team, UnoCard card)
    {
        yield return new WaitForSeconds(0.6f);
        if (!active || Phase != UnoPhase.ChooseColor || CardSelectionManager.Instance == null) yield break;

        colorChoiceOpen = true;
        Sprite[] sprites = database.GetById("Wild").chosenColorSprites;
        CardSelectionManager.Instance.ShowColorChoice(sprites, chosen =>
        {
            colorChoiceOpen = false;
            SendColor(team, (int)chosen);
        });
    }

    // 모든 클라이언트에서 같은 순서로 실행되는 "와일드 색상 확정"
    public void ApplyColor(int team, int colorValue) => HostRun(K_Color, team, colorValue, 0, () => ApplyColorCore(team, colorValue));

    private bool ApplyColorCore(int team, int colorValue)
    {
        if (!active || GameOver || Phase != UnoPhase.ChooseColor || team != CurrentTeam) return false;
        if (colorValue < (int)UnoColor.Red || colorValue > (int)UnoColor.Blue) return false;

        colorChoiceOpen = false;
        Match.SetCurrentColor((UnoColor)colorValue);
        OnColorChosen?.Invoke(Match.CurrentColor);
        ContinueAfterPlay(team, pendingWildCard);
        return true;
    }

    // 카드(와일드는 색까지)가 정해진 뒤의 공통 처리: 효과 -> (리버스로 손패가 1장이 되면 UNO 경쟁) -> 이동 횟수 설정
    private void ContinueAfterPlay(int team, UnoCard card)
    {
        ApplyCardEffect(team, card);

        if (card.Kind == UnoKind.Reverse)
        {
            // 교환 직후 손패가 1장이 된 쪽이 있으면 그 자리에서 UNO 경쟁 (둘 다 1장이면 카드를 낸 팀 쪽부터)
            int owner = Match.GetHandCount(team) == 1 ? team : Match.GetHandCount(1 - team) == 1 ? 1 - team : -1;
            if (owner >= 0)
            {
                BeginRace(owner, () => SetupMoves(team, card));
                return;
            }
        }

        SetupMoves(team, card);
    }

    private void SetupMoves(int team, UnoCard card)
    {
        MovesRequired = UnoRules.MovesFor(card);
        MovesDone = 0;
        MinMoveRule = UnoRules.HasMinMoveRule(card);
        movedTiles.Clear();

        if (MovesRequired == 0)
        {
            // 이동이 없는 카드(부활 5~8): 기물과 위치를 고르고 부활시킨 뒤 바로 턴 종료
            StartRevive(team, card.Number);
            return;
        }

        Phase = UnoPhase.ChessMove;
        OnStateChanged?.Invoke();
        SkillUIManager.Instance?.RefreshUIState();
    }

    // 낸 카드의 즉시 효과: 0 = 손패 리셋, 리버스 = 손패 교환, 스킵 = 이동 후 상대 턴 건너뜀.
    // (+2/+4 중첩은 UnoMatch.PlayCard가 이미 처리했고, 와일드 색은 고른 색으로 CurrentColor가 정해졌다. 부활은 5단계)
    private void ApplyCardEffect(int team, UnoCard card)
    {
        if (card.Kind == UnoKind.Number && card.Number == 0)
        {
            Match.ResetHand(team);
            AfterHandRebuilt(team);
            CenterAnnouncer.Show("0 카드: 손패를 모두 섞어 다시 뽑았습니다");
            OnStateChanged?.Invoke();
        }
        else if (card.Kind == UnoKind.Reverse)
        {
            Match.SwapHands(); // 15장이 될 수 없으므로 상한 검사는 건너뛴다
            AfterSwap();
            CenterAnnouncer.Show("리버스: 서로의 손패를 교환했습니다");
            OnStateChanged?.Invoke();
        }
        else if (card.Kind == UnoKind.Skip)
        {
            skipOpponent = true;
            CenterAnnouncer.Show("스킵: 이동 후 상대 턴을 건너뜁니다");
        }
    }

    // 모든 클라이언트에서 같은 순서로 실행되는 "카드 뽑기" 적용. 뽑은 뒤에는 항상 상대 턴으로 넘어간다.
    public void ApplyDraw(int team) => HostRun(K_Draw, team, 0, 0, () => ApplyDrawCore(team));

    private bool ApplyDrawCore(int team)
    {
        if (!active || GameOver) return false;
        if (team != CurrentTeam || Phase != UnoPhase.SelectCard) return false;
        // 낼 카드가 있으면 뽑을 수 없다 (게스트는 상대 손패를 모르므로 호스트가 이미 검증한 이벤트를 믿는다)
        if (role != NetRole.Guest && Match.HasPlayableCard(team)) return false;

        UnoDrawResult result = Match.PendingDraw > 0 ? Match.ResolvePendingDraw(team) : Match.Draw(team, 1);
        cardActionDone = true;
        AfterDraw(team, result.Drawn);
        OnCardsDrawn?.Invoke(team, result.Drawn);

        if (result.Lost)
        {
            GameEndManager.Instance?.NotifyUnoWin(1 - team, GameEndManager.GameEndReason.HandOverflow);
            return true;
        }

        FinishTurn();
        return true;
    }

    // 임시 색상 선택: 남은 손패에서 가장 많은 색 (동률이면 Red -> Blue 순). 4단계에서 선택 UI로 대체된다.
    private UnoColor ChooseAutoColor(int team, int excludeIndex)
    {
        var hand = Match.GetHand(team);
        Span<int> counts = stackalloc int[4];
        for (int i = 0; i < hand.Count; i++)
        {
            if (i == excludeIndex) continue;
            UnoColor c = hand[i].Color;
            if (c >= UnoColor.Red && c <= UnoColor.Blue) counts[(int)c]++;
        }

        int best = 0;
        for (int c = 1; c < 4; c++)
            if (counts[c] > counts[best]) best = c;
        return (UnoColor)best;
    }

    #endregion

    #region 부활 (5~8)
    // 이번 판에 잡힌 팀별 기물 목록 (킹 제외, 승급으로 사라진 폰은 잡힌 것이 아니므로 들어오지 않는다)
    private readonly List<ChessPieceType>[] capturedPieces = { new List<ChessPieceType>(), new List<ChessPieceType>() };
    private int reviveNumber;
    private ChessPieceType reviveType;
    private readonly List<Vector2Int> reviveTiles = new List<Vector2Int>();

    public event Action<int, List<int>> OnReviveSelectStarted;  // (팀, 고를 수 있는 capturedPieces 인덱스들)
    public event Action OnReviveSelectEnded;

    public IReadOnlyList<ChessPieceType> GetCaptured(int team) => capturedPieces[team];

    // PieceCapture가 기물을 잡은 직후 호출한다
    public static void NoteCaptured(ChessPieces victim)
    {
        if (!Active || victim == null) return;
        if (victim.type == ChessPieceType.WhiteKing || victim.type == ChessPieceType.BlackKing) return;
        Instance.capturedPieces[victim.team].Add(victim.type);
    }

    private static bool IsPawn(ChessPieceType t) => t == ChessPieceType.WhitePawn || t == ChessPieceType.BlackPawn;

    // 5~8번 카드(number)로 type 기물을 부활시킬 수 있는 빈 칸들: 내 진영 1,2랭크의 세로 줄 number 또는 number-4번째 (폰은 2랭크만)
    private List<Vector2Int> FindReviveTiles(int team, ChessPieceType type, int number)
    {
        var tiles = new List<Vector2Int>();
        var board = ChessBoard.Instance;
        if (board == null) return tiles;

        int[] columns = { number - 1, number - 5 };
        int[] ranks = IsPawn(type) ? new[] { 2 } : new[] { 1, 2 };
        foreach (int col in columns)
        {
            if (col < 0 || col >= ChessBoard.TileCountX) continue;
            foreach (int rank in ranks)
            {
                int y = team == 0 ? rank - 1 : 8 - rank;
                if (board.GetPieceAt(col, y) == null) tiles.Add(new Vector2Int(col, y));
            }
        }
        return tiles;
    }

    private void StartRevive(int team, int number)
    {
        reviveNumber = number;
        var options = new List<int>();
        var seen = new HashSet<ChessPieceType>();
        for (int i = 0; i < capturedPieces[team].Count; i++)
        {
            ChessPieceType t = capturedPieces[team][i];
            if (!seen.Add(t)) continue; // 같은 종류는 한 번만 보여 준다
            if (FindReviveTiles(team, t, number).Count > 0) options.Add(i);
        }

        if (options.Count == 0)
        {
            // 잡힌 기물이 없거나 놓을 빈칸이 없다: 카드는 불발되고 턴이 넘어간다
            CenterAnnouncer.Show("부활 불발: 부활시킬 수 있는 기물이나 빈칸이 없습니다");
            FinishTurn();
            return;
        }

        Phase = UnoPhase.ReviveSelectPiece;
        OnStateChanged?.Invoke();
        SkillUIManager.Instance?.RefreshUIState();
        OnReviveSelectStarted?.Invoke(team, options);
    }

    // UI가 호출: 부활시킬 기물(capturedPieces 인덱스) 선택
    public void RequestRevivePiece(int capturedIndex)
    {
        if (!active || Phase != UnoPhase.ReviveSelectPiece || !IsViewerTurn) return;
        Send(K_Revive, ViewTeam, capturedIndex, 0);
    }

    public void ApplyRevivePiece(int team, int capturedIndex)
        => HostRun(K_Revive, team, capturedIndex, 0, () => ApplyRevivePieceCore(team, capturedIndex));

    private bool ApplyRevivePieceCore(int team, int capturedIndex)
    {
        if (!active || GameOver || Phase != UnoPhase.ReviveSelectPiece || team != CurrentTeam) return false;
        if (capturedIndex < 0 || capturedIndex >= capturedPieces[team].Count) return false;

        ChessPieceType type = capturedPieces[team][capturedIndex];
        var tiles = FindReviveTiles(team, type, reviveNumber);
        if (tiles.Count == 0) return false;

        reviveType = type;
        reviveTiles.Clear();
        reviveTiles.AddRange(tiles);
        Phase = UnoPhase.ReviveSelectTile;

        OnReviveSelectEnded?.Invoke();
        BoardTileHighlighter.Instance?.HighlightCustomTiles(reviveTiles); // 노란색 이동 하이라이트 재사용
        CenterAnnouncer.Show("부활시킬 칸을 선택하세요");
        OnStateChanged?.Invoke();
        return true;
    }

    // ChessInteractionManager.HandleClick이 호출: 부활 위치 선택 단계의 보드 클릭을 가로챈다 (처리했으면 true)
    public bool TryHandleReviveClick(int x, int y)
    {
        if (!active || Phase != UnoPhase.ReviveSelectTile) return false;
        if (!reviveTiles.Contains(new Vector2Int(x, y))) return true; // 노란 칸이 아니면 무시

        int team = CurrentTeam;
        BoardTileHighlighter.Instance?.ClearCustomHighlights();
        ChessBoard.Instance.PromotePieceAt(x, y, reviveType, team);
        capturedPieces[team].Remove(reviveType);
        reviveTiles.Clear();
        SoundManager.Instance?.PlayMove();
        FinishTurn();
        return true;
    }
    #endregion

    #region 체스 이동 연동
    // Piecemovement가 기물을 옮긴 직후 호출: 이번 턴에 이동한 기물이 선 칸을 기록한다 (프로모션으로 기물 객체가 바뀌어도 칸은 같다)
    public static void NoteMoved(ChessPieces piece)
    {
        if (!Active || piece == null) return;
        Instance.movedTiles.Add(piece.currentX * 8 + piece.currentY);
    }

    // ChessInteractionManager.CanSelect가 호출: 우노 모드에서 이 기물을 지금 고를 수 있는가
    public bool CanSelectPiece(ChessPieces piece)
    {
        if (!active || piece == null) return true;
        if (Phase != UnoPhase.ChessMove) return false;               // 카드를 내기 전에는 이동할 수 없다
        if (movedTiles.Contains(piece.currentX * 8 + piece.currentY)) return false; // 이번 턴에 이미 움직인 기물은 다시 못 움직인다
        return true;
    }

    // GameManager.PieceMoved가 호출: 이동 1회가 끝났다.
    public void OnMoveFinished()
    {
        if (!active || Phase != UnoPhase.ChessMove) return;

        MovesDone++;
        ChessInteractionManager.Instance?.DeselectPiece();

        if (MovesDone >= MovesRequired)
        {
            FinishTurn();
            return;
        }

        OnStateChanged?.Invoke();
        SkillUIManager.Instance?.RefreshUIState();
    }

    private void FinishTurn()
    {
        // 다음 턴 시작 이벤트(HandleTurnStarted)가 단계를 SelectCard로 되돌린다
        bool keepTurn = skipOpponent;
        skipOpponent = false;
        GameManager.Instance?.EndTurn(keepTurn);
    }
    #endregion
}
