using UnityEngine;

// 체스 기물의 종류를 표현하는 열거형 (흑/백 각 6종류 + 없음)
public enum ChessPieceType
{
    None = 0,
    BlackPawn = 1,
    BlackKnight = 2,
    BlackBishop = 3,
    BlackRook = 4,
    BlackQueen = 5,
    BlackKing = 6,
    WhitePawn = 7,
    WhiteKnight = 8,
    WhiteBishop = 9,
    WhiteRook = 10,
    WhiteQueen = 11,
    WhiteKing = 12
}

// 모든 체스 기물(Bishop, King, Knight, Pawn, Queen, Rook)의 공통 베이스 클래스.
// 팀 소속, 보드 좌표, 기물 종류, 이동/스킬 상태 등 기물이 공통으로 가지는 데이터와
// 스킬 쿨타임 감소, 턴 상태 초기화 같은 공용 기능을 제공한다.
public class ChessPieces : MonoBehaviour
{
    #region 기물 기본 정보
    public int team;              // 소속 팀 (0: 흑, 1: 백 등 프로젝트 규칙에 따름)
    public int currentX;          // 현재 보드 상의 X 좌표(열)
    public int currentY;          // 현재 보드 상의 Y 좌표(행)
    public ChessPieceType type;   // 기물 종류
    public bool hasMoved;         // 최초 이동 여부 (캐슬링, 폰 첫 2칸 이동 등 규칙 판정에 사용)
    #endregion

    #region 턴 및 스킬 상태
    public int moveCountThisTurn = 0;              // 이번 턴에 이 기물이 이동한 횟수
    public int currentSkillCooldown = 0;           // 현재 남은 스킬 쿨타임(턴 수)
    public bool IsThreatenedTarget { get; set; }   // 상대에게 위협받고 있는(공격 대상이 되는) 기물인지 여부
    #endregion

    #region 이동 애니메이션용 목표값
    Vector3 desirePosition; // 부드러운 이동 애니메이션의 목표 위치 (현재 미사용 - 추후 이동 연출용)
    Vector3 desireScale;    // 부드러운 이동 애니메이션의 목표 크기 (현재 미사용 - 추후 이동 연출용)
    #endregion

    #region 공개 메서드
    // 증강 효과 등에 의해 스킬 쿨타임을 즉시 감소시킨다.
    public void ReduceSkillCooldown(int amount)
    {
        currentSkillCooldown = Mathf.Max(0, currentSkillCooldown - amount);
        Debug.Log($"[{gameObject.name}] 스킬 쿨타임 {amount}턴 감소! (남은 쿨타임: {currentSkillCooldown})");
    }

    // 턴이 넘어갈 때 이번 턴 이동 횟수를 초기화한다.
    public void ResetTurnState()
    {
        moveCountThisTurn = 0;
    }
    #endregion
}
