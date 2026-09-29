using UnityEngine;
using UnityEngine.UI;

// 방 번호 입력 패널 UI. JOIN 버튼을 누르면 열리고, 입력한 방 번호로 참가를 시도한다.
// 입력창은 한글 플레이스홀더 표시를 위해 legacy InputField를 사용한다 (기본 TMP 폰트는 한글을 지원하지 않음).
public class RoomCodePanelUI : MonoBehaviour
{
    #region 인스펙터 설정값
    [SerializeField] private BasicSpawner spawner;
    [SerializeField] private InputField codeInputField;
    #endregion

    #region 공개 메서드
    // JOIN 버튼에서 호출: 입력창을 비우고 패널을 연다.
    public void OpenPanel()
    {
        if (codeInputField != null)
        {
            codeInputField.text = string.Empty;
        }

        gameObject.SetActive(true);
    }

    // 취소 버튼 등에서 호출: 패널을 닫는다.
    public void ClosePanel()
    {
        gameObject.SetActive(false);
    }

    // 확인 버튼에서 호출: 입력된 방 번호로 참가를 시도한다.
    public void OnClickConfirm()
    {
        if (codeInputField == null || spawner == null) return;

        string code = codeInputField.text.Trim();
        if (string.IsNullOrEmpty(code)) return;

        spawner.JoinGame(code);
    }
    #endregion
}
