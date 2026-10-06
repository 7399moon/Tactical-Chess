using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ChessRules의 체크 판정 및 타일 공격 범위 계산을 전담하는 partial 파일.
public static partial class ChessRules
{
    #region 킹 위치 및 체크 판정
    // 지정된 팀의 킹 위치를 찾아 체크 여부를 확인
    public static bool IsKingInCheck(ChessPieces[,] board, int team) =>
        IsKingInCheck(board, team, FindKing(board, team));
    // 특정 좌표에 위치한 킹이 적 팀으로부터 체크 상태인지 검사
    public static bool IsKingInCheck(ChessPieces[,] board, int team, Vector2Int kingPos)
    {
        if (kingPos.x < 0) return false;
        return IsSquareAttacked(board, kingPos.x, kingPos.y, 1 - team);
    }

    // 보드 전체를 탐색하여 해당 팀 킹의 위치를 반환
    public static Vector2Int FindKing(ChessPieces[,] board, int team)
    {
        for (int x = 0; x < BoardSize; x++)
        {
            for (int y = 0; y < BoardSize; y++)
            {
                ChessPieces piece = board[x, y];
                if (piece != null && piece.team == team && IsKing(piece))
                    return new Vector2Int(x, y);
            }
        }
        return new Vector2Int(-1, -1);
    }
    #endregion

    #region 공격 범위 계산
    // 지정한 좌표 (x, y)를 공격할 수 있는 상대 팀 기물들의 목록을 반환
    public static List<ChessPieces> GetAttackers(ChessPieces[,] board, int x, int y, int byTeam)
    {
        var attackers = new List<ChessPieces>(4);

        for (int bx = 0; bx < BoardSize; bx++)
        {
            for (int by = 0; by < BoardSize; by++)
            {
                ChessPieces attacker = board[bx, by];
                if (attacker != null && attacker.team == byTeam && IsAttackingSquare(board, attacker, x, y))
                    attackers.Add(attacker);
            }
        }

        return attackers;
    }

    // 특정 좌표 (x, y)가 지정된 팀 기물에 의해 공격받고 있는지 검사
    public static bool IsSquareAttacked(ChessPieces[,] board, int x, int y, int byTeam)
    {
        if (!IsInBoard(x, y)) return false;

        for (int bx = 0; bx < BoardSize; bx++)
        {
            for (int by = 0; by < BoardSize; by++)
            {
                ChessPieces attacker = board[bx, by];
                if (attacker != null && attacker.team == byTeam && IsAttackingSquare(board, attacker, x, y))
                    return true;
            }
        }

        return false;
    }

    // 특정 기물이 지정된 좌표 (x, y)를 공격하고 있는지 타입별 패턴으로 확인
    private static bool IsAttackingSquare(ChessPieces[,] board, ChessPieces attacker, int x, int y)
    {
        switch (attacker.type)
        {
            case ChessPieceType.WhitePawn:
            case ChessPieceType.BlackPawn:
                int dir = attacker.team == 0 ? 1 : -1;

                // 기본 대각선 1칸 공격 판정
                bool diagonalAttack = (y == attacker.currentY + dir) &&
                                     (x == attacker.currentX - 1 || x == attacker.currentX + 1);

                // [노말 15] 정면 돌격: 정면 1칸 공격 판정
                bool frontalAttack = (x == attacker.currentX && y == attacker.currentY + dir) &&
                                    HasAugment(attacker.team, AugPawnFrontalCapture);

                return diagonalAttack || frontalAttack;

            case ChessPieceType.WhiteKnight:
            case ChessPieceType.BlackKnight:
                // 나이트의 L자 이동 패턴 (dx, dy가 각각 1, 2)
                int dxK = Mathf.Abs(x - attacker.currentX);
                int dyK = Mathf.Abs(y - attacker.currentY);

                // 기본 L자 공격 판정
                bool standardAttack = (dxK == 1 && dyK == 2) || (dxK == 2 && dyK == 1);

                // [노말 8] 박차 가하기: 상하좌우 2칸 직선 공격 판정
                bool chargeAttack = ((dxK == 0 && dyK == 2) || (dxK == 2 && dyK == 0)) &&
                                    HasAugment(attacker.team, AugKnightStraightCharge);

                return standardAttack || chargeAttack;

            case ChessPieceType.WhiteKing:
            case ChessPieceType.BlackKing:
                // 킹의 인접 8칸 범위
                int dx = Mathf.Abs(x - attacker.currentX);
                int dy = Mathf.Abs(y - attacker.currentY);
                return dx <= 1 && dy <= 1 && (dx != 0 || dy != 0);

            case ChessPieceType.WhiteBishop:
            case ChessPieceType.BlackBishop:
                return IsSlidingAttack(board, attacker, x, y, diagonal: true, straight: false);

            case ChessPieceType.WhiteRook:
            case ChessPieceType.BlackRook:
                return IsSlidingAttack(board, attacker, x, y, diagonal: false, straight: true);

            case ChessPieceType.WhiteQueen:
            case ChessPieceType.BlackQueen:
                return IsSlidingAttack(board, attacker, x, y, diagonal: true, straight: true);
        }

        return false;
    }

    // 직선/대각선 슬라이딩 기물(룩, 비숍, 퀸)이 경로 차단 없이 타깃 칸을 공격하는지 검사
    private static bool IsSlidingAttack(ChessPieces[,] board, ChessPieces attacker, int x, int y, bool diagonal, bool straight)
    {
        int dx = x - attacker.currentX;
        int dy = y - attacker.currentY;

        bool isDiagonal = dx != 0 && Mathf.Abs(dx) == Mathf.Abs(dy);
        bool isStraight = (dx == 0) != (dy == 0);

        if ((!diagonal || !isDiagonal) && (!straight || !isStraight))
            return false;

        int stepX = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
        int stepY = dy == 0 ? 0 : (dy > 0 ? 1 : -1);

        int currentX = attacker.currentX + stepX;
        int currentY = attacker.currentY + stepY;

        // 목표 칸 도달 전 경로상의 장애물 기물 유무 검사
        while (currentX != x || currentY != y)
        {
            if (!IsInBoard(currentX, currentY) || board[currentX, currentY] != null)
                return false;

            currentX += stepX;
            currentY += stepY;
        }

        return true;
    }
    #endregion
}