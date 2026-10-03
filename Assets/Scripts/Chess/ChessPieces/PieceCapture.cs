using System.Collections;
using UnityEngine;

// 기물 캡처 시 쉴드 판정, 보드 데이터 제거, 승리 조건/스킬 이벤트 통지 및 축소 애니메이션을 처리하는 클래스.
public class PieceCapture : MonoBehaviour
{
    [Header("Animation Settings")]
    [SerializeField] private float captureDuration = 0.3f; // 스케일 축소 연출 시간

    private ChessBoard cachedBoard;

    #region 유니티 생명주기
    // 시작 시 ChessBoard 참조를 캐싱
    private void Awake()
    {
        cachedBoard = FindAnyObjectByType<ChessBoard>();
    }
    #endregion

    #region 공개 메서드
    // 지정된 희생 기물(victim)을 쉴드 검사 및 보드 데이터 제거 후 애니메이션과 함께 파괴
    public void CapturePieceAt(ChessPieces victim, ChessPieces attacker = null)
    {
        if (victim == null) return;

        // 1. 쉴드 상태 검사 (보호 중이면 캡처 취소)
        if (PieceSkillManager.Instance != null && PieceSkillManager.Instance.IsShielded(victim))
        {
            Debug.Log($"[{victim.name}] 기물은 쉴드 상태이므로 공격당하지 않습니다.");
            CenterAnnouncer.Show("쉴드 효과로 보호받고 있어 공격할 수 없습니다.");
            return;
        }

        // 2. 캡처 사운드 재생 (여기까지 왔다는 것은 쉴드에 막히지 않은 실제 캡처가 확정된 것)
        SoundManager.Instance?.PlayCapture();

        // 3. 체스보드 배열 데이터에서 기물 제거
        EnsureBoardCached();
        if (cachedBoard != null)
        {
            cachedBoard.SetPieceAt(victim.currentX, victim.currentY, null);
        }

        // 4. 퀸 아우라 스택 통지
        if (QueenSkill.Instance != null)
        {
            QueenSkill.Instance.NotifyPieceCaptured(attacker, victim);
        }

        if (AugmentManager.Instance != null)
        {
            AugmentManager.Instance.OnPieceCaptured(attacker, victim);
        }

        // 5. 킹 처치 시 게임 종료 이벤트 통지
        bool isKing = (victim.type == ChessPieceType.WhiteKing || victim.type == ChessPieceType.BlackKing);
        if (isKing && GameEndManager.Instance != null)
        {
            GameEndManager.Instance.NotifyKingCaptured(victim.team);
        }

        // 6. 스케일 축소 연출 후 파괴
        StartCoroutine(CaptureRoutine(victim));
    }
    #endregion

    #region 연출 및 내부 유틸리티
    // 기물의 스케일을 서서히 축소시킨 후 GameObject를 파괴하는 연출 코루틴
    private IEnumerator CaptureRoutine(ChessPieces piece)
    {
        Vector3 startScale = piece.transform.localScale;
        float duration = Mathf.Max(0.0001f, captureDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            piece.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            yield return null;
        }
        Destroy(piece.gameObject);
    }

    // 캐싱된 ChessBoard 참조가 널일 경우 재할당
    private void EnsureBoardCached()
    {
        if (cachedBoard == null)
            cachedBoard = FindAnyObjectByType<ChessBoard>();
    }
    #endregion
}