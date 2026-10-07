using System;
using System.Collections.Generic;

#region 결과 타입
// 카드 내기 실패 사유
public enum UnoPlayFail
{
    None,
    InvalidPlayer,       // 플레이어 번호가 0/1이 아님
    InvalidIndex,        // 손패 인덱스가 범위를 벗어남
    NotPlayable,         // 지금 낼 수 없는 카드
    ColorRequired        // 와일드 계열인데 색상(Red~Blue)을 고르지 않았거나 잘못된 값
}

// 카드 내기 결과
public readonly struct UnoPlayResult
{
    public readonly bool Success;
    public readonly UnoPlayFail Fail;
    public readonly UnoCard Card;
    public readonly int HandCountAfter;

    public UnoPlayResult(bool success, UnoPlayFail fail, UnoCard card, int handCountAfter)
    {
        Success = success;
        Fail = fail;
        Card = card;
        HandCountAfter = handCountAfter;
    }

    // 카드를 내서 손패가 1장이 됐는가 (UNO 경쟁 시작 조건)
    public bool TriggersUnoRace => Success && HandCountAfter == 1;
    // 카드를 내서 손패가 0장이 됐는가 (효과 처리를 마치면 손패 비우기 승리)
    public bool EmptiedHand => Success && HandCountAfter == 0;
}

// 카드 뽑기 결과
public readonly struct UnoDrawResult
{
    public readonly int Requested;        // 뽑으려던 장수
    public readonly int Drawn;            // 실제로 뽑은 장수
    public readonly bool Lost;            // 손패가 15장이 되어 패배했는가 (15번째 카드를 뽑는 순간 즉시 멈춘다)
    public readonly bool DeckExhausted;   // 뽑을 카드가 모자라 다 뽑지 못했는가

    public UnoDrawResult(int requested, int drawn, bool lost, bool deckExhausted)
    {
        Requested = requested;
        Drawn = drawn;
        Lost = lost;
        DeckExhausted = deckExhausted;
    }
}
#endregion

// 우노 모드 한 판의 카드 상태(양쪽 손패, 덱, 현재 색, +2/+4 중첩)를 관리하는 순수 로직 클래스.
// 체스 이동이나 턴 진행은 다루지 않고 "카드가 어디에 있고 무엇을 낼 수 있는가"만 책임진다.
// 플레이어 번호는 0 = 백, 1 = 흑 (GameManager.CurrentTurn과 같은 규칙).
public class UnoMatch
{
    private readonly List<UnoCard>[] hands = { new List<UnoCard>(), new List<UnoCard>() };

    public UnoDeck Deck { get; }

    // 현재 색. 와일드를 낸 뒤에는 고른 색, 시작 카드가 와일드이면 Wild(아무 카드나 낼 수 있음)
    public UnoColor CurrentColor { get; private set; }

    // 쌓여 있는 +2/+4 장수 (0이면 중첩 없음)
    public int PendingDraw { get; private set; }

    // 와일드 +4가 한 번이라도 들어가 이후에는 와일드 +4로만 반격할 수 있는 상태
    public bool StackLockedToDraw4 { get; private set; }

    public UnoCard TopCard => Deck.Top;

    // 이번 판의 설정값 (로비에서 정함). 기본값은 UnoRules 상수.
    public int StartingHandSize { get; }
    public int LoseHandSize { get; }

    #region 생성
    // 새 판 시작: 덱을 섞고 양쪽에 시작 손패를 나눠준 뒤, 랜덤 카드 1장을 버림 더미에 올린다.
    // 시작 카드의 효과(+2, 스킵 등)는 발동하지 않고, 시작 카드가 와일드이면 아무 카드나 낼 수 있다.
    public UnoMatch(IEnumerable<UnoCard> cards, Random rng,
                    int startingHandSize = UnoRules.StartingHandSize, int loseHandSize = UnoRules.LoseHandSize)
    {
        StartingHandSize = Math.Max(1, startingHandSize);
        LoseHandSize = Math.Max(StartingHandSize + 1, loseHandSize);
        Deck = new UnoDeck(cards, rng);

        int needed = UnoRules.PlayerCount * StartingHandSize + 1;
        if (Deck.DrawPileCount < needed)
            throw new ArgumentException($"카드가 부족합니다. 최소 {needed}장이 필요합니다.", nameof(cards));

        for (int i = 0; i < StartingHandSize; i++)
        {
            for (int p = 0; p < UnoRules.PlayerCount; p++)
            {
                Deck.TryDraw(out UnoCard card);
                hands[p].Add(card);
            }
        }

        Deck.TryDraw(out UnoCard start);
        Deck.Discard(start);
        CurrentColor = start.IsWild ? UnoColor.Wild : start.Color;
    }

    // 이미 구성된 상태로 시작 (재접속 상태 복구, 테스트용). deck에는 버림 더미 최상단 카드가 이미 있어야 한다.
    public UnoMatch(UnoDeck deck, IEnumerable<UnoCard> hand0, IEnumerable<UnoCard> hand1, UnoColor currentColor,
                    int loseHandSize = UnoRules.LoseHandSize, int startingHandSize = UnoRules.StartingHandSize)
    {
        StartingHandSize = startingHandSize;
        LoseHandSize = loseHandSize;
        Deck = deck ?? throw new ArgumentNullException(nameof(deck));
        if (!deck.HasTop)
            throw new ArgumentException("버림 더미에 최상단 카드가 있어야 합니다.", nameof(deck));

        hands[0].AddRange(hand0);
        hands[1].AddRange(hand1);
        CurrentColor = currentColor;
    }
    #endregion

    #region 게스트용 비공개 뷰 (7단계)
    // 게스트의 화면용 상태: 양쪽 손패와 뽑을 카드 더미가 전부 "비공개 카드"이고, 정체는 호스트가 알려 주는 대로만 채워진다.
    // drawPileCount = 전체 덱 장수 - 양쪽 시작 손패 - 시작 카드 1장
    public static UnoMatch CreateHiddenView(int drawPileCount, int startingHandSize, int loseHandSize)
    {
        var pile = new List<UnoCard>(drawPileCount);
        for (int i = 0; i < drawPileCount; i++) pile.Add(UnoCard.Hidden);
        var deck = new UnoDeck(pile, new Random(), shuffle: false);
        deck.Discard(UnoCard.Hidden);

        var hand = new List<UnoCard>(startingHandSize);
        for (int i = 0; i < startingHandSize; i++) hand.Add(UnoCard.Hidden);
        return new UnoMatch(deck, hand, hand, UnoColor.Wild, loseHandSize, startingHandSize);
    }

    // 호스트가 보낸 첫 배분 반영: 내 손패와 시작 카드
    public void ApplyDeal(int ownTeam, IList<UnoCard> ownHand, UnoCard startCard)
    {
        ReplaceHand(ownTeam, ownHand);
        Deck.ReplaceTop(startCard);
        CurrentColor = startCard.IsWild ? UnoColor.Wild : startCard.Color;
    }

    public void ReplaceCard(int player, int handIndex, UnoCard card)
    {
        if (IsValidPlayer(player) && handIndex >= 0 && handIndex < hands[player].Count) hands[player][handIndex] = card;
    }

    // 손패 전체를 주어진 카드들로 바꾼다
    public void ReplaceHand(int player, IList<UnoCard> cards)
    {
        if (!IsValidPlayer(player)) return;
        hands[player].Clear();
        for (int i = 0; i < cards.Count; i++) hands[player].Add(cards[i]);
    }

    // 손패의 마지막 cards.Count장을 바꾼다 (방금 뽑은 카드의 정체를 채울 때)
    public void ReplaceTail(int player, IList<UnoCard> cards)
    {
        if (!IsValidPlayer(player)) return;
        List<UnoCard> hand = hands[player];
        int n = Math.Min(cards.Count, hand.Count);
        for (int i = 0; i < n; i++) hand[hand.Count - n + i] = cards[cards.Count - n + i];
    }
    #endregion

    #region 손패 조회
    public IReadOnlyList<UnoCard> GetHand(int player) => hands[player];
    public int GetHandCount(int player) => hands[player].Count;

    // 손패가 15장 이상이면 패배
    public bool IsLost(int player) => hands[player].Count >= LoseHandSize;

    public bool CanPlay(int player, int handIndex)
    {
        if (!IsValidPlayer(player) || handIndex < 0 || handIndex >= hands[player].Count) return false;
        if (hands[player][handIndex].IsHidden) return false;
        return UnoRules.CanPlay(hands[player][handIndex], Deck.Top, CurrentColor, PendingDraw, StackLockedToDraw4);
    }

    // 낼 수 있는 카드의 손패 인덱스를 result에 채운다 (UI에서 재사용 가능한 리스트를 넘겨 할당을 피한다)
    public void GetPlayableIndices(int player, List<int> result)
    {
        result.Clear();
        for (int i = 0; i < hands[player].Count; i++)
        {
            if (CanPlay(player, i)) result.Add(i);
        }
    }

    // 낼 수 있는 카드가 하나라도 있는가 (없으면 카드 뽑기 버튼 활성화)
    public bool HasPlayableCard(int player)
    {
        for (int i = 0; i < hands[player].Count; i++)
        {
            if (CanPlay(player, i)) return true;
        }
        return false;
    }
    #endregion

    #region 카드 내기 / 뽑기
    // 손패의 카드를 한 장 낸다. 와일드 계열은 chosenColor(Red~Blue)가 필요하다.
    // +2/+4를 내면 중첩 장수가 쌓이고, 와일드 +4가 들어가면 이후에는 와일드 +4로만 반격할 수 있다.
    // 카드 효과(이동, 부활, 손패 교환 등)는 이 클래스가 처리하지 않는다.
    public UnoPlayResult PlayCard(int player, int handIndex, UnoColor chosenColor = UnoColor.Wild)
    {
        if (!IsValidPlayer(player))
            return new UnoPlayResult(false, UnoPlayFail.InvalidPlayer, default, 0);

        List<UnoCard> hand = hands[player];
        if (handIndex < 0 || handIndex >= hand.Count)
            return new UnoPlayResult(false, UnoPlayFail.InvalidIndex, default, hand.Count);

        UnoCard card = hand[handIndex];
        if (card.IsHidden || !UnoRules.CanPlay(card, Deck.Top, CurrentColor, PendingDraw, StackLockedToDraw4))
            return new UnoPlayResult(false, UnoPlayFail.NotPlayable, card, hand.Count);

        // 와일드 계열은 Red~Blue 중 하나를 반드시 골라야 한다 (네트워크로 들어온 잘못된 값도 여기서 걸러낸다)
        if (card.IsWild && (chosenColor < UnoColor.Red || chosenColor > UnoColor.Blue))
            return new UnoPlayResult(false, UnoPlayFail.ColorRequired, card, hand.Count);

        hand.RemoveAt(handIndex);
        Deck.Discard(card);
        CurrentColor = card.IsWild ? chosenColor : card.Color;

        if (card.IsDrawCard)
        {
            PendingDraw += card.DrawAmount;
            if (card.Kind == UnoKind.WildDraw4) StackLockedToDraw4 = true;
        }

        return new UnoPlayResult(true, UnoPlayFail.None, card, hand.Count);
    }

    // 현재 색을 직접 바꾼다. 와일드를 낸 직후(색 미정 = Wild)와 색 선택 확정에 쓴다.
    public void SetCurrentColor(UnoColor color)
    {
        CurrentColor = color;
    }

    // player의 손패에 카드를 count장 뽑는다. 손패가 15장이 되는 순간 즉시 멈추고 패배로 알린다.
    public UnoDrawResult Draw(int player, int count)
    {
        List<UnoCard> hand = hands[player];
        int drawn = 0;
        bool lost = false;

        for (int i = 0; i < count; i++)
        {
            if (!Deck.TryDraw(out UnoCard card)) break;

            hand.Add(card);
            drawn++;

            if (hand.Count >= LoseHandSize)
            {
                lost = true;
                break;
            }
        }

        return new UnoDrawResult(count, drawn, lost, !lost && drawn < count);
    }

    // 쌓여 있는 +2/+4를 player가 모두 가져간다 (반격하지 못했을 때). 중첩 상태는 초기화된다.
    public UnoDrawResult ResolvePendingDraw(int player)
    {
        int amount = PendingDraw;
        PendingDraw = 0;
        StackLockedToDraw4 = false;
        return Draw(player, amount);
    }
    #endregion

    #region 카드 효과에 쓰이는 손패 조작
    // 두 플레이어의 손패를 통째로 교환 (리버스)
    public void SwapHands()
    {
        List<UnoCard> temp = hands[0];
        hands[0] = hands[1];
        hands[1] = temp;
    }

    // 손패 전부를 덱에 섞어 넣고 같은 장수만큼 다시 뽑는다 (숫자 0). 다시 뽑은 장수를 반환
    public int ResetHand(int player)
    {
        List<UnoCard> hand = hands[player];
        int count = hand.Count;

        Deck.ReturnToDrawPile(hand);
        hand.Clear();
        return Deck.DrawMany(count, hand);
    }
    #endregion

    private static bool IsValidPlayer(int player) => player >= 0 && player < UnoRules.PlayerCount;
}
