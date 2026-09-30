using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;
using System.Linq;
using System;

// Photon Fusion 네트워크 세션 시작(호스트/참가)과 팀/대기 상태 갱신을 담당하는 클래스.
// INetworkRunnerCallbacks의 필수 콜백들을 구현하며, 실제 게임에서 사용하지 않는 콜백은 빈 구현으로 남겨둔다.
public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    // 게임오버 UI의 "타이틀로 이동" 버튼처럼, 이 클라이언트의 NetworkRunner를 직접 다뤄야 하는
    // 다른 클래스에서 GameObject.Find 없이 접근할 수 있도록 하는 싱글턴 참조.
    public static BasicSpawner Instance { get; private set; }

    #region 인스펙터 설정값
    [SerializeField] private string lobbySceneName = "LobbyScene"; // Fusion 세션 시작 시 가장 먼저 로드할 로비 씬 이름
    [SerializeField] private string gameSceneName = "GameScene"; // 로비에서 "게임 시작" 시 로드할 게임 씬 이름
    [SerializeField] private string titleSceneName = "StartScene"; // "타이틀로 이동" 시 되돌아갈 타이틀 씬 이름
    [SerializeField] private UnityEngine.UI.Text roomCodeDisplayText; // 호스트가 생성한 방 번호를 보여줄 텍스트 (한글 표시를 위해 legacy Text 사용)
    [SerializeField] private NetworkPrefabRef chessSyncPrefab; // 체스 클릭 동기화용 네트워크 오브젝트 (호스트가 1회 스폰)

    [Header("시작 메뉴 정리 (세션 시작 후 비활성화)")]
    // GameScene은 Additive로 로드되어 StartScene이 언로드되지 않는다. 시작 메뉴 Canvas가 Screen Space -
    // Overlay라 그대로 두면 게임 화면 위에 계속 덮여 "Host를 눌러도 게임 화면으로 안 넘어가는" 것처럼 보이고,
    // EventSystem/MainCamera 중복으로 인한 경고와 입력 충돌도 생긴다. 세션 시작 성공 시 셋 다 꺼준다.
    [SerializeField] private GameObject startMenuCanvas;      // StartScene의 Canvas (Host/Join 버튼 등)
    [SerializeField] private GameObject startMenuEventSystem; // StartScene의 EventSystem
    [SerializeField] private GameObject startMenuCamera;      // StartScene의 Main Camera
    #endregion

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
    #endregion

    #region 세션 시작 / 참가
    private const int RoomCodeMin = 0;
    private const int RoomCodeMax = 10000; // 0000 ~ 9999, 4자리 방 번호
    private const int RoomPlayerCount = 2; // 방 하나당 최대 인원 (고정 2명)

    private NetworkRunner _runner;
    private bool _chessSyncSpawned; // ChessNetworkSync 중복 스폰 방지 플래그
    private string _roomCode;       // 현재 세션의 방 번호 (GameScene 대기 화면 상단에 표시)

    // NetworkRunner를 생성하고 지정된 모드(Host/Client)로 지정된 방 번호(세션 이름)의 세션을 시작/참가한다.
    async void StartGame(GameMode mode, string sessionCode)
    {
        // OnPlayerJoined 콜백이 아래 await가 재개되기 전에 먼저 들어올 수 있으므로,
        // UpdateGameStartState에서 바로 참조할 수 있도록 가장 먼저 저장해둔다.
        _roomCode = sessionCode;

        // 새 세션이므로 로비/매치 공유 상태를 초기화한다(호스트는 자기 닉네임을 로비 슬롯에 채워둔다).
        MatchSession.Reset();
        LobbyState.Reset();
        if (mode == GameMode.Host)
            LobbyState.HostNick = PlayerProfile.Nickname;

        // Create the Fusion runner. 체스 동작은 전부 RPC(ChessNetworkSync)로 동기화되고
        // 틱 단위 입력(OnInput)이 필요 없으므로 ProvideInput은 사용하지 않는다.
        _runner = gameObject.AddComponent<NetworkRunner>();

        // 지정된 방 번호(세션 이름)로 세션을 시작(Host)하거나 참가(Client)한다. 인원은 2명으로 제한된다.
        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = sessionCode,
            PlayerCount = RoomPlayerCount,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (!result.Ok)
        {
            Debug.LogError($"[BasicSpawner] 세션 시작/참가 실패 (mode={mode}, code={sessionCode}): {result.ShutdownReason}");
            return;
        }

        // 세션 시작에 성공했으므로 시작 메뉴(StartScene UI/EventSystem/Camera)를 정리한다.
        if (startMenuCanvas != null) startMenuCanvas.SetActive(false);
        if (startMenuEventSystem != null) startMenuEventSystem.SetActive(false);
        if (startMenuCamera != null) startMenuCamera.SetActive(false);

        // 로비 씬은 씬 권한(호스트)만 로드하며, LoadSceneMode.Additive로 로드해 현재 씬(StartScene)을
        // 언로드하지 않는다. 참가자(클라이언트)는 별도 호출 없이 자동으로 동기화되어 함께 로드된다.
        // (예전 코드는 StartGameArgs.Scene에 직접 씬을 지정해 기본 모드(Single)로 로드되어
        //  StartScene 자체가 언로드되는 문제가 있었다 - BasicSpawner도 함께 사라져 대기 화면/팀
        //  배정 로직이 전부 동작하지 않게 됨)
        if (_runner.IsSceneAuthority)
        {
            var scene = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{lobbySceneName}.unity"));
            if (scene.IsValid)
            {
                _runner.LoadScene(scene, LoadSceneMode.Additive);
            }
        }
    }

    // 이 클라이언트가 호스트(서버)인지 (로비 UI에서 호스트 전용 조작 구분용)
    public bool IsHost => _runner != null && _runner.IsServer;

    // 현재 방 번호 (로비 UI 상단 표시용)
    public string RoomCode => _roomCode;

    // 로비 "게임 시작": 호스트(씬 권한자)가 GameScene을 Additive로 로드하면 게스트도 자동으로 따라 로드한다.
    // 로비 씬은 언로드하지 않고(GameStartController가 시작 시 숨김) 그대로 둔다.
    public void LoadGameScene()
    {
        if (_runner == null || !_runner.IsSceneAuthority) return;
        var scene = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{gameSceneName}.unity"));
        if (scene.IsValid)
            _runner.LoadScene(scene, LoadSceneMode.Additive);
    }

    // 호스트로 새 방을 생성: 4자리 방 번호를 무작위로 만들고 화면에 표시한 뒤 세션을 시작한다.
    public void HostGame()
    {
        string code = UnityEngine.Random.Range(RoomCodeMin, RoomCodeMax).ToString("0000");

        if (roomCodeDisplayText != null)
        {
            roomCodeDisplayText.text = $"생성된 방 번호: {code}";
        }

        StartGame(GameMode.Host, code);
    }

    // 클라이언트로 입력받은 방 번호의 세션에 참가 (방 번호 입력 UI의 확인 버튼에서 호출)
    public void JoinGame(string sessionCode)
    {
        StartGame(GameMode.Client, sessionCode);
    }
    #endregion

    #region 세션 종료 (타이틀로 이동)
    // GoToTitle()이 스스로 Runner.Shutdown()을 호출한 경우, 그 결과로 들어오는 OnShutdown 콜백을
    // "예기치 않은 세션 종료(호스트가 사라짐 등)"로 오인해 HandleUnexpectedSessionEnd가 중복
    // 처리하지 않도록 구분하는 플래그.
    private bool _intentionalShutdown;

    // GoToTitle()과 HandleUnexpectedSessionEnd()가 공통으로 수행하는 "타이틀 화면으로 복귀" 뒷정리
    // (로컬 팀 배정 초기화 + StartScene을 Single 모드로 로드해 현재 로드된 모든 씬을 언로드).
    // NetworkRunner 자체의 정리(있으면 Shutdown, 없으면 스킵)는 두 호출부의 상황이 서로 달라
    // (하나는 아직 살아있는 러너를 직접 종료해야 하고, 다른 하나는 이미 종료된 러너 참조만 정리하면
    // 됨) 각 호출부에 남겨두고, 이 메서드는 그 이후의 공통 부분만 담당한다.
    private void ReturnToTitleScreen()
    {
        GameStartController.ResetLocalTeam();
        MatchSession.Reset();
        LobbyState.Reset();
        PlayerProfile.ClearTeamNicknames(); // 이전 게임의 팀별 닉네임 초기화
        SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
    }

    // 게임오버 UI의 "타이틀로 이동" 버튼과, 매치 도중 참가자가 나가 인원이 2명 미만이 된 경우(아래
    // OnPlayerLeft)에서 공통으로 호출. 이 클라이언트의 세션을 완전히 종료하고 타이틀 화면으로 돌아간다.
    //
    // NetworkRunner를 정리하지 않고 그냥 씬만 갈아치우면 Fusion 세션/소켓이 좀비 상태로 남아
    // 이후 재호스팅/재접속 시 네트워킹 상태가 꼬일 수 있으므로, 반드시 Runner.Shutdown()으로
    // 먼저 세션을 정상 종료한 뒤에 씬을 로드한다.
    // LoadSceneMode.Single로 로드해 현재 로드되어 있는 모든 씬(숨겨진 StartScene + Additive
    // GameScene)을 전부 언로드하고 타이틀 씬을 완전히 새로 불러오므로, 이 클라이언트의 모든
    // 매니저/UI 상태가 앱을 껐다 켠 것과 동일하게 깨끗한 초기 상태로 돌아간다.
    public async void GoToTitle()
    {
        _intentionalShutdown = true;

        if (_runner != null)
        {
            await _runner.Shutdown();
            _runner = null;
            _chessSyncSpawned = false;
        }

        ReturnToTitleScreen();
    }
    #endregion

    #region 대기 상태 갱신
    // 새 플레이어가 접속하면 현재 접속 인원에 따라 대기 상태를 갱신한다.
    // (예전에는 여기서 플레이어 캐릭터(큐브) 프리팹을 스폰했으나, 실제 체스 게임과 무관한 Fusion 템플릿
    //  잔재였고 게임 시작 시 화면에 불필요한 큐브가 보이는 원인이었다 - 스폰 로직 제거)
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // 호스트 본인이 세션에 들어온 시점에 ChessNetworkSync를 1회 스폰한다(세션 전체 동안 유지 - 로비 RPC도 사용).
        if (runner.IsServer && player == runner.LocalPlayer && !_chessSyncSpawned)
        {
            runner.Spawn(chessSyncPrefab);
            _chessSyncSpawned = true;
        }
    }

    // 참가자가 나가면(호스트는 남아있는 경우) 호출되는 콜백. 이 콜백은 세션이 살아있는 쪽(=호스트)
    // 에서만 발생한다 - 참가자 입장에서 호스트가 사라지는 경우는 세션 자체가 끝나버리므로
    // OnPlayerLeft가 아니라 OnShutdown/OnDisconnectedFromServer로 전달된다(아래
    // HandleUnexpectedSessionEnd 참고).
    //
    // 방 정원이 2명으로 고정되어 있어 누군가 나가면 인원은 항상 2명 미만이 되므로(호스트 혼자
    // 대기하다 나가는 경우는 애초에 존재하지 않음 - 나갈 상대가 있으려면 인원이 2명이었어야 함),
    // 별도 인원 체크 없이 곧바로 방을 완전히 종료하고 타이틀 화면으로 복귀한다("호스트/참가 상관없이
    // 인원이 2명 미만이 되면 방이 사라지고 시작 화면으로 복귀" 요구사항 - 예전에는 세션/방 번호를
    // 그대로 유지한 채 대기 화면만 다시 보여주는 "가벼운" 경로였으나, 두 이탈 경로의 결과를
    // 통일하기 위해 제거함).
    //
    // 2026-10-01 로비 도입: 로비 단계(MatchSession.InMatch == false)에서 게스트가 나가면 호스트는 로비에 남고
    // 게스트 슬롯/선택만 초기화한다(D-1). 게임 단계에서 나가면 기존처럼 양쪽 모두 타이틀로 복귀한다.
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (!MatchSession.InMatch && runner.IsServer)
        {
            LobbyState.ClearGuest();
            ChessNetworkSync.Instance?.BroadcastLobby();
            return;
        }
        GoToTitle();
    }

    #endregion

    #region 예기치 않은 세션 종료 처리 (호스트가 사라져 세션이 끊긴 참가자 측)
    // OnShutdown/OnDisconnectedFromServer가 같은 종료에 대해 중복으로 들어올 수 있어
    // 1회만 처리하도록 막는 재진입 방지 플래그.
    private bool _handledUnexpectedEnd;

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        HandleUnexpectedSessionEnd($"OnShutdown:{shutdownReason}");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        HandleUnexpectedSessionEnd($"OnDisconnectedFromServer:{reason}");
    }

    // 이 프로젝트는 호스트 마이그레이션을 구현하지 않았으므로(OnHostMigration은 빈 구현), 호스트가
    // 사라지면(연결 끊김/앱 종료 등) 참가자(클라이언트) 입장에선 "플레이어 한 명이 나간 것"이 아니라
    // 세션 전체가 끝나버린다 - 그래서 OnPlayerLeft가 아니라 OnShutdown/OnDisconnectedFromServer로
    // 전달된다. "타이틀로 이동" 버튼처럼 이 클라이언트가 스스로 의도적으로 Shutdown()을 호출한
    // 경우(_intentionalShutdown)는 GoToTitle()이 이미 자체적으로 처리 중이므로 여기서 다시
    // 손대지 않는다.
    //
    // 남아있는 참가자는 더 이상 유효한 세션/방 번호가 없으므로(호스트 없이는 이어갈 방법이 없다)
    // 같은 자리에서 대기 화면을 계속 보여줄 수 없다. 가장 견고한 방법은 GoToTitle()과 동일하게
    // 씬을 전부 새로 로드해 모든 매니저 상태를 깨끗하게 초기화하고 타이틀 화면(StartScene)에
    // 그대로 남겨두는 것이다 - 새 방을 자동으로 재호스팅하지 않는다("호스트/참가 상관없이 인원이
    // 2명 미만이 되면 방이 사라지고 시작 화면으로 복귀" 요구사항: 예전에는 여기서 남은 참가자를
    // 곧바로 새 방의 호스트로 자동 전환시켰으나, 두 이탈 경로의 결과를 통일하기 위해 제거함).
    //
    // 이 콜백이 호출되는 시점엔 Fusion이 이미 이 클라이언트의 러너를 종료 처리한 뒤이므로(그 결과로
    // 이 콜백 자체가 발생한 것), GoToTitle()과 달리 Runner.Shutdown()을 다시 호출할 필요는 없다 -
    // 참조만 정리하고 공통 뒷정리(ReturnToTitleScreen)로 넘어간다.
    private void HandleUnexpectedSessionEnd(string reasonLabel)
    {
        if (_intentionalShutdown)
        {
            _intentionalShutdown = false;
            return;
        }

        if (_handledUnexpectedEnd) return;
        _handledUnexpectedEnd = true;

        Debug.Log($"[BasicSpawner] 예기치 않은 세션 종료 감지({reasonLabel}) - 방을 종료하고 시작 화면으로 복귀");

        _runner = null;
        _chessSyncSpawned = false;
        ReturnToTitleScreen();
    }
    #endregion

    #region INetworkRunnerCallbacks 미사용 콜백
    // 아래 콜백들은 이 프로젝트에서 별도 처리가 필요 없어 빈 구현으로 남겨둔 인터페이스 필수 메서드들이다.
    // (체스 동작은 ChessNetworkSync의 RPC로만 동기화되므로 OnInput/NetworkInputData는 사용하지 않는다)
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    #endregion
}
