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
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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

    #region 재시작 릴레이
    // 게임오버 화면의 "게임 재시작" 버튼 클릭을 모든 클라이언트에 동일하게 전파한다.
    // 한쪽 클라이언트만 로컬로 GameStartController.StartMatch()를 호출하면 반대쪽은 게임오버 화면에
    // 그대로 멈춰있고 보드/턴/증강 상태가 서로 어긋나게 되므로, 반드시 양쪽이 동시에 동일한 리셋을
    // 수행하도록 RPC로 중계한다(기존 카드 선택 RPC들과 동일한 패턴 - 발신자 자신도 RpcTargets.All에
    // 포함되어 함께 리셋됨).
    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_RelayRestartMatch()
    {
        GameStartController.Instance?.StartMatch();
    }
    #endregion
}
