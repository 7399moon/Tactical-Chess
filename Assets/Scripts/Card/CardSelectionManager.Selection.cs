using System;
using System.Collections;
using UnityEngine;

// CardSelectionManager의 카드 클릭 이후 처리 partial 파일.
// 클릭 연출(애니메이션), 선택 결과 반영(프로모션/증강/이진선택), 카드 인터랙션 제어를 담당한다.
//
// 네트워크 동기화 설계: 카드를 클릭한 클라이언트는 CloseCardSelection()(또는 네트워크 대전 중
// 증강 선택의 경우 OnCardSelected 자체)에서 "확정된 결과"만 계산해 ChessNetworkSync RPC로
// 전파하고, 실제 게임 상태 반영(승급 실행/증강 등록/즉시 효과/콜백 호출)은 ApplyPromotionChoice/
// ApplyAugmentChoice/ApplyPieceChoice에서 수행한다. 이 세 메서드는 RPC를 통해 양쪽 클라이언트
// (클릭한 쪽 포함, RpcTargets.All)에서 항상 동일하게 호출되므로 게임 상태 자체는 항상 일치한다.
//
// 2026-09-18 재설계: 프로모션/이진선택은 원래부터 "한 팀만의 결정"이라 문제가 없었지만, 증강
// 선택은 매 체크포인트마다 White/Black 두 팀이 각자 독립적으로 카드를 골라야 하는데, 기존에는
// ApplyAugmentChoice가 무조건 패널을 닫고(FinalizeCardSelectionUI) 남은 팀이 있으면 곧바로 그
// 팀의 선택 창을 열어버려서(ShowCardSelection) "누가 골랐든 양쪽 화면이 전부 새로 고침되는" 버그가
// 있었다. 지금은 "게임 상태 반영(AddAugment/즉시 효과/pendingAugmentTeams 갱신)"은 여전히 항상
// 양쪽에서 동일하게 실행하되, "내 화면에 패널을 띄우거나 닫는 것"은 team == LocalTeam(또는 로컬
// 테스트 모드)일 때만 수행하도록 분리했다. 자세한 흐름은 OnCardSelected/ApplyAugmentChoice 주석 참고.
//
// 2026-09-18 후속 수정(이진 선택으로 이어지는 증강): 위 재설계 직후, "이진 선택(ShowPieceChoice)이
// 뜨는 증강 카드를 고르면 이진 선택 화면이 아예 뜨지 않고 턴이 바로 진행되는" 회귀 버그가
// 있었다. 원인은 ApplyAugmentChoice가 team을 pendingAugmentTeams에서 곧바로 제거해 "이 팀은
// 끝났다"고 판단해 버리는 데 있었다 - 그 증강이 실제로는 아직 안 끝난(이진 선택이 남은) 채로.
// teamsAwaitingPieceChoice가 "이진 선택이 아직 안 끝난 팀"을 추적해 이 문제를 해결한다: 그
// 목록에 있는 동안은 pendingAugmentTeams에서 제거하지 않고, 체크포인트 완료 처리(패널 닫기/
// 타이머 재개/다음 팀으로 체이닝)도 미룬다. ApplyPieceChoice가 실제로 이진 선택을 완료했을 때
// 비로소 이어서 처리한다. 자세한 흐름은 ApplyAugmentChoice/ApplyPieceChoice/HandleAugmentTeamDone
// 주석 참고.
public partial class CardSelectionManager
{
    #region 선택 및 연출
    // 카드 클릭 시 애니메이션 및 연출 시작
    private void OnCardSelected(CardUI selectedCard)
    {
        // 여러 카드를 빠르게 클릭하는 것 방지
        if (isClosing) return;
        isClosing = true;

        currentSelectedCard = selectedCard;
        SetCardsInteractable(false);

        // 네트워크 대전 중 증강 선택은 "내가 고른 뒤 상대도 고를 때까지 기다려야" 하는 화면이라,
        // 기존처럼 클릭 즉시 카드가 날아가는 애니메이션을 재생하고 패널을 닫아버리면 안 된다.
        // 대신: 체크 표시 + "상대방 선택 대기중" 상태로 전환하고, 결과만 즉시 상대에게 전파한다.
        // 애니메이션/패널 종료는 ApplyAugmentChoice(및 이진 선택으로 이어졌다면 ApplyPieceChoice)가
        // 이 체크포인트가 완전히 끝난 것을 확인한 뒤에 재생한다.
        if (currentMode == CardSelectionMode.Augment && GameStartController.LocalTeam >= 0)
        {
            SendSelectedAugmentCard(selectedCard);
            return;
        }

        StartCoroutine(AnimateCardsRoutine(selectedCard, CloseCardSelection));
    }

    // 선택된 카드의 augmentId를 찾아 확정 결과를 전파한다(네트워크 대전 중 증강 선택 전용).
    // 애니메이션/패널 종료는 이 시점에 하지 않는다 - 체크 표시 + 대기 상태만 로컬에 반영한다.
    private void SendSelectedAugmentCard(CardUI selectedCard)
    {
        int cardIndex = Array.IndexOf(cardList, selectedCard);
        if (currentOfferedAugments != null && cardIndex >= 0 && cardIndex < currentOfferedAugments.Count)
        {
            AugmentData chosen = currentOfferedAugments[cardIndex];
            if (chosen != null && !string.IsNullOrEmpty(chosen.augmentId))
            {
                selectedCard.SetSelected(true);
                SetWaitingStatus(true);
                SendAugmentChoice(pendingPromotionTeam, chosen.augmentId);
                return;
            }
        }

        // 유효한 카드를 찾지 못한 예외 상황: 대기 상태로 남겨두지 말고 패널을 정리한다.
        FinalizeCardSelectionUI(wasPromotion: false);
    }

    // 선택한 카드는 상단으로, 미선택 카드는 하단으로 이동하는 애니메이션 코루틴.
    // onComplete: 애니메이션이 끝난 뒤 실행할 마무리 처리. 카드를 직접 클릭한 일반 흐름(프로모션/
    // 이진선택/로컬 테스트 증강)에서는 CloseCardSelection을 넘겨 "결과 계산 + 전파"까지 수행하고,
    // 네트워크 대전 증강 선택에서 두 팀 모두 완료된 뒤 재생하는 마무리 애니메이션에서는 이미 결과를
    // 전파한 뒤이므로 FinalizeCardSelectionUI만 넘겨 패널 정리만 수행한다(HandleAugmentTeamDone 참고).
    private IEnumerator AnimateCardsRoutine(CardUI selectedCard, System.Action onComplete)
    {
        for (int i = 0; i < cardList.Length; i++)
        {
            CardUI card = cardList[i];
            if (card == null) continue;

            startPositions[i] = card.transform.localPosition;
            targetPositions[i] = startPositions[i] + (card == selectedCard ? Vector3.up * moveUpDistance : -Vector3.up * moveDownDistance);
        }

        float elapsed = 0f;

        while (elapsed < animDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float smoothT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / animDuration));

            for (int i = 0; i < cardList.Length; i++)
            {
                if (cardList[i] != null)
                    cardList[i].transform.localPosition = Vector3.Lerp(startPositions[i], targetPositions[i], smoothT);
            }

            yield return null;
        }

        onComplete?.Invoke();
    }

    // 카드 선택 결과를 계산해 상대에게도 전파한다(로컬 테스트 중이면 직접 적용).
    // 실제 게임 상태 반영은 ApplyXxxChoice에서 이뤄지므로, 여기서는 어떤 결과가 나왔는지만 확정한다.
    // (네트워크 대전 중 증강 선택은 OnCardSelected/SendSelectedAugmentCard에서 애니메이션 전에
    // 이미 결과를 전파하므로 이 메서드를 거치지 않는다.)
    void CloseCardSelection()
    {
        if (currentMode == CardSelectionMode.Promotion && currentSelectedCard != null)
        {
            int cardIndex = Array.IndexOf(cardList, currentSelectedCard);
            if (cardIndex >= 0 && cardIndex < promotionOptions.Count)
            {
                PromotablePieceType selectedPromotable = promotionOptions[cardIndex].pieceType;
                ChessPieceType targetType = ConvertToChessPieceType(selectedPromotable, pendingPromotionTeam);
                SendPromotionChoice(pendingPromotionTeam, pendingPromotionTile, targetType);
                return;
            }
        }
        else if (currentMode == CardSelectionMode.Augment && currentSelectedCard != null)
        {
            // 로컬 테스트 모드(LocalTeam < 0)에서만 이 경로를 탄다(네트워크 대전 중에는
            // OnCardSelected가 애니메이션 전에 SendSelectedAugmentCard로 이미 처리했다).
            int cardIndex = Array.IndexOf(cardList, currentSelectedCard);
            if (currentOfferedAugments != null && cardIndex >= 0 && cardIndex < currentOfferedAugments.Count)
            {
                AugmentData chosen = currentOfferedAugments[cardIndex];
                if (chosen != null && !string.IsNullOrEmpty(chosen.augmentId))
                {
                    SendAugmentChoice(pendingPromotionTeam, chosen.augmentId);
                    return;
                }
            }
        }
        else if (currentMode == CardSelectionMode.PieceChoice && currentSelectedCard != null)
        {
            int cardIndex = Array.IndexOf(cardList, currentSelectedCard);
            if (pieceChoiceOptions != null && cardIndex >= 0 && cardIndex < pieceChoiceOptions.Length && pieceChoiceOptions[cardIndex] != null)
            {
                SendPieceChoice(pendingPieceChoiceTeam, pieceChoiceOptions[cardIndex].pieceType);
                return;
            }
        }

        // 유효한 선택 결과를 계산하지 못한 예외 상황: 패널만 정리한다.
        FinalizeCardSelectionUI(wasPromotion: false);
    }

    // 아래 3개 Send 메서드: 로컬에서 확정된 카드 선택 결과를 상대에게도 전파한다.
    // 네트워크 대전이 아니면(로컬 테스트) RPC 없이 바로 적용한다.
    private void SendPromotionChoice(int team, Vector2Int tile, ChessPieceType type)
    {
        if (GameStartController.LocalTeam >= 0 && ChessNetworkSync.Instance != null)
            ChessNetworkSync.Instance.RPC_RelayPromotionCardChoice(team, tile.x, tile.y, (int)type);
        else
            ApplyPromotionChoice(team, tile, type);
    }

    private void SendAugmentChoice(int team, string augmentId)
    {
        if (GameStartController.LocalTeam >= 0 && ChessNetworkSync.Instance != null)
            ChessNetworkSync.Instance.RPC_RelayAugmentChoice(team, augmentId);
        else
            ApplyAugmentChoice(team, augmentId);
    }

    private void SendPieceChoice(int team, PromotablePieceType type)
    {
        if (GameStartController.LocalTeam >= 0 && ChessNetworkSync.Instance != null)
            ChessNetworkSync.Instance.RPC_RelayPieceChoice(team, (int)type);
        else
            ApplyPieceChoice(team, type);
    }

    // ChessNetworkSync RPC를 통해(또는 로컬 테스트 시 직접) 호출되어, 확정된 승급을 양쪽 클라이언트에 동일하게 적용한다.
    public void ApplyPromotionChoice(int team, Vector2Int tile, ChessPieceType type)
    {
        ChessBoard boardToUse = chessBoard != null ? chessBoard : ChessBoard.Instance;
        boardToUse?.PromotePieceAt(tile.x, tile.y, type, team);
        FinalizeCardSelectionUI(wasPromotion: true);
    }

    // ChessNetworkSync RPC를 통해(또는 로컬 테스트 시 직접) 호출되어, 확정된 증강을 양쪽 클라이언트에 동일하게 적용한다.
    //
    // "게임 상태 반영(공유, 항상 양쪽에서 동일하게 실행)"과 "내 화면의 패널/체크 표시(로컬 UI)"를
    // 분리한다: AddAugment/즉시 효과 적용은 team과 무관하게 항상 실행하지만, pendingAugmentTeams에서
    // 이 team을 실제로 "제거"하고 체크포인트 완료 마무리(HandleAugmentTeamDone)를 실행하는 것은
    // 이 증강이 이진 선택(ShowPieceChoice)으로 이어지지 않았을 때에만 한다 - 이어졌다면
    // teamsAwaitingPieceChoice에 team이 추가되어 있으므로, 실제 이진 선택이 끝날 때(ApplyPieceChoice)
    // 까지 "이 팀은 아직 끝나지 않음"으로 취급한다. (예전에는 여기서 무조건 team을 제거하고 마무리
    // 처리를 해버려서, 이진 선택 화면이 뜨기도 전에 체크포인트가 끝난 것으로 처리되어 이진 선택이
    // 아예 뜨지 않고 턴이 바로 진행되는 버그가 있었다.)
    //
    // 패널을 닫거나 다음 팀의 선택 창을 여는 것 등 실제 화면 조작은 HandleAugmentTeamDone에서
    // 수행한다 - 로컬 테스트 모드에서는 예전처럼 그 자리에서 이어서 하고, 네트워크 대전 모드에서는
    // 오직 "이 체크포인트의 모든 대상 팀이 완전히 끝났을 때"에만(공유되는 시점) 내 화면을 정리한다.
    // 그래야 상대 팀의 선택이 내 화면(체크 표시/대기 상태)을 건드리지 않는다.
    public void ApplyAugmentChoice(int team, string augmentId)
    {
        // 2026-09-23 수정: 실제로 선택이 확정되는 이 시점에만 소모 처리한다. 카드 뽑기(PickAugments)
        // 단계에서는 더 이상 usedAugmentIds에 추가하지 않으므로, 오퍼에 함께 제시되었지만 선택되지
        // 않은 나머지 카드들은 다음 체크포인트에 다시 등장할 수 있다.
        if (!string.IsNullOrEmpty(augmentId))
            usedAugmentIds.Add(augmentId);

        AugmentManager.Instance?.AddAugment(team, augmentId);

        ChessBoard boardToUse = chessBoard != null ? chessBoard : ChessBoard.Instance;

        if (GameStartController.LocalTeam < 0)
        {
            // 로컬 테스트 모드: 패널을 먼저 닫아야 한다. ApplyImmediateEffect가 ShowPieceChoice
            // 등으로 새 UI 상태(IsSelecting = true 등)를 다시 열 수 있는데, 순서가 바뀌면 방금 연
            // 새 상태를 여기서 곧바로 덮어써 버리게 된다. 타이머 재개 여부는 아래
            // FinalizeAugmentTeamIfReady/HandleAugmentTeamDone이 판단하므로 여기서는 재개하지 않는다.
            FinalizeCardSelectionUI(wasPromotion: false, resumeTimer: false);
            Augmenteffects.ApplyImmediateEffect(augmentId, team, boardToUse);
            FinalizeAugmentTeamIfReady(team);
            return;
        }

        // 네트워크 대전 모드: 이 RPC는 내 팀의 선택이든 상대 팀의 선택이든 양쪽 클라이언트에서
        // 항상 호출된다. 게임 상태 반영(AddAugment/즉시 효과)은 이미 team과 무관하게 실행하니,
        // 아래부터는 내 화면(패널/체크 표시/대기 상태)에 직접 손대지 않는다 - ShowPieceChoice(이
        // 선택에 관여하는 클라이언트라면 스스로 판단해 패널을 새로 연다)나 HandleAugmentTeamDone
        // (모든 팀이 완료됐을 때만)이 알아서 처리하도록 맡긴다. 상대 팀의 선택이 도착했다고 해서
        // 내가 이미 열어둔 패널이나 방금 표시한 체크/대기 상태를 건드리면 안 된다(재설계 전
        // 버그 1의 원인이었던 부분).
        Augmenteffects.ApplyImmediateEffect(augmentId, team, boardToUse);
        FinalizeAugmentTeamIfReady(team);
    }

    // ApplyImmediateEffect 호출 직후 이 팀의 증강 체크포인트가 즉시 완료됐는지, 아니면 이진 선택
    // (teamsAwaitingPieceChoice) 또는 보드 타겟 클릭(teamsAwaitingBoardTarget, 2026-09-21 수정 1-2)이
    // 남아 대기해야 하는지 판정한다. 완료됐다면 pendingAugmentTeams에서 제거하고
    // HandleAugmentTeamDone까지 호출한다 - 남은 대기가 있다면 아무것도 하지 않고, ApplyPieceChoice
    // 또는 HandlePromotionTargetResolved가 나중에 이어서 처리하도록 맡긴다.
    private void FinalizeAugmentTeamIfReady(int team)
    {
        bool awaitingPieceChoice = teamsAwaitingPieceChoice.Contains(team);
        bool awaitingBoardTarget = !awaitingPieceChoice && ChessInteractionManager.Instance != null
            && ChessInteractionManager.Instance.IsTeamAwaitingPromotionTarget(team);

        if (awaitingBoardTarget)
        {
            teamsAwaitingBoardTarget.Add(team);

            // 2026-10-05 수정: 네트워크 대전 모드에서 즉시 승급 계열 증강을 고르면 "승급시킬 아군
            // 기물 클릭하세요"만 콘솔에 뜨고 보드 클릭이 전혀 먹히지 않는 버그가 있었다. 로컬
            // 테스트 모드는 ApplyAugmentChoice 맨 앞에서 패널을 이미 닫고 들어가서 문제가 없었지만,
            // 네트워크 모드는 패널을 닫는 시점이 HandleAugmentTeamDone(=체크포인트가 완전히 끝났을
            // 때)뿐이라, 보드 타겟 대기 중에도 카드 선택 패널이 화면을 계속 덮어 보드 클릭을 막고
            // 있었다. 이 선택에 실제로 관여한 클라이언트에서만 지금 패널을 닫아(타이머는 재개하지
            // 않음 - 시간제한이 있다면 그대로 정지 상태 유지) 보드를 클릭 가능하게 만든다.
            // 실제 체크포인트 완료 처리는 HandlePromotionTargetResolved가 보드 클릭을 받은 뒤 이어서 한다.
            if (IsInteractiveForTeam(team))
                FinalizeCardSelectionUI(wasPromotion: false, resumeTimer: false);
        }

        if (awaitingPieceChoice || awaitingBoardTarget)
            return; // 이진 선택 또는 보드 타겟 클릭이 아직 남아있음 - 여기서는 완료 처리를 하지 않는다.

        pendingAugmentTeams.Remove(team);
        HandleAugmentTeamDone(team);
    }

    // ChessNetworkSync RPC를 통해(또는 로컬 테스트 시 직접) 호출되어, 이진 선택 결과를 양쪽 클라이언트에 동일하게 적용한다.
    //
    // ShowPieceChoice는 항상 이 콜백/teamsAwaitingPieceChoice 등록을 양쪽 클라이언트에서 동일하게
    // 수행하므로(공유 로직), 아래 판정도 양쪽 클라이언트에서 항상 동일한 결과를 낸다.
    public void ApplyPieceChoice(int team, PromotablePieceType type)
    {
        System.Action<PromotablePieceType> callback = pieceChoiceCallback;

        // 이 이진 선택이 증강 체크포인트의 일부였는지 확인한다(현재 이 클래스에서 ShowPieceChoice의
        // 유일한 호출 경로가 증강 즉시효과이므로 사실상 항상 true이지만, 방어적으로 체크한다).
        bool wasAwaitingForAugment = teamsAwaitingPieceChoice.Remove(team);

        // 콜백을 먼저 실행한다(2026-09-21 수정 1-2: 순서 변경) - 이 콜백(예: 나이트/비숍 선택 완료)이
        // ChessInteractionManager.StartTargetPromotionMode를 호출해 "승급 대상 보드 클릭 대기" 상태를
        // 새로 열 수 있는데, 그 결과를 아래에서 곧바로 확인해야 이 팀의 체크포인트를 지금 끝낼지
        // 아니면 실제 보드 클릭까지 더 기다려야 할지 정확히 판단할 수 있다.
        callback?.Invoke(type);

        bool awaitingBoardTarget = wasAwaitingForAugment && ChessInteractionManager.Instance != null
            && ChessInteractionManager.Instance.IsTeamAwaitingPromotionTarget(team);
        if (awaitingBoardTarget)
            teamsAwaitingBoardTarget.Add(team);

        // 이 이진 선택으로 이 팀의 증강 체크포인트가 "완전히" 끝났는지: 애초에 증강 체크포인트의
        // 일부였고(wasAwaitingForAugment), 그 결과가 다시 보드 타겟 클릭으로 이어지지 않았을 때만.
        bool augmentFullyDone = wasAwaitingForAugment && !awaitingBoardTarget;
        if (augmentFullyDone)
            pendingAugmentTeams.Remove(team);

        bool allTeamsDone = pendingAugmentTeams.Count == 0;

        // 이 클라이언트에서 실제로 이진 선택 패널을 띄웠던 경우에만(=이 선택에 관여하는 팀의
        // 클라이언트에서만) 패널을 정리한다. 관여하지 않는 클라이언트(네트워크 대전 중 상대방)는
        // 애초에 이 이진 선택 패널을 연 적이 없으므로 건드리면 안 된다 - 자신의 다른(별개) 화면
        // 상태를 잘못 초기화하게 된다. 보드 타겟 클릭이 아직 남아있거나(awaitingBoardTarget) 상대
        // 팀이 아직 남아있다면 타이머는 재개하지 않는다 - HandleAugmentTeamDone(이 메서드 또는
        // HandlePromotionTargetResolved를 통해) 이 모든 대기가 끝난 뒤에 재개한다.
        if (IsInteractiveForTeam(team))
        {
            bool resumeTimer = !wasAwaitingForAugment || (augmentFullyDone && allTeamsDone);
            FinalizeCardSelectionUI(wasPromotion: false, resumeTimer: resumeTimer);
        }

        if (augmentFullyDone)
            HandleAugmentTeamDone(team);
        // else: 보드 타겟 클릭이 아직 남아있다(awaitingBoardTarget) - HandlePromotionTargetResolved가
        // ChessInteractionManager.OnPromotionTargetResolved 이벤트를 통해 실제 완료 시 이어서 처리한다.
    }

    // ChessInteractionManager.OnPromotionTargetResolved 이벤트 핸들러: 증강 효과로 시작된 "승급 대상
    // 보드 클릭" 대기가 실제로 완료됐을 때 호출된다(보드 클릭 릴레이 경로를 통해 양쪽 클라이언트
    // 모두에서 공유 상태로 호출됨). 이 팀의 증강 체크포인트가 이 보드 타겟 클릭을 기다리고 있던
    // 경우에만(teamsAwaitingBoardTarget에 등록되어 있던 경우) 비로소 pendingAugmentTeams에서마저
    // 제거하고 체크포인트 완료 마무리(HandleAugmentTeamDone)를 진행한다 - 그래야 "증강 선택 - 이진
    // 선택(있다면) - 효과 발동/보드 상호작용(있다면) - 턴 진행" 순서가 실제 보드 상호작용까지 다
    // 끝난 뒤에만 턴/타이머가 재개되도록 보장된다(2026-09-21 수정 1-2).
    private void HandlePromotionTargetResolved(int team)
    {
        if (!teamsAwaitingBoardTarget.Remove(team)) return; // 증강 체크포인트와 무관한 승급이었음(방어적 처리)

        pendingAugmentTeams.Remove(team);
        HandleAugmentTeamDone(team);
    }

    // 한 팀의 증강 체크포인트 대상이 "완전히" 끝났을 때(이진 선택으로 이어지지 않았거나, 이어졌다면
    // 그 이진 선택까지 완료됐을 때) 호출되는 공통 마무리 처리. 호출 시점에는 이미
    // pendingAugmentTeams에서 team이 제거되어 있어야 한다(ApplyAugmentChoice/ApplyPieceChoice 참고).
    private void HandleAugmentTeamDone(int team)
    {
        bool allTeamsDone = pendingAugmentTeams.Count == 0;

        if (GameStartController.LocalTeam < 0)
        {
            // 로컬 테스트 모드: 남은 팀이 있으면 이어서 그 팀의 선택 창을 연다.
            // (패널은 ApplyAugmentChoice 또는 ApplyPieceChoice 쪽에서 이미 닫아뒀다.)
            if (!allTeamsDone)
                ShowCardSelection(promotion: false, team: pendingAugmentTeams[0]);
            else
                GameManager.Instance?.ResumeTimer();

            return;
        }

        if (!allTeamsDone) return; // 아직 상대 팀 선택이 남아있음 - 내 화면은 그대로 둔다.

        // 두 팀 모두 선택을 마친 시점 - 이 시점부터는 다시 공유/대칭적으로 처리해도 안전하다.
        GameManager.Instance?.ResumeTimer();

        if (IsSelecting && currentSelectedCard != null)
        {
            // 내가 이미 카드를 골라 체크 표시 + 대기 상태였다면, 이제서야 미뤄뒀던 카드 이동
            // 애니메이션을 재생하고 패널을 닫는다. (SendSelectedAugmentCard에서 이미 결과를
            // 전파했으므로 여기서는 결과를 다시 계산하지 않는 FinalizeCardSelectionUI만 넘긴다.)
            SoundManager.Instance?.PlayAugmentBothSelected();
            StartCoroutine(AnimateCardsRoutine(currentSelectedCard, () => FinalizeCardSelectionUI(wasPromotion: false, resumeTimer: true)));
        }
        else if (IsSelecting)
        {
            // 내 팀이 애초에 상한 도달로 체크포인트 대상이 아니어서 패널을 연 적이 없거나,
            // 그 밖의 사유로 아직 패널이 열려 있는 예외 상황 - 애니메이션 없이 바로 정리한다.
            FinalizeCardSelectionUI(wasPromotion: false, resumeTimer: true);
        }
        // else: 애초에 패널을 연 적이 없거나(예: 내 팀이 상한 도달) 이미 닫힌 클라이언트(예:
        // 이진 선택 패널을 ApplyPieceChoice가 방금 닫음) - 정리할 UI가 없다.
    }

    // 카드 선택 패널을 닫고 관련 상태를 정리한 뒤 타이머를 재개하는 공통 마무리 처리.
    // 카드를 직접 클릭한 클라이언트와, RPC로 결과만 전달받은(지켜만 본) 클라이언트 모두
    // 이 메서드를 거쳐야 각자의 화면에서 패널이 닫히고 타이머가 재개된다.
    // resumeTimer=false이면 패널만 정리하고 타이머는 그대로 정지 상태로 둔다.
    // (증강 체크포인트에서 아직 선택하지 않은 팀이 남아있을 때, 또는 이진 선택으로 이어져
    // 아직 체크포인트가 끝나지 않았을 때 사용)
    private void FinalizeCardSelectionUI(bool wasPromotion, bool resumeTimer = true)
    {
        if (cardSelectionPanel != null)
            cardSelectionPanel.SetActive(false);

        SetWaitingStatus(false);

        IsSelecting = false;
        isClosing = false;
        currentSelectedCard = null;
        pieceChoiceCallback = null;
        pieceChoiceOptions = null;

        if (GameManager.Instance != null)
        {
            if (resumeTimer)
                GameManager.Instance.ResumeTimer();
            if (wasPromotion)
                GameManager.Instance.PieceMoved();
        }
    }

    // PromotablePieceType을 팀에 맞는 ChessPieceType으로 변환
    private ChessPieceType ConvertToChessPieceType(PromotablePieceType promotable, int team)
    {
        bool isWhite = (team == 0);

        switch (promotable)
        {
            case PromotablePieceType.Queen:
                return isWhite ? ChessPieceType.WhiteQueen : ChessPieceType.BlackQueen;
            case PromotablePieceType.Rook:
                return isWhite ? ChessPieceType.WhiteRook : ChessPieceType.BlackRook;
            case PromotablePieceType.Bishop:
                return isWhite ? ChessPieceType.WhiteBishop : ChessPieceType.BlackBishop;
            case PromotablePieceType.Knight:
                return isWhite ? ChessPieceType.WhiteKnight : ChessPieceType.BlackKnight;
            default:
                return isWhite ? ChessPieceType.WhiteQueen : ChessPieceType.BlackQueen;
        }
    }

    // 카드 선택 인터랙션 활성화/비활성화
    void SetCardsInteractable(bool value)
    {
        foreach (CardUI card in cardList)
        {
            if (card != null)
                card.SetInteractable(value);
        }
    }
    #endregion
}
