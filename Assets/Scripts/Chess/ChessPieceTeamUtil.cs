// 백/흑 색상 구분 없이 "백색 기준 타입"을 인자로 받아, 지정된 팀 색상에 맞는 ChessPieceType으로 변환하는 공용 유틸리티.
// 스킬/증강 로직이 팀 0(White) 하나에만 하드코딩되어 있던 부분을 흑팀에도 동일하게 적용하기 위해 도입됨.
public static class ChessPieceTeamUtil
{
    public static ChessPieceType ResolveForTeam(ChessPieceType whiteFormType, int team)
    {
        if (team == 0) return whiteFormType;

        switch (whiteFormType)
        {
            case ChessPieceType.WhitePawn: return ChessPieceType.BlackPawn;
            case ChessPieceType.WhiteKnight: return ChessPieceType.BlackKnight;
            case ChessPieceType.WhiteBishop: return ChessPieceType.BlackBishop;
            case ChessPieceType.WhiteRook: return ChessPieceType.BlackRook;
            case ChessPieceType.WhiteQueen: return ChessPieceType.BlackQueen;
            case ChessPieceType.WhiteKing: return ChessPieceType.BlackKing;
            default: return whiteFormType;
        }
    }
}
