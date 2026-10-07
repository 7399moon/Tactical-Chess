using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 카드 선택 화면(프로모션 / 증강 / 이진 선택 공용 "Card UI" 패널)을 화면 아래로 내렸다 올리는 토글 기능.
// 선택 중에 체스판과 보유 증강을 확인할 수 있도록, 패널 전체를 화면 밖 아래로 슬라이드시킨다.
// - 이 스크립트는 Card UI 패널 오브젝트에 붙는다. 패널이 켜질 때(= 새 카드 선택 화면이 열릴 때)마다
//   항상 "펼침" 상태로 초기화하고 토글 버튼을 보여주며, 패널이 꺼지면 토글 버튼도 숨긴다.
// - 패널을 내려도 카드 선택 상태(IsSelecting)와 턴 타이머는 그대로 유지된다(보드 입력은 계속 잠김).
// - 토글 버튼은 패널 밖(Canvas 직속)에 두어 패널이 내려가도 계속 눌러서 다시 올릴 수 있다.
// - 순수 로컬 UI 기능이라 네트워크 동기화는 필요 없다.
public class CardPanelLowerToggle : MonoBehaviour
{
    #region 인스펙터 설정값
    [SerializeField] private Button toggleButton;       // 패널 밖에 있는 토글 버튼
    [SerializeField] private Text toggleLabel;          // 토글 버튼 라벨
    [SerializeField] private Button confirmButton;      // 확인 버튼 (패널과 함께 보이고 숨겨짐, 클릭 처리는 CardSelectionManager)
    [SerializeField] private float lowerDistance = 1300f; // 내릴 때 이동 거리 (Canvas 기준 px, 화면 높이보다 크게)
    [SerializeField] private float slideDuration = 0.25f; // 슬라이드 시간 (초)
    [SerializeField] private string raisedLabel = "▼ 내리기";     // 펼쳐진 상태에서 버튼에 표시할 문구
    [SerializeField] private string loweredLabel = "▲ 카드 선택";  // 내려간 상태에서 버튼에 표시할 문구
    #endregion

    #region 내부 상태
    private RectTransform panelRect;
    private bool isLowered;
    private bool listenerAdded;
    private Coroutine slideRoutine;
    #endregion

    #region 유니티 생명주기
    // 패널이 켜질 때마다(= 새 카드 선택 화면이 열릴 때) 펼침 상태로 초기화하고 버튼을 보여준다.
    private void OnEnable()
    {
        if (panelRect == null) panelRect = (RectTransform)transform;

        if (toggleButton != null && !listenerAdded)
        {
            toggleButton.onClick.AddListener(Toggle);
            listenerAdded = true;
        }

        ResetToRaised();
        if (toggleButton != null) toggleButton.gameObject.SetActive(true);
        if (confirmButton != null) confirmButton.gameObject.SetActive(true);
    }

    // 패널이 꺼지면(선택 완료) 버튼을 숨기고 위치를 원래대로 돌려놓는다.
    private void OnDisable()
    {
        ResetToRaised();
        if (toggleButton != null) toggleButton.gameObject.SetActive(false);
        if (confirmButton != null) confirmButton.gameObject.SetActive(false);
    }
    #endregion

    #region 토글 동작
    // 버튼 클릭 시 펼침 <-> 내림 전환
    private void Toggle()
    {
        isLowered = !isLowered;
        UpdateLabel();

        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = StartCoroutine(SlideTo(isLowered ? -lowerDistance : 0f));
    }

    // 즉시 펼침 상태로 복귀 (애니메이션 없이)
    private void ResetToRaised()
    {
        if (slideRoutine != null)
        {
            StopCoroutine(slideRoutine);
            slideRoutine = null;
        }

        isLowered = false;
        if (panelRect != null)
        {
            Vector2 pos = panelRect.anchoredPosition;
            pos.y = 0f;
            panelRect.anchoredPosition = pos;
        }
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (toggleLabel != null) toggleLabel.text = isLowered ? loweredLabel : raisedLabel;
    }

    // 패널을 목표 y 위치까지 부드럽게 이동 (Time.timeScale과 무관하게 동작)
    private IEnumerator SlideTo(float targetY)
    {
        float startY = panelRect.anchoredPosition.y;
        float elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideDuration));
            Vector2 pos = panelRect.anchoredPosition;
            pos.y = Mathf.Lerp(startY, targetY, t);
            panelRect.anchoredPosition = pos;
            yield return null;
        }

        Vector2 finalPos = panelRect.anchoredPosition;
        finalPos.y = targetY;
        panelRect.anchoredPosition = finalPos;
        slideRoutine = null;
    }
    #endregion
}
