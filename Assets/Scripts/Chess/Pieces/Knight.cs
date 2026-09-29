// 나이트(Knight) 기물 클래스.
// 이동 규칙과 공격 판정은 ChessRules에서 기물 종류(ChessPieceType)를 기준으로 처리하므로,
// 이 클래스는 현재 별도의 고유 로직 없이 ChessPieces의 공통 데이터/기능만 사용한다.
// (최적화: 아무 동작도 하지 않는 빈 Start/Update를 제거해 매 프레임 불필요한 콜백 호출을 없앴다.)
public class Knight : ChessPieces
{
}
