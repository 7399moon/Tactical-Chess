// 우노 모드의 고정 규칙 값과 "이 카드를 지금 낼 수 있는가" 판정을 모아둔 정적 클래스.
// 게임 상태에 의존하지 않고 값만 받아 판정하므로 UI/네트워크 없이 단위 테스트할 수 있다.
public static class UnoRules
{
    #region 규칙 상수
    public const int PlayerCount = 2;       // 0 = 백, 1 = 흑 (GameManager.CurrentTurn과 같은 규칙)
    public const int StartingHandSize = 7;  // 시작 손패 기본값 (로비에서 1~8로 변경 가능)
    public const int MaxHandSize = 14;      // 최대 보유 장수 기본값 (= LoseHandSize - 1)
    public const int LoseHandSize = 15;     // 손패가 이 장수가 되는 순간 패배 (기본값, 로비의 "최대 패 장수")
    public const int MinStartingHand = 1, MaxStartingHand = 8;   // 로비 설정 범위
    public const int MinLoseHand = 10, MaxLoseHand = 20;         // 로비 설정 범위
    public const int UnoRacePenalty = 2;    // UNO 경쟁에서 지면 뽑는 장수
    #endregion

    // 카드 한 장이 요구하는 체스 이동 횟수 (턴 흐름 규칙)
    //  숫자 1~4: 숫자만큼 / 5~8(부활): 이동 없음 / 0, 스킵, +2, 리버스, 와일드, 와일드 +4: 1회
    public static int MovesFor(UnoCard card)
    {
        if (card.Kind != UnoKind.Number) return 1;
        if (card.Number >= 1 && card.Number <= 4) return card.Number;
        return card.Number >= 5 ? 0 : 1;
    }

    // "최소 1회는 이동해야 한다" 제한은 숫자 1~4 카드에만 적용된다
    public static bool HasMinMoveRule(UnoCard card)
    {
        return card.Kind == UnoKind.Number && card.Number >= 1 && card.Number <= 4;
    }

    // 버림 더미 최상단(top)과 현재 색(currentColor)을 기준으로 card를 지금 낼 수 있는지 판정한다.
    //  - 중첩 중(pendingDraw > 0)일 때는 반격 가능한 카드만 낼 수 있다.
    //    +2가 쌓인 상태: +2 또는 와일드 +4 / 와일드 +4가 한 번이라도 들어간 상태(stackLockedToDraw4): 와일드 +4만
    //  - 평소에는 와일드 계열은 항상, 그 외에는 색상/숫자/특수 종류 중 하나가 맞으면 낼 수 있다.
    //  - currentColor가 Wild이면(시작 카드가 와일드) 어떤 카드든 낼 수 있다.
    public static bool CanPlay(UnoCard card, UnoCard top, UnoColor currentColor, int pendingDraw = 0, bool stackLockedToDraw4 = false)
    {
        if (pendingDraw > 0)
            return stackLockedToDraw4 ? card.Kind == UnoKind.WildDraw4 : card.IsDrawCard;

        if (card.IsWild) return true;
        if (currentColor == UnoColor.Wild) return true;
        if (card.Color == currentColor) return true;

        // 같은 종류(스킵-스킵, +2-+2, 리버스-리버스) 또는 같은 숫자(숫자 카드끼리)
        if (card.Kind != top.Kind) return false;
        return card.Kind != UnoKind.Number || card.Number == top.Number;
    }
}
