using System;
using System.Collections.Generic;
using System.Text;

// 덱/손패/버림 더미에서 오가는 "카드 한 장"의 런타임 값. 색상, 종류, 숫자만 담은 작은 불변 구조체라서
// 복사 비용이 작고, 같은 종류의 카드끼리는 서로 같은 값(==)으로 비교된다. (스프라이트 등 표시 정보는 UnoCardData에서 조회)
public readonly struct UnoCard : IEquatable<UnoCard>
{
    public readonly UnoColor Color;
    public readonly UnoKind Kind;
    public readonly int Number;           // 숫자 카드가 아니면 0

    public UnoCard(UnoColor color, UnoKind kind, int number = 0)
    {
        Color = color;
        Kind = kind;
        Number = number;
    }

    #region 네트워크용: 비공개 카드 자리표시자와 압축 코드 (7단계)
    // "무슨 카드인지 모르는" 카드. 상대 손패와 뽑을 카드 더미는 게스트 쪽에서 이 값으로만 채워진다.
    public static readonly UnoCard Hidden = new UnoCard(UnoColor.Wild, UnoKind.Number, -1);
    public bool IsHidden => Kind == UnoKind.Number && Number < 0;

    // 카드 한 장을 16비트 코드로 압축한다 (색 << 8 | 종류 << 4 | 숫자). 비공개 카드는 0xFFFF
    public ushort Encode()
    {
        if (IsHidden) return 0xFFFF;
        return (ushort)(((int)Color << 8) | ((int)Kind << 4) | (Number & 0xF));
    }

    // 압축 코드를 카드로 되돌린다. 범위를 벗어난 값은 비공개 카드로 취급한다 (잘못된 네트워크 값 방어)
    public static UnoCard Decode(ushort code)
    {
        if (code == 0xFFFF) return Hidden;
        int color = (code >> 8) & 0xF, kind = (code >> 4) & 0xF, number = code & 0xF;
        if (color > (int)UnoColor.Wild || kind > (int)UnoKind.WildDraw4 || number > 9) return Hidden;
        return new UnoCard((UnoColor)color, (UnoKind)kind, number);
    }
    #endregion

    // UnoCardData(에셋)에서 런타임 카드 값을 만든다
    public static UnoCard FromData(UnoCardData data)
    {
        return new UnoCard(data.color, data.kind, data.number);
    }

    #region 분류
    public bool IsWild => Kind == UnoKind.Wild || Kind == UnoKind.WildDraw4;
    public bool IsDrawCard => Kind == UnoKind.Draw2 || Kind == UnoKind.WildDraw4;

    // +2는 2, 와일드 +4는 4, 그 외는 0
    public int DrawAmount
    {
        get
        {
            switch (Kind)
            {
                case UnoKind.Draw2: return 2;
                case UnoKind.WildDraw4: return 4;
                default: return 0;
            }
        }
    }
    #endregion

    // UnoCardData.cardId와 같은 규칙의 고유 식별자 (예: Red_3, Blue_Skip, Wild, WildDraw4)
    public string Id
    {
        get
        {
            switch (Kind)
            {
                case UnoKind.Number: return $"{Color}_{Number}";
                case UnoKind.Skip: return $"{Color}_Skip";
                case UnoKind.Draw2: return $"{Color}_Draw2";
                case UnoKind.Reverse: return $"{Color}_Reverse";
                case UnoKind.Wild: return "Wild";
                default: return "WildDraw4";
            }
        }
    }

    #region 동등 비교
    public bool Equals(UnoCard other) => Color == other.Color && Kind == other.Kind && Number == other.Number;
    public override bool Equals(object obj) => obj is UnoCard other && Equals(other);
    public override int GetHashCode() => ((int)Color * 31 + (int)Kind) * 31 + Number;
    public static bool operator ==(UnoCard a, UnoCard b) => a.Equals(b);
    public static bool operator !=(UnoCard a, UnoCard b) => !a.Equals(b);
    public override string ToString() => Id;
    #endregion
}

// 카드 목록 <-> 문자열 (RPC 인자용). 카드 한 장 = 16진수 4자리.
public static class UnoCardCodec
{
    public static string Pack(IReadOnlyList<UnoCard> cards)
    {
        var sb = new StringBuilder(cards.Count * 4);
        for (int i = 0; i < cards.Count; i++) sb.Append(cards[i].Encode().ToString("X4"));
        return sb.ToString();
    }

    public static List<UnoCard> Unpack(string packed)
    {
        var list = new List<UnoCard>();
        if (string.IsNullOrEmpty(packed)) return list;
        for (int i = 0; i + 4 <= packed.Length; i += 4)
        {
            list.Add(ushort.TryParse(packed.Substring(i, 4), System.Globalization.NumberStyles.HexNumber, null, out ushort code)
                ? UnoCard.Decode(code) : UnoCard.Hidden);
        }
        return list;
    }
}
