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
        FillAvailableMoves(board, piece, moves, enPassantTarget);
        return moves;
    }

    // 지정한 리스트(moves)에 가상 이동을 채운다. 호출 측이 리스트를 재사용할 수 있게 분리한 내부 구현.
    private static void FillAvailableMoves(ChessPieces[,] board, ChessPieces piece, List<Vector2Int> moves, Vector2Int? enPassantTarget)
    {
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
    }

    // 자신의 킹이 체크되는 자살 수를 배제한 최종 합법 수(Legal Moves)를 반환
    // 최적화: 가상 이동 리스트 하나를 그대로 재사용해 불법 수를 제자리에서 제거한다(리스트 2~3개 할당 -> 1개).
    public static List<Vector2Int> GetLegalMoves(ChessPieces[,] board, ChessPieces piece, Vector2Int? enPassantTarget = null)
    {
        var moves = GetAvailableMoves(board, piece, enPassantTarget);

        bool movingKing = IsKing(piece);
        Vector2Int ownKingPos = movingKing ? default : FindKing(board, piece.team);

        int write = 0;
        for (int read = 0; read < moves.Count; read++)
        {
            Vector2Int move = moves[read];
            Vector2Int kingPosForCheck = movingKing ? move : ownKingPos;

            if (WouldLeaveKingInCheck(board, piece, move, enPassantTarget, kingPosForCheck))
                continue;

            moves[write++] = move;
        }
        moves.RemoveRange(write, moves.Count - write);

        // 2026-10-05 수정: 앙파상 캡처는 도착 칸이 빈 칸이라 기존 FilterArmisticeMoves의
        // "도착 칸에 적이 있는 수만 제거" 판정을 피해가 휴전 협정 중에도 캡처가 가능했다.
        // enPassantTarget을 함께 넘겨 그 좌표로 가는 수도 캡처 수로 식별해 제거한다.
        int armisticeTurns = GameManager.Instance != null ? GameManager.Instance.armisticeTurns : 0;
        if (armisticeTurns > 0)
            RemoveArmisticeCapturesInPlace(board, piece, moves, enPassantTarget);

        return moves;
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

                if (HasAnyLegalMove(board, piece, enPassantTarget))
                    return true;
            }
        }

        return false;
    }

    // 재사용 버퍼 (HasAnyLegalMove 전용). 유니티 메인 스레드에서만 호출되며 재진입하지 않는다.
    private static readonly List<Vector2Int> legalScanBuffer = new List<Vector2Int>(32);

    // 한 기물에 합법 수가 "하나라도" 있는지 검사한다.
    // 최적화: 전체 합법 수 목록을 만들지 않고, 첫 합법 수를 찾는 즉시 true로 조기 종료한다(리스트 할당 없음).
    private static bool HasAnyLegalMove(ChessPieces[,] board, ChessPieces piece, Vector2Int? enPassantTarget)
    {
        legalScanBuffer.Clear();
        FillAvailableMoves(board, piece, legalScanBuffer, enPassantTarget);

        bool movingKing = IsKing(piece);
        Vector2Int ownKingPos = movingKing ? default : FindKing(board, piece.team);
        int armisticeTurns = GameManager.Instance != null ? GameManager.Instance.armisticeTurns : 0;

        for (int i = 0; i < legalScanBuffer.Count; i++)
        {
            Vector2Int move = legalScanBuffer[i];

            // 휴전 협정 중 캡처 수는 합법 수에서 제외 (값싼 검사를 먼저 수행)
            if (armisticeTurns > 0 && IsArmisticeCapture(board, piece, move, enPassantTarget))
                continue;

            Vector2Int kingPosForCheck = movingKing ? move : ownKingPos;
            if (!WouldLeaveKingInCheck(board, piece, move, enPassantTarget, kingPosForCheck))
                return true;
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
