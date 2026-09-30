using UnityEngine;
using UnityEngine.UI;

// 시작 화면의 닉네임 입력칸(1~16자)을 제어한다.
// - 입력 중 줄바꿈 등 제어문자를 제거하고, 최대 16자까지만 입력받는다.
// - 유효한 닉네임(공백 제외 1~16자)이 있을 때만 HOST / JOIN 버튼을 활성화하고, 아니면 안내 문구를 보여준다.
// - 유효한 닉네임은 즉시 PlayerProfile.Nickname에 반영되어 HOST / JOIN 시 그대로 사용된다.
// - 마지막으로 사용한 닉네임은 저장해 두었다가 다음 실행 때 자동으로 채운다.
public class NicknameInputUI : MonoBehaviour
{
    #region 인스펙터 설정값
    [SerializeField] private InputField nicknameInput;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private GameObject warningObject; // "닉네임을 입력해야 합니다" 안내 (선택)
    #endregion

    #region 유니티 생명주기
    private void Awake()
    {
        if (nicknameInput == null) return;

        nicknameInput.characterLimit = PlayerProfile.MaxLength;
        nicknameInput.lineType = InputField.LineType.SingleLine;
        nicknameInput.onValueChanged.AddListener(OnValueChanged);
        nicknameInput.onEndEdit.AddListener(OnEndEdit);
    }

    // 저장된 닉네임을 채우고 버튼 상태를 갱신
    private void Start()
    {
        if (nicknameInput == null) return;

        nicknameInput.text = PlayerProfile.LoadSavedNickname();
        OnValueChanged(nicknameInput.text);
    }
    #endregion

    #region 입력 처리
    // 입력이 바뀔 때마다: 제어문자 제거 -> 닉네임 반영 -> 버튼/안내 갱신
    private void OnValueChanged(string value)
    {
        string stripped = PlayerProfile.StripControlChars(value);
        if (stripped != value)
        {
            nicknameInput.SetTextWithoutNotify(stripped);
            value = stripped;
        }

        PlayerProfile.Nickname = value;
        bool valid = PlayerProfile.IsValid(value);

        if (hostButton != null) hostButton.interactable = valid;
        if (joinButton != null) joinButton.interactable = valid;
        if (warningObject != null) warningObject.SetActive(!valid);
    }

    // 입력이 끝나면 앞뒤 공백을 정리해 표시하고 저장
    private void OnEndEdit(string value)
    {
        nicknameInput.SetTextWithoutNotify(PlayerProfile.Normalize(value));
        OnValueChanged(nicknameInput.text);
        PlayerProfile.SaveNickname();
    }
    #endregion
}
