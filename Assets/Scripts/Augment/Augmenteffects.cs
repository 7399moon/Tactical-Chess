using System.Collections.Generic;
using UnityEngine;

// 카드를 고르는 즉시 1회만 발동하고 끝나는 증강 효과(부활, 생성 등)를 처리.
public class Augmenteffects : MonoBehaviour
{
    #region 공개 API
    // CardSelectionManager가 증강 카드를 확정할 때 호출한다.
    // augmentId에 매칭되는 즉시발동형 증강 효과를 실행한다.
    public static void ApplyImmediateEffect(string augmentId, int team, ChessBoard board)
    {
        if (board == null) return;

        switch (augmentId)
        {
            // 노말 증강
            case "pawn_revive": // 노말6: 죽은 폰 하나를 2랭크에 부활
                TryRevive(board, team, ChessPieceType.WhitePawn, ChessPieceType.BlackPawn, startingCount: 8, rank: 2);
                break;

            case "knight_conscription": // 노말9: 폰 2개를 희생해 1랭크에 나이트 생성
                TryConscript(board, team, sacrificeCount: 2, ChessPieceType.WhiteKnight, ChessPieceType.BlackKnight, rank: 1);
                break;

            case "bishop_conscription": // 노말10: 폰 2개를 희생해 1랭크에 비숍 생성
                TryConscript(board, team, sacrificeCount: 2, ChessPieceType.WhiteBishop, ChessPieceType.BlackBishop, rank: 1);
                break;

            case "rook_conscription": // 노말11: 폰 3개를 희생해 1랭크에 룩 생성
                TryConscript(board, team, sacrificeCount: 3, ChessPieceType.WhiteRook, ChessPieceType.BlackRook, rank: 1);
                break;

            case "pawn_instant_minor_promote": // 노말13: 이진 선택 후 클릭한 기물을 선택한 기물(나이트/비숍)로 승급
                StartKnightBishopPromotion(team, board);
                break;

            case "temporary_ceasefire": // 노말14: 휴전 협정
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.armisticeTurns = 6;
                    Debug.Log("[Augment] 휴전 협정이 발동되었습니다! 6턴 동안 공격 및 캡처가 금지됩니다.");
                }
                break;

            // 레어 증강
            case "queen_conscription": // 레어10: 폰 5개를 희생해 1랭크에 퀸 생성
                TryConscript(board, team, sacrificeCount: 5, ChessPieceType.WhiteQueen, ChessPieceType.BlackQueen, rank: 1);
                break;

            case "minor_piece_revive": // 레어11: 죽은 비숍 또는 나이트 하나를 1랭크에 부활 (비숍 쪽을 먼저 확인)
                if (!TryRevive(board, team, ChessPieceType.WhiteBishop, ChessPieceType.BlackBishop, startingCount: 2, rank: 1))
                    TryRevive(board, team, ChessPieceType.WhiteKnight, ChessPieceType.BlackKnight, startingCount: 2, rank: 1);
                break;

            case "minor_instant_rook_promote": // 레어12: 전격 승급 (나이트 또는 비숍 -> 룩)
                {
                    ChessPieceType knightSrc = team == 0 ? ChessPieceType.WhiteKnight : ChessPieceType.BlackKnight;
                    ChessPieceType bishopSrc = team == 0 ? ChessPieceType.WhiteBishop : ChessPieceType.BlackBishop;
                    StartSinglePiecePromotion(team, PromotablePieceType.Rook, knightSrc, bishopSrc);
                }
                break;

            // 유니크 증강
            case "rook_instant_queen_promote": // 유니크7: 즉위 (룩 -> 퀸)
                {
                    ChessPieceType rookSrc = team == 0 ? ChessPieceType.WhiteRook : ChessPieceType.BlackRook;
                    StartSinglePiecePromotion(team, PromotablePieceType.Queen, rookSrc);
                }
                break;

            case "rook_revive": // 유니크8: 죽은 룩 하나를 1랭크에 부활
                TryRevive(board, team, ChessPieceType.WhiteRook, ChessPieceType.BlackRook, startingCount: 2, rank: 1);
                break;
            
            // 레전더리 증강
            case "queen_revive": // 레전더리4: 죽은 퀸 하나를 1랭크에 부활
                TryRevive(board, team, ChessPieceType.WhiteQueen, ChessPieceType.BlackQueen, startingCount: 1, rank: 1);
                break;
        }
    }
    #endregion

    #region 부활 / 징집 처리
    // "N랭크" 표기를 실제 보드 y좌표로 변환 (백/흑 진행 방향이 반대라 계산식이 다름)
    private static int RankToY(int rank, int team) => team == 0 ? rank - 1 : 8 - rank;

    // 지정한 랭크의 빈 칸에 하나를 부활
    private static bool TryRevive(ChessBoard board, int team, ChessPieceType whiteType, ChessPieceType blackType, int startingCount, int rank)
    {
        ChessPieceType type = team == 0 ? whiteType : blackType;

        if (CountPieces(board, type, team) >= startingCount)
            return false; // 죽은 개체가 없으면 효과 없음

        int y = RankToY(rank, team);
        if (!TryFindEmptySquareOnRank(board, y, out int emptyX))
            return false; // 놓을 빈 칸이 없으면 효과 없음

        board.PromotePieceAt(emptyX, y, type, team);
        return true;
    }

    // 폰 sacrificeCount개를 희생해서 지정한 랭크의 빈 칸에 새 기물 하나를 생성
    // 2026-10-05 수정: 1랭크가 꽉 차 있으면(폰이 다 전진해 있거나 다른 기물이 막고 있는 경우 등)
    // 예전에는 그대로 "효과 없음"으로 끝나버렸다. 1랭크에 빈 칸이 없으면 2랭크에서 빈 칸을 찾도록
    // 폴백한다 (2랭크마저 없으면 기존과 동일하게 효과 없이 종료 - 폰도 희생되지 않음).
    private static void TryConscript(ChessBoard board, int team, int sacrificeCount, ChessPieceType whiteNewType, ChessPieceType blackNewType, int rank)
    {
        ChessPieceType pawnType = team == 0 ? ChessPieceType.WhitePawn : ChessPieceType.BlackPawn;
        ChessPieceType newType = team == 0 ? whiteNewType : blackNewType;

        int y = RankToY(rank, team);
        if (!TryFindEmptySquareOnRank(board, y, out int emptyX))
        {
            y = RankToY(rank + 1, team);
            if (!TryFindEmptySquareOnRank(board, y, out emptyX))
                return; // 1랭크, 2랭크 모두 빈 칸이 없으면 효과 없음 (폰은 희생시키지 않음)
        }

        if (!TrySacrificePawns(board, team, pawnType, sacrificeCount))
            return; // 폰이 부족하면 효과 없음

        board.PromotePieceAt(emptyX, y, newType, team);
    }

    // 지정한 개수만큼 해당 팀의 폰을 찾아 파괴 (개수가 부족하면 아무것도 파괴하지 않고 false 반환)
    private static bool TrySacrificePawns(ChessBoard board, int team, ChessPieceType pawnType, int count)
    {
        var found = new List<Vector2Int>();

        for (int x = 0; x < ChessBoard.TileCountX && found.Count < count; x++)
        {
            for (int y = 0; y < ChessBoard.TileCountY && found.Count < count; y++)
            {
                ChessPieces p = board.GetPieceAt(x, y);
                if (p != null && p.team == team && p.type == pawnType)
                    found.Add(new Vector2Int(x, y));
            }
        }

        if (found.Count < count)
            return false;

        foreach (Vector2Int pos in found)
        {
            ChessPieces piece = board.GetPieceAt(pos.x, pos.y);
            if (piece == null) continue;

            board.SetPieceAt(pos.x, pos.y, null);
            Object.Destroy(piece.gameObject);
        }

        return true;
    }
    #endregion

    #region 공용 유틸
    // 보드 위에서 지정한 팀/종류의 기물 개수를 센다.
    private static int CountPieces(ChessBoard board, ChessPieceType type, int team)
    {
        int count = 0;

        for (int x = 0; x < ChessBoard.TileCountX; x++)
            for (int y = 0; y < ChessBoard.TileCountY; y++)
            {
                ChessPieces p = board.GetPieceAt(x, y);
                if (p != null && p.team == team && p.type == type)
                    count++;
            }

        return count;
    }

    // 지정한 랭크(y)에서 비어있는 칸의 x좌표를 하나 찾는다. 없으면 false 반환.
    private static bool TryFindEmptySquareOnRank(ChessBoard board, int y, out int emptyX)
    {
        for (int x = 0; x < ChessBoard.TileCountX; x++)
        {
            if (board.GetPieceAt(x, y) == null)
            {
                emptyX = x;
                return true;
            }
        }

        emptyX = -1;
        return false;
    }
    #endregion

    #region 즉시 승급 처리
    // 나이트/비숍 중 하나를 고르는 이진 선택 UI를 띄운 뒤, 선택 완료 시 승급 대상 폰을 클릭하도록 전환한다.
    private static void StartKnightBishopPromotion(int team, ChessBoard board)
    {
        if (CardSelectionManager.Instance == null) return;

        PromotionOptionData optionKnight = CardSelectionManager.Instance.KnightPromotionData;
        PromotionOptionData optionBishop = CardSelectionManager.Instance.BishopPromotionData;

        // 1. 이진 UI 표시 (team: 이 선택 결과가 귀속될 팀. 네트워크 대전 중에는 이 팀의 클라이언트만 클릭 가능)
        CardSelectionManager.Instance.ShowPieceChoice(team, optionKnight, optionBishop, (chosenType) =>
        {
            ChessPieceType targetChessType = chosenType == PromotablePieceType.Knight
                ? (team == 0 ? ChessPieceType.WhiteKnight : ChessPieceType.BlackKnight)
                : (team == 0 ? ChessPieceType.WhiteBishop : ChessPieceType.BlackBishop);

            ChessPieceType validSourcePawn = team == 0 ? ChessPieceType.WhitePawn : ChessPieceType.BlackPawn;

            // 2. 이진 선택 완료 시, 승급시킬 폰을 클릭할 수 있도록 InteractionManager 전환
            if (ChessInteractionManager.Instance != null)
            {
                ChessInteractionManager.Instance.StartTargetPromotionMode(team, targetChessType, validSourcePawn);
            }
        });
    }

    // 선택 UI 없이, 지정된 원본 기물 종류 중 하나를 즉시 클릭해 승급시키는 모드로 진입한다.
    private static void StartSinglePiecePromotion(int team, PromotablePieceType promotableType, params ChessPieceType[] validSourceTypes)
    {
        ChessPieceType targetChessType = GetChessPieceType(promotableType, team);

        // 카드 선택창(이진 선택) 없이 즉시 타깃 클릭 대기 모드로 진입
        if (ChessInteractionManager.Instance != null)
        {
            ChessInteractionManager.Instance.StartTargetPromotionMode(team, targetChessType, validSourceTypes);
        }
    }

    // 승급 가능 기물 종류(PromotablePieceType)와 팀 정보를 실제 ChessPieceType으로 변환한다.
    private static ChessPieceType GetChessPieceType(PromotablePieceType promotable, int team)
    {
        bool isWhite = (team == 0);
        switch (promotable)
        {
            case PromotablePieceType.Queen:
                return isWhite ? ChessPieceType.WhiteQueen : ChessPieceType.BlackQueen;
            case PromotablePieceType.Rook:
                return isWhite ? ChessPieceType.WhiteRook : ChessPieceType.BlackRook;
            case PromotablePieceType.Bishop:
                return isWhite ? ChessPieceType.WhiteBishop : ChessPieceType.BlackBishop;
            case PromotablePieceType.Knight:
                return isWhite ? ChessPieceType.WhiteKnight : ChessPieceType.BlackKnight;
            default:
                return isWhite ? ChessPieceType.WhiteQueen : ChessPieceType.BlackQueen;
        }
    }
    #endregion
}
