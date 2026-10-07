using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

// 우노 모드 1단계(데이터/덱/규칙/15장 패배 판정) 단위 테스트. UI, 네트워크, 체스 보드 없이 순수 로직만 검증한다.
public class UnoLogicTests
{
    private const string DatabasePath = "Assets/Data/UnoCardDatabase.asset";

    #region 테스트 도우미
    private static UnoCard N(UnoColor color, int number) => new UnoCard(color, UnoKind.Number, number);
    private static UnoCard K(UnoColor color, UnoKind kind) => new UnoCard(color, kind);
    private static readonly UnoCard Wild = new UnoCard(UnoColor.Wild, UnoKind.Wild);
    private static readonly UnoCard WildDraw4 = new UnoCard(UnoColor.Wild, UnoKind.WildDraw4);

    private static UnoCardDatabase LoadDatabase()
    {
        var db = AssetDatabase.LoadAssetAtPath<UnoCardDatabase>(DatabasePath);
        Assert.IsNotNull(db, $"{DatabasePath}가 없습니다. 메뉴 '체스 증강/우노 카드 에셋 생성·갱신'을 먼저 실행하세요.");
        return db;
    }

    // 섞지 않은 덱 + 직접 구성한 손패/버림 더미 최상단으로 시작하는 상태를 만든다
    private static UnoMatch MakeMatch(UnoCard top, UnoColor currentColor, IEnumerable<UnoCard> hand0, IEnumerable<UnoCard> hand1, IEnumerable<UnoCard> drawPile = null)
    {
        var deck = new UnoDeck(drawPile ?? new List<UnoCard>(), new Random(1), shuffle: false);
        deck.Discard(top);
        return new UnoMatch(deck, hand0, hand1, currentColor);
    }

    private static List<UnoCard> Repeat(UnoCard card, int count) => Enumerable.Repeat(card, count).ToList();
    #endregion

    #region 카드 데이터 (실제 에셋 검증)
    [Test]
    public void Database_Has50KindsAnd104Cards()
    {
        UnoCardDatabase db = LoadDatabase();
        Assert.AreEqual(50, db.allCards.Count);
        Assert.AreEqual(104, db.TotalCardCount);
        Assert.AreEqual(104, db.BuildDeck().Count);
    }

    [Test]
    public void Database_ColorCardsHave24PerColor_AndWildHave4Each()
    {
        List<UnoCard> deck = LoadDatabase().BuildDeck();

        foreach (UnoColor color in new[] { UnoColor.Red, UnoColor.Yellow, UnoColor.Green, UnoColor.Blue })
            Assert.AreEqual(24, deck.Count(c => c.Color == color), $"{color} 카드 수");

        Assert.AreEqual(4, deck.Count(c => c.Kind == UnoKind.Wild));
        Assert.AreEqual(4, deck.Count(c => c.Kind == UnoKind.WildDraw4));
    }

    [Test]
    public void Database_ExcludesNumber9_AndEveryNumber0To8ExistsTwicePerColor()
    {
        List<UnoCard> deck = LoadDatabase().BuildDeck();

        Assert.IsFalse(deck.Any(c => c.Kind == UnoKind.Number && c.Number > 8), "숫자 9 이상은 덱에 없어야 한다");
        for (int n = 0; n <= 8; n++)
            Assert.AreEqual(8, deck.Count(c => c.Kind == UnoKind.Number && c.Number == n), $"숫자 {n} 총 장수");
    }

    [Test]
    public void Database_CardIdsAreUnique_AndMatchRuntimeCardIds()
    {
        UnoCardDatabase db = LoadDatabase();

        Assert.AreEqual(db.allCards.Count, db.allCards.Select(d => d.cardId).Distinct().Count(), "cardId 중복");
        foreach (UnoCardData data in db.allCards)
        {
            Assert.AreEqual(data.cardId, UnoCard.FromData(data).Id, $"{data.name}의 cardId와 UnoCard.Id 불일치");
            Assert.AreSame(data, db.Get(UnoCard.FromData(data)), $"{data.cardId} 조회 실패");
        }
    }

    [Test]
    public void Database_EveryCardHasSprite_AndWildHaveFourColorSprites()
    {
        foreach (UnoCardData data in LoadDatabase().allCards)
        {
            Assert.IsNotNull(data.sprite, $"{data.cardId} 스프라이트 누락");
            if (data.kind == UnoKind.Wild || data.kind == UnoKind.WildDraw4)
            {
                Assert.AreEqual(4, data.chosenColorSprites.Length, $"{data.cardId} 색상 변형 개수");
                Assert.IsFalse(data.chosenColorSprites.Any(s => s == null), $"{data.cardId} 색상 변형 스프라이트 누락");
                Assert.AreNotSame(data.sprite, data.GetSprite(UnoColor.Red));
            }
        }
    }
    #endregion

    #region 사용 가능 판정
    [Test]
    public void CanPlay_MatchesColorNumberOrKind()
    {
        UnoCard top = N(UnoColor.Red, 3);

        Assert.IsTrue(UnoRules.CanPlay(N(UnoColor.Red, 7), top, UnoColor.Red), "같은 색");
        Assert.IsTrue(UnoRules.CanPlay(N(UnoColor.Blue, 3), top, UnoColor.Red), "같은 숫자");
        Assert.IsFalse(UnoRules.CanPlay(N(UnoColor.Blue, 4), top, UnoColor.Red), "색도 숫자도 다름");
        Assert.IsFalse(UnoRules.CanPlay(K(UnoColor.Blue, UnoKind.Skip), top, UnoColor.Red), "숫자 카드 위에 다른 색 스킵");

        UnoCard topSkip = K(UnoColor.Red, UnoKind.Skip);
        Assert.IsTrue(UnoRules.CanPlay(K(UnoColor.Blue, UnoKind.Skip), topSkip, UnoColor.Red), "같은 종류(스킵)");
        Assert.IsFalse(UnoRules.CanPlay(N(UnoColor.Blue, 0), topSkip, UnoColor.Red), "스킵 위에 다른 색 숫자");
    }

    [Test]
    public void CanPlay_WildAlwaysPlayable_AndChosenColorDecidesNextCard()
    {
        UnoCard top = N(UnoColor.Red, 3);
        Assert.IsTrue(UnoRules.CanPlay(Wild, top, UnoColor.Red));
        Assert.IsTrue(UnoRules.CanPlay(WildDraw4, top, UnoColor.Red));

        // 와일드로 파랑을 고른 뒤: 파랑만 낼 수 있다
        UnoCard topWild = Wild;
        Assert.IsTrue(UnoRules.CanPlay(N(UnoColor.Blue, 5), topWild, UnoColor.Blue));
        Assert.IsFalse(UnoRules.CanPlay(N(UnoColor.Red, 5), topWild, UnoColor.Blue));
    }

    [Test]
    public void CanPlay_StartingWildAllowsAnyCard()
    {
        Assert.IsTrue(UnoRules.CanPlay(N(UnoColor.Green, 8), Wild, UnoColor.Wild));
        Assert.IsTrue(UnoRules.CanPlay(K(UnoColor.Yellow, UnoKind.Reverse), Wild, UnoColor.Wild));
    }

    [Test]
    public void CanPlay_PendingStack_OnlyCounterCardsAllowed()
    {
        UnoCard top = K(UnoColor.Red, UnoKind.Draw2);

        // +2가 쌓인 상태: 색이 달라도 +2, 와일드 +4로 반격 가능. 숫자/일반 와일드는 불가
        Assert.IsTrue(UnoRules.CanPlay(K(UnoColor.Blue, UnoKind.Draw2), top, UnoColor.Red, pendingDraw: 2));
        Assert.IsTrue(UnoRules.CanPlay(WildDraw4, top, UnoColor.Red, pendingDraw: 2));
        Assert.IsFalse(UnoRules.CanPlay(N(UnoColor.Red, 3), top, UnoColor.Red, pendingDraw: 2));
        Assert.IsFalse(UnoRules.CanPlay(Wild, top, UnoColor.Red, pendingDraw: 2));

        // +4가 한 번 들어간 뒤: +4로만 반격 가능 (+2로 되돌릴 수 없다)
        Assert.IsTrue(UnoRules.CanPlay(WildDraw4, WildDraw4, UnoColor.Blue, pendingDraw: 6, stackLockedToDraw4: true));
        Assert.IsFalse(UnoRules.CanPlay(K(UnoColor.Blue, UnoKind.Draw2), WildDraw4, UnoColor.Blue, pendingDraw: 6, stackLockedToDraw4: true));
    }
    #endregion

    #region 덱 / 셔플 / 재섞기
    [Test]
    public void Deck_ShuffleKeepsEveryCard()
    {
        List<UnoCard> cards = LoadDatabase().BuildDeck();
        var deck = new UnoDeck(cards, new Random(42));

        var drawn = new List<UnoCard>();
        Assert.AreEqual(104, deck.DrawMany(200, drawn), "카드가 있는 만큼만 뽑힌다");
        Assert.AreEqual(0, deck.DrawPileCount);

        Func<UnoCard, string> key = c => c.Id;
        CollectionAssert.AreEquivalent(cards.Select(key).ToList(), drawn.Select(key).ToList());
    }

    [Test]
    public void Deck_SameSeedSameOrder_DifferentSeedDifferentOrder()
    {
        List<UnoCard> cards = LoadDatabase().BuildDeck();

        List<string> Order(int seed)
        {
            var deck = new UnoDeck(cards, new Random(seed));
            var list = new List<UnoCard>();
            deck.DrawMany(104, list);
            return list.Select(c => c.Id).ToList();
        }

        CollectionAssert.AreEqual(Order(7), Order(7));
        CollectionAssert.AreNotEqual(Order(7), Order(8));
    }

    [Test]
    public void Deck_DrawFromEmptyPile_ReshufflesDiscardKeepingTopCard()
    {
        var deck = new UnoDeck(new List<UnoCard>(), new Random(1), shuffle: false);
        UnoCard a = N(UnoColor.Red, 1), b = N(UnoColor.Red, 2), c = N(UnoColor.Red, 3), top = N(UnoColor.Blue, 4);
        deck.Discard(a); deck.Discard(b); deck.Discard(c); deck.Discard(top);

        Assert.IsTrue(deck.TryDraw(out UnoCard drawn));

        Assert.AreEqual(1, deck.ReshuffleCount);
        Assert.AreEqual(1, deck.DiscardCount, "최상단 카드 한 장만 남는다");
        Assert.AreEqual(top, deck.Top);
        Assert.AreEqual(2, deck.DrawPileCount, "3장이 뽑을 더미로 이동하고 1장을 뽑았다");
        CollectionAssert.Contains(new[] { a, b, c }, drawn);
    }

    [Test]
    public void Deck_DrawFailsWhenNothingLeftToReshuffle()
    {
        var deck = new UnoDeck(new List<UnoCard>(), new Random(1), shuffle: false);
        deck.Discard(N(UnoColor.Red, 1));

        Assert.IsFalse(deck.TryDraw(out _));
        Assert.AreEqual(0, deck.ReshuffleCount);
        Assert.AreEqual(1, deck.DiscardCount);
        Assert.AreEqual(0, deck.ReshuffleDiscard(), "버림 더미가 1장이면 옮길 카드가 없다");
    }

    [Test]
    public void Deck_TopOnEmptyDiscardThrows()
    {
        var deck = new UnoDeck(new List<UnoCard>(), new Random(1));
        Assert.IsFalse(deck.HasTop);
        Assert.Throws<InvalidOperationException>(() => { var _ = deck.Top; });
    }
    #endregion

    #region 한 판 상태 (UnoMatch)
    [Test]
    public void Match_Setup_Deals7EachAndOneStartCard_AndConservesCards()
    {
        var match = new UnoMatch(LoadDatabase().BuildDeck(), new Random(3));

        Assert.AreEqual(7, match.GetHandCount(0));
        Assert.AreEqual(7, match.GetHandCount(1));
        Assert.AreEqual(1, match.Deck.DiscardCount);
        Assert.AreEqual(104 - 15, match.Deck.DrawPileCount);
        Assert.AreEqual(match.TopCard.IsWild ? UnoColor.Wild : match.TopCard.Color, match.CurrentColor);
        Assert.AreEqual(0, match.PendingDraw, "시작 카드의 효과는 발동하지 않는다");
    }

    [Test]
    public void Match_SetupWithTooFewCardsThrows()
    {
        Assert.Throws<ArgumentException>(() => new UnoMatch(Repeat(N(UnoColor.Red, 1), 10), new Random(1)));
    }

    [Test]
    public void Match_StartingWildTop_AllowsAnyCardInHand()
    {
        UnoMatch match = MakeMatch(Wild, UnoColor.Wild, new[] { N(UnoColor.Green, 8), K(UnoColor.Blue, UnoKind.Reverse) }, new List<UnoCard>());

        Assert.IsTrue(match.CanPlay(0, 0));
        Assert.IsTrue(match.CanPlay(0, 1));
    }

    [Test]
    public void Match_PlayCard_RemovesFromHand_UpdatesTopAndColor()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 3), UnoColor.Red,
            new[] { N(UnoColor.Red, 7), N(UnoColor.Blue, 3), N(UnoColor.Green, 1) }, new List<UnoCard>());

        UnoPlayResult result = match.PlayCard(0, 1);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.HandCountAfter);
        Assert.AreEqual(N(UnoColor.Blue, 3), match.TopCard);
        Assert.AreEqual(UnoColor.Blue, match.CurrentColor);
    }

    [Test]
    public void Match_PlayCard_RejectsInvalidInputs()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 3), UnoColor.Red, new[] { N(UnoColor.Blue, 9) }, new List<UnoCard>());

        Assert.AreEqual(UnoPlayFail.InvalidPlayer, match.PlayCard(2, 0).Fail);
        Assert.AreEqual(UnoPlayFail.InvalidIndex, match.PlayCard(0, 5).Fail);
        Assert.AreEqual(UnoPlayFail.InvalidIndex, match.PlayCard(0, -1).Fail);
        Assert.AreEqual(UnoPlayFail.NotPlayable, match.PlayCard(0, 0).Fail);
        Assert.AreEqual(1, match.GetHandCount(0), "실패한 카드는 손패에 남는다");
    }

    [Test]
    public void Match_PlayWild_RequiresColor_AndSetsCurrentColor()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 3), UnoColor.Red, new[] { Wild, N(UnoColor.Green, 1) }, new List<UnoCard>());

        Assert.AreEqual(UnoPlayFail.ColorRequired, match.PlayCard(0, 0).Fail);
        Assert.AreEqual(2, match.GetHandCount(0));

        Assert.IsTrue(match.PlayCard(0, 0, UnoColor.Green).Success);
        Assert.AreEqual(UnoColor.Green, match.CurrentColor);
        Assert.IsTrue(match.CanPlay(0, 0), "고른 색(초록) 카드를 낼 수 있다");
    }

    [Test]
    public void Match_PlayCard_ReportsUnoRaceAndEmptyHand()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 3), UnoColor.Red,
            new[] { N(UnoColor.Red, 1), N(UnoColor.Red, 2) }, new List<UnoCard>());

        UnoPlayResult first = match.PlayCard(0, 0);
        Assert.IsTrue(first.TriggersUnoRace);
        Assert.IsFalse(first.EmptiedHand);

        UnoPlayResult second = match.PlayCard(0, 0);
        Assert.IsFalse(second.TriggersUnoRace);
        Assert.IsTrue(second.EmptiedHand);
    }

    [Test]
    public void Match_StackAccumulates_AndDraw4LocksCounters()
    {
        var hand = new[] { K(UnoColor.Red, UnoKind.Draw2), WildDraw4, K(UnoColor.Blue, UnoKind.Draw2) };
        UnoMatch match = MakeMatch(N(UnoColor.Red, 3), UnoColor.Red, hand, hand);

        match.PlayCard(0, 0);                                   // +2
        Assert.AreEqual(2, match.PendingDraw);
        Assert.IsFalse(match.StackLockedToDraw4);

        match.PlayCard(1, 1, UnoColor.Blue);                    // +4 반격 (누적 6)
        Assert.AreEqual(6, match.PendingDraw);
        Assert.IsTrue(match.StackLockedToDraw4);

        // 플레이어 0의 남은 손패: [와일드 +4, 파랑 +2]
        Assert.IsTrue(match.CanPlay(0, 0), "+4가 들어간 뒤에도 와일드 +4로는 반격할 수 있다");
        Assert.IsFalse(match.CanPlay(0, 1), "+4가 들어간 뒤에는 +2로 반격할 수 없다");
    }

    [Test]
    public void Match_ResolvePending_DrawsStackedAmountAndClearsState()
    {
        UnoMatch match = MakeMatch(K(UnoColor.Red, UnoKind.Draw2), UnoColor.Red,
            new[] { K(UnoColor.Red, UnoKind.Draw2) }, new List<UnoCard>(), Repeat(N(UnoColor.Green, 1), 10));

        match.PlayCard(0, 0);
        UnoDrawResult result = match.ResolvePendingDraw(1);

        Assert.AreEqual(2, result.Drawn);
        Assert.IsFalse(result.Lost);
        Assert.AreEqual(2, match.GetHandCount(1));
        Assert.AreEqual(0, match.PendingDraw);
        Assert.IsFalse(match.StackLockedToDraw4);
    }

    [Test]
    public void Match_Draw_StopsAtFifteenCardsAndMarksLoss()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 1), UnoColor.Red, Repeat(N(UnoColor.Blue, 1), 13), new List<UnoCard>(),
            Repeat(N(UnoColor.Green, 2), 10));

        UnoDrawResult result = match.Draw(0, 4);   // 13 -> 14 -> 15(패배, 여기서 멈춤)

        Assert.IsTrue(result.Lost);
        Assert.AreEqual(2, result.Drawn);
        Assert.AreEqual(15, match.GetHandCount(0));
        Assert.IsTrue(match.IsLost(0));
        Assert.AreEqual(8, match.Deck.DrawPileCount, "패배 후 남은 카드는 뽑지 않는다");
    }

    [Test]
    public void Match_Draw_FourteenCardsIsNotALoss()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 1), UnoColor.Red, Repeat(N(UnoColor.Blue, 1), 12), new List<UnoCard>(),
            Repeat(N(UnoColor.Green, 2), 10));

        UnoDrawResult result = match.Draw(0, 2);

        Assert.IsFalse(result.Lost);
        Assert.AreEqual(14, match.GetHandCount(0));
        Assert.IsFalse(match.IsLost(0));
    }

    [Test]
    public void Match_Draw_ReportsDeckExhaustion()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 1), UnoColor.Red, new List<UnoCard>(), new List<UnoCard>(),
            Repeat(N(UnoColor.Green, 2), 2));

        UnoDrawResult result = match.Draw(0, 5);

        Assert.AreEqual(2, result.Drawn);
        Assert.IsTrue(result.DeckExhausted);
        Assert.IsFalse(result.Lost);
    }

    [Test]
    public void Match_Draw_ReshufflesDiscardWhenDrawPileRunsOut()
    {
        var deck = new UnoDeck(new List<UnoCard>(), new Random(1), shuffle: false);
        deck.Discard(N(UnoColor.Red, 1)); deck.Discard(N(UnoColor.Red, 2)); deck.Discard(N(UnoColor.Red, 3));
        var match = new UnoMatch(deck, new List<UnoCard>(), new List<UnoCard>(), UnoColor.Red);

        UnoDrawResult result = match.Draw(0, 2);

        Assert.AreEqual(2, result.Drawn);
        Assert.AreEqual(1, deck.ReshuffleCount);
        Assert.AreEqual(N(UnoColor.Red, 3), match.TopCard, "최상단 카드는 그대로 남는다");
    }

    [Test]
    public void Match_PlayWild_RejectsInvalidColorValues()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 3), UnoColor.Red, new[] { Wild }, new List<UnoCard>());

        Assert.AreEqual(UnoPlayFail.ColorRequired, match.PlayCard(0, 0, UnoColor.Wild).Fail);
        Assert.AreEqual(UnoPlayFail.ColorRequired, match.PlayCard(0, 0, (UnoColor)9).Fail);
        Assert.AreEqual(UnoPlayFail.ColorRequired, match.PlayCard(0, 0, (UnoColor)(-1)).Fail);
        Assert.AreEqual(1, match.GetHandCount(0), "잘못된 색으로는 카드가 사용되지 않는다");
        Assert.AreEqual(UnoColor.Red, match.CurrentColor);
    }

    [Test]
    public void Match_ResolvePending_CanCauseLoss()
    {
        // 손패 10장인 플레이어가 중첩 +6을 가져가면 15번째 카드에서 패배한다
        UnoMatch match = MakeMatch(WildDraw4, UnoColor.Blue, new[] { WildDraw4 }, Repeat(N(UnoColor.Blue, 1), 10),
            Repeat(N(UnoColor.Green, 2), 10));
        match.PlayCard(0, 0, UnoColor.Blue);    // +4
        match.PlayCard(1, 0, UnoColor.Blue);    // 일반 카드라 불가 - 중첩 중이므로 실패해야 한다
        Assert.AreEqual(10, match.GetHandCount(1));
        Assert.AreEqual(4, match.PendingDraw);

        UnoDrawResult result = match.ResolvePendingDraw(1);

        Assert.AreEqual(4, result.Drawn);
        Assert.IsFalse(result.Lost, "10 + 4 = 14장은 패배가 아니다");

        UnoMatch match2 = MakeMatch(WildDraw4, UnoColor.Blue, new[] { WildDraw4 }, Repeat(N(UnoColor.Blue, 1), 12),
            Repeat(N(UnoColor.Green, 2), 10));
        match2.PlayCard(0, 0, UnoColor.Blue);
        UnoDrawResult lost = match2.ResolvePendingDraw(1);
        Assert.IsTrue(lost.Lost);
        Assert.AreEqual(3, lost.Drawn, "12 + 3 = 15장째에서 멈춘다");
        Assert.AreEqual(0, match2.PendingDraw);
    }

    [Test]
    public void Database_GetByCardValue_ReturnsSameAssetAsGetById()
    {
        UnoCardDatabase db = LoadDatabase();
        UnoCard card = N(UnoColor.Yellow, 6);

        Assert.AreSame(db.GetById("Yellow_6"), db.Get(card));
        Assert.IsNull(db.Get(N(UnoColor.Yellow, 9)), "숫자 9 카드는 데이터가 없다");
        Assert.IsNull(db.GetById("nope"));
    }

    [Test]
    public void Match_HasPlayableCard_FalseWhenNothingMatches()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 3), UnoColor.Red,
            new[] { N(UnoColor.Blue, 4), K(UnoColor.Green, UnoKind.Skip) }, new[] { Wild });

        Assert.IsFalse(match.HasPlayableCard(0), "낼 카드가 없으면 카드 뽑기 버튼이 활성화되어야 한다");
        Assert.IsTrue(match.HasPlayableCard(1));

        var indices = new List<int>();
        match.GetPlayableIndices(0, indices);
        Assert.IsEmpty(indices);
        match.GetPlayableIndices(1, indices);
        CollectionAssert.AreEqual(new[] { 0 }, indices);
    }

    [Test]
    public void Match_SwapHands_ExchangesBothHands()
    {
        UnoMatch match = MakeMatch(N(UnoColor.Red, 3), UnoColor.Red,
            new[] { N(UnoColor.Red, 1) }, new[] { N(UnoColor.Blue, 2), N(UnoColor.Blue, 3) });

        match.SwapHands();

        Assert.AreEqual(2, match.GetHandCount(0));
        Assert.AreEqual(1, match.GetHandCount(1));
        Assert.AreEqual(N(UnoColor.Red, 1), match.GetHand(1)[0]);
    }

    [Test]
    public void Match_ResetHand_KeepsHandSizeAndConservesTotalCards()
    {
        List<UnoCard> all = LoadDatabase().BuildDeck();
        var match = new UnoMatch(all, new Random(11));
        int total = match.GetHandCount(0) + match.GetHandCount(1) + match.Deck.DrawPileCount + match.Deck.DiscardCount;

        int redrawn = match.ResetHand(0);

        Assert.AreEqual(7, redrawn);
        Assert.AreEqual(7, match.GetHandCount(0));
        Assert.AreEqual(total, match.GetHandCount(0) + match.GetHandCount(1) + match.Deck.DrawPileCount + match.Deck.DiscardCount);
    }

    [Test]
    public void Match_CustomStartingHandAndLoseSize_AreApplied()
    {
        var match = new UnoMatch(LoadDatabase().BuildDeck(), new Random(5), startingHandSize: 3, loseHandSize: 10);

        Assert.AreEqual(3, match.GetHandCount(0));
        Assert.AreEqual(3, match.GetHandCount(1));
        Assert.AreEqual(104 - 7, match.Deck.DrawPileCount);

        UnoDrawResult r = match.Draw(0, 20);
        Assert.IsTrue(r.Lost);
        Assert.AreEqual(10, match.GetHandCount(0), "손패가 설정한 최대 패 장수(10)가 되는 순간 멈추고 패배");
        Assert.IsTrue(match.IsLost(0));
        Assert.IsFalse(match.IsLost(1));
    }

    [Test]
    public void Match_LoseSizeIsAtLeastStartingHandPlusOne()
    {
        var match = new UnoMatch(LoadDatabase().BuildDeck(), new Random(5), startingHandSize: 8, loseHandSize: 2);
        Assert.AreEqual(9, match.LoseHandSize);
        Assert.IsFalse(match.IsLost(0));
    }

    [Test]
    public void MovesFor_FollowsCardRules()
    {
        Assert.AreEqual(1, UnoRules.MovesFor(N(UnoColor.Red, 1)));
        Assert.AreEqual(4, UnoRules.MovesFor(N(UnoColor.Red, 4)));
        Assert.AreEqual(1, UnoRules.MovesFor(N(UnoColor.Red, 0)), "0번 카드는 손패 리셋 후 이동 1회");
        for (int n = 5; n <= 8; n++)
            Assert.AreEqual(0, UnoRules.MovesFor(N(UnoColor.Blue, n)), "5~8 부활 카드는 이동 없음");
        foreach (UnoKind kind in new[] { UnoKind.Skip, UnoKind.Draw2, UnoKind.Reverse })
            Assert.AreEqual(1, UnoRules.MovesFor(K(UnoColor.Green, kind)));
        Assert.AreEqual(1, UnoRules.MovesFor(Wild));
        Assert.AreEqual(1, UnoRules.MovesFor(WildDraw4));
    }

    [Test]
    public void MinMoveRule_OnlyForNumbers1To4()
    {
        for (int n = 0; n <= 8; n++)
            Assert.AreEqual(n >= 1 && n <= 4, UnoRules.HasMinMoveRule(N(UnoColor.Red, n)), $"숫자 {n}");
        Assert.IsFalse(UnoRules.HasMinMoveRule(K(UnoColor.Red, UnoKind.Skip)));
        Assert.IsFalse(UnoRules.HasMinMoveRule(WildDraw4));
    }
    #endregion

    #region 네트워크용 (7단계)
    [Test]
    public void CardCodec_RoundTrips_AllDeckCardsAndHidden()
    {
        foreach (var c in LoadDatabase().BuildDeck().Distinct())
            Assert.AreEqual(c, UnoCard.Decode(c.Encode()), c.Id);
        Assert.IsTrue(UnoCard.Decode(UnoCard.Hidden.Encode()).IsHidden);
        Assert.IsTrue(UnoCard.Decode(0x0777).IsHidden, "범위를 벗어난 코드는 비공개 카드");

        var list = new List<UnoCard> { N(UnoColor.Red, 3), Wild, UnoCard.Hidden };
        CollectionAssert.AreEqual(list, UnoCardCodec.Unpack(UnoCardCodec.Pack(list)));
    }

    [Test]
    public void HiddenView_CannotPlayHiddenCards_AndAppliesDeal()
    {
        var view = UnoMatch.CreateHiddenView(100, 7, 15);
        Assert.AreEqual(7, view.GetHandCount(0));
        Assert.AreEqual(100, view.Deck.DrawPileCount);
        Assert.IsFalse(view.HasPlayableCard(0));
        Assert.IsFalse(view.PlayCard(0, 0, UnoColor.Red).Success);

        var hand = Enumerable.Repeat(N(UnoColor.Blue, 2), 7).ToList();
        view.ApplyDeal(1, hand, N(UnoColor.Blue, 5));
        Assert.AreEqual(UnoColor.Blue, view.CurrentColor);
        Assert.IsTrue(view.CanPlay(1, 0));
        Assert.IsFalse(view.CanPlay(0, 0), "상대 손패는 계속 비공개");
    }

    [Test]
    public void ReplaceTail_And_ReplaceCard_OnlyTouchRequestedSlots()
    {
        var view = UnoMatch.CreateHiddenView(50, 3, 15);
        view.Draw(0, 2);
        view.ReplaceTail(0, new List<UnoCard> { N(UnoColor.Red, 1), N(UnoColor.Green, 2) });
        Assert.IsTrue(view.GetHand(0)[2].IsHidden);
        Assert.AreEqual(N(UnoColor.Red, 1), view.GetHand(0)[3]);
        Assert.AreEqual(N(UnoColor.Green, 2), view.GetHand(0)[4]);
        Assert.IsTrue(view.GetHand(0)[0].IsHidden);
        view.ReplaceCard(0, 0, Wild);
        Assert.AreEqual(Wild, view.GetHand(0)[0]);
    }
    #endregion
}
