using UnityEngine;

// 우노 카드 "종류" 하나(예: 빨강 3, 노랑 스킵, 와일드 +4)의 데이터를 정의하는 ScriptableObject 에셋 클래스.
// AugmentData와 같은 방식으로 Assets/Data/Uno 폴더에 에셋으로 저장하고, UnoCardDatabase가 한곳에 모은다.
// 같은 종류의 카드가 덱에 몇 장 들어가는지는 copies로 정한다 (덱 구성이 코드가 아닌 데이터로 관리된다).

#region 열거형 정의
// 우노 카드의 색상. Wild는 색이 없는 검정 와일드 카드를 뜻하며,
// UnoMatch.CurrentColor에서는 "아무 색이나 낼 수 있는 상태"(시작 카드가 와일드일 때)를 뜻하기도 한다.
public enum UnoColor
{
    Red,
    Yellow,
    Green,
    Blue,
    Wild
}

// 우노 카드의 종류 (효과 단위)
public enum UnoKind
{
    Number,      // 숫자 카드 (0~8). 1~4는 이동, 5~8은 부활, 0은 손패 리셋
    Skip,
    Draw2,
    Reverse,     // 손패 교환
    Wild,
    WildDraw4
}
#endregion

// 개별 우노 카드 종류의 데이터를 정의하는 ScriptableObject 에셋 클래스
[CreateAssetMenu(fileName = "NewUnoCard", menuName = "체스 증강/우노 카드 데이터")]
public class UnoCardData : ScriptableObject
{
    #region 식별용 정보
    [Header("식별용 정보")]
    public string cardId;                 // 시스템 내부 고유 식별자 (예: Red_3, Blue_Skip, Wild, WildDraw4). UnoCard.Id와 같아야 한다
    #endregion

    #region 카드 종류
    [Header("카드 종류")]
    public UnoColor color;                // 카드 색상 (와일드 계열은 Wild)
    public UnoKind kind;                  // 카드 종류
    public int number;                    // 숫자 카드의 숫자 (숫자 카드가 아니면 0)
    [Min(1)]
    public int copies = 2;                // 덱에 들어가는 장수
    #endregion

    #region UI 표시 정보
    [Header("UI 표시 정보")]
    public string displayName;            // 카드 표기 이름
    [TextArea(2, 5)]
    public string description;            // 카드 설명 텍스트
    public Sprite sprite;                 // 카드 스프라이트 (UNO Cards 시트)
    [Tooltip("와일드 계열 전용: 색상을 고른 뒤 표시할 스프라이트 (Red, Yellow, Green, Blue 순서)")]
    public Sprite[] chosenColorSprites;
    #endregion

    #region 조회
    // 와일드 계열 카드에서 색상이 선택된 뒤에 보여줄 스프라이트를 반환. 색상 변형이 없으면 기본 스프라이트를 반환한다.
    public Sprite GetSprite(UnoColor chosenColor)
    {
        if (chosenColor == UnoColor.Wild || chosenColorSprites == null)
            return sprite;

        int index = (int)chosenColor;
        if (index < 0 || index >= chosenColorSprites.Length || chosenColorSprites[index] == null)
            return sprite;

        return chosenColorSprites[index];
    }
    #endregion
}
