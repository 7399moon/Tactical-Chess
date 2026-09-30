using System;
using System.Text;
using UnityEngine;

// 플레이어 닉네임 관련 데이터를 보관하는 static 클래스.
// - 내 닉네임: 시작 화면에서 입력한 값(1~16자). PlayerPrefs에 저장해 다음 실행 때 복원한다.
// - 팀별 닉네임: 게임 중 화면에 표시할 백(0) / 흑(1) 팀 플레이어의 닉네임.
//   ChessNetworkSync의 RPC로 서로 교환하며, 씬이 바뀌어도 유지된다(타이틀 복귀 시 초기화).
public static class PlayerProfile
{
    public const int MaxLength = 16;
    private const string SavedNicknameKey = "Profile.Nickname";

    #region 내 닉네임
    private static string nickname = string.Empty;

    // 현재 입력된 내 닉네임 (항상 정규화된 값: 제어문자 제거, 앞뒤 공백 제거, 최대 16자)
    public static string Nickname
    {
        get => nickname;
        set => nickname = Normalize(value);
    }

    // 마지막으로 저장된 닉네임을 불러온다 (없으면 빈 문자열)
    public static string LoadSavedNickname()
    {
        return Normalize(PlayerPrefs.GetString(SavedNicknameKey, string.Empty));
    }

    // 현재 닉네임을 저장 (유효한 경우에만)
    public static void SaveNickname()
    {
        if (IsValid(nickname))
            PlayerPrefs.SetString(SavedNicknameKey, nickname);
    }
    #endregion

    #region 정규화 / 검사
    // 줄바꿈 등 제어문자만 제거 (입력 중에는 공백을 자르면 띄어쓰기를 입력할 수 없으므로 공백은 유지)
    public static string StripControlChars(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (!char.IsControl(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    // 최종 닉네임 형태: 제어문자 제거 + 앞뒤 공백 제거 + 최대 길이 제한
    public static string Normalize(string raw)
    {
        string cleaned = StripControlChars(raw).Trim();
        return cleaned.Length > MaxLength ? cleaned.Substring(0, MaxLength) : cleaned;
    }

    // 정규화 후 1~16자이면 유효
    public static bool IsValid(string raw)
    {
        int length = Normalize(raw).Length;
        return length >= 1 && length <= MaxLength;
    }
    #endregion

    #region 팀별 닉네임 (게임 중 표시용)
    private static readonly string[] teamNicknames = new string[2];

    // 팀 닉네임이 바뀔 때 발생 (표시 UI 갱신용)
    public static event Action OnTeamNicknamesChanged;

    // 해당 팀(0=백, 1=흑)의 닉네임. 아직 모르면 null.
    public static string GetTeamNickname(int team)
    {
        return (team == 0 || team == 1) ? teamNicknames[team] : null;
    }

    // 팀 닉네임을 저장하고, 기존 값과 달라졌으면 true를 반환한다.
    public static bool SetTeamNickname(int team, string value)
    {
        if (team != 0 && team != 1) return false;

        string normalized = Normalize(value);
        if (teamNicknames[team] == normalized) return false;

        teamNicknames[team] = normalized;
        OnTeamNicknamesChanged?.Invoke();
        return true;
    }

    // 타이틀로 돌아갈 때 이전 게임의 닉네임이 남지 않도록 초기화
    public static void ClearTeamNicknames()
    {
        teamNicknames[0] = null;
        teamNicknames[1] = null;
        OnTeamNicknamesChanged?.Invoke();
    }
    #endregion
}
