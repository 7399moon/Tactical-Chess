using System;
using UnityEngine;

// 환경설정(음량 / 해상도 / 화면 모드) 값을 보관하고, 실제 게임에 적용하며, PlayerPrefs에 저장/복원하는 싱글턴.
// 어느 씬에서 시작하든 앱 실행 시 자동으로 1회 생성되어(DontDestroyOnLoad) 저장된 설정을 즉시 적용하므로
// 씬마다 배치할 필요가 없다. 설정 UI(SettingsPanelUI)는 이 클래스의 값을 읽고 변경 메서드만 호출한다.
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    // 설정 패널이 열려 있는 동안 true. 패널 뒤의 체스판이 클릭되지 않도록 BoardInputHandler가 참조한다.
    public static bool IsPanelOpen { get; set; }

    #region 상수
    // 선택 가능한 해상도 목록 (UI 버튼 순서와 동일)
    public static readonly Vector2Int[] ResolutionOptions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1366, 768),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
    };

    private const string VolumeKey = "Settings.Volume";
    private const string ResolutionWidthKey = "Settings.ResolutionWidth";
    private const string ResolutionHeightKey = "Settings.ResolutionHeight";
    private const string FullScreenKey = "Settings.FullScreen";
    #endregion

    #region 현재 설정값
    public int Volume { get; private set; } = 100;            // 0~100 (%)
    public int ResolutionWidth { get; private set; }
    public int ResolutionHeight { get; private set; }
    public bool IsFullScreen { get; private set; }            // true = 전체화면, false = 창 모드

    // 설정값이 바뀔 때마다 발생 (UI가 자신의 표시를 갱신하는 데 사용)
    public event Action OnSettingsChanged;
    #endregion

    #region 유니티 생명주기
    // 첫 씬이 로드되기 전에 싱글턴을 자동 생성한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("SettingsManager");
        DontDestroyOnLoad(go);
        go.AddComponent<SettingsManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        IsPanelOpen = false;

        LoadAndApply();
    }

    private void OnApplicationQuit()
    {
        Save();
    }
    #endregion

    #region 저장값 불러오기 / 적용
    // 저장된 설정을 읽어 적용한다. 저장된 값이 없으면(첫 실행) 화면 설정은 건드리지 않고 현재 상태를 그대로 표시한다.
    private void LoadAndApply()
    {
        Volume = Mathf.Clamp(PlayerPrefs.GetInt(VolumeKey, 100), 0, 100);

        bool hasScreenSetting = PlayerPrefs.HasKey(ResolutionWidthKey) || PlayerPrefs.HasKey(FullScreenKey);

        IsFullScreen = PlayerPrefs.HasKey(FullScreenKey)
            ? PlayerPrefs.GetInt(FullScreenKey) == 1
            : Screen.fullScreenMode != FullScreenMode.Windowed;

        ResolutionWidth = PlayerPrefs.GetInt(ResolutionWidthKey, Screen.width);
        ResolutionHeight = PlayerPrefs.GetInt(ResolutionHeightKey, Screen.height);

        // 저장된 해상도가 현재 모니터보다 크면(모니터 교체 등) 들어가는 가장 큰 해상도로 낮춘다.
        if (hasScreenSetting && !FitsDisplay(ResolutionWidth, ResolutionHeight))
        {
            Vector2Int fallback = GetLargestFittingResolution();
            ResolutionWidth = fallback.x;
            ResolutionHeight = fallback.y;
        }

        ApplyVolume();
        if (hasScreenSetting) ApplyScreen();
    }

    // 음량 적용: AudioListener.volume으로 모든 소리(효과음 등)를 한 번에 조절한다.
    private void ApplyVolume()
    {
        AudioListener.volume = Volume / 100f;
    }

    // 해상도와 화면 모드 적용. 전체화면은 해상도 선택이 그대로 유효한 ExclusiveFullScreen을 사용한다.
    private void ApplyScreen()
    {
        Screen.SetResolution(
            ResolutionWidth,
            ResolutionHeight,
            IsFullScreen ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed);
    }
    #endregion

    #region 설정 변경 (UI에서 호출)
    // 음량 변경 (0~100). 드래그 중 매번 호출되므로 저장은 Save()에서 한꺼번에 처리한다.
    public void SetVolume(int percent)
    {
        Volume = Mathf.Clamp(percent, 0, 100);
        PlayerPrefs.SetInt(VolumeKey, Volume);
        ApplyVolume();
        OnSettingsChanged?.Invoke();
    }

    // 해상도 변경. 모니터보다 큰 해상도는 무시한다.
    public void SetResolution(int width, int height)
    {
        if (!FitsDisplay(width, height)) return;

        ResolutionWidth = width;
        ResolutionHeight = height;
        PlayerPrefs.SetInt(ResolutionWidthKey, width);
        PlayerPrefs.SetInt(ResolutionHeightKey, height);
        ApplyScreen();
        OnSettingsChanged?.Invoke();
    }

    // 화면 모드 변경 (true = 전체화면, false = 창 모드)
    public void SetFullScreen(bool fullScreen)
    {
        IsFullScreen = fullScreen;
        PlayerPrefs.SetInt(FullScreenKey, fullScreen ? 1 : 0);
        // 해상도 키가 없는 상태에서 모드만 바꿔도 현재 해상도가 함께 저장되도록 보장
        PlayerPrefs.SetInt(ResolutionWidthKey, ResolutionWidth);
        PlayerPrefs.SetInt(ResolutionHeightKey, ResolutionHeight);
        ApplyScreen();
        OnSettingsChanged?.Invoke();
    }

    // 변경된 값을 디스크에 저장 (설정 패널을 닫을 때, 앱 종료 시 호출)
    public void Save()
    {
        PlayerPrefs.Save();
    }
    #endregion

    #region 해상도 유틸
    // 해당 해상도가 현재 모니터(바탕화면) 크기 안에 들어가는지 여부
    public static bool FitsDisplay(int width, int height)
    {
        return width <= Display.main.systemWidth && height <= Display.main.systemHeight;
    }

    // 모니터에 들어가는 가장 큰 선택지를 반환 (하나도 없으면 가장 작은 선택지)
    private static Vector2Int GetLargestFittingResolution()
    {
        for (int i = ResolutionOptions.Length - 1; i >= 0; i--)
        {
            if (FitsDisplay(ResolutionOptions[i].x, ResolutionOptions[i].y))
                return ResolutionOptions[i];
        }
        return ResolutionOptions[0];
    }
    #endregion
}
