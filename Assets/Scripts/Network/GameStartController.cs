using UnityEngine;

// GameScene의 대기 상태 및 팀 배정(카메라 포함)을 관리하는 컨트롤러.
// 방 인원이 2명이 되기 전까지는 게임 UI와 체스판/기물을 모두 숨기고 "상대방 입장 대기 중" 화면만 보여주며,
// BasicSpawner가 접속 인원 변화를 감지할 때마다 StartMatch/ShowWaiting을 호출해 상태를 전환한다.
public class GameStartController : MonoBehaviour
{
    // 다른 매니저들과 동일한 싱글턴 패턴. 게임오버 UI의 "게임 재시작" 버튼(RPC 수신측)처럼
    // GameObject.Find 없이 곧바로 StartMatch()를 호출해야 하는 곳에서 사용.
    public static GameStartController Instance { get; private set; }

    #region 인스펙터 설정값
    [SerializeField] private GameObject gameCanvas; // 실제 게임 UI(체스판 UI 등)가 들어있는 Canvas
    [SerializeField] private GameObject waitingUI;  // "상대방 입장 대기 중" 안내 UI
    [SerializeField] private UnityEngine.UI.Text roomCodeText; // 대기 화면 상단에 표시할 방 번호 텍스트

    [Header("팀별 카메라")]
    [SerializeField] private GameObject whiteCamera; // 백색 기물 방향 카메라 (기존 Main Camera)
    [SerializeField] private GameObject blackCamera; // 흑색 기물 방향 카메라 (신규)
    #endregion

    // 이 클라이언트(로컬 플레이어)에게 배정된 팀. 0 = White(호스트), 1 = Black(참가자), -1 = 아직 미배정(네트워크 대전 아님)
    public static int LocalTeam { get; private set; } = -1;

    #region 유니티 생명주기
    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // 씬 로드 시 기본적으로 대기 상태(체스판/기물 숨김)로 시작한다.
    // ChessBoard.Awake()에서 Instance가 설정되므로 Start() 시점에는 항상 준비되어 있다.
    private void Start()
    {
        ChessBoard.Instance?.SetBoardVisible(false);
    }
    #endregion

    #region 매치 시작 / 대기 상태 전환
    // 방 인원이 2명이 되어 게임을 시작할 때 호출: 게임 UI와 체스판/기물을 켜고 대기 화면을 끈다.
    //
    // 앱을 다시 켜지 않고 같은 프로세스 안에서 새 매치를 시작하는 경우(재접속, 재호스팅,
    // 게임오버 화면의 "게임 재시작" 버튼 등) ChessBoard/GameManager/PieceSkillManager/AugmentManager/
    // GameEndManager/GameOverUI는 씬이 다시 로드되지 않아 전부 이전 매치의 상태를 그대로 들고 있다.
    // 게임 화면을 보여주기 전에 반드시 전부 초기 상태로 되돌려서, 이전 매치에서 옮긴 기물/스킬 쿨타임/
    // 증강/턴 진행/결과 화면이 새 매치로 이어지지 않도록 한다.
    public void StartMatch()
    {
        ResetMatchState();

        SoundManager.Instance?.PlayGameStart();

        if (gameCanvas != null) gameCanvas.SetActive(true);
        if (waitingUI != null) waitingUI.SetActive(false);
        ChessBoard.Instance?.SetBoardVisible(true);
    }

    // 방 인원이 2명 미만일 때 호출: 게임 UI와 체스판/기물을 끄고 대기 화면을 켠다.
    public void ShowWaiting()
    {
        if (gameCanvas != null) gameCanvas.SetActive(false);
        if (waitingUI != null) waitingUI.SetActive(true);
        ChessBoard.Instance?.SetBoardVisible(false);
    }

    // 매치 도중 상대방이 나가 인원이 2명 미만으로 떨어졌을 때 호출(BasicSpawner.OnPlayerLeft 등).
    // 진행 중이던 매치 상태(기물 위치/스킬 쿨타임/증강/턴/게임오버 결과)를 StartMatch()와 동일하게
    // 전부 초기화한 뒤 대기 화면으로 되돌린다 - 남은 자리에 새 상대가 들어와도(같은 방 번호로 재입장)
    // 이전 매치의 잔여 상태가 새 매치로 새지 않도록 한다. 아직 아무도 나간 적 없는 최초 대기
    // 상태(인원 1명)에서 호출돼도 이미 초기 상태인 매니저들을 다시 초기화할 뿐이라 안전하다.
    public void ResetAndShowWaiting()
    {
        ResetMatchState();
        ShowWaiting();
    }

    // StartMatch()/ResetAndShowWaiting()이 공유하는 매치 상태 초기화 로직.
    private void ResetMatchState()
    {
        ChessBoard.Instance?.ResetBoard();
        PieceSkillManager.Instance?.ResetAll();
        AugmentManager.Instance?.ResetAll();
        GameEndManager.Instance?.ResetGameEnd();
        GameOverUI.Instance?.HideResult();
        ChessInteractionManager.Instance?.ResetInteractionState();
        GameManager.Instance?.ResetForNewMatch();
    }
    #endregion

    #region 방 번호 / 팀 배정
    // 대기 화면 상단에 방 번호를 표시 (BasicSpawner가 세션 시작 직후 1회 호출).
    public void SetRoomCode(string code)
    {
        if (roomCodeText != null) roomCodeText.text = $"방 번호: {code}";
    }

    // 이 클라이언트의 팀을 배정 (0: White, 1: Black). BasicSpawner가 인원 2명 도달 시 1회 호출.
    public void AssignLocalTeam(int team)
    {
        LocalTeam = team;
    }

    // 배정된 팀에 맞는 카메라만 켜고 반대쪽은 끈다 (백팀->기존 카메라, 흑팀->신규 카메라).
    public void AssignTeamCamera(int team)
    {
        if (whiteCamera != null) whiteCamera.SetActive(team == 0);
        if (blackCamera != null) blackCamera.SetActive(team == 1);
    }

    // "타이틀로 이동" 버튼 등으로 세션을 완전히 떠날 때 팀 배정을 초기화한다.
    // static 필드라 씬이 다시 로드돼도(도메인 리로드 없이는) 값이 그대로 남아있어,
    // 다음 세션이 AssignLocalTeam으로 새 값을 덮어쓰기 전까지 이전 팀 값이 잠깐 남는 것을 방지한다.
    public static void ResetLocalTeam()
    {
        LocalTeam = -1;
    }
    #endregion
}
