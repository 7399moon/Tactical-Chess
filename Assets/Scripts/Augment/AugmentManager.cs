using System.Collections.Generic;
using UnityEngine;

// 팀별 증강(Augment) 획득 상태를 관리하는 싱글톤 매니저 클래스.
public class AugmentManager : MonoBehaviour
{
    #region 싱글턴 및 팀별 상태 초기화
    public static AugmentManager Instance { get; private set; }

    // 팀별 패시브 스킬 범위 추가 수치 (0: 백, 1: 흑)
    private Dictionary<int, int> knightThreatRangeBonus = new Dictionary<int, int> { { 0, 0 }, { 1, 0 } };
    private Dictionary<int, int> rookShieldRangeBonus = new Dictionary<int, int> { { 0, 0 }, { 1, 0 } };

    // 팀별 쿨타임 감소 패시브 보유 여부
    private Dictionary<int, bool> hasKnightKillCdReduction = new Dictionary<int, bool> { { 0, false }, { 1, false } }; // 레어2
    private Dictionary<int, bool> hasThreatTargetKillCdReduction = new Dictionary<int, bool> { { 0, false }, { 1, false } }; // 레어3
    private Dictionary<int, bool> hasBishopKillCdReduction = new Dictionary<int, bool> { { 0, false }, { 1, false } }; // 레어5
    private Dictionary<int, bool> hasRookKillCdReduction = new Dictionary<int, bool> { { 0, false }, { 1, false } }; // 레어7

    // 노말12(긴급 교체) 팀별 남은 사용 횟수 (증강 획득 시 2회로 설정, 사용할 때마다 차감)
    private readonly Dictionary<int, int> emergencySwapUsesLeft = new Dictionary<int, int> { { 0, 0 }, { 1, 0 } };

    // 팀별로 획득한 증강 ID 집합 (0: White, 1: Black)
    private readonly Dictionary<int, HashSet<string>> acquiredAugments = new Dictionary<int, HashSet<string>>
    {
        { 0, new HashSet<string>() },
        { 1, new HashSet<string>() }
    };

    // 싱글턴 인스턴스 등록
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    #endregion

    #region 증강 획득/조회 및 패시브 효과
    // 지정된 팀에 증강 ID를 등록
    public void AddAugment(int team, string augmentId)
    {
        if (string.IsNullOrEmpty(augmentId)) return;

        // 예외 상황(기본 0, 1 외의 팀 번호) 대응을 위한 자동 방어 로직
        if (!acquiredAugments.TryGetValue(team, out var augmentSet))
        {
            augmentSet = new HashSet<string>();
            acquiredAugments[team] = augmentSet;
        }

        if (augmentSet.Add(augmentId))
        {
            Debug.Log($"[AugmentManager] {(team == 0 ? "White" : "Black")} 팀이 '{augmentId}' 증강을 획득했습니다.");

            // 신규 획득 시에만 패시브 수치 및 조건 등록
            ApplyPassiveAugmentEffect(team, augmentId);
        }
    }

    // 해당 팀이 특정 증강을 보유하고 있는지 확인
    public bool HasAugment(int team, string augmentId)
    {
        if (string.IsNullOrEmpty(augmentId)) return false;

        return acquiredAugments.TryGetValue(team, out var set) && set.Contains(augmentId);
    }

    // 해당 팀이 지금까지 획득한 증강 개수 조회 (팀별 증강 상한 판정 등에 사용).
    // acquiredAugments가 곧 "팀별로 실제 보유한 증강"의 유일한 출처이므로, 별도의 카운터 필드를
    // 두지 않고 이 컬렉션의 크기를 그대로 상한 판정 기준으로 사용한다.
    public int GetAcquiredCount(int team)
    {
        return acquiredAugments.TryGetValue(team, out var set) ? set.Count : 0;
    }

    // 해당 팀이 보유한 증강 ID 목록 조회(읽기 전용). "보유 증강 확인" UI 등 표시 전용 용도.
    public IReadOnlyCollection<string> GetAcquiredAugmentIds(int team)
    {
        if (acquiredAugments.TryGetValue(team, out var set))
            return set;
        return System.Array.Empty<string>();
    }

    // 새로 획득한 증강 ID에 따라 패시브 수치 및 지속 효과를 바인딩
    private void ApplyPassiveAugmentEffect(int team, string augmentId)
    {
        switch (augmentId)
        {
            // --- 1. 스킬 범위 증가 계열 ---
            case "knight_threat_range_up": // 레어1: 위협 확장 (나이트)
                knightThreatRangeBonus[team] = knightThreatRangeBonus.GetValueOrDefault(team) + 1;
                break;

            case "rook_shield_range_up": // 레어6: 쉴드 확장 (룩)
                rookShieldRangeBonus[team] = rookShieldRangeBonus.GetValueOrDefault(team) + 1;
                break;

            // --- 2. 스킬 쿨타임 감소 계열 (처치 조건) ---
            case "knight_cooldown_on_kill": // 레어2: 사냥꾼의 본능 (나이트 적 처치)
                hasKnightKillCdReduction[team] = true;
                break;

            case "knight_cooldown_on_threat_kill": // 레어3: 연쇄 위협 (위협 대상 처치)
                hasThreatTargetKillCdReduction[team] = true;
                break;

            case "bishop_cooldown_on_kill": // 레어5: 워프 가속 (비숍 적 처치)
                hasBishopKillCdReduction[team] = true;
                break;

            case "rook_cooldown_on_kill": // 레어7: 수호자의 응징 (룩 적 처치)
                hasRookKillCdReduction[team] = true;
                break;

            // --- 3. 횟수 제한형 ---
            case "king_emergency_swap": // 노말12: 긴급 교체 (게임당 2회)
                emergencySwapUsesLeft[team] = 2;
                break;

            // --- 4. 즉시 연동형 (다른 매니저 상태를 직접 변경) ---
            case "king_regency": // 레전더리5: 섭정 (지휘 사용 시 턴 유지, 대신 쿨타임 8->12 증가)
                // "턴 유지" 여부는 사용 시점에 ChessInteractionManager가 HasAugment(actingTeam, "king_regency")로
                // 직접 조회한다(팀 구분 없는 SkillUIManager 플랫 bool에 기록하면 한쪽 팀만 보유해도
                // 다른 팀에게까지 효과가 새어나가는 문제가 있었다). 쿨타임 기본값 변경도 반드시 이 팀(team)
                // 에게만 적용한다(2026-09-21 수정 1-3: 예전에는 SetKingCommandCooldownBase(12)가 팀 인자 없이
                // 호출되어, PieceSkillManager 쪽의 단일 필드를 통해 미보유 팀에게까지 12로 새는 문제가 있었다).
                if (PieceSkillManager.Instance != null)
                    PieceSkillManager.Instance.SetKingCommandCooldownBase(team, 12);
                break;
        }
    }

    // 나이트 위협 범위 보너스 조회
    public int GetKnightThreatRangeBonus(int team) => knightThreatRangeBonus.TryGetValue(team, out int v) ? v : 0;
    // 룩 쉴드 범위 보너스 조회
    public int GetRookShieldRangeBonus(int team) => rookShieldRangeBonus.TryGetValue(team, out int v) ? v : 0;

    // 긴급 교체 잔여 횟수 조회 및 소모
    public int GetEmergencySwapUsesLeft(int team) => emergencySwapUsesLeft.TryGetValue(team, out int v) ? v : 0;
    public void ConsumeEmergencySwapUse(int team)
    {
        if (emergencySwapUsesLeft.ContainsKey(team) && emergencySwapUsesLeft[team] > 0)
            emergencySwapUsesLeft[team]--;
    }

    // 처치 이벤트 발생 시 쿨타임 감소 조건 체크 및 처리
    public void OnPieceCaptured(ChessPieces attacker, ChessPieces victim)
    {
        if (attacker == null || victim == null || PieceSkillManager.Instance == null) return;

        int team = attacker.team;

        // 레어2: 나이트가 적 처치
        if (IsKnight(attacker.type) && hasKnightKillCdReduction.GetValueOrDefault(team))
            PieceSkillManager.Instance.ReduceCooldown(attacker, 1);

        // 레어3(연쇄 위협): 위협 대상을 잡음 (victim에 위협 표식이 있는 경우)
        // 2026-10-05 수정: 예전에는 이 조건을 만족하면 무조건 "처치한 기물(attacker) 자신"의 쿨타임을
        // 깎았다. 연쇄 위협은 "나이트의 위협 스킬" 쿨타임을 감소시키는 증강인데, 나이트가 아닌 다른
        // 기물(비숍/룩 등)이 마무리를 지은 경우 엉뚱하게 그 기물 자신의 스킬 쿨타임이 깎여버리는
        // 버그였다(예: 워프 가속+연쇄 위협을 함께 보유한 비숍이 위협당한 기물을 처치하면, 룰상으로는
        // 위협 쿨타임만 깎여야 하는데 실제로는 비숍 자신의 워프 쿨타임이 깎였다 - 반대로 아래 레어5
        // 조건도 함께 성립해 워프 쿨타임이 별도로 또 깎이면서 중복 감소까지 발생할 수 있었다).
        // 이제 "처치한 기물의 종류와 무관하게 위협 스킬(나이트) 쿨타임만" 깎도록, attacker가 나이트가
        // 아니면 해당 팀에서 쿨타임이 걸려 있는 나이트를 찾아 그 쿨타임을 감소시킨다.
        if (hasThreatTargetKillCdReduction.GetValueOrDefault(team) && victim.IsThreatenedTarget)
        {
            ChessPieces knightToReduce = IsKnight(attacker.type) ? attacker : FindKnightOnCooldown(team);
            if (knightToReduce != null)
                PieceSkillManager.Instance.ReduceCooldown(knightToReduce, 1);
        }

        // 레어5: 비숍이 적 처치
        if (IsBishop(attacker.type) && hasBishopKillCdReduction.GetValueOrDefault(team))
            PieceSkillManager.Instance.ReduceCooldown(attacker, 1);

        // 레어7: 룩이 적 처치
        if (IsRook(attacker.type) && hasRookKillCdReduction.GetValueOrDefault(team))
            PieceSkillManager.Instance.ReduceCooldown(attacker, 1);
    }

    // 기물 종류 판별용 유틸 (팀 무관하게 나이트/비숍/룩인지 확인)
    private bool IsKnight(ChessPieceType t) => t == ChessPieceType.WhiteKnight || t == ChessPieceType.BlackKnight;
    private bool IsBishop(ChessPieceType t) => t == ChessPieceType.WhiteBishop || t == ChessPieceType.BlackBishop;
    private bool IsRook(ChessPieceType t) => t == ChessPieceType.WhiteRook || t == ChessPieceType.BlackRook;

    // 2026-10-05 추가: 레어3(연쇄 위협)용 - 처치한 기물이 나이트가 아닐 때, 쿨타임 감소를 받을
    // "그 팀의 나이트(위협 스킬이 쿨타임 중인 개체)"를 보드에서 찾는다. 나이트가 여럿이어도
    // 실제로 위협을 사용해 쿨타임이 걸려 있는 나이트만 대상이어야 하므로 IsOnCooldown으로 필터링한다.
    private ChessPieces FindKnightOnCooldown(int team)
    {
        if (ChessBoard.Instance == null || ChessBoard.Instance.Pieces == null || PieceSkillManager.Instance == null)
            return null;

        ChessPieces[,] board = ChessBoard.Instance.Pieces;
        for (int x = 0; x < ChessBoard.TileCountX; x++)
        {
            for (int y = 0; y < ChessBoard.TileCountY; y++)
            {
                ChessPieces p = board[x, y];
                if (p != null && p.team == team && IsKnight(p.type) && PieceSkillManager.Instance.IsOnCooldown(p))
                    return p;
            }
        }

        return null;
    }
    #endregion

    #region 매치 리셋
    // 새 매치를 시작할 때 이전 매치에서 획득한 증강 효과를 모두 초기화한다.
    // (레전더리5 "섭정"처럼 다른 매니저 상태를 직접 바꿔놓은 효과도 함께 원상 복구)
    public void ResetAll()
    {
        knightThreatRangeBonus[0] = 0; knightThreatRangeBonus[1] = 0;
        rookShieldRangeBonus[0] = 0; rookShieldRangeBonus[1] = 0;

        hasKnightKillCdReduction[0] = false; hasKnightKillCdReduction[1] = false;
        hasThreatTargetKillCdReduction[0] = false; hasThreatTargetKillCdReduction[1] = false;
        hasBishopKillCdReduction[0] = false; hasBishopKillCdReduction[1] = false;
        hasRookKillCdReduction[0] = false; hasRookKillCdReduction[1] = false;

        emergencySwapUsesLeft[0] = 0; emergencySwapUsesLeft[1] = 0;

        acquiredAugments[0].Clear();
        acquiredAugments[1].Clear();
    }
    #endregion
}
