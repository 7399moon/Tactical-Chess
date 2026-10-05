using System.Collections.Generic;
using UnityEngine;

// ChessRules의 기물별 기본 이동 및 증강(Augment) 적용 이동 로직을 전담하는 partial 파일.
public static partial class ChessRules
{
    private const int BoardSize = 8;

    #region 방향 벡터 상수
    // 룩/퀸의 직선 이동 방향
    private static readonly Vector2Int[] StraightDirections =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    // 비숍/퀸의 대각선 이동 방향
    private static readonly Vector2Int[] DiagonalDirections =
    {
        new Vector2Int(1, 1), new Vector2Int(1, -1),
        new Vector2Int(-1, 1), new Vector2Int(-1, -1)
    };

    // 나이트의 L자 이동 8방향
    private static readonly Vector2Int[] KnightOffsets =
    {
        new Vector2Int(1, 2), new Vector2Int(2, 1),
        new Vector2Int(2, -1), new Vector2Int(1, -2),
        new Vector2Int(-1, -2), new Vector2Int(-2, -1),
        new Vector2Int(-2, 1), new Vector2Int(-1, 2)
    };

    // 킹 주변 8방향 (긴급 교체 대상 탐색용으로 직선+대각선 합쳐서 따로 보관)
    private static readonly Vector2Int[] AdjacentDirections =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
    };
    #endregion

    #region 공용 헬퍼 메서드
    // 좌표가 8x8 보드 범위 안에 있는지 검사
    private static bool IsInBoard(int x, int y) => x >= 0 && x < BoardSize && y >= 0 && y < BoardSize;

    private static bool IsKing(ChessPieces piece) =>
        piece != null && (piece.type == ChessPieceType.WhiteKing || piece.type == ChessPieceType.BlackKing);

    private static bool IsPawn(ChessPieces piece) =>
        piece != null && (piece.type == ChessPieceType.WhitePawn || piece.type == ChessPieceType.BlackPawn);

    private static bool IsRook(ChessPieces piece) =>
        piece != null && (piece.type == ChessPieceType.WhiteRook || piece.type == ChessPieceType.BlackRook);

    // 해당 팀이 특정한 증강(Augment)을 보유하고 있는지 검사하는 헬퍼 메서드
    private static bool HasAugment(int team, string augmentId) =>
        AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(team, augmentId);
    #endregion

    #region 슬라이딩 이동 생성 (비숍/룩/퀸)
    // 비숍/룩/퀸 공통: 직선 또는 대각선 방향으로 가상 슬라이딩 이동 경로 생성
    private static void GetSlidingMoves(ChessPieces[,] board, ChessPieces piece, List<Vector2Int> moves, bool diagonal, bool straight)
    {
        // 노말4/5: 관통 활보(비숍)·관통 돌격(룩) - 이동 경로의 아군 기물 1개까지 통과 가능
        bool allowPassThrough = false;
        if (piece.type == ChessPieceType.WhiteBishop || piece.type == ChessPieceType.BlackBishop)
            allowPassThrough = HasAugment(piece.team, "bishop_pass_through");
        else if (piece.type == ChessPieceType.WhiteRook || piece.type == ChessPieceType.BlackRook)
            allowPassThrough = HasAugment(piece.team, "rook_pass_through");

        if (straight) AddSlidingDirections(board, piece, moves, StraightDirections, allowPassThrough);
        if (diagonal) AddSlidingDirections(board, piece, moves, DiagonalDirections, allowPassThrough);
    }

    // 지정된 방향으로 한 칸씩 탐색하며 빈 칸 이동 및 적 기물 캡처 경로 추가
    private static void AddSlidingDirections(ChessPieces[,] board, ChessPieces piece, List<Vector2Int> moves, Vector2Int[] directions, bool allowPassThrough = false)
    {
        foreach (Vector2Int dir in directions)
        {
            int x = piece.currentX + dir.x;
            int y = piece.currentY + dir.y;
            bool hasPassedAlly = false; // 이 방향에서 이미 아군 하나를 통과했는지

            while (IsInBoard(x, y))
            {
                ChessPieces target = board[x, y];
                if (target == null)
                {
                    moves.Add(new Vector2Int(x, y));
                }
                else if (target.team != piece.team)
                {
                    moves.Add(new Vector2Int(x, y));    // 적 기물은 캡처 가능한 마지막 칸으로 추가
                    break;
                }
                else
                {
                    if (allowPassThrough && !hasPassedAlly)
                        hasPassedAlly = true;           // 착지는 못 하지만(moves에 추가 안 함) 통과는 하고 계속 진행
                    else
                        break;                          // 증강이 없거나 이미 한 번 통과했으면 종료
                }

                x += dir.x;
                y += dir.y;
            }
        }
    }
    #endregion

    #region 나이트 / 킹 이동 생성
    // 나이트의 8가지 L자 이동 경로 탐색
    private static void GetKnightMoves(ChessPieces[,] board, ChessPieces piece, List<Vector2Int> moves)
    {
        foreach (Vector2Int offset in KnightOffsets)
        {
            int x = piece.currentX + offset.x;
            int y = piece.currentY + offset.y;

            if (IsInBoard(x, y))
            {
                ChessPieces target = board[x, y];
                if (target == null || target.team != piece.team)
                    moves.Add(new Vector2Int(x, y));
            }
        }

        // 노말8 [박차 가하기]: 상하좌우 2칸 직선 이동 추가 (4방향)
        if (HasAugment(piece.team, "knight_straight_charge"))
        {
            Vector2Int[] straightTwoSteps =
            {
                new Vector2Int(0, 2), new Vector2Int(0, -2),
                new Vector2Int(2, 0), new Vector2Int(-2, 0)
            };

            foreach (Vector2Int offset in straightTwoSteps)
            {
                int targetX = piece.currentX + offset.x;
                int targetY = piece.currentY + offset.y;

                if (IsInBoard(targetX, targetY))
                {
                    ChessPieces target = board[targetX, targetY];
                    if (target == null || target.team != piece.team)
                        moves.Add(new Vector2Int(targetX, targetY));
                }
            }
        }
    }

    // 킹의 8방향 인접 이동 및 캐슬링 가능 경로 탐색
    private static void GetKingMoves(ChessPieces[,] board, ChessPieces piece, List<Vector2Int> moves)
    {
        const int maxDistance = 1;

        AddKingDirectionalMoves(board, piece, moves, StraightDirections, maxDistance);
        AddKingDirectionalMoves(board, piece, moves, DiagonalDirections, maxDistance);

        AddCastlingMoves(board, piece, moves);

        // 노말12 [긴급 교체]: 체크 상태일 때 1칸 이내 아군 기물과 위치 교환 가능
        bool hasSwapUsesLeft = AugmentManager.Instance != null && AugmentManager.Instance.GetEmergencySwapUsesLeft(piece.team) > 0;
        if (HasAugment(piece.team, "king_emergency_swap") && hasSwapUsesLeft && IsKingInCheck(board, piece.team))
        {
            foreach (Vector2Int dir in AdjacentDirections)
            {
                int targetX = piece.currentX + dir.x;
                int targetY = piece.currentY + dir.y;

                if (IsInBoard(targetX, targetY))
                {
                    ChessPieces targetPiece = board[targetX, targetY];
                    // 인접 칸에 아군 기물이 있는 경우 이동 후보로 추가 (위치 교환용)
                    // 2026-10-05 수정: 교환 대상 아군이 위협(이동 불가) 상태이면 그 기물은 어떤 방식으로도
                    // 옮겨질 수 없어야 하므로, 긴급 교체의 교환 후보에서도 제외한다.
                    bool targetImmobilized = PieceSkillManager.Instance != null
                        && PieceSkillManager.Instance.IsImmobilized(targetPiece);
                    if (targetPiece != null && targetPiece.team == piece.team && !targetImmobilized)
                    {
                        moves.Add(new Vector2Int(targetX, targetY));
                    }
                }
            }
        }
    }

    // 킹 지정 방향 및 최대 수치(maxDistance)까지 이동 탐색
    private static void AddKingDirectionalMoves(ChessPieces[,] board, ChessPieces piece, List<Vector2Int> moves, Vector2Int[] directions, int maxDistance)
    {
        foreach (Vector2Int dir in directions)
        {
            for (int step = 1; step <= maxDistance; step++)
            {
                int x = piece.currentX + dir.x * step;
                int y = piece.currentY + dir.y * step;

                if (!IsInBoard(x, y)) break;

                ChessPieces target = board[x, y];
                if (target == null)
                {
                    moves.Add(new Vector2Int(x, y));
                }
                else
                {
                    if (target.team != piece.team)
                        moves.Add(new Vector2Int(x, y));
                    break;
                }
            }
        }
    }

    // 캐슬링 조건 검사 및 가능 후보 추가
    private static void AddCastlingMoves(ChessPieces[,] board, ChessPieces king, List<Vector2Int> moves)
    {
        if (king.hasMoved || !IsKing(king)) return;

        int homeRow = king.team == 0 ? 0 : 7;
        if (king.currentX != 4 || king.currentY != homeRow) return;

        int opponentTeam = 1 - king.team;
        if (IsSquareAttacked(board, king.currentX, homeRow, opponentTeam)) return;

        TryAddCastling(board, king, homeRow, opponentTeam, 7, 1, moves);  // 킹사이드
        TryAddCastling(board, king, homeRow, opponentTeam, 0, -1, moves); // 퀸사이드
    }

    // 킹사이드/퀸사이드 세부 조건(경로 장애물, 통과 타일 공격 판정 등) 검사
    private static void TryAddCastling(ChessPieces[,] board, ChessPieces king, int y, int opponentTeam, int rookX, int direction, List<Vector2Int> moves)
    {
        ChessPieces rook = board[rookX, y];
        if (rook == null || rook.hasMoved || !IsRook(rook) || rook.team != king.team) return;

        for (int x = king.currentX + direction; x != rookX; x += direction)
            if (board[x, y] != null) return;

        int passX = king.currentX + direction;
        int landX = king.currentX + direction * 2;

        if (IsSquareAttacked(board, passX, y, opponentTeam) || IsSquareAttacked(board, landX, y, opponentTeam))
            return;

        moves.Add(new Vector2Int(landX, y));
    }
    #endregion

    #region 폰 이동 생성
    // 폰의 기본 전진/대각선 캡처/앙파상 및 증강 스킬에 따른 특수 이동 경로 탐색
    private static void GetPawnMoves(ChessPieces[,] board, ChessPieces piece, List<Vector2Int> moves, Vector2Int? enPassantTarget)
    {
        int direction = piece.team == 0 ? 1 : -1;
        int startRow = piece.team == 0 ? 1 : 6;

        int x = piece.currentX;
        int y = piece.currentY + direction;

        // 1. 일반 전진 및 정면 돌격 검사
        if (IsInBoard(x, y))
        {
            ChessPieces frontPiece = board[x, y];

            if (frontPiece == null)
            {
                moves.Add(new Vector2Int(x, y));

                bool atStartRow = piece.currentY == startRow;
                int doubleY = piece.currentY + direction * 2;

                if (atStartRow && IsInBoard(x, doubleY) && board[x, doubleY] == null)
                {
                    moves.Add(new Vector2Int(x, doubleY));

                    // 노말2 [삼보 전진]: 첫 이동시 세칸 이동 가능
                    if (HasAugment(piece.team, "pawn_triple_step"))
                    {
                        int tripleY = piece.currentY + direction * 3;
                        if (IsInBoard(x, tripleY) && board[x, tripleY] == null)
                            moves.Add(new Vector2Int(x, tripleY));
                    }
                }
            }
            // 노말15 [정면 돌격]: 정면 적 캡처 허용
            else if (frontPiece.team != piece.team && HasAugment(piece.team, "pawn_frontal_capture"))
            {
                moves.Add(new Vector2Int(x, y));
            }
        }

        // 2. 대각선 캡처 및 앙파상 검사
        int captureY = piece.currentY + direction;

        for (int i = 0; i < 2; i++)
        {
            int captureX = piece.currentX + (i == 0 ? -1 : 1);
            if (!IsInBoard(captureX, captureY)) continue;

            ChessPieces target = board[captureX, captureY];
            if (target != null && target.team != piece.team)
            {
                moves.Add(new Vector2Int(captureX, captureY));
            }
            else if (target == null && enPassantTarget.HasValue && enPassantTarget.Value.x == captureX && enPassantTarget.Value.y == captureY)
            {
                moves.Add(new Vector2Int(captureX, captureY));
            }
        }

        // 3. 노말1 [측면 돌파]: 좌우 1칸 빈 공간 이동 허용
        if (HasAugment(piece.team, "pawn_side_step"))
        {
            for (int i = 0; i < 2; i++)
            {
                int sideX = piece.currentX + (i == 0 ? -1 : 1);
                int sideY = piece.currentY;

                if (IsInBoard(sideX, sideY) && board[sideX, sideY] == null)
                    moves.Add(new Vector2Int(sideX, sideY));
            }
        }

        // 4. 노말7 [폰의 각성]: 특정 랭크 도달 시 비어있는 대각선 칸 전진 이동 허용
        if (HasAugment(piece.team, "pawn_awakening"))
        {
            int awakenRank = piece.team == 0 ? 5 : 2;

            if (piece.currentY == awakenRank)
            {
                for (int i = 0; i < 2; i++)
                {
                    int diagX = piece.currentX + (i == 0 ? -1 : 1);
                    int diagY = piece.currentY + direction;

                    if (IsInBoard(diagX, diagY) && board[diagX, diagY] == null)
                        moves.Add(new Vector2Int(diagX, diagY));
                }
            }
        }
    }
    #endregion

    #region 휴전 협정 로직
    // 노말14 [휴전 협정]: 6턴간 서로 캡처 불가능
    // 2026-10-05 수정: 앙파상 캡처는 도착 칸(move) 자체가 비어 있어(실제로 잡히는 폰은 옆 칸에 있음)
    // 기존의 "도착 칸에 적이 있으면 제거" 판정을 통과해버렸다. enPassantTarget과 일치하는 수는
    // 폰의 캡처 수로 간주해 별도로도 제거한다.
    public static List<Vector2Int> FilterArmisticeMoves(ChessPieces[,] board, ChessPieces piece, List<Vector2Int> rawMoves, int armisticeTurns, Vector2Int? enPassantTarget = null)
    {
        if (armisticeTurns <= 0)
            return rawMoves;

        List<Vector2Int> validMoves = new List<Vector2Int>();

        foreach (Vector2Int move in rawMoves)
        {
            ChessPieces target = board[move.x, move.y];
            bool isEnPassantCapture = enPassantTarget.HasValue && move == enPassantTarget.Value && IsPawn(piece);

            // 이동 타깃 칸이 빈 칸이고, 앙파상 캡처도 아닌 경우에만 승인
            if (target == null && !isEnPassantCapture)
            {
                validMoves.Add(move);
            }
        }

        return validMoves;
    }
    #endregion
}