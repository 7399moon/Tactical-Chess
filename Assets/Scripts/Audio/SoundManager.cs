using UnityEngine;

// 게임 전반의 효과음(SFX) 재생을 총괄하는 싱글턴 매니저.
// 실제로 사용하는 클립들은 원본 에셋팩(Casual Game Sounds)에서 Assets/Audio/SFX/ 폴더로 복사한 뒤
// "DM-CGS-번호_용도" 형식으로 이름을 바꿔 보관한다 - 인스펙터에서 어떤 파일이 어디에 쓰이는지
// 파일명만 보고 바로 알아볼 수 있도록 하기 위함(원본 에셋팩 폴더는 그대로 유지).
// (풀 대전 상황을 고려해 항상 audioSource.PlayOneShot을 사용 - 여러 사운드가 겹쳐도 서로를 끊지 않음)
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] private AudioSource audioSource;

    [Header("1. 체스 기본 진행")]
    [SerializeField] private AudioClip moveClip;         // 1-1. 기물 이동 (워프 제외) - Assets/Audio/SFX/DM-CGS-01_Move.wav (0.3초로 길이 조정된 버전)
    [SerializeField] private AudioClip captureClip;      // 1-2. 캡처 - Assets/Audio/SFX/DM-CGS-19_Capture_AugmentCardAppear.wav (3-2 증강 카드 등장과 공용)
    [SerializeField] private AudioClip turnEndClip;      // 1-3. 턴 종료 - Assets/Audio/SFX/DM-CGS-31_TurnEnd.wav
    [SerializeField] private AudioClip checkClip;        // 1-3. 체크 진입 - Assets/Audio/SFX/DM-CGS-17_Check.wav
    [SerializeField] private AudioClip gameStartClip;    // 1-4. 게임 시작 - Assets/Audio/SFX/DM-CGS-12_GameStart.wav
    [SerializeField] private AudioClip gameWinClip;      // 1-5. 게임 승리 - Assets/Audio/SFX/DM-CGS-18_GameWin.wav
    [SerializeField] private AudioClip gameLoseClip;     // 1-6. 게임 패배 - Assets/Audio/SFX/DM-CGS-10_GameLose.wav
    [SerializeField] private AudioClip stalemateClip;    // 1-7. 무승부(스테일메이트) - Assets/Audio/SFX/DM-CGS-36_Stalemate.wav

    [Header("2. 스킬")]
    [SerializeField] private AudioClip threatClip;       // 2-1. 위협 - Assets/Audio/SFX/DM-CGS-38_Threat.wav
    [SerializeField] private AudioClip shieldClip;       // 2-2. 쉴드 - Assets/Audio/SFX/DM-CGS-32_Shield.wav
    [SerializeField] private AudioClip warpClip;         // 2-3. 워프 - Assets/Audio/SFX/DM-CGS-30_Warp.wav
    [SerializeField] private AudioClip commandClip;      // 2-4. 지휘 - Assets/Audio/SFX/DM-CGS-15_Command.wav
    [SerializeField] private AudioClip promotionClip;    // 2-5. 프로모션 - Assets/Audio/SFX/DM-CGS-26_Promotion.wav

    [Header("3. UI")]
    [SerializeField] private AudioClip skillButtonClickClip;    // 3-1. 스킬 버튼 클릭(턴 종료 버튼 제외) - Assets/Audio/SFX/DM-CGS-16_SkillButtonClick.wav
    [SerializeField] private AudioClip augmentBothSelectedClip; // 3-3. 증강 카드 양쪽 모두 선택 후 애니메이션 - Assets/Audio/SFX/DM-CGS-45_AugmentBothSelected.wav

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
    // 3-2. 증강 카드 등장: 1-2 캡처와 동일한 DM-CGS-19_Capture_AugmentCardAppear.wav 클립을 그대로 재사용한다.
    public void PlayAugmentCardAppear() => PlayClip(captureClip);
    public void PlayAugmentBothSelected() => PlayClip(augmentBothSelectedClip);
    #endregion
}
