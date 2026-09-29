using System.Collections.Generic;
using UnityEngine;

// 체스 규칙(ChessRules)의 메인 공개 API 및 체크/합법 수(Legal Move) 검증을 담당하는 partial 파일.
// 파일이 길어져 기능별로 partial class로 분할되어 있다.
//  - ChessRules.cs          : 공개 이동 API(GetAvailableMoves/GetLegalMoves 등) 및 체크 시뮬레이션
//  - ChessRules.Attack.cs   : 체크 판정 및 특정 칸에 대한 공격 범위 계산
//  - ChessRules.Movement.cs : 기물별 기본 이동 생성 로직 및 증강 적용 이동, 공용 상수/헬퍼
public static partial class ChessRules
{
    #region 공개 이동 API
    // 체크 상태를 고려하지 않은 가상 이동 좌표(Pseudo-Legal Moves)를 반환
    public static List<Vector2Int> GetAvailableMoves(ChessPieces[,] board, ChessPieces piece, Vector2Int? enPassantTarget = null)
    {
        var moves = new List<Vector2Int>(16);

        switch (piece.type)
        {
            case ChessPieceType.WhitePawn:
            case ChessPieceType.BlackPawn:
                GetPawnMoves(board, piece, moves, enPassantTarget);
                break;

            case ChessPieceType.WhiteKnight:
            case ChessPieceType.BlackKnight:
                GetKnightMoves(board, piece, moves);
                break;

            case ChessPieceType.WhiteBishop:
            case ChessPieceType.BlackBishop:
                GetSlidingMoves(board, piece, moves, diagonal: true, straight: false);
                break;

            case ChessPieceType.WhiteRook:
            case ChessPieceType.BlackRook:
                GetSlidingMoves(board, piece, moves, diagonal: false, straight: true);
                break;

            case ChessPieceType.WhiteQueen:
            case ChessPieceType.BlackQueen:
                GetSlidingMoves(board, piece, moves, diagonal: true, straight: true);
                break;

            case ChessPieceType.WhiteKing:
            case ChessPieceType.BlackKing:
                GetKingMoves(board, piece, moves);
                break;
        }

        return moves;
    }

    // 자신의 킹이 체크되는 자살 수를 배제한 최종 합법 수(Legal Moves)를 반환
    public static List<Vector2Int> GetLegalMoves(ChessPieces[,] board, ChessPieces piece, Vector2Int? enPassantTarget = null)
    {
        var pseudoLegal = GetAvailableMoves(board, piece, enPassantTarget);
        var legal = new List<Vector2Int>(pseudoLegal.Count);

        bool movingKing = IsKing(piece);
        Vector2Int ownKingPos = movingKing ? default : FindKing(board, piece.team);

        foreach (var move in pseudoLegal)
        {
            Vector2Int kingPosForCheck = movingKing ? move : ownKingPos;

            if (!WouldLeaveKingInCheck(board, piece, move, enPassantTarget, kingPosForCheck))
                legal.Add(move);
        }

        int armisticeTurns = GameManager.Instance != null ? GameManager.Instance.armisticeTurns : 0;
        legal = FilterArmisticeMoves(board, piece, legal, armisticeTurns);

        return legal;
    }

    // 지정된 팀에 합법 수가 남아있는지 검사 (체크메이트/스테일메이트 판별용)
    public static bool HasAnyLegalMoves(ChessPieces[,] board, int team, Vector2Int? enPassantTarget = null)
    {
        for (int x = 0; x < BoardSize; x++)
        {
            for (int y = 0; y < BoardSize; y++)
            {
                ChessPieces piece = board[x, y];
                if (piece == null || piece.team != team)
                    continue;

                if (GetLegalMoves(board, piece, enPassantTarget).Count > 0)
                    return true;
            }
        }

        return false;
    }
    #endregion

    #region 체크 시뮬레이션 (내부)
    // 가상의 이동을 보드에 적용하여 해당 수 수행 시 자신의 킹이 체크 상태에 노출되는지 시뮬레이션
    private static bool WouldLeaveKingInCheck(
        ChessPieces[,] board,
        ChessPieces piece,
        Vector2Int to,
        Vector2Int? enPassantTarget,
        Vector2Int ownKingPos)
    {
        int fromX = piece.currentX;
        int fromY = piece.currentY;

        if (!IsInBoard(to.x, to.y)) return true;

        ChessPieces captured = board[to.x, to.y];
        bool isPawn = IsPawn(piece);
        bool isKing = IsKing(piece);

        bool isEnPassant = isPawn && to.x != fromX && captured == null &&
                           enPassantTarget.HasValue && enPassantTarget.Value == to;

        ChessPieces capturedEnPassant = null;
        int capturedEnPassantX = to.x;
        int capturedEnPassantY = fromY;

        // 1. 앙파상 판정 및 유효성 검사
        if (isEnPassant)
        {
            capturedEnPassant = board[capturedEnPassantX, capturedEnPassantY];
            if (capturedEnPassant == null || capturedEnPassant.team == piece.team || !IsPawn(capturedEnPassant))
                return true;
        }

        bool isCastling = isKing && Mathf.Abs(to.x - fromX) == 2;
        ChessPieces rook = null;
        int rookFromX = -1;
        int rookToX = -1;

        // 2. 캐슬링 판정 및 룩 상태 검사
        if (isCastling)
        {
            int direction = to.x > fromX ? 1 : -1;
            rookFromX = direction > 0 ? 7 : 0;
            rookToX = to.x - direction;
            rook = board[rookFromX, fromY];

            if (rook == null || rook.team != piece.team || !IsRook(rook))
                return true;
        }

        // 3. 보드 배열 내 가상 이동 처리
        board[fromX, fromY] = null;
        board[to.x, to.y] = piece;
        piece.currentX = to.x;
        piece.currentY = to.y;

        if (capturedEnPassant != null)
            board[capturedEnPassantX, capturedEnPassantY] = null;

        if (rook != null)
        {
            board[rookFromX, fromY] = null;
            board[rookToX, fromY] = rook;
            rook.currentX = rookToX;
            rook.currentY = fromY;
        }

        // 4. 가상 이동 후 체크 여부 확인
        Vector2Int kingPos = isKing ? to : ownKingPos;
        bool inCheck = IsKingInCheck(board, piece.team, kingPos);

        // 5. 보드 상태 원상복구 (Rollback)
        if (rook != null)
        {
            board[rookToX, fromY] = null;
            board[rookFromX, fromY] = rook;
            rook.currentX = rookFromX;
            rook.currentY = fromY;
        }

        if (capturedEnPassant != null)
            board[capturedEnPassantX, capturedEnPassantY] = capturedEnPassant;

        board[fromX, fromY] = piece;
        board[to.x, to.y] = captured;
        piece.currentX = fromX;
        piece.currentY = fromY;

        return inCheck;
    }
    #endregion
}
