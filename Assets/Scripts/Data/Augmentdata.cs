using UnityEngine;

// 개별 체스 증강 카드 하나의 데이터(등급, 태그, 표시 정보)를 정의하는 ScriptableObject 에셋 클래스.
// 기획자가 유니티 에디터에서 "체스 증강/증강 데이터" 메뉴로 직접 에셋을 생성해 값을 채운다.

#region 열거형 정의
// 증강 카드의 희귀도 등급을 정의
public enum AugmentRarity
{
    Normal,
    Rare,
    Unique,
    Legendary
}

// 증강 스킬이 적용되는 체스 기물 태그 (비트 플래그로 여러 기물을 동시에 지정 가능)
[System.Flags]
public enum PieceTag
{
    None = 0,
    Pawn = 1 << 0,
    Knight = 1 << 1,
    Bishop = 1 << 2,
    Rook = 1 << 3,
    Queen = 1 << 4,
    King = 1 << 5,
    All = Pawn | Knight | Bishop | Rook | Queen | King
}
#endregion

// 개별 체스 증강 카드의 데이터 및 옵션을 정의하는 ScriptableObject 에셋 클래스
[CreateAssetMenu(fileName = "NewAugment", menuName = "체스 증강/증강 데이터")]
public class AugmentData : ScriptableObject
{
    #region 식별용 정보
    [Header("식별용 정보")]
    public string augmentId;              // 시스템 내부 고유 식별자
    #endregion

    #region UI 표시 정보
    [Header("UI 표시 정보")]
    public string displayName;            // 카드 표기 이름
    [TextArea(2, 5)]
    public string description;            // 카드 설명 텍스트
    public Sprite icon;                   // 카드 아이콘 이미지
    #endregion

    #region 분류 및 태그
    [Header("분류 및 태그")]
    public AugmentRarity rarity;          // 증강 등급 (Normal, Rare 등)
    public PieceTag relatedPieces;        // 연관 기물 비트마스크
    #endregion
}
