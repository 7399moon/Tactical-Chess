using System;
using UnityEngine;
using UnityEngine.UI;

// 게임 전체의 진행, 턴 제한 시간, 턴 전환 및 UI 표시를 총괄 관리하는 싱글톤 클래스.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    #region 인스펙터 설정값
    [Header("Turn Settings")]
    [SerializeField] private bool useTurnSystem = true;    // 턴 시스템 사용 여부
    [SerializeField] private float turnTime = 30f;         // 턴 제한 시간 (초)

    [Header("Turn UI")]
    [SerializeField] private Image fill;                  // 시간 표시 Fill Image
    [SerializeField] private RectTransform fillWave;       // [변경] 액체 윗면(물결) 이미지: Fill 끝을 따라 이동 (선택 사항)
    [SerializeField] private GameObject turnTimeBar;       // 시간바 부모 오브젝트
    [SerializeField] private Text turnCountText;          // 턴 진행 수 표시 텍스트
    #endregion

    #region 내부 상태 필드
    private int currentTurn = 0;            // 0 = White / 1 = Black
    private int turnCount = 1;              // 체스 게임 전체 진행 턴 수
    private float invTurnLimit = 1f;         // 1 / 턴 제한 시간 (매 프레임 나눗셈 대신 곱셈으로 정규화)
    private float currentTurnTime;          // 현재 턴의 남은 제한 시간
    private bool hasMovedThisTurn = false;  // 현재 턴 내 기물 이동 완료 여부
    private bool isPaused = false;          // 증강 선택 UI 오픈 등의 사유로 타이머 일시정지 여부

    // 앙파상 타깃 좌표 (폰 2칸 전진 시 활성화)
    private Vector2Int? enPassantTarget = null;
    #endregion

    #region 외부 공개 프로퍼티
    public int CurrentTurn => currentTurn;
    public int TurnCount => turnCount;
    public int armisticeTurns = 0;
    public float CurrentTurnTime => currentTurnTime;
    public bool HasMovedThisTurn => hasMovedThisTurn;
    public bool UseTurnSystem => useTurnSystem;
    public bool IsPaused => isPaused;
    public Vector2Int? EnPassantTarget => enPassantTarget;
    #endregion

    #region 이벤트
    public event Action OnTurnTimeout;             // 시간 초과 시 선택 기물/하이라이트 정리를 위한 이벤트
    public event Action<int> OnTurnStarted;        // 턴 시작 시 실행되는 이벤트 (매개변수: currentTurn)
    #endregion

    #region 유니티 생명주기
    // 싱글턴 인스턴스 등록
    private void Awake()
    {
        // 싱글턴 패턴
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 턴 시스템을 사용하면 시간바를 켜고 백팀부터 첫 턴을 시작
    private void Start()
    {
        if (!useTurnSystem) return;

        if (turnTimeBar != null)
            turnTimeBar.SetActive(useTurnSystem);
        RefreshBarVisibility();

        turnCount = 1;
        StartTurn(0); // 백팀(0)부터 게임 시작
    }

    // 매 프레임 턴 타이머를 갱신 (턴 시스템 미사용/일시정지/게임종료 시 스킵)
    private void Update()
    {
        // 증강 선택 제한 시간 표시 중에는 같은 세로 바를 선택 남은 시간 표시로 빌려 쓴다.
        if (selectionBarValue >= 0f)
            DrawBar(selectionBarValue);

        if (!useTurnSystem || isPaused) return;
        if (!MatchSettings.EffectiveTurnTimeLimit) return; // 턴 시간 제한 OFF(또는 우노 모드): 무제한

        // 게임 종료 상태 시 타이머 업데이트 중단
        if (GameEndManager.Instance != null && GameEndManager.Instance.IsGameOver)
            return;

        UpdateTurnTimer();
    }
    #endregion

    #region 앙파상 처리
    // 폰 2칸 전진 시 앙파상 타깃 좌표 설정
    public void SetEnPassantTarget(Vector2Int target) => enPassantTarget = target;
    // 앙파상 타깃 좌표 초기화
    public void ClearEnPassantTarget() => enPassantTarget = null;
    #endregion

    #region 타이머 일시정지 제어
    // 증강 선택 등 UI가 열릴 때 턴 타이머를 멈춘다.
    public void PauseTimer() => isPaused = true;
    // 멈췄던 턴 타이머를 다시 재개한다.
    public void ResumeTimer() => isPaused = false;

    // 매 프레임 남은 시간을 줄이고 UI 업데이트 및 타임아웃 판정 수행
    private void UpdateTurnTimer()
    {
        currentTurnTime -= Time.deltaTime;

        if (currentTurnTime <= 0f)
        {
            currentTurnTime = 0f;
            OnTurnTimeout?.Invoke();
            RequestEndTurn();
            return;
        }

        UpdateTurnUI();
    }
    #endregion

    #region 턴 상태 관리
    // 신규 턴을 시작하고 턴 상태 및 UI를 초기화
    private void StartTurn(int team)
    {
        currentTurn = team;
        currentTurnTime = TurnLimitSeconds;
        invTurnLimit = currentTurnTime > 0f ? 1f / currentTurnTime : 1f;
        hasMovedThisTurn = false;

        ResetAllPiecesTurnState();

        UpdateTurnUI();
        UpdateTurnCountUI();

        OnTurnStarted?.Invoke(currentTurn);
    }

    // 현재 턴을 종료하고 다음 팀으로 턴을 전환
    // keepSameTeam: 우노 스킵 카드처럼 상대 턴을 건너뛰고 같은 팀이 한 번 더 진행할 때 true
    public void EndTurn(bool keepSameTeam = false)
    {
        if (!useTurnSystem) return;
        if (UnoTurnController.Active && UnoTurnController.Instance.ConsumeSkip()) keepSameTeam = true; // 우노 스킵: 수동 종료도 상대 턴을 건너뜀

        SoundManager.Instance?.PlayTurnEnd();

        if (armisticeTurns > 0)
        {
            Debug.Log($"[GameManager] 휴전 협정 진행 중... (남은 턴: {armisticeTurns})");
            armisticeTurns--;
        }

        // 10턴 단위 주기마다 증강 카드 선택 체크포인트 시작. 두 팀(White/Black) 모두 각자
        // 독립적으로 증강 카드를 선택해야 하며, CardSelectionManager가 내부적으로 순서를 관리해
        // 두 팀 모두 선택을 마쳤을 때에만 타이머가 재개되도록 처리한다.
        if (turnCount > 1 && turnCount % 10 == 0)
        {
            CardSelectionManager.Instance?.TriggerAugmentCheckpoint();
        }

        turnCount++;
        if (!keepSameTeam) currentTurn = 1 - currentTurn;
        StartTurn(currentTurn);
    }

    // 네트워크 대전 중 로컬에서만 발생하는 턴 종료 트리거(시간 초과, 수동 "턴 종료" 버튼)를
    // 상대에게도 동일하게 전파한다. 이동으로 인한 턴 종료(PieceMoved)는 이미 보드 클릭 자체가
    // RPC로 중계되어 양쪽에서 동일하게 실행되므로 여기를 거치지 않아도 자연히 동기화된다.
    // 네트워크 대전이 아니면(로컬 테스트 등) 기존처럼 즉시 종료한다.
    //
    // 요청 시점의 turnCount를 함께 전달해, "지금 끝내려는 턴이 몇 턴인지"를 RPC에 담는다.
    // (아래 EndTurnFromNetwork 주석 참고 — 이게 중복 종료를 막는 핵심 키.)
    public void RequestEndTurn()
    {
        if (GameStartController.LocalTeam >= 0 && ChessNetworkSync.Instance != null)
        {
            ChessNetworkSync.Instance.RPC_RelayEndTurn(turnCount);
        }
        else
        {
            EndTurn();
        }
    }

    // RPC로 전달된 턴 종료 요청을 처리.
    // 양쪽 클라이언트의 타이머가 거의 동시에 시간 초과를 감지하면 각자 RequestEndTurn을 호출하므로,
    // 한 클라이언트 입장에서 같은 턴(N)에 대한 종료 요청이 "내 요청"과 "상대 요청" 두 번 도착할 수 있다.
    // 이때 requestedTurnCount(요청이 만들어진 시점의 turnCount = N)를 현재 turnCount와 비교해서,
    // 첫 번째 요청 처리로 이미 턴이 N+1로 넘어간 뒤 도착한 두 번째 요청(여전히 N을 가리킴)은
    // "이미 처리된 낡은 요청"으로 판단해 무시한다. (turnCount 값 자체가 매번 바뀌므로
    // 처리 이후의 turnCount가 아니라 "요청이 생성된 시점의 turnCount"를 비교 기준으로 삼아야 한다.)
    public void EndTurnFromNetwork(int requestedTurnCount)
    {
        if (requestedTurnCount != turnCount) return;
        EndTurn();
    }

    // 지정 기물이 현재 턴에 이동 가능한 상태인지 검사
    public bool CanMovePiece(ChessPieces piece)
    {
        if (!useTurnSystem) return true;
        return piece != null && piece.team == currentTurn && !hasMovedThisTurn;
    }

    // 보드 위 모든 기물의 "이번 턴 이동 횟수"를 초기화
    private void ResetAllPiecesTurnState()
    {
        if (ChessBoard.Instance == null || ChessBoard.Instance.Pieces == null) return;

        var pieces = ChessBoard.Instance.Pieces;
        for (int x = 0; x < ChessBoard.TileCountX; x++)
        {
            for (int y = 0; y < ChessBoard.TileCountY; y++)
            {
                if (pieces[x, y] != null)
                {
                    pieces[x, y].ResetTurnState();
                }
            }
        }
    }

    // 기물 이동 처리
    public void PieceMoved()
    {
        if (!useTurnSystem) return;

        // 우노 모드: 이동 1회가 끝났다. 요구된 횟수를 채웠을 때만 UnoTurnController가 턴을 넘긴다.
        if (UnoTurnController.Active)
        {
            UnoTurnController.Instance.OnMoveFinished();
            return;
        }

        hasMovedThisTurn = true;
        EndTurn();
    }

    // 새 매치를 시작할 때 턴 진행 상태를 초기 상태로 되돌린다.
    // (이전 매치의 턴 수/휴전 카운트/앙파상 타깃이 새 매치에 그대로 남아있던 문제 수정)
    public void ResetForNewMatch()
    {
        armisticeTurns = 0;
        enPassantTarget = null;
        isPaused = false;
        turnCount = 1;
        selectionBarValue = -1f;
        RefreshBarVisibility();

        if (useTurnSystem)
            StartTurn(0); // 백팀(0)부터 다시 시작 (OnTurnStarted 이벤트로 다른 매니저들의 턴 상태도 함께 초기화됨)
    }
    #endregion

    #region UI 표시 갱신
    // 턴 진행 시간에 따른 Fill Image 게이지 및 색상(녹색 -> 노랑 -> 빨강) 변경
    private void UpdateTurnUI()
    {
        if (fill == null) return;

        if (selectionBarValue >= 0f) return; // 증강 선택 시간 표시 중에는 턴 타이머가 바를 건드리지 않는다
        DrawBar(currentTurnTime * invTurnLimit);
    }

    // 세로 바 Fill 갱신 + 물결 위치. [변경] 새 시간바 아트(주황 액체)를 쓰므로 평소엔 원본 색을 유지하고,
    // 남은 시간이 25% 이하일 때만 붉게 물들인다.
    private const float LowTimeThreshold = 0.25f;                       // 이 비율 이하일 때 붉게 물든다
    private const float InvLowTimeThreshold = 1f / LowTimeThreshold;    // 나눗셈 대신 곱셈용 역수(컴파일 타임 상수)
    private static readonly Color LowTimeColor = new Color(1f, 0.35f, 0.3f, 1f);

    private void DrawBar(float normalizedTime)
    {
        if (fill == null) return;
        normalizedTime = Mathf.Clamp01(normalizedTime);
        fill.fillAmount = normalizedTime;

        fill.color = normalizedTime > LowTimeThreshold
            ? Color.white
            : Color.Lerp(LowTimeColor, Color.white, normalizedTime * InvLowTimeThreshold);

        if (fillWave != null)
        {
            float h = fill.rectTransform.rect.height;
            float wh = fillWave.rect.height;
            // 2026-10-02 수정: wave는 하단 피벗(0.5, 0)이라 anchoredPosition.y가 곧 wave의 "아랫변" 위치.
            // 기존엔 fill 윗면(normalizedTime * h)보다 wh*0.6만큼 아래에 둬서 wave가 액체 속에 파묻힌 것처럼
            // 보였음 - wave 아랫변을 fill 윗면과 정확히 일치시키도록 오프셋 제거(가득 찼을 때만 바 밖으로
            // 안 튀어나오게 h - wh로 계속 clamp).
            float y = Mathf.Min(normalizedTime * h, h - wh);
            fillWave.anchoredPosition = new Vector2(0f, y);
            fillWave.gameObject.SetActive(normalizedTime > 0.01f);
        }
    }

    #region 로비 규칙 적용 (MatchSettings)
    // 이번 판 턴당 제한 시간(초): 로비에서 정한 값. (인스펙터 turnTime은 로비 없이 단독 실행할 때의 기본값)
    private float TurnLimitSeconds => MatchSettings.TurnSeconds > 0 ? MatchSettings.TurnSeconds : turnTime;

    // 증강 선택 제한 시간 표시용 오버라이드 값(0~1). 음수면 사용 안 함.
    private float selectionBarValue = -1f;
    private Image[] turnBarImages;

    // CardSelectionManager가 증강 선택 남은 시간을 세로 바에 표시/해제한다 (normalized < 0 이면 해제).
    public void SetSelectionBar(float normalized)
    {
        bool wasActive = selectionBarValue >= 0f;
        selectionBarValue = normalized < 0f ? -1f : Mathf.Clamp01(normalized);
        if (wasActive != (selectionBarValue >= 0f)) RefreshBarVisibility();
        if (selectionBarValue >= 0f) DrawBar(selectionBarValue); // 같은 프레임에 즉시 fill/wave 위치를 맞춘다(한 프레임 지연 방지)
    }

    // 턴 시간 제한 OFF면 시간 바(배경+Fill 이미지)만 숨긴다. 턴 수 텍스트(TurnCount)는 바의 자식이라
    // 오브젝트를 끄지 않고 이미지 컴포넌트만 끈다. 증강 선택 시간 표시 중에는 다시 보여준다.
    public void RefreshBarVisibility()
    {
        if (turnTimeBar == null) return;
        bool visible = MatchSettings.EffectiveTurnTimeLimit || selectionBarValue >= 0f;
        if (turnBarImages == null)
            turnBarImages = turnTimeBar.GetComponentsInChildren<Image>(true); // 한 번만 조회해 캐싱(매번 배열 할당 방지)
        for (int i = 0; i < turnBarImages.Length; i++)
            if (turnBarImages[i] != null) turnBarImages[i].enabled = visible;
        if (visible) UpdateTurnUI();
    }
    #endregion

    // 턴 수 표기 텍스트 갱신
    private void UpdateTurnCountUI()
    {
        if (turnCountText != null)
            turnCountText.text = $"Turn {turnCount}";
    }
    #endregion
}
