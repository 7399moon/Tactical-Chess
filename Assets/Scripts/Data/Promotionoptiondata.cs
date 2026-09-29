using UnityEngine;

// 폰 프로모션(승급) 선택 UI에서 사용할 승급 옵션 하나의 데이터를 정의하는 ScriptableObject 에셋 클래스.

// 프로모션 선택 시 승급 가능한 기물 종류를 정의
public enum PromotablePieceType
{
    Knight,
    Bishop,
    Rook,
    Queen
}

// 프로모션 선택 UI 카드에 표시될 기본 승급 옵션 정보를 담는 ScriptableObject 클래스
[CreateAssetMenu(fileName = "NewPromotionOption", menuName = "체스 증강/프로모션 옵션 데이터")]
public class PromotionOptionData : ScriptableObject
{
    #region 승급 대상 기물 종류
    [Header("승급 대상 기물 종류")]
    public PromotablePieceType pieceType; // 승급 결과 기물 타깃 (Knight, Bishop, Rook, Queen)
    #endregion

    #region UI 표시 정보
    [Header("UI 표시 정보")]
    public string displayName;            // 카드 표기 이름 (예: 퀸으로 승급)
    [TextArea(2, 5)]
    public string description;            // 카드 설명 텍스트
    public Sprite icon;                   // 카드 아이콘 이미지
    #endregion
}
