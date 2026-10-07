using System;
using System.Collections.Generic;

// 뽑을 카드 더미(drawPile)와 낸 카드 기록(discardHistory)을 관리하는 순수 로직 클래스.
//  - 낸 카드는 순서대로 discardHistory에 저장되고, 맨 마지막 카드가 버림 더미의 최상단이다.
//  - 뽑을 카드가 없으면 최상단 카드 한 장만 남기고 나머지를 뽑을 카드 더미로 되돌려 섞는다.
// 섞기는 외부에서 받은 System.Random으로 하므로, 같은 시드면 항상 같은 순서가 나온다
// (호스트가 시드를 정해 결과를 동기화하는 네트워크 단계에서도 그대로 쓸 수 있다).
public class UnoDeck
{
    private readonly List<UnoCard> drawPile;
    private readonly List<UnoCard> discardHistory = new List<UnoCard>();
    private readonly Random rng;

    public UnoDeck(IEnumerable<UnoCard> cards, Random rng, bool shuffle = true)
    {
        if (cards == null) throw new ArgumentNullException(nameof(cards));

        this.rng = rng ?? new Random();
        drawPile = new List<UnoCard>(cards);
        if (shuffle) Shuffle(drawPile);
    }

    #region 상태 조회
    public int DrawPileCount => drawPile.Count;
    public int DiscardCount => discardHistory.Count;
    public int ReshuffleCount { get; private set; }        // 지금까지 버림 더미를 다시 섞은 횟수
    public IReadOnlyList<UnoCard> DiscardHistory => discardHistory;
    public bool HasTop => discardHistory.Count > 0;

    // 버림 더미 최상단 카드 (낸 카드가 없으면 예외)
    public UnoCard Top
    {
        get
        {
            if (discardHistory.Count == 0)
                throw new InvalidOperationException("버림 더미가 비어 있습니다.");
            return discardHistory[discardHistory.Count - 1];
        }
    }
    #endregion

    #region 뽑기 / 버리기
    // 카드 한 장을 뽑는다. 뽑을 카드가 없으면 버림 더미를 다시 섞어 채우고, 그래도 없으면 false를 반환한다.
    public bool TryDraw(out UnoCard card)
    {
        if (drawPile.Count == 0)
            ReshuffleDiscard();

        if (drawPile.Count == 0)
        {
            card = default;
            return false;
        }

        int last = drawPile.Count - 1;
        card = drawPile[last];
        drawPile.RemoveAt(last);
        return true;
    }

    // count장을 뽑아 into에 추가하고, 실제로 뽑은 장수를 반환한다 (카드가 모자라면 count보다 작을 수 있다)
    public int DrawMany(int count, List<UnoCard> into)
    {
        int drawn = 0;
        for (int i = 0; i < count; i++)
        {
            if (!TryDraw(out UnoCard card)) break;
            into.Add(card);
            drawn++;
        }
        return drawn;
    }

    // 최상단 카드의 정체를 바꾼다 (게스트가 시작 카드 값을 받았을 때)
    public void ReplaceTop(UnoCard card)
    {
        if (discardHistory.Count == 0) discardHistory.Add(card);
        else discardHistory[discardHistory.Count - 1] = card;
    }

    // 낸 카드를 버림 더미 최상단에 올린다
    public void Discard(UnoCard card)
    {
        discardHistory.Add(card);
    }

    // 카드들을 뽑을 카드 더미에 되돌리고 전체를 다시 섞는다 (0번 카드: 손패 리셋용)
    public void ReturnToDrawPile(IEnumerable<UnoCard> cards)
    {
        drawPile.AddRange(cards);
        Shuffle(drawPile);
    }
    #endregion

    #region 재섞기
    // 버림 더미에서 가장 위(마지막) 카드만 남기고, 나머지를 기록에서 삭제한 뒤 뽑을 카드 더미에 추가하고 섞는다.
    // 옮긴 카드 수를 반환 (버림 더미에 카드가 1장 이하면 아무것도 하지 않고 0)
    public int ReshuffleDiscard()
    {
        int movable = discardHistory.Count - 1;
        if (movable <= 0) return 0;

        drawPile.AddRange(discardHistory.GetRange(0, movable));
        discardHistory.RemoveRange(0, movable);
        Shuffle(drawPile);

        ReshuffleCount++;
        return movable;
    }

    // Fisher-Yates 셔플
    private void Shuffle(List<UnoCard> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            UnoCard temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
    #endregion
}
