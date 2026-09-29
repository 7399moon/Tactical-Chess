using System.Collections.Generic;
using UnityEngine;

// ChessInteractionManager의 체크 상태 갱신 및 BoardTileHighlighter 중계 메서드 partial 파일.
public partial class ChessInteractionManager
{
    // 체크 진입 사운드가 "체크가 아니었다가 체크가 된 순간"에만 1회 재생되도록 이전 판정 결과를 기억
    private bool wasCheckActive = false;

    #region 체크 상태 및 외부 중계
    // 턴이 시작될 때 체크 하이라이트를 갱신하고 게임 종료 여부를 검사
    private void HandleTurnStarted(int team)
    {
        UpdateCheckHighlight();
        GameEndManager.Instance?.CheckGameEnd(team);
    }

    // 양 팀을 순회하며 체크 상태인 킹을 찾아 하이라이트를 갱신
    public void UpdateCheckHighlight()
    {
        Vector2Int checkedKingTile = -Vector2Int.one;

        for (int team = 0; team < 2; team++)
        {
            if (ChessRules.IsKingInCheck(board.Pieces, team))
            {
                checkedKingTile = ChessRules.FindKing(board.Pieces, team);
                break;
            }
        }

        tileHighlighter.SetCheckedKingTile(checkedKingTile);

        // 체크가 아니었다가 방금 체크 상태로 바뀐 순간(진입)에만 사운드 재생 (매 턴 갱신마다 중복 재생 방지)
        bool isCheckActiveNow = checkedKingTile != -Vector2Int.one;
        if (isCheckActiveNow && !wasCheckActive)
            SoundManager.Instance?.PlayCheck();
        wasCheckActive = isCheckActiveNow;
    }

    // 아래 세 메서드는 BoardTileHighlighter 호출을 외부(GameEndManager 등)에 중계하는 역할만 한다.
    public void HighlightCheckmateAttackers(List<Vector2Int> attackerTiles) => tileHighlighter?.HighlightCheckmateAttackers(attackerTiles);
    public void HighlightCustomTiles(List<Vector2Int> tiles) => tileHighlighter?.HighlightCustomTiles(tiles);
    public void ClearCustomHighlights() => tileHighlighter?.ClearCustomHighlights();
    #endregion
}
