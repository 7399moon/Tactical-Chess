using System.Collections.Generic;
using UnityEngine;

// PieceSkillManager의 나이트/비숍/룩/킹 액티브 스킬 발동 로직 partial 파일.
public partial class PieceSkillManager
{
    #region 스킬 발동 처리
    // 나이트 - 위협: 주변 N칸(기본 1, 레어1 보유 시 +보너스) 적에게 1턴간 이동 불가 부여 (쿨타임 4턴)
    // 단일 대상 호출용 오버로드 (기존 호출부 호환용)
    public bool TryUseThreat(ChessPieces knight, ChessPieces target) =>
        TryUseThreat(knight, target != null ? new List<ChessPieces> { target } : null);

    // 유니크1(다중 위협) 보유 시 대상 2명을 동시에 지정 가능.
    // 2026-10-03 수정: 다중 위협 증강 보유 시 2명을 채우지 못하면 위협이 아예 발동되지 않아,
    // 적이 사정거리 안에 1명뿐인 상황에서는 위협을 쓸 방법이 없었다. bypassMinimumCount를 true로
    // 넘기면(같은 대상을 재클릭해 "이 인원으로 바로 발동" 의사를 표시한 경우) 목표 인원 미달이어도
    // 지금까지 고른 대상만으로 발동을 허용한다. 대상이 0명이면 당연히 허용하지 않는다.
    public bool TryUseThreat(ChessPieces knight, List<ChessPieces> targets, bool bypassMinimumCount = false)
    {
        if (knight == null || targets == null || targets.Count == 0 || IsOnCooldown(knight))
            return false;

        int requiredCount = HasAugment(knight.team, "knight_threat_target_up") ? 2 : 1;
        if (!bypassMinimumCount && targets.Count < requiredCount) return false;

        // 레어1(위협 확장): AugmentManager에 누적된 범위 보너스를 실제로 반영
        int range = 1 + (AugmentManager.Instance != null ? AugmentManager.Instance.GetKnightThreatRangeBonus(knight.team) : 0);

        foreach (ChessPieces target in targets)
        {
            if (target == null || target.team == knight.team) return false;
            if (Mathf.Abs(knight.currentX - target.currentX) > range || Mathf.Abs(knight.currentY - target.currentY) > range)
                return false;
        }

        // 레전더리1(처형자의 표식): 대상 중 1명을 즉시 처형 (사용 시 쿨타임 2턴 증가)
        // 2026-10-05 수정: 휴전 협정(temporary_ceasefire) 중에는 일반 이동 캡처뿐 아니라 스킬/증강에
        // 의한 캡처도 전부 금지되어야 하는데, 처형자의 표식은 ChessRules의 이동 생성/필터링을 거치지
        // 않고 PieceCapture.CapturePieceAt을 직접 호출해 처치하므로 휴전 협정 필터를 그대로 피해갔다.
        // 휴전 협정 중에는 처형 자체(및 그에 따른 쿨타임 증가분)를 적용하지 않는다.
        bool ceasefireActive = GameManager.Instance != null && GameManager.Instance.armisticeTurns > 0;
        bool executes = HasAugment(knight.team, "knight_execution") && !ceasefireActive;

        // 쿨타임을 먼저 확정해야, 처형으로 인한 처치 시 쿨타임 감소류 증강(사냥꾼의 본능 등)이
        // 방금 세팅된 값을 기준으로 정상적으로 차감된다.
        cooldowns[knight] = knightThreatCooldown + (executes ? 2 : 0);

        foreach (ChessPieces target in targets)
        {
            immobilized[target] = 2; // 다음 턴까지 유지
            target.IsThreatenedTarget = true; // 레어3(연쇄 위협) 판정을 위해 표식 부여

            // 위협 VFX: 대상 위치에서 위에서 아래로 떨어지는 붉은 이펙트를 위협 지속시간(위 immobilized 턴 수) 동안 유지
            // (기존 인스턴스가 있으면 교체) — 보드/기물 바닥에 가려지지 않도록 0.1만큼 위로 띄워서 재생
            RemoveThreatVfx(target);
            GameObject threatVfx = SpawnPieceVfx(threatVfxPrefab, target, Vector3.up * 0.1f);
            if (threatVfx != null) threatVfxInstances[target] = threatVfx;
        }

        if (executes)
        {
            ChessPieces executionTarget = targets[0];
            PieceCapture capture = FindAnyObjectByType<PieceCapture>();
            if (capture != null)
                capture.CapturePieceAt(executionTarget, knight);
        }

        SoundManager.Instance?.PlayThreat();
        NotifySkillUsed(knight.team);
        return true;
    }

    // 비숍 - 워프: 인접 1칸으로 기물 즉시 이동 (쿨타임 4턴)
    // 기본은 빈 칸만 가능하지만, 레어4(워프 스왑) 보유 시 아군과 자리 교환, 레전더리2(차원 암살) 보유 시 적 처치 후 이동 가능
    // 유니크2(연속 워프) 보유 시 1턴에 2회까지 사용 가능(두 번째 사용 시 쿨타임 +2턴)
    public bool TryUseWarp(ChessPieces bishop, Vector2Int targetPos, ChessBoard board)
    {
        // 2026-10-05 수정: 위협(이동 불가) 상태인 기물은 일반 이동뿐 아니라 어떤 스킬/증강으로도
        // 움직일 수 없어야 하는데, 워프는 PieceMovement를 거치지 않고 비숍을 직접 좌표 이동시키므로
        // IsImmobilized 검사가 빠져 있었다. 비숍 자신이 위협 상태면 워프 자체를 거부한다.
        if (bishop == null || board == null || IsOnCooldown(bishop) || IsImmobilized(bishop)) return false;

        // 상하좌우 1칸 검사
        int dx = Mathf.Abs(targetPos.x - bishop.currentX);
        int dy = Mathf.Abs(targetPos.y - bishop.currentY);
        if (dx + dy != 1) return false;

        int team = bishop.team;
        ChessPieces occupant = board.GetPieceAt(targetPos.x, targetPos.y);

        // 2026-10-05 수정: 차원 암살(워프로 적 처치)도 휴전 협정 중에는 금지되어야 한다 (TryUseThreat의
        // 처형자의 표식과 동일한 이유 - PieceCapture.CapturePieceAt을 직접 호출해 휴전 협정 필터를 피해간다).
        bool ceasefireActive = GameManager.Instance != null && GameManager.Instance.armisticeTurns > 0;

        bool allowSwap = occupant != null && occupant.team == team && HasAugment(team, "bishop_warp_swap");
        bool allowAttack = occupant != null && occupant.team != team && HasAugment(team, "bishop_warp_attack") && !ceasefireActive;

        if (occupant != null && !allowSwap && !allowAttack)
        {
            if (occupant.team != team && ceasefireActive && HasAugment(team, "bishop_warp_attack"))
                Debug.Log("휴전 협정 중에는 차원 암살로 적을 처치할 수 없습니다.");
            return false; // 증강 없이는 점유된 칸으로 워프 불가
        }

        if (allowAttack && IsShielded(occupant))
        {
            Debug.Log("대상 기물은 쉴드 상태여서 차원 암살로 처치할 수 없습니다.");
            return false;
        }

        // 2026-10-05 수정: 워프 스왑 대상 아군이 위협 상태면 그 아군 역시 어떤 방식으로도 옮겨질 수
        // 없어야 하므로, 스왑으로 그 기물을 움직이는 것도 차단한다.
        if (allowSwap && IsImmobilized(occupant))
        {
            Debug.Log("대상 기물은 위협 상태여서 워프 스왑으로 이동시킬 수 없습니다.");
            return false;
        }

        int fromX = bishop.currentX;
        int fromY = bishop.currentY;

        // 연속 워프 여부 판정 및 쿨타임 확정 (실제 이동/처치 실행 전에 먼저 계산)
        bool hasContinuousWarp = HasAugment(team, "bishop_double_warp");
        int usesSoFar = warpUsesThisTurn.GetValueOrDefault(team) + 1;
        bool chainContinues = hasContinuousWarp && usesSoFar < 2;

        // 체인 중 한 번이라도 차원 암살을 사용했는지 누적 기록 (첫 워프에서 처치하고 두 번째는 그냥 이동해도 디메리트 유지)
        if (allowAttack) warpChainUsedAttack[team] = true;

        // 2026-10-05 수정: 예전에는 chainContinues가 true인 동안(연속 워프의 1번째 사용 직후) 쿨타임을
        // 전혀 설정하지 않고 2번째 사용이 끝나거나 포기할 때까지 미뤄뒀다. 그런데 1번째 워프만 쓰고
        // 2번째를 쓰지 않은 채 턴을 넘기면(혹은 그냥 더 이상 쓰지 않으면) 쿨타임 자체가 아예 적용되지
        // 않는 버그(사실상 매 턴 공짜 워프)가 있었다. 이제 매 사용마다 그 시점까지의 사용 횟수를
        // 기준으로 쿨타임을 항상 즉시 계산/반영한다: 1회만 쓰면 기본값(4턴), 2회까지 다 쓰면
        // 연속 워프 디메리트(+2턴)가 추가로 붙어 6턴 - 차원 암살 디메리트(+2턴)는 기존처럼 별도로 누적된다.
        int cooldown = bishopWarpCooldown;
        if (hasContinuousWarp && usesSoFar >= 2) cooldown += 2; // 연속 워프를 다 사용한 턴의 디메리트
        if (warpChainUsedAttack.GetValueOrDefault(team)) cooldown += 2; // 차원 암살로 처치했을 때의 디메리트
        cooldowns[bishop] = cooldown;

        if (!chainContinues)
        {
            warpUsesThisTurn[team] = 0;
            warpChainUsedAttack[team] = false;
        }
        else
        {
            warpUsesThisTurn[team] = usesSoFar;
        }

        if (allowSwap)
        {
            // ChessBoard.SwapPieces가 양쪽 기물의 좌표/트랜스폼을 모두 갱신
            board.SwapPieces(new Vector2Int(fromX, fromY), targetPos);
        }
        else
        {
            if (allowAttack)
            {
                PieceCapture capture = FindAnyObjectByType<PieceCapture>();
                if (capture != null)
                    capture.CapturePieceAt(occupant, bishop);
                else
                    board.SetPieceAt(targetPos.x, targetPos.y, null);
            }

            board.SetPieceAt(fromX, fromY, null);
            board.SetPieceAt(targetPos.x, targetPos.y, bishop);
            bishop.currentX = targetPos.x;
            bishop.currentY = targetPos.y;
        }

        // 워프 VFX: 도착 지점에서 연보라 이펙트가 나타났다가 사라짐 (1회성)
        // 보드 표면에 가려지지 않도록 보드 높이보다 0.1만큼 위에 재생
        if (warpVfxPrefab != null)
        {
            Vector3 warpSpawnPos = board.GetTileCenter(targetPos.x, targetPos.y) + Vector3.up * 0.1f;
            GameObject warpVfx = Instantiate(warpVfxPrefab, warpSpawnPos, Quaternion.identity);
            Destroy(warpVfx, warpVfxLifetime);
        }

        SoundManager.Instance?.PlayWarp();

        if (!chainContinues)
            NotifySkillUsed(team);

        return true;
    }

    // 연속 워프(유니크2) 보유 시, 방금 사용한 워프가 이번 턴의 첫 번째 사용이라 한 번 더 사용 가능한 상태인지 확인
    public bool CanWarpAgain(int team) =>
        HasAugment(team, "bishop_double_warp") && warpUsesThisTurn.GetValueOrDefault(team) == 1;

    // 룩 - 쉴드: 주변 N칸(기본 1, 레어6 보유 시 +보너스) 아군에게 1턴간 피격 무효 보호막 부여 (쿨타임 6턴)
    // 단일 대상 호출용 오버로드 (기존 호출부 호환용)
    public bool TryUseShield(ChessPieces rook, ChessPieces target, bool canTargetSelf = false) =>
        TryUseShield(rook, target != null ? new List<ChessPieces> { target } : null);

    // 유니크3(광역 수호) 보유 시 대상 2명을 동시에 지정 가능.
    // 2026-10-04 수정: 나이트 위협(TryUseThreat)과 동일한 이유로 bypassMinimumCount 추가 - 광역 수호
    // 보유 시 2명을 채우지 못하면(사정거리 안에 아군이 1명뿐인 등) 쉴드를 영영 쓸 수 없었다. 같은 대상을
    // 재클릭하면(ChessInteractionManager.Skills.cs) 목표 인원 미달이어도 지금까지 고른 대상만으로 발동한다.
    public bool TryUseShield(ChessPieces rook, List<ChessPieces> targets, bool bypassMinimumCount = false)
    {
        if (rook == null || targets == null || targets.Count == 0 || IsOnCooldown(rook))
            return false;

        int requiredCount = HasAugment(rook.team, "rook_shield_target_up") ? 2 : 1;
        if (!bypassMinimumCount && targets.Count < requiredCount) return false;

        // 레어8(자체 방벽): 증강 보유 시 자기 자신 타겟팅을 자동으로 허용
        bool allowSelfTarget = HasAugment(rook.team, "rook_self_shield");

        // 레어6(쉴드 확장): AugmentManager에 누적된 범위 보너스를 실제로 반영
        int range = 1 + (AugmentManager.Instance != null ? AugmentManager.Instance.GetRookShieldRangeBonus(rook.team) : 0);

        foreach (ChessPieces target in targets)
        {
            if (target == null || target.team != rook.team) return false;
            if (target == rook && !allowSelfTarget) return false;
            if (Mathf.Abs(rook.currentX - target.currentX) > range || Mathf.Abs(rook.currentY - target.currentY) > range)
                return false;
        }

        // 유니크4(장기 수호): 지속시간 2턴 증가.
        int duration = HasAugment(rook.team, "rook_shield_duration_up") ? 6 : 2;
        foreach (ChessPieces target in targets)
        {
            shielded[target] = duration;

            // 쉴드 VFX: 대상을 감싸는 푸른 이펙트를 쉴드 지속시간 동안 유지 (기존 인스턴스가 있으면 교체)
            // 보드/기물 바닥에 가려지지 않도록 0.1만큼 위로 띄워서 재생
            RemoveShieldVfx(target);
            GameObject shieldVfx = SpawnPieceVfx(shieldVfxPrefab, target, Vector3.up * 0.1f);
            if (shieldVfx != null) shieldVfxInstances[target] = shieldVfx;
        }

        cooldowns[rook] = rookShieldCooldown;

        SoundManager.Instance?.PlayShield();
        NotifySkillUsed(rook.team);
        return true;
    }

    // 킹 - 지휘: 선택 아군에게 연속 2회 이동권 부여 (기본 쿨타임 8턴, 팀별 kingCommandCooldownBase 참고.
    // 2026-09-21 수정 1-3: 예전에는 팀 구분 없는 단일 kingCommandCooldown 필드를 사용해, "섭정" 증강을
    // 한쪽 팀만 보유해도 다른 팀의 킹 지휘 쿨타임 기본값까지 12로 새는 문제가 있었다. 이제 사용한
    // 기물(king)의 팀에 해당하는 kingCommandCooldownBase[team]만 적용한다.)
    //
    // 2026-09-23 수정: 지휘 대상이 위협이나 아군 기물에 막혀 애초에 이동할 수 있는 수가 하나도 없으면
    // 지휘를 걸어도 절대 완료될 수 없어 게임 진행이 멈춰버렸다. 활성화 시점에 실제로 이동 가능한
    // 기물만 대상으로 선택할 수 있도록 수 계산으로 미리 검증한다.
    public bool TryUseCommand(ChessPieces king, ChessPieces target)
    {
        if (king == null || target == null || IsOnCooldown(king)) return false;

        // 2026-10-05 수정: 아군 전용 스킬인데 팀 검증이 빠져 있어 적 기물도 지휘 대상으로 지정할 수
        // 있었다(나이트 위협/룩 쉴드는 이미 상대/아군 검증을 하는데 지휘만 빠져 있었음). 이 때문에
        // 상대 기물을 지정해 상대의 다음 턴에 그 기물만 움직이도록 강제하는 것이 가능한 심각한
        // 버그였다. 또한 이미 지휘가 예약/활성 중인 팀이 중복으로 다시 지휘를 거는 것도 막는다.
        if (target.team != king.team) return false;

        // 2026-10-05 수정: 킹 자기 자신을 지휘 대상으로 지정하는 것을 막는다. 왕의 보폭(king_range_up)을
        // 함께 보유한 상태에서 킹이 스스로를 지휘하면, 지휘로 보장된 2회 이동이 왕의 보폭의 "추가
        // 이동 1회" 판정/소모 로직과 뒤엉켜(ProcessKingDoubleMove가 지휘의 이동을 보너스 이동으로
        // 오인) 왕의 보폭 효과가 통째로 증발하는 상호작용 버그가 있었다. 자기 자신 지정 자체가
        // 기능적으로도 의미가 없으므로(킹이 킹에게 명령) 아예 차단한다.
        if (target == king) return false;

        if (pendingCommandTarget.ContainsKey(king.team) || IsCommandActiveForTeam(king.team))
            return false;

        if (!HasAnyLegalMove(target))
        {
            Debug.Log("[지휘] 지정한 기물은 현재 이동할 수 없어 지휘 대상으로 선택할 수 없습니다.");
            CenterAnnouncer.Show("이동할 수 없는 기물은 지휘 대상으로 선택할 수 없습니다.");
            return false;
        }

        // 2026-10-05 수정: 예전에는 여기서 곧바로 commandedPiece/commandMovesLeft를 활성화해,
        // "섭정"(턴 유지) 보유 시 캐스팅 즉시 같은 턴 안에서 지휘 대상이 2회 이동까지 끝내버리는
        // 버그가 있었다(왕의 보폭과 겹치면 아예 다른 기물의 보너스 이동 슬롯까지 가로채는 문제도
        // 있었음). 지휘는 항상 "예약"만 해두고, 실제 활성화(2회 이동 가능 상태로 전환)는
        // HandleTurnStarted에서 지휘 대상 팀의 다음 턴이 시작될 때 비로소 이뤄지도록 한다 -
        // 섭정으로 턴이 유지되어 캐스팅한 팀이 같은 턴에 다른 기물을 마저 움직이더라도, 지휘
        // 대상 자체는 그 턴엔 전혀 움직일 수 없고 반드시 다음 턴에만 2회 이동하게 된다.
        pendingCommandTarget[king.team] = target;

        cooldowns[king] = kingCommandCooldownBase[king.team];

        // 지휘 VFX: 대상 머리 위에 노란 이펙트를 부여(예약 표시), 실제 2회 이동이 끝날 때까지 유지
        // 2026-10-05 수정(멀티플레이 버그): 기존에는 GetHeadLocalOffset(기물 렌더러 전체 높이 기반 동적
        // 오프셋)을 사용해, 나이트 기준 약 2.33, 킹 기준 약 3.5 유닛이나 되는 큰 높이로 VFX가 스폰됐다.
        // 같은 0.15 스케일을 쓰는 위협/쉴드 VFX는 둘 다 고정된 Vector3.up * 0.1f 오프셋을 써서 문제없이
        // 보이므로, 지휘 VFX만 유독 "기존보다 크고 높이 떠서 보이는" 원인은 스케일이 아니라 이 오프셋이었다.
        // 다른 세 스킬과 동일한 고정 오프셋으로 맞춰 피규어 머리 바로 위에 보이도록 수정한다.
        if (commandVfxInstanceByTeam.TryGetValue(king.team, out GameObject existingCommandVfx) && existingCommandVfx != null)
            Destroy(existingCommandVfx);
        commandVfxInstanceByTeam[king.team] = SpawnPieceVfx(commandVfxPrefab, target, Vector3.up * 0.1f);

        SoundManager.Instance?.PlayCommand();
        NotifySkillUsed(king.team);
        return true;
    }

    // 지휘 기물이 이동할 때마다 이동 횟수를 차감 (2026-10-05 수정: 팀별로 독립 차감)
    public void OnCommandPieceMoved(int team)
    {
        int left = commandMovesLeftByTeam.GetValueOrDefault(team);
        if (left > 0)
        {
            left--;
            commandMovesLeftByTeam[team] = left;
            if (left <= 0)
                ClearCommandState(team);
        }
    }

    // 지정한 기물이 지금 당장 둘 수 있는 합법적인 수가 하나라도 있는지 확인 (지휘 대상 선정/유지 검증용)
    private bool HasAnyLegalMove(ChessPieces piece)
    {
        if (piece == null || ChessBoard.Instance == null) return false;

        Vector2Int? enPassantTarget = GameManager.Instance != null ? GameManager.Instance.EnPassantTarget : null;
        var moves = ChessRules.GetLegalMoves(ChessBoard.Instance.Pieces, piece, enPassantTarget);
        return moves != null && moves.Count > 0;
    }

    // 현재 지휘 대상이 여전히 이동할 수 있는 상태인지 외부(PieceMovement)에서 확인하기 위한 공개 API
    public bool CommandTargetHasLegalMove(int team) => HasAnyLegalMove(GetCommandedPiece(team));

    // 지휘 대상이 상대 턴 사이에 위협을 당하는 등으로 더 이상 이동할 수 없는 상태가 되면, 완료를
    // 기다리지 못하고 지휘 효과를 강제로 종료해 게임 진행이 멈추지 않도록 한다 (PieceMovement에서 호출).
    public void ForceEndCommand(int team) => ClearCommandState(team);

    // 지휘 상태(대상/잔여 이동 횟수/VFX) 정리 공통 처리 (2026-10-05 수정: 팀별로 독립 정리)
    private void ClearCommandState(int team)
    {
        commandedPieceByTeam.Remove(team);
        commandMovesLeftByTeam.Remove(team);

        if (commandVfxInstanceByTeam.TryGetValue(team, out GameObject vfx))
        {
            if (vfx != null) Destroy(vfx);
            commandVfxInstanceByTeam.Remove(team);
        }
    }

    // "현재 턴을 진행 중인 팀"이 스킬을 사용했을 때 SkillUIManager(단일/플랫 상태)에 통지.
    // 예전에는 team == 0(백)으로 고정되어 있어 흑팀이 스킬을 사용해도 SkillUIManager의
    // HasUsedSkillThisTurn 등이 갱신되지 않는 문제가 있었다. SkillUIManager의 이번 턴 상태는
    // 항상 "현재 턴 팀"을 나타내므로 GameManager.CurrentTurn과 비교해야 한다.
    private void NotifySkillUsed(int team)
    {
        if (GameManager.Instance != null && team == GameManager.Instance.CurrentTurn && SkillUIManager.Instance != null)
        {
            SkillUIManager.Instance.SetSkillUsedThisTurn();
        }
    }
    #endregion
}
