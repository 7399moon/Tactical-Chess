using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 게임 종료 조건(체크메이트/스테일메이트/킹 직접 캡처)을 감지하고, 승패/무승부 이벤트를 발생시키는 클래스.
public class GameEndManager : MonoBehaviour
{
    public static GameEndManager Instance { get; private set; }

    // 게임 종료 원인
    public enum GameEndReason { Checkmate, KingCaptured, Stalemate, HandOverflow, HandEmpty } // 뒤의 둘은 우노 모드

    #region 참조 및 설정값
    [Header("References")]
    [SerializeField] private ChessBoard board;
    [SerializeField] private ChessInteractionManager interactionManager;

    [Header("Timing")]
    [SerializeField] private float checkmateHighlightDelay = 1.5f; // 체크메이트 강조 대기 시간
    #endregion

    #region 프로퍼티 및 이벤트
    public bool IsGameOver { get; private set; } = false;
    public event Action<int, GameEndReason> OnWin;  // 승리 팀(0: White, 1: Black), 승리 사유
    public event Action<GameEndReason> OnDraw;      // 무승부 사유
    #endregion

    #region 유니티 생명주기
    // 싱글턴 인스턴스 등록 (중복 존재 시 새 인스턴스를 파괴)
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    #endregion

    #region 게임 종료 판정
    // 턴 전환 직후 다음 차례 팀의 합법 수 유무 및 체크 여부를 검사하여 게임 종료 상태를 판정
    public void CheckGameEnd(int teamToMoveNext)
    {
        if (IsGameOver) return;

        Vector2Int? enPassantTarget = GameManager.Instance?.EnPassantTarget;

        // 합법 수가 존재하면 게임을 계속 진행
        if (ChessRules.HasAnyLegalMoves(board.Pieces, teamToMoveNext, enPassantTarget))
            return;

        // 합법 수가 없을 때: 킹이 체크 상태면 체크메이트, 아니면 스테일메이트
        if (ChessRules.IsKingInCheck(board.Pieces, teamToMoveNext))
            StartCoroutine(HandleCheckmateRoutine(teamToMoveNext));
        else
            HandleStalemate();
    }

    // 킹이 물리적으로 캡처되었을 때 즉시 승리 처리하는 특수 이벤트 통지
    public void NotifyKingCaptured(int capturedTeam)
    {
        if (IsGameOver) return;

        IsGameOver = true;
        OnWin?.Invoke(1 - capturedTeam, GameEndReason.KingCaptured);
    }

    // 우노 모드 승리 (상대 패가 최대 장수에 도달 = HandOverflow, 내 손패를 모두 비움 = HandEmpty)
    public void NotifyUnoWin(int winningTeam, GameEndReason reason)
    {
        if (IsGameOver) return;

        IsGameOver = true;
        OnWin?.Invoke(winningTeam, reason);
    }

    // 새 매치를 시작할 때 이전 매치의 종료 상태를 초기화한다.
    // (이 플래그가 true로 남아있으면 GameManager.Update()의 턴 타이머와 CheckGameEnd 판정이
    //  새 매치에서도 계속 멈춰있게 된다)
    public void ResetGameEnd()
    {
        IsGameOver = false;
    }
    #endregion

    #region 내부 처리 로직
    // 체크메이트 시 킹을 위협하는 기물 타일을 잠시 강조 표시한 후 승리 이벤트를 발생시키는 연출 코루틴
    private IEnumerator HandleCheckmateRoutine(int losingTeam)
    {
        IsGameOver = true;

        Vector2Int kingPos = ChessRules.FindKing(board.Pieces, losingTeam);
        int winningTeam = 1 - losingTeam;

        List<ChessPieces> attackers = ChessRules.GetAttackers(board.Pieces, kingPos.x, kingPos.y, winningTeam);
        List<Vector2Int> attackerTiles = new List<Vector2Int>(attackers.Count);

        for (int i = 0; i < attackers.Count; i++)
        {
            attackerTiles.Add(new Vector2Int(attackers[i].currentX, attackers[i].currentY));
        }

        interactionManager?.HighlightCheckmateAttackers(attackerTiles);

        yield return new WaitForSeconds(checkmateHighlightDelay);

        OnWin?.Invoke(winningTeam, GameEndReason.Checkmate);
    }

    // 스테일메이트 조건 달성 시 무승부 이벤트를 호출
    private void HandleStalemate()
    {
        IsGameOver = true;
        OnDraw?.Invoke(GameEndReason.Stalemate);
    }
    #endregion
}
