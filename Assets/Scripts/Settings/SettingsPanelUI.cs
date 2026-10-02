using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 환경설정 버튼(화면 우측 최상단)과 환경설정 패널(음량 / 해상도 / 화면 모드)을 제어하는 UI 스크립트.
// SettingsCanvas 프리팹에 붙어 시작 씬 / 게임 씬(이후 로비 씬)에 각각 배치된다.
// 패널을 열어도 게임은 일시정지하지 않는다(Time.timeScale 변경 없음 - 턴 시간 제한은 계속 진행).
// 같은 세션에서 여러 씬의 SettingsCanvas가 동시에 로드되면(시작 씬 위에 게임 씬이 Additive로 로드됨)
// 가장 나중에 로드된 것만 보이도록 나머지는 자동으로 숨긴다.
public class SettingsPanelUI : MonoBehaviour
{
    #region 인스펙터 설정값
    [Header("전체 표시 루트 (여러 씬에 중복 배치 시 숨김 처리 대상)")]
    [SerializeField] private GameObject visualRoot;

    [Header("열기 / 닫기")]
    [SerializeField] private Button openButton;   // 우측 최상단 환경설정 버튼
    [SerializeField] private GameObject panel;    // 패널 전체(배경 차단막 포함)
    [SerializeField] private Button closeButton;

    [Header("음량")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Text volumeValueText;

    [Header("해상도 (SettingsManager.ResolutionOptions 순서와 동일)")]
    [SerializeField] private Button[] resolutionButtons;

    [Header("화면 모드")]
    [SerializeField] private Button windowedButton;
    [SerializeField] private Button fullScreenButton;

    [Header("버튼 색상")]
    // 버튼 스프라이트(btn_normal)에 곱해지는 틴트: 기본 = 원본색, 선택 = 금색, 사용 불가 = 어둡게
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(1f, 0.78f, 0.3f, 1f);
    [SerializeField] private Color unavailableColor = new Color(0.45f, 0.45f, 0.45f, 1f);
    #endregion

    #region 내부 상태
    // 현재 로드된 모든 SettingsPanelUI (로드 순서대로). 마지막 항목만 화면에 표시된다.
    private static readonly List<SettingsPanelUI> s_instances = new List<SettingsPanelUI>();
    private bool isOpen;
    #endregion

    #region 유니티 생명주기
    private void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(TogglePanel);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);
        if (windowedButton != null) windowedButton.onClick.AddListener(() => SettingsManager.Instance?.SetFullScreen(false));
        if (fullScreenButton != null) fullScreenButton.onClick.AddListener(() => SettingsManager.Instance?.SetFullScreen(true));

        if (volumeSlider != null)
        {
            volumeSlider.wholeNumbers = true;
            volumeSlider.minValue = 0;
            volumeSlider.maxValue = 100;
            volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
        }

        for (int i = 0; i < resolutionButtons.Length; i++)
        {
            int index = i; // 람다 캡처용 지역 복사
            resolutionButtons[i].onClick.AddListener(() => OnResolutionClicked(index));
        }

        if (panel != null) panel.SetActive(false);
    }

    private void OnEnable()
    {
        // 이 씬의 SettingsCanvas가 가장 최신이므로, 먼저 로드된 다른 씬의 것은 숨긴다.
        foreach (var other in s_instances)
            other.SetVisible(false);
        s_instances.Add(this);
        SetVisible(true);

        if (SettingsManager.Instance != null)
            SettingsManager.Instance.OnSettingsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        ClosePanel();
        s_instances.Remove(this);

        if (SettingsManager.Instance != null)
            SettingsManager.Instance.OnSettingsChanged -= Refresh;

        // 가장 나중 것이 사라졌다면 그 이전 것을 다시 보여준다.
        if (s_instances.Count > 0)
            s_instances[s_instances.Count - 1].SetVisible(true);
    }

    // ESC 키로도 패널을 열고 닫을 수 있다 (화면에 보이는 SettingsCanvas에서만 동작).
    private void Update()
    {
        if (visualRoot != null && visualRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            TogglePanel();
    }
    #endregion

    #region 패널 열기 / 닫기
    private void TogglePanel()
    {
        if (isOpen) ClosePanel();
        else OpenPanel();
    }

    private void OpenPanel()
    {
        if (isOpen || panel == null) return;
        isOpen = true;
        panel.SetActive(true);
        SettingsManager.IsPanelOpen = true;
        Refresh();
    }

    private void ClosePanel()
    {
        if (!isOpen) return;
        isOpen = false;
        if (panel != null) panel.SetActive(false);
        SettingsManager.IsPanelOpen = false;
        SettingsManager.Instance?.Save();
    }

    // 여러 씬에 SettingsCanvas가 중복될 때 표시 여부 전환 (숨길 때 열려 있던 패널은 닫는다)
    private void SetVisible(bool visible)
    {
        if (!visible) ClosePanel();
        if (visualRoot != null) visualRoot.SetActive(visible);
    }
    #endregion

    #region UI 이벤트 처리
    // 슬라이더를 드래그할 때마다 음량 적용 (표시 갱신은 SettingsManager 이벤트를 통해 Refresh에서 처리)
    private void OnVolumeSliderChanged(float value)
    {
        SettingsManager.Instance?.SetVolume(Mathf.RoundToInt(value));
    }

    private void OnResolutionClicked(int index)
    {
        Vector2Int option = SettingsManager.ResolutionOptions[index];
        SettingsManager.Instance?.SetResolution(option.x, option.y);
    }
    #endregion

    #region 표시 갱신
    // 현재 설정값에 맞춰 슬라이더 / 퍼센트 텍스트 / 버튼 색상과 활성 상태를 갱신한다.
    private void Refresh()
    {
        var settings = SettingsManager.Instance;
        if (settings == null) return;

        if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(settings.Volume);
        if (volumeValueText != null) volumeValueText.text = $"{settings.Volume}%";

        // 해상도: 현재 선택은 강조, 모니터보다 큰 해상도는 비활성(어둡게)
        for (int i = 0; i < resolutionButtons.Length; i++)
        {
            Vector2Int option = SettingsManager.ResolutionOptions[i];
            bool fits = SettingsManager.FitsDisplay(option.x, option.y);
            bool selected = settings.ResolutionWidth == option.x && settings.ResolutionHeight == option.y;

            resolutionButtons[i].interactable = fits;
            SetButtonColor(resolutionButtons[i], !fits ? unavailableColor : selected ? selectedColor : normalColor);
        }

        // 화면 모드: 현재 모드의 버튼은 비활성(강조), 반대 모드 버튼만 누를 수 있다.
        if (windowedButton != null)
        {
            windowedButton.interactable = settings.IsFullScreen;
            SetButtonColor(windowedButton, settings.IsFullScreen ? normalColor : selectedColor);
        }
        if (fullScreenButton != null)
        {
            fullScreenButton.interactable = !settings.IsFullScreen;
            SetButtonColor(fullScreenButton, settings.IsFullScreen ? selectedColor : normalColor);
        }
    }

    private static void SetButtonColor(Button button, Color color)
    {
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.color = color;
    }
    #endregion
}
