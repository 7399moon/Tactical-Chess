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
        bool executes = HasAugment(knight.team, "knight_execution");

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
        if (bishop == null || board == null || IsOnCooldown(bishop)) return false;

        // 상하좌우 1칸 검사
        int dx = Mathf.Abs(targetPos.x - bishop.currentX);
        int dy = Mathf.Abs(targetPos.y - bishop.currentY);
        if (dx + dy != 1) return false;

        int team = bishop.team;
        ChessPieces occupant = board.GetPieceAt(targetPos.x, targetPos.y);

        bool allowSwap = occupant != null && occupant.team == team && HasAugment(team, "bishop_warp_swap");
        bool allowAttack = occupant != null && occupant.team != team && HasAugment(team, "bishop_warp_attack");

        if (occupant != null && !allowSwap && !allowAttack)
            return false; // 증강 없이는 점유된 칸으로 워프 불가

        if (allowAttack && IsShielded(occupant))
        {
            Debug.Log("대상 기물은 쉴드 상태여서 차원 암살로 처치할 수 없습니다.");
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

        if (!chainContinues)
        {
            int cooldown = bishopWarpCooldown;
            if (hasContinuousWarp) cooldown += 2; // 연속 워프를 다 사용한 턴의 디메리트
            if (warpChainUsedAttack.GetValueOrDefault(team)) cooldown += 2; // 차원 암살로 처치했을 때의 디메리트
            cooldowns[bishop] = cooldown;
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

        if (!HasAnyLegalMove(target))
        {
            Debug.Log("[지휘] 지정한 기물은 현재 이동할 수 없어 지휘 대상으로 선택할 수 없습니다.");
            CenterAnnouncer.Show("이동할 수 없는 기물은 지휘 대상으로 선택할 수 없습니다.");
            return false;
        }

        commandedPiece = target;
        commandMovesLeft = 2;

        cooldowns[king] = kingCommandCooldownBase[king.team];

        // 지휘 VFX: 대상 머리 위에 노란 이펙트를 부여, 지휘로 두 번 이동할 때까지 유지
        if (commandVfxInstance != null) Destroy(commandVfxInstance);
        commandVfxInstance = SpawnPieceVfx(commandVfxPrefab, target, GetHeadLocalOffset(target));

        SoundManager.Instance?.PlayCommand();
        NotifySkillUsed(king.team);
        return true;
    }

    // 지휘 기물이 이동할 때마다 이동 횟수를 차감
    public void OnCommandPieceMoved()
    {
        if (commandMovesLeft > 0)
        {
            commandMovesLeft--;
            if (commandMovesLeft <= 0)
                ClearCommandState();
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
    public bool CommandTargetHasLegalMove() => HasAnyLegalMove(commandedPiece);

    // 지휘 대상이 상대 턴 사이에 위협을 당하는 등으로 더 이상 이동할 수 없는 상태가 되면, 완료를
    // 기다리지 못하고 지휘 효과를 강제로 종료해 게임 진행이 멈추지 않도록 한다 (PieceMovement에서 호출).
    public void ForceEndCommand() => ClearCommandState();

    // 지휘 상태(대상/잔여 이동 횟수/VFX) 정리 공통 처리
    private void ClearCommandState()
    {
        commandedPiece = null;
        commandMovesLeft = 0;

        if (commandVfxInstance != null)
        {
            Destroy(commandVfxInstance);
            commandVfxInstance = null;
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
