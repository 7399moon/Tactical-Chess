using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// GameEndManager의 승패/무승부 이벤트를 구독하여 페이드 연출과 함께 결과 UI를 출력하는 클래스.
public class GameOverUI : MonoBehaviour
{
    public static GameOverUI Instance { get; private set; }

    #region 참조 및 연출 설정값
    [Header("References")]
    [SerializeField] private GameObject resultPanel; // 결과 화면 패널
    [SerializeField] private Image darkOverlay;      // 전체 반투명 검은 오버레이
    [SerializeField] private Text resultText;        // 승리 메인 텍스트
    [SerializeField] private Text reasonText;        // 상세 사유 텍스트

    [Header("Buttons")]
    [SerializeField] private Button restartButton;   // "게임 재시작" 버튼
    [SerializeField] private Button goToTitleButton; // "타이틀로 이동" 버튼

    [Header("Fade Settings")]
    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private float overlayTargetAlpha = 0.6f;
    #endregion

    #region 유니티 생명주기 및 이벤트 구독
    // 시작 시 결과 패널과 오버레이를 완전히 숨긴 상태로 초기화
    private void Awake()
    {
        Instance = this;

        if (resultPanel != null)
            resultPanel.SetActive(false);

        if (darkOverlay != null)
        {
            Color c = darkOverlay.color;
            c.a = 0f;
            darkOverlay.color = c;
        }

        if (restartButton != null) restartButton.onClick.AddListener(OnRestartButtonPressed);
        if (goToTitleButton != null) goToTitleButton.onClick.AddListener(OnGoToTitleButtonPressed);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable() => RegisterEvents();
    private void OnDisable() => UnregisterEvents();

    private void Start()
    {
        // OnEnable 시점에 GameEndManager.Instance가 null이었을 경우를 대비해 Start에서 한번 더 보장
        RegisterEvents();
    }

    // GameEndManager의 승리/무승부 이벤트를 구독
    private void RegisterEvents()
    {
        if (GameEndManager.Instance == null) return;

        // 중복 구독 방지
        UnregisterEvents();

        GameEndManager.Instance.OnWin += HandleWin;
        GameEndManager.Instance.OnDraw += HandleDraw;
    }

    // GameEndManager 이벤트 구독을 해제
    private void UnregisterEvents()
    {
        if (GameEndManager.Instance == null) return;

        GameEndManager.Instance.OnWin -= HandleWin;
        GameEndManager.Instance.OnDraw -= HandleDraw;
    }
    #endregion

    #region 이벤트 핸들러
    // 승리 이벤트 발생 시, 네트워크 대전 중이면 "내 팀 기준" 승리/패배 문구를,
    // 아니라면(로컬 테스트 등) 기존처럼 진영 이름 기준 문구를 결과창에 표시
    private void HandleWin(int winningTeam, GameEndManager.GameEndReason reason)
    {
        string mainText;

        if (GameStartController.LocalTeam >= 0)
        {
            bool isLocalWin = winningTeam == GameStartController.LocalTeam;
            mainText = isLocalWin ? "승리!" : "패배...";
            if (isLocalWin) SoundManager.Instance?.PlayGameWin();
            else SoundManager.Instance?.PlayGameLose();
        }
        else
        {
            mainText = (winningTeam == 0 ? "백 진영" : "흑 진영") + " 승리!";
            SoundManager.Instance?.PlayGameWin();
        }

        Show(mainText, GetReasonText(reason));
    }

    // 무승부 이벤트 발생 시 "무승부" 문구와 사유를 결과창에 표시
    private void HandleDraw(GameEndManager.GameEndReason reason)
    {
        SoundManager.Instance?.PlayStalemate();
        Show("무승부", GetReasonText(reason));
    }
    #endregion

    #region 버튼 핸들러
    // "게임 재시작" 버튼 클릭 핸들러.
    // 네트워크 대전 중(LocalTeam이 배정되어 있고 ChessNetworkSync가 스폰되어 있음)이면 RPC로
    // 양쪽 클라이언트에 동일한 리셋을 전파하고(GameManager.RequestEndTurn과 동일한 판별 조건 사용),
    // 그 외(로컬/비네트워크 테스트)에는 이 클라이언트에서 바로 리셋한다.
    private void OnRestartButtonPressed()
    {
        if (GameStartController.LocalTeam >= 0 && ChessNetworkSync.Instance != null)
            ChessNetworkSync.Instance.RPC_RelayRestartMatch();
        else
            GameStartController.Instance?.StartMatch();
    }

    // "타이틀로 이동" 버튼 클릭 핸들러.
    // 게임오버 UI 자체가 원래부터 클라이언트별 로컬 표시라서, 이 동작도 상대에게 전파할 필요 없이
    // 이 클라이언트만 로컬로 세션을 나가고 타이틀 화면으로 돌아간다(각자 독립적으로 나가는 것은 정상 동작).
    private void OnGoToTitleButtonPressed()
    {
        BasicSpawner.Instance?.GoToTitle();
    }
    #endregion

    #region 결과 UI 표시 및 연출
    // 종료 사유 enum을 UI 출력용 한글 문자열로 변환
    private string GetReasonText(GameEndManager.GameEndReason reason)
    {
        switch (reason)
        {
            case GameEndManager.GameEndReason.Checkmate:
                return "체크메이트로 승리";
            case GameEndManager.GameEndReason.KingCaptured:
                return "킹 캡처로 승리";
            case GameEndManager.GameEndReason.Stalemate:
                return "스테일메이트로 무승부";
            default:
                return string.Empty;
        }
    }

    // 결과 패널을 활성화하고 오버레이 페이드인 연출을 시작
    private void Show(string mainText, string reason)
    {
        if (resultText != null) resultText.text = mainText;
        if (reasonText != null) reasonText.text = reason;
        if (resultPanel != null) resultPanel.SetActive(true);

        StartCoroutine(FadeInOverlayRoutine());
    }

    // 반투명 오버레이 패널 알파값을 Lerp로 증가시키는 페이드 연출 코루틴
    private IEnumerator FadeInOverlayRoutine()
    {
        if (darkOverlay == null) yield break;

        Color c = darkOverlay.color;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(0f, overlayTargetAlpha, elapsed / fadeDuration);
            darkOverlay.color = c;
            yield return null;
        }

        c.a = overlayTargetAlpha;
        darkOverlay.color = c;
    }

    // 새 매치를 시작할 때 이전 매치의 결과 화면(승리/패배/무승부 패널)을 완전히 숨긴다.
    // (이전 매치 종료 시 켜진 resultPanel/오버레이가 새 매치에서도 그대로 남아있던 문제 수정)
    public void HideResult()
    {
        StopAllCoroutines();

        if (resultPanel != null)
            resultPanel.SetActive(false);

        if (darkOverlay != null)
        {
            Color c = darkOverlay.color;
            c.a = 0f;
            darkOverlay.color = c;
        }
    }
    #endregion
}
