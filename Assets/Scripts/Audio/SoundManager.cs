using UnityEngine;

// 게임 전반의 효과음(SFX) 재생을 총괄하는 싱글턴 매니저.
// Casual Game Sounds 에셋팩의 클립들을 이벤트별로 매핑해 AudioSource.PlayOneShot으로 재생한다.
// (풀 대전 상황을 고려해 항상 audioSource.PlayOneShot을 사용 - 여러 사운드가 겹쳐도 서로를 끊지 않음)
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] private AudioSource audioSource;

    [Header("1. 체스 기본 진행")]
    [SerializeField] private AudioClip moveClip;         // 1-1. 기물 이동 (워프 제외) - DM-CGS-01 (0.3초로 길이 조정된 버전 사용)
    [SerializeField] private AudioClip captureClip;      // 1-2. 캡처 - DM-CGS-19 (3-2 증강 카드 등장과 동일 클립 공용)
    [SerializeField] private AudioClip turnEndClip;      // 1-3. 턴 종료 - DM-CGS-31
    [SerializeField] private AudioClip checkClip;        // 1-3. 체크 진입 - DM-CGS-17
    [SerializeField] private AudioClip gameStartClip;    // 1-4. 게임 시작 - DM-CGS-12
    [SerializeField] private AudioClip gameWinClip;      // 1-5. 게임 승리 - DM-CGS-18
    [SerializeField] private AudioClip gameLoseClip;     // 1-6. 게임 패배 - DM-CGS-10
    [SerializeField] private AudioClip stalemateClip;    // 1-7. 무승부(스테일메이트) - DM-CGS-36

    [Header("2. 스킬")]
    [SerializeField] private AudioClip threatClip;       // 2-1. 위협 - DM-CGS-38
    [SerializeField] private AudioClip shieldClip;       // 2-2. 쉴드 - DM-CGS-32
    [SerializeField] private AudioClip warpClip;         // 2-3. 워프 - DM-CGS-30
    [SerializeField] private AudioClip commandClip;      // 2-4. 지휘 - DM-CGS-15
    [SerializeField] private AudioClip promotionClip;    // 2-5. 프로모션 - DM-CGS-26

    [Header("3. UI")]
    [SerializeField] private AudioClip skillButtonClickClip;    // 3-1. 스킬 버튼 클릭(턴 종료 버튼 제외) - DM-CGS-16
    [SerializeField] private AudioClip augmentBothSelectedClip; // 3-3. 증강 카드 양쪽 모두 선택 후 애니메이션 - DM-CGS-45

    #region 유니티 생명주기
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }
    #endregion

    #region 재생 헬퍼
    private void PlayClip(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip);
    }
    #endregion

    #region 1. 체스 기본 진행
    public void PlayMove() => PlayClip(moveClip);
    public void PlayCapture() => PlayClip(captureClip);
    public void PlayTurnEnd() => PlayClip(turnEndClip);
    public void PlayCheck() => PlayClip(checkClip);
    public void PlayGameStart() => PlayClip(gameStartClip);
    public void PlayGameWin() => PlayClip(gameWinClip);
    public void PlayGameLose() => PlayClip(gameLoseClip);
    public void PlayStalemate() => PlayClip(stalemateClip);
    #endregion

    #region 2. 스킬
    public void PlayThreat() => PlayClip(threatClip);
    public void PlayShield() => PlayClip(shieldClip);
    public void PlayWarp() => PlayClip(warpClip);
    public void PlayCommand() => PlayClip(commandClip);
    public void PlayPromotion() => PlayClip(promotionClip);
    #endregion

    #region 3. UI
    public void PlaySkillButtonClick() => PlayClip(skillButtonClickClip);
    // 3-2. 증강 카드 등장: 1-2 캡처와 동일한 DM-CGS-19 클립을 그대로 재사용한다.
    public void PlayAugmentCardAppear() => PlayClip(captureClip);
    public void PlayAugmentBothSelected() => PlayClip(augmentBothSelectedClip);
    #endregion
}
