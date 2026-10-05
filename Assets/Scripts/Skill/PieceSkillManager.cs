using System.Collections.Generic;
using UnityEngine;

// 기물별 액티브 스킬(나이트-위협, 비숍-워프, 룩-쉴드, 킹-지휘)의 쿨타임 및 상태 효과(부동, 보호막 등)를 관리하는 싱글톤 클래스.
// 파일이 길어져 기능별로 partial class로 분할되어 있다.
//  - PieceSkillManager.cs        : 핵심 필드, 초기화, 턴 시작 시 쿨타임/상태 틱 처리
//  - PieceSkillManager.Status.cs : 상태(쿨타임/부동/쉴드/지휘) 조회 및 수정 API
//  - PieceSkillManager.Skills.cs : 나이트/비숍/룩/킹 액티브 스킬 발동 로직
public partial class PieceSkillManager : MonoBehaviour
{
    public static PieceSkillManager Instance { get; private set; }

    #region 인스펙터 설정값 (스킬 쿨타임, 턴 단위)
    [Header("Skill Cooldown Settings (Turns)")]
    [SerializeField] private int knightThreatCooldown = 4;
    [SerializeField] private int bishopWarpCooldown = 4;
    [SerializeField] private int rookShieldCooldown = 6;
    [SerializeField] private int kingCommandCooldown = 8; // 인스펙터 기본값(팀별 kingCommandCooldownBase 배열의 초기값으로 쓰인다)
    #endregion

    #region 스킬 VFX 프리팹
    [Header("Skill VFX Prefabs")]
    [SerializeField] private GameObject threatVfxPrefab;   // 나이트 위협: 대상 머리 위에서 아래로 떨어지는 붉은 이펙트 (위협 지속시간 동안 유지)
    [SerializeField] private GameObject shieldVfxPrefab;   // 룩 쉴드: 대상을 감싸는 푸른 이펙트 (쉴드 지속시간 동안 유지)
    [SerializeField] private GameObject commandVfxPrefab;  // 킹 지휘: 대상 머리 위에 뜨는 노란 이펙트 (지휘 추가 이동 소모 전까지 유지)
    [SerializeField] private GameObject warpVfxPrefab;     // 비숍 워프: 워프 도착 지점에서 재생되는 연보라 이펙트 (1회성)

    [SerializeField] private float warpVfxLifetime = 0.5f;   // 워프 VFX 자동 파괴까지의 시간(초)
    #endregion

    #region 내부 상태 필드
    // 기물별 상태 타이머 딕셔너리
    private readonly Dictionary<ChessPieces, int> cooldowns = new Dictionary<ChessPieces, int>();
    private readonly Dictionary<ChessPieces, int> immobilized = new Dictionary<ChessPieces, int>();
    private readonly Dictionary<ChessPieces, int> shielded = new Dictionary<ChessPieces, int>();

    // 킹 지휘 대상 보관: 캐스팅 시점엔 "예약"만 되고(pendingCommandTarget), 지휘 대상 팀의 다음
    // 턴이 시작될 때 HandleTurnStarted에서 비로소 commandedPiece/commandMovesLeft로 활성화된다
    // (2026-10-05 수정: 캐스팅한 바로 그 턴에 대상이 움직여버리는 버그 수정 - 아래 HandleTurnStarted 참고).
    private readonly Dictionary<int, ChessPieces> pendingCommandTarget = new Dictionary<int, ChessPieces>();

    // 유니크2(연속 워프) 판정용: 이번 턴에 사용한 워프 횟수 (팀별)
    private readonly Dictionary<int, int> warpUsesThisTurn = new Dictionary<int, int> { { 0, 0 }, { 1, 0 } };
    // 연속 워프 체인 도중 차원 암살(적 처치)이 한 번이라도 있었는지 (체인 종료 시 쿨타임 디메리트 판정용)
    private readonly Dictionary<int, bool> warpChainUsedAttack = new Dictionary<int, bool> { { 0, false }, { 1, false } };

    // GC Alloc 방지용 Key 캐싱 리스트
    private readonly List<ChessPieces> tickCacheKeys = new List<ChessPieces>();

    // 2026-10-05 수정(2차): pendingCommandTarget은 팀별로 분리돼 있었지만, 정작 "활성화된" 지휘
    // 상태(commandedPiece/commandMovesLeft)는 팀 구분 없는 단일 필드였다. 그 결과 양 팀이 각자
    // 지휘를 예약해두면, 나중에 활성화되는 팀의 HandleTurnStarted 호출이 먼저 활성화돼 있던 다른
    // 팀의 지휘 상태를 통째로 덮어써 버리는 치명적 버그가 있었다(그 팀의 지휘 대상/잔여 이동이
    // 흔적도 없이 사라지고 VFX도 고아 상태로 남음). 팀별 Dictionary로 완전히 분리해 관리한다.
    private readonly Dictionary<int, ChessPieces> commandedPieceByTeam = new Dictionary<int, ChessPieces>();
    private readonly Dictionary<int, int> commandMovesLeftByTeam = new Dictionary<int, int>();

    // 위협/쉴드/지휘 VFX 인스턴스 추적 (상태 종료 시 직접 파괴하기 위함)
    private readonly Dictionary<ChessPieces, GameObject> threatVfxInstances = new Dictionary<ChessPieces, GameObject>();
    private readonly Dictionary<ChessPieces, GameObject> shieldVfxInstances = new Dictionary<ChessPieces, GameObject>();
    private readonly Dictionary<int, GameObject> commandVfxInstanceByTeam = new Dictionary<int, GameObject>();

    // 킹 지휘 스킬의 "기본" 쿨타임(사용 직후 다시 채워지는 값)을 팀별로 독립 관리한다.
    // (2026-09-21 수정 1-3: 예전에는 kingCommandCooldown이 팀 구분 없는 단일 필드라, 레전더리5
    //  "섭정"(king_regency) 증강을 한쪽 팀만 보유해도 SetKingCommandCooldownBase(12) 호출이
    //  다른 팀의 킹 지휘 쿨타임 기본값까지 12로 바꿔버리는 누수가 있었다. AugmentManager가 이미
    //  "섭정" 보유 여부 자체는 팀별로 정확히 관리하고 있었으므로, 이 쿨타임 기본값도 팀별 배열로
    //  분리해 실제 보유 팀에게만 적용되도록 한다. 인덱스 0=White, 1=Black.)
    private readonly int[] kingCommandCooldownBase = new int[2];

    // 인스펙터에 설정된 킹 지휘 쿨타임의 원래 기본값 (레전더리5 "섭정" 증강이 런타임에 8->12로 바꿔놓을 수 있어,
    // 새 매치 시작 시 원래 값으로 되돌리기 위해 Awake 시점에 캐싱해둔다)
    private int kingCommandCooldownDefault;
    #endregion

    #region 외부 공개 프로퍼티
    // 2026-10-05 수정: 팀별로 완전히 독립된 지휘 상태를 조회하도록 team 매개변수를 받는 메서드로
    // 바꿨다(기존의 팀 구분 없는 단일 프로퍼티가 위 commandedPieceByTeam 필드 주석에 적은 버그의
    // 원인이었다). 호출부는 전부 "지금 행동 중인 팀" 또는 "확인하려는 기물의 팀"을 이미 알고
    // 있으므로 team을 넘기는 것으로 자연스럽게 치환된다.
    public bool IsCommandActiveForTeam(int team) =>
        commandedPieceByTeam.TryGetValue(team, out ChessPieces p) && p != null && commandMovesLeftByTeam.GetValueOrDefault(team) > 0;
    public ChessPieces GetCommandedPiece(int team) => commandedPieceByTeam.TryGetValue(team, out ChessPieces p) ? p : null;
    // 2026-10-04 추가: 턴 시작 시 "지휘 대상을 N회 더 이동시켜야 합니다" 화면 안내(CenterAnnouncer)에 사용.
    public int GetCommandMovesLeft(int team) => commandMovesLeftByTeam.GetValueOrDefault(team);
    #endregion

    #region 유니티 생명주기
    // 싱글턴 인스턴스 등록
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        kingCommandCooldownDefault = kingCommandCooldown;
        kingCommandCooldownBase[0] = kingCommandCooldownDefault;
        kingCommandCooldownBase[1] = kingCommandCooldownDefault;
    }

    // 턴 시작 이벤트 구독
    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnTurnStarted += HandleTurnStarted;
    }

    // 파괴 시 이벤트 구독 해제
    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnTurnStarted -= HandleTurnStarted;
    }
    #endregion

    #region 턴 시작 시 상태 갱신
    // 턴 시작 시 실행: 각 딕셔너리의 턴 수치를 1씩 갱신하고 지휘 예약 건을 처리
    private void HandleTurnStarted(int newTurnTeam)
    {
        TickDictionary(cooldowns);

        // [수정] 위협 상태(immobilized)가 이번 틱에 실제로 만료되는 대상은
        // IsThreatenedTarget 플래그도 같이 꺼줘야 레어3(연쇄 위협) 판정이 꼬이지 않는다.
        TickImmobilizedDictionary();

        TickShieldDictionary();

        // 연속 워프 사용 횟수 초기화 (해당 팀의 새 턴 시작)
        warpUsesThisTurn[newTurnTeam] = 0;
        warpChainUsedAttack[newTurnTeam] = false;

        // 2026-10-05 수정: 지휘는 캐스팅한 턴엔 예약만 되어 있고(pendingCommandTarget), 지휘 대상
        // 팀의 턴이 "새로" 시작되는 바로 이 시점에만 실제로 활성화(2회 이동 가능)된다. 캐스팅한
        // 팀이 섭정 등으로 같은 턴을 계속 이어가더라도 여기(새 턴 시작)를 거치지 않으므로 지휘
        // 대상은 그 턴엔 전혀 움직일 수 없다.
        if (pendingCommandTarget.TryGetValue(newTurnTeam, out ChessPieces target) && target != null)
        {
            commandedPieceByTeam[newTurnTeam] = target;
            commandMovesLeftByTeam[newTurnTeam] = 2;
            pendingCommandTarget.Remove(newTurnTeam);
        }
    }

    // 딕셔너리 값을 1 감소시키며, 0 이하인 항목은 안전하게 제거
    private void TickDictionary(Dictionary<ChessPieces, int> dict)
    {
        tickCacheKeys.Clear();
        tickCacheKeys.AddRange(dict.Keys);

        int count = tickCacheKeys.Count;
        for (int i = 0; i < count; i++)
        {
            ChessPieces piece = tickCacheKeys[i];
            if (piece == null)
            {
                dict.Remove(piece);
                continue;
            }

            dict[piece]--;
            if (dict[piece] <= 0)
                dict.Remove(piece);
        }
    }

    // immobilized 전용 틱 (만료 시 IsThreatenedTarget 플래그 및 위협 VFX도 함께 해제)
    private void TickImmobilizedDictionary()
    {
        tickCacheKeys.Clear();
        tickCacheKeys.AddRange(immobilized.Keys);

        int count = tickCacheKeys.Count;
        for (int i = 0; i < count; i++)
        {
            ChessPieces piece = tickCacheKeys[i];
            if (piece == null) { immobilized.Remove(piece); RemoveThreatVfx(piece); continue; }

            immobilized[piece]--;
            if (immobilized[piece] <= 0)
            {
                immobilized.Remove(piece);
                piece.IsThreatenedTarget = false;
                RemoveThreatVfx(piece);
            }
        }
    }

    // 쉴드 전용 틱 (만료 시 해당 기물에 붙은 쉴드 VFX 인스턴스도 함께 파괴)
    private void TickShieldDictionary()
    {
        tickCacheKeys.Clear();
        tickCacheKeys.AddRange(shielded.Keys);

        int count = tickCacheKeys.Count;
        for (int i = 0; i < count; i++)
        {
            ChessPieces piece = tickCacheKeys[i];
            if (piece == null) { shielded.Remove(piece); RemoveShieldVfx(piece); continue; }

            shielded[piece]--;
            if (shielded[piece] <= 0)
            {
                shielded.Remove(piece);
                RemoveShieldVfx(piece);
            }
        }
    }
    #endregion

    #region 스킬 VFX 유틸리티
    // 지정한 기물의 자식으로 VFX 프리팹을 생성한다. lifetime이 양수면 그 시간 뒤 자동 파괴(1회성 연출용).
    private GameObject SpawnPieceVfx(GameObject prefab, ChessPieces piece, Vector3 localOffset, float lifetime = -1f)
    {
        if (prefab == null || piece == null) return null;

        GameObject instance = Instantiate(prefab, piece.transform);
        instance.transform.localPosition = localOffset;
        instance.transform.localRotation = Quaternion.identity;

        if (lifetime > 0f)
            Destroy(instance, lifetime);

        return instance;
    }

    // 쉴드 상태가 해제된 기물의 VFX 인스턴스를 찾아 파괴하고 추적 목록에서 제거
    private void RemoveShieldVfx(ChessPieces piece)
    {
        if (piece == null) return;

        if (shieldVfxInstances.TryGetValue(piece, out GameObject vfx))
        {
            if (vfx != null) Destroy(vfx);
            shieldVfxInstances.Remove(piece);
        }
    }

    // 위협 상태가 해제된(만료된) 기물의 VFX 인스턴스를 찾아 파괴하고 추적 목록에서 제거
    private void RemoveThreatVfx(ChessPieces piece)
    {
        if (piece == null) return;

        if (threatVfxInstances.TryGetValue(piece, out GameObject vfx))
        {
            if (vfx != null) Destroy(vfx);
            threatVfxInstances.Remove(piece);
        }
    }

    #endregion

    #region 매치 리셋
    // 새 매치를 시작할 때 이전 매치의 스킬 상태(쿨타임/부동/쉴드/지휘/워프 기록)를 모두 초기화한다.
    // 증강으로 변경됐을 수 있는 킹 지휘 쿨타임 기본값도 양쪽 팀 모두 인스펙터 원본값으로 되돌린다.
    public void ResetAll()
    {
        cooldowns.Clear();
        immobilized.Clear();
        shielded.Clear();
        pendingCommandTarget.Clear();

        warpUsesThisTurn[0] = 0;
        warpUsesThisTurn[1] = 0;
        warpChainUsedAttack[0] = false;
        warpChainUsedAttack[1] = false;

        commandedPieceByTeam.Clear();
        commandMovesLeftByTeam.Clear();

        // 이전 매치에서 남아있을 수 있는 위협/쉴드/지휘 VFX 인스턴스 정리
        foreach (var kv in threatVfxInstances)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        threatVfxInstances.Clear();

        foreach (var kv in shieldVfxInstances)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        shieldVfxInstances.Clear();

        foreach (var kv in commandVfxInstanceByTeam)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        commandVfxInstanceByTeam.Clear();

        kingCommandCooldownBase[0] = kingCommandCooldownDefault;
        kingCommandCooldownBase[1] = kingCommandCooldownDefault;
    }
    #endregion
}
