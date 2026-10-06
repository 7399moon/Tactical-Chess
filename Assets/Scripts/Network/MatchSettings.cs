using System;
using UnityEngine;

// 로비에서 호스트가 정한 "이번 판의 규칙". 호스트가 스냅샷 RPC로 모든 피어에 동일하게 전파하고,
// GameScene의 매니저들(GameManager/CardSelectionManager/SkillUIManager/QueenSkill 등)이 직접 읽는다.
// 로비를 거치지 않고 GameScene만 단독 실행하는 경우에는 기본값(= 기존 동작)이 그대로 적용된다.
public static class MatchSettings
{
    public const int MinAugments = 3;
    public const int MaxAugmentsLimit = 10;
    public const float AugmentSelectSeconds = 30f; // 증강 선택 제한 시간 (고정)

    public static bool AugmentEnabled = true;     // 증강 시스템
    public static int MaxAugments = 6;            // 팀당 최대 보유 증강 수 (3~10)
    public static bool AugmentTimeLimit = false;  // 증강 선택 시간 제한
    public static bool SkillEnabled = true;       // 스킬 시스템 (OFF면 퀸 아우라도 비활성)
    public static bool TurnTimeLimit = true;      // 턴 시간 제한
    public static int TurnSeconds = 30;           // 턴당 제한 시간(초)

    public static event Action OnChanged;

    public static void ResetToDefault()
    {
        AugmentEnabled = true; MaxAugments = 6; AugmentTimeLimit = false;
        SkillEnabled = true; TurnTimeLimit = true; TurnSeconds = 30;
        OnChanged?.Invoke();
    }

    // 규칙 간 의존성 정리: 스킬 OFF -> 증강 OFF 강제, 증강 OFF -> 증강 시간 제한 OFF, 값 범위 보정.
    public static void Normalize()
    {
        if (!SkillEnabled) AugmentEnabled = false;
        if (!AugmentEnabled) AugmentTimeLimit = false;
        MaxAugments = Mathf.Clamp(MaxAugments, MinAugments, MaxAugmentsLimit);
        if (TurnSeconds < 1) TurnSeconds = 30;
    }

    public static void Apply(bool augOn, int maxAug, bool augTime, bool skillOn, bool turnTime, int turnSec)
    {
        AugmentEnabled = augOn; MaxAugments = maxAug; AugmentTimeLimit = augTime;
        SkillEnabled = skillOn; TurnTimeLimit = turnTime; TurnSeconds = turnSec;
        Normalize();
        OnChanged?.Invoke();
    }
}

// 로비의 공유 상태(호스트가 권한자). 진영 선택값: -1 미선택, 0 백, 1 흑, 2 랜덤.
public static class LobbyState
{
    public const int PickNone = -1, PickWhite = 0, PickBlack = 1, PickRandom = 2;

    public static string HostNick = "";
    public static string GuestNick = "";
    public static bool GuestPresent;
    public static int HostPick = PickNone;
    public static int GuestPick = PickNone;

    public static event Action OnChanged;

    public static void Reset()
    {
        HostNick = ""; GuestNick = ""; GuestPresent = false;
        HostPick = PickNone; GuestPick = PickNone;
        MatchSettings.ResetToDefault();
        OnChanged?.Invoke();
    }

    // 게스트가 나갔을 때(로비 단계): 게스트 슬롯과 선택만 초기화하고 호스트는 로비에 남는다.
    public static void ClearGuest()
    {
        GuestNick = ""; GuestPresent = false; GuestPick = PickNone;
        OnChanged?.Invoke();
    }

    public static void Notify() => OnChanged?.Invoke();

    // 진영 선택 요청 검증(호스트 권한자에서만 호출). 같은 색을 상대가 이미 골랐으면 거부.
    public static bool TrySetPick(bool isHost, int pick)
    {
        if (pick < PickNone || pick > PickRandom) return false;
        if (!isHost && !GuestPresent) return false;
        int other = isHost ? GuestPick : HostPick;
        if ((pick == PickWhite || pick == PickBlack) && other == pick) return false;
        if (isHost) HostPick = pick; else GuestPick = pick;
        OnChanged?.Invoke();
        return true;
    }

    // 시작 가능 조건(D-5): 게스트가 있고 양쪽 모두 진영(랜덤 포함)을 골랐다.
    public static bool CanStart => GuestPresent && HostPick != PickNone && GuestPick != PickNone;

    // 호스트의 팀(0/1) 결정: 한쪽이 색을 고르면 그대로/반대, 둘 다 랜덤이면 무작위.
    public static int ResolveHostTeam()
    {
        if (HostPick == PickWhite || HostPick == PickBlack) return HostPick;
        if (GuestPick == PickWhite) return 1;
        if (GuestPick == PickBlack) return 0;
        return UnityEngine.Random.Range(0, 2);
    }

    // 클라이언트 측: 호스트가 보낸 스냅샷 적용.
    public static void ApplySnapshot(string hostNick, string guestNick, bool guestPresent, int hostPick, int guestPick)
    {
        HostNick = hostNick; GuestNick = guestNick; GuestPresent = guestPresent;
        HostPick = hostPick; GuestPick = guestPick;
        OnChanged?.Invoke();
    }
}

// 로비 -> 게임 씬 전환 정보. RPC_LobbyStart 수신 시 세팅되고, GameScene의 GameStartController가 소비한다.
public static class MatchSession
{
    public static bool InMatch;      // 로비를 떠나 게임 씬 단계에 들어갔는지 (true면 이탈 시 타이틀 복귀)
    public static bool Pending;      // GameStartController가 아직 소비하지 않은 시작 요청
    public static int LocalTeam = -1;

    // 2026-10-06 추가: 증강 체크포인트 등급(CardSelectionManager.GetCheckpointRarity)을 결정하는 매치별
    // 난수 시드. 기존에는 이 시드가 없어 GameManager.TurnCount(체크포인트마다 항상 10/20/30...으로
    // 고정)만으로 등급을 뽑았기 때문에, 매 게임 첫 증강(10턴째)이 모든 대전에서 항상 같은 등급(레전더리/
    // "플래티넘")으로 고정되어 보이는 버그가 있었다. 네트워크 대전 중에는 양쪽 클라이언트가 여전히
    // 같은 등급을 봐야 하므로, 호스트(로비 시작) 또는 재시작을 누른 쪽이 한 번만 뽑아 RPC로 전파한
    // 값을 여기 저장해 양쪽이 공유한다. 정적 필드 기본값 자체도 무작위로 초기화해, 로비를 거치지 않는
    // 로컬 테스트 모드의 첫 매치에서도 고정값(0)으로 시작하지 않도록 한다.
    public static int AugmentSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

    public static void Begin(int hostTeam, bool isHost, int augmentSeed)
    {
        LocalTeam = isHost ? hostTeam : 1 - hostTeam;
        AugmentSeed = augmentSeed;
        PlayerProfile.SetTeamNickname(hostTeam, LobbyState.HostNick);
        PlayerProfile.SetTeamNickname(1 - hostTeam, LobbyState.GuestNick);
        InMatch = true;
        Pending = true;
    }

    public static void Reset()
    {
        InMatch = false; Pending = false; LocalTeam = -1; AugmentSeed = 0;
    }
}
