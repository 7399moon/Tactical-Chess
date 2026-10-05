using System.Collections.Generic;
using UnityEngine;

// 퀸 전용 패시브 스킬(아우라) 관리 클래스.
// 적 기물 처치 시 스택이 적립되며, 6스택 달성 시 아군 기물을 한 단계 승급시킬 수 있다.
// 승급 단계: 폰 -> 나이트 -> 비숍 -> 룩 -> 퀸
// (필드에 퀸이 생존해 있을 때만 작동하며, 모든 퀸 파괴 시 스택이 초기화된다)
public class QueenSkill : MonoBehaviour
{
    public static QueenSkill Instance { get; private set; }

    #region 인스펙터 설정값
    [Header("Settings")]
    [SerializeField] private int maxStack = 6;              // 최대 적립 가능 스택
    [SerializeField] private ChessBoard chessBoard;         // 체스판 참조
    #endregion

    // 팀별 현재 스택 (팀 0: White, 팀 1: Black)
    private readonly Dictionary<int, int> teamAuraStacks = new Dictionary<int, int>();

    #region 유니티 생명주기
    // 싱글턴 등록 및 팀별 스택 초기화
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 팀별 스택 초기화
        teamAuraStacks[0] = 0;
        teamAuraStacks[1] = 0;
    }

    // 체스보드 참조가 비어있으면 씬에서 탐색해 캐싱
    private void Start()
    {
        if (chessBoard == null)
            chessBoard = FindAnyObjectByType<ChessBoard>();
    }
    #endregion

    #region 공개 상태 조회 및 스택 처리
    // 지정한 팀의 현재 아우라 스택 수치를 반환
    public int GetStack(int team) => !MatchSettings.SkillEnabled ? 0 : teamAuraStacks.TryGetValue(team, out int stack) ? stack : 0;

    // 유니크5(아우라 가속): 보유 시 발동에 필요한 스택이 1 감소
    public int GetRequiredStack(int team) =>
        HasAugment(team, "queen_aura_stack_reduction") ? Mathf.Max(1, maxStack - 1) : maxStack;

    // 지정한 팀의 스택이 발동 요구치에 도달했는지 확인
    public bool IsMaxStack(int team) => GetStack(team) >= GetRequiredStack(team);

    // 기물이 캡처되었을 때 스택 적립 및 퀸 파괴에 따른 초기화 연산을 수행
    public void NotifyPieceCaptured(ChessPieces attacker, ChessPieces victim)
    {
        if (victim == null) return;
        if (!MatchSettings.SkillEnabled) return; // 스킬 시스템 OFF: 퀸 아우라도 비활성

        // 1. 피격당한 기물이 퀸일 경우: 팀 내 살아있는 다른 퀸이 없다면 스택 초기화
        if (IsQueen(victim))
        {
            if (!HasLivingQueen(victim.team, victim))
            {
                teamAuraStacks[victim.team] = 0;
                Debug.Log($"{victim.team}팀의 모든 퀸이 파괴되어 아우라 스택이 0으로 초기화되었습니다.");
            }
            else
            {
                Debug.Log($"{victim.team}팀의 퀸이 파괴되었으나, 다른 퀸이 생존해 있어 스택이 유지됩니다.");
            }
        }

        if (attacker == null) return;
        int team = attacker.team;

        // 2. 공격측 진영에 생존한 퀸이 없으면 스택 적립 불가
        if (!HasLivingQueen(team)) return;

        // 3. 스택 가산량 계산
        //    기본: 퀸이 직접 잡으면 2, 그 외 아군이 잡으면 1
        //    레어9(여왕의 사냥): 퀸이 직접 잡을 때 +1 추가
        //    유니크6(여왕의 위엄): 누가 잡든 아군이 얻는 모든 스택에 +1 추가 (레어9와 중복 적용됨)
        int gainStack = IsQueen(attacker) ? 2 : 1;

        if (IsQueen(attacker) && HasAugment(team, "queen_stack_on_kill"))
            gainStack += 1;

        if (HasAugment(team, "queen_stack_gain_up"))
            gainStack += 1;

        // 4. 최대 스택 범위 내 수치 갱신
        teamAuraStacks[team] = Mathf.Min(maxStack, teamAuraStacks[team] + gainStack);
    }

    // 6스택 달성 후 타깃 아군 기물을 승급 처리
    // 2026-10-05 수정: 예전에는 target의 team을 그대로 "발동 팀"으로 사용해서, 적 기물을 클릭해도
    // (심지어 적 팀이 마침 6스택이었다면) 적 기물이 승급당하는 등 팀 검증이 전혀 없었다.
    // 반드시 실제로 스킬을 발동한 팀(actingTeam)의 턴이고, 대상도 그 팀 소속이어야만 승급을 허용한다.
    public bool TryPromoteTarget(ChessPieces targetPiece, int actingTeam)
    {
        if (targetPiece == null || chessBoard == null) return false;
        if (targetPiece.team != actingTeam) return false;

        int team = targetPiece.team;
        if (!IsMaxStack(team)) return false;
        if (!HasLivingQueen(team)) return false;

        // 폰, 나이트, 비숍, 룩만 승급 가능 (최대 단계 및 킹/퀸 제외)
        ChessPieceType nextType = GetNextPromotedType(targetPiece.type, team);
        if (nextType == targetPiece.type) return false;

        // 레전더리3(여왕의 축복): 아우라 발동 시 대상이 두 단계 승급
        if (HasAugment(team, "queen_aura_double_promote"))
        {
            ChessPieceType secondStepType = GetNextPromotedType(nextType, team);
            if (secondStepType != nextType)
                nextType = secondStepType;
        }

        chessBoard.PromotePieceAt(targetPiece.currentX, targetPiece.currentY, nextType, team);

        // 승급 처리 후 스택 소모
        teamAuraStacks[team] = 0;
        return true;
    }
    #endregion

    #region 내부 로직 헬퍼
    // 체스판에 특정 팀의 퀸이 1개 이상 살아있는지 검사
    private bool HasLivingQueen(int team, ChessPieces ignorePiece = null)
    {
        if (chessBoard == null || chessBoard.Pieces == null) return false;

        ChessPieces[,] board = chessBoard.Pieces;

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                ChessPieces p = board[x, y];
                // 제외 대상(방금 피격된 퀸)이 아닌 살아있는 퀸이 존재하면 true
                if (p != null && p != ignorePiece && p.team == team && IsQueen(p))
                    return true;
            }
        }
        return false;
    }

    // 해당 기물이 퀸인지 판별
    private bool IsQueen(ChessPieces piece) =>
        piece != null && (piece.type == ChessPieceType.WhiteQueen || piece.type == ChessPieceType.BlackQueen);

    // AugmentManager 보유 여부를 짧게 확인하기 위한 헬퍼
    private bool HasAugment(int team, string augmentId) =>
        AugmentManager.Instance != null && AugmentManager.Instance.HasAugment(team, augmentId);

    // 기물 등급 상승 단계별(폰 -> 나이트 -> 비숍 -> 룩 -> 퀸) 다음 체스 기물 타입을 계산
    private ChessPieceType GetNextPromotedType(ChessPieceType currentType, int team)
    {
        bool isWhite = (team == 0);
        switch (currentType)
        {
            case ChessPieceType.WhitePawn:
            case ChessPieceType.BlackPawn:
                return isWhite ? ChessPieceType.WhiteKnight : ChessPieceType.BlackKnight;

            case ChessPieceType.WhiteKnight:
            case ChessPieceType.BlackKnight:
                return isWhite ? ChessPieceType.WhiteBishop : ChessPieceType.BlackBishop;

            case ChessPieceType.WhiteBishop:
            case ChessPieceType.BlackBishop:
                return isWhite ? ChessPieceType.WhiteRook : ChessPieceType.BlackRook;

            case ChessPieceType.WhiteRook:
            case ChessPieceType.BlackRook:
                return isWhite ? ChessPieceType.WhiteQueen : ChessPieceType.BlackQueen;

            default:
                return currentType;
        }
    }
    #endregion
}