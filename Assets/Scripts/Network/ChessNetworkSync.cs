using Fusion;
using UnityEngine;

// 체스 보드 클릭(이동/스킬 대상 지정 등)과 턴 종료(시간 초과, 수동 턴 종료 버튼)를
// 모든 클라이언트에 동일하게 전파해 로컬에서만 진행되던 체스 로직을 두 플레이어 사이에 동기화하는 네트워크 릴레이 컴포넌트.
// 호스트가 게임 시작 시(인원 2명) 1개만 스폰하며, 클릭이 발생하면 좌표만 RPC로 전달하고
// 실제 처리(이동 규칙 검증, 캡처, 스킬 등)는 각 클라이언트가 동일한 로직(ChessInteractionManager)으로 재현한다.
//
// 스킬 버튼 클릭(대기 모드 진입/취소)이나 카드 선택(승급/증강/이진선택) 결과처럼
// "보드 좌표"만으로는 상대에게 전달되지 않는 로컬 UI 상호작용도 아래 RPC들을 통해 동일하게 전파한다.
public class ChessNetworkSync : NetworkBehaviour
{
    public static ChessNetworkSync Instance { get; private set; }

    #region 인스턴스 수명 관리
    // Fusion이 네트워크 스폰을 완료했을 때 호출 (일반 Awake보다 늦게, 네트워크 준비 후 실행됨)
    public override void Spawned()
    {
        Instance = this;

        // 내 닉네임을 상대에게 알린다(상대 화면의 닉네임 표시용).
        ShareLocalNickname();

        // 게스트(호스트가 아닌 쪽)는 스폰 직후 로비 입장 신고를 보낸다 -> 호스트가 게스트 슬롯을 채운다.
        if (!Runner.IsServer)
            RPC_LobbyGuestHello(PlayerProfile.Nickname);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
    #endregion

    #region 닉네임 교환
    // 내 팀 번호와 닉네임을 모든 클라이언트에 전파한다. 팀 배정 전이면(LocalTeam < 0) 보내지 않는다.
    public void ShareLocalNickname()
    {
        int team = GameStartController.LocalTeam;
        if (team < 0) return;

        RPC_ShareNickname(team, PlayerProfile.Nickname);
    }

    // 팀별 닉네임을 저장한다. 상대의 "새" 닉네임을 처음 받았다면 상대가 내 닉네임을 놓쳤을 수 있으므로
    // 내 닉네임을 한 번 더 보낸다(이미 같은 값이면 회신하지 않으므로 무한 반복되지 않는다).
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_ShareNickname(int team, string nickname)
    {
        bool changed = PlayerProfile.SetTeamNickname(team, nickname);

        if (changed && team != GameStartController.LocalTeam)
            ShareLocalNickname();
    }
    #endregion

    #region 보드 입력 / 턴 / 스킬 모드 릴레이
    // 로컬 플레이어가 보드를 클릭한 좌표(x, y)를 모든 클라이언트(자기 자신 포함)에 전파한다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RelayBoardClick(int x, int y)
    {
        ChessInteractionManager.Instance?.HandleRelayedClick(x, y);
    }

    // 턴 종료 요청(시간 초과, 수동 "턴 종료" 버튼)을 모든 클라이언트에 동일하게 전파한다.
    // 이 RPC 없이 각 클라이언트가 독립적으로 EndTurn()을 호출하면(특히 턴 타이머 시간 초과 시)
    // 두 화면의 턴이 서로 다른 시점에 넘어가 완전히 어긋나게 된다.
    // requestedTurnCount: 요청을 만든 시점의 GameManager.TurnCount. 양쪽 클라이언트가 거의 동시에
    // 같은 턴 종료를 요청해 RPC가 두 번 도착해도, GameManager.EndTurnFromNetwork가 이 값으로
    // "이미 처리된 요청인지"를 판별해 중복 종료를 막는다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RelayEndTurn(int requestedTurnCount)
    {
        GameManager.Instance?.EndTurnFromNetwork(requestedTurnCount);
    }

    // 스킬 버튼(나이트/비숍/룩/퀸/킹) 클릭에 의한 "대기 모드 진입/취소"를 모든 클라이언트에 동일하게 전파한다.
    // 보드 클릭과 달리 스킬 버튼 클릭은 로컬 UI 이벤트라서 좌표 릴레이만으로는 상대 화면에 반영되지 않으며,
    // 이 상태(ActiveSkillType 등)가 상대에게도 동일해야 이후의 보드 클릭 릴레이가 올바른 스킬로 해석된다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RelaySkillModeToggle(int team, int skillType)
    {
        SkillUIManager.Instance?.ApplySkillModeToggle(team, (PendingSkillType)skillType);
    }
    #endregion

    #region 카드 선택 결과 릴레이
    // 폰 승급 카드 선택 결과(4종 중 택1)를 모든 클라이언트에 동일하게 전파해 동일한 기물로 승급되도록 한다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RelayPromotionCardChoice(int team, int x, int y, int chessPieceType)
    {
        CardSelectionManager.Instance?.ApplyPromotionChoice(team, new Vector2Int(x, y), (ChessPieceType)chessPieceType);
    }

    // 증강 카드 선택 결과를 모든 클라이언트에 동일하게 전파한다.
    // 각 클라이언트가 독립적으로(무작위) 뽑은 카드 목록이 서로 다를 수 있으므로,
    // 실제로 "결정된 증강 ID" 자체를 전달해 양쪽이 동일한 증강을 획득하도록 한다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RelayAugmentChoice(int team, string augmentId)
    {
        CardSelectionManager.Instance?.ApplyAugmentChoice(team, augmentId);
    }

    // 이진 선택(나이트/비숍 등) 카드의 선택 결과를 모든 클라이언트에 동일하게 전파한다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RelayPieceChoice(int team, int chosenPromotableType)
    {
        CardSelectionManager.Instance?.ApplyPieceChoice(team, (PromotablePieceType)chosenPromotableType);
    }
    #endregion

    #region 우노 카드 릴레이
    // 카드 내기/뽑기 요청을 모든 클라이언트에 같은 순서로 전달한다 (각자 같은 시드의 UnoMatch에 적용).
    // color: 와일드 색상(0~3), -1이면 자동 선택. 4단계에서 색상 선택 UI가 이 값을 채운다.
    // ── 호스트 권한 구조 (7단계) ──
    // 게스트는 "하고 싶은 일(명령)"만 호스트에게 보내고, 호스트가 검증·적용한 뒤 결과 이벤트를 게스트에게 보낸다.
    // 상대 손패와 뽑을 카드 더미는 호스트만 알고, 게스트에게는 "내 손패에 들어온 카드"만 이벤트에 실려 간다.
    // kind: UnoTurnController.K_* (0 카드 내기, 1 색 선택, 2 뽑기, 3 UNO 경쟁 클릭, 4 부활 기물 선택)

    // 게스트 -> 호스트 명령
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_UnoCmd(int kind, int team, int a, int b)
    {
        UnoTurnController.Instance?.OnCommandFromGuest(kind, team, a, b);
    }

    // 호스트 -> 게스트 결과 이벤트. code = 낸 카드 코드, secrets = 게스트 손패에 들어온 카드들 (게스트 본인에게만 의미 있는 값)
    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
    public void RPC_UnoEvt(int kind, int team, int a, int b, int code, string secrets)
    {
        UnoTurnController.Instance?.ReceiveEvent(kind, team, a, b, code, secrets);
    }

    // 호스트 -> 게스트 첫 배분: 게스트의 시작 손패와 시작 카드. matchId는 판 구분용 (MatchSession.AugmentSeed)
    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
    public void RPC_UnoDeal(int matchId, string guestHand, int startCard)
    {
        UnoTurnController.ReceiveDeal(matchId, guestHand, startCard);
    }
    #endregion

    #region 재시작 릴레이
    // 게임오버 화면의 "게임 재시작" 버튼 클릭을 모든 클라이언트에 동일하게 전파한다.
    // 한쪽 클라이언트만 로컬로 GameStartController.StartMatch()를 호출하면 반대쪽은 게임오버 화면에
    // 그대로 멈춰있고 보드/턴/증강 상태가 서로 어긋나게 되므로, 반드시 양쪽이 동시에 동일한 리셋을
    // 수행하도록 RPC로 중계한다(기존 카드 선택 RPC들과 동일한 패턴 - 발신자 자신도 RpcTargets.All에
    // 포함되어 함께 리셋됨).
    // augmentSeed: 재시작을 누른 쪽이 한 번 뽑아 전파하는 새 매치별 증강 등급 난수 시드. 이 값을 먼저
    // MatchSession에 반영한 뒤 StartMatch()를 호출해야, 재시작한 새 매치의 첫 증강 체크포인트도
    // 이전 매치와 같은 등급으로 고정되지 않는다(MatchSession.AugmentSeed 주석 참고).
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RelayRestartMatch(int augmentSeed)
    {
        MatchSession.AugmentSeed = augmentSeed;
        GameStartController.Instance?.StartMatch();
    }
    #endregion

    #region 로비 동기화 (호스트 권한자)
    // 게스트 -> 호스트: 로비 입장 신고(닉네임). 호스트만 처리하고 전체 스냅샷을 다시 방송한다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_LobbyGuestHello(string nickname)
    {
        if (!Runner.IsServer) return;
        if (MatchSession.InMatch) return;
        LobbyState.GuestNick = PlayerProfile.Normalize(nickname);
        LobbyState.GuestPresent = true;
        LobbyState.GuestPick = LobbyState.PickNone;
        LobbyState.Notify();
        BroadcastLobby();
    }

    // 게스트 -> 호스트: 진영 선택 요청. 호스트가 순서대로 검증(같은 색 중복 거부)한 뒤 스냅샷을 방송한다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_LobbyRequestPick(int pick)
    {
        if (!Runner.IsServer) return;
        if (MatchSession.InMatch) return;
        LobbyState.TrySetPick(false, pick);
        BroadcastLobby(); // 거부된 경우에도 게스트 화면을 권한자 상태로 되돌리기 위해 항상 방송
    }

    // 호스트: 현재 로비 상태 전체(닉네임/선택/규칙)를 모든 피어에 방송한다.
    public void BroadcastLobby()
    {
        if (!Runner.IsServer) return;
        MatchSettings.Normalize();
        // flags: 2 = 증강 선택 시간 제한, 8 = 턴 시간 제한 (1/4는 예전 증강/스킬 비트였고, 이제 모드(int)에서 파생한다)
        int flags = (MatchSettings.AugmentTimeLimit ? 2 : 0) | (MatchSettings.TurnTimeLimit ? 8 : 0);
        RPC_LobbySnapshot(LobbyState.HostNick, LobbyState.GuestNick, LobbyState.GuestPresent,
            LobbyState.HostPick, LobbyState.GuestPick, flags, MatchSettings.MaxAugments, MatchSettings.TurnSeconds,
            (int)MatchSettings.Mode, MatchSettings.UnoStartHand, MatchSettings.UnoMaxHand);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_LobbySnapshot(string hostNick, string guestNick, bool guestPresent, int hostPick, int guestPick,
                                  int flags, int maxAugments, int turnSeconds,
                                  int mode, int unoStartHand, int unoMaxHand)
    {
        if (Runner.IsServer) return; // 호스트는 이미 권한자 상태를 가지고 있다
        if (MatchSession.InMatch) return;
        MatchSettings.Apply((MatchMode)mode, maxAugments, (flags & 2) != 0, (flags & 8) != 0, turnSeconds, unoStartHand, unoMaxHand);
        LobbyState.ApplySnapshot(hostNick, guestNick, guestPresent, hostPick, guestPick);
    }

    // 호스트 -> 모두: 게임 시작. 호스트가 정한 호스트 팀(0/1)을 받아 각자 자기 팀/닉네임을 확정한다.
    // 이 RPC 이후 호스트가 GameScene을 Additive로 로드하고, GameStartController가 MatchSession을 소비한다.
    // augmentSeed: 호스트가 한 번 뽑아 함께 전파하는 매치별 증강 등급 난수 시드(MatchSession.AugmentSeed
    // 주석 참고). RPC 파라미터는 호출한 쪽에서 계산되어 그대로 전송되므로, 수신하는 양쪽 클라이언트가
    // 별도 조율 없이도 항상 같은 값을 받는다.
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_LobbyStart(int hostTeam, int augmentSeed)
    {
        if (MatchSession.InMatch) return;
        MatchSession.Begin(hostTeam, Runner.IsServer, augmentSeed);
    }
    #endregion
}
