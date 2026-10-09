using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 모든 버튼에 공통으로 적용되는 호버/클릭 피드백.
// - 마우스 호버 시: 버튼의 targetGraphic 색이 어두워진다.
// - 누르는 동안: 버튼 전체 크기가 0.95배로 작아져(눌린 느낌) 떼면 즉시 원래 크기로 돌아온다.
// GlobalButtonFeedbackInstaller가 씬에 있는 모든 UnityEngine.UI.Button에 이 컴포넌트를 자동으로
// 붙여주므로, 버튼마다 수동으로 추가할 필요는 없다.
[RequireComponent(typeof(Button))]
public class UIButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private const float HoverDarkenFactor = 0.8f; // 호버 시 RGB에 곱하는 값(낮을수록 더 어두워짐)
    private const float PressScale = 0.95f;

    private Button button;
    private Graphic targetGraphic;
    private Color normalColor;
    private Vector3 baseScale;
    private bool isHovering;

    private void Awake()
    {
        button = GetComponent<Button>();
        targetGraphic = button != null ? button.targetGraphic : null;

        // Button 자체의 ColorTint 전환과 이 컴포넌트의 색 변경이 겹쳐 서로 다른 타이밍으로
        // 두 번 바뀌지 않도록, 색/크기 피드백은 이 컴포넌트가 전담하고 Button의 전환은 꺼둔다.
        if (button != null)
            button.transition = Selectable.Transition.None;

        if (targetGraphic != null)
            normalColor = targetGraphic.color;

        baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        isHovering = false;
        transform.localScale = baseScale;
        ApplyColor();
    }

    private void OnDisable()
    {
        isHovering = false;
        transform.localScale = baseScale;
        ApplyColor();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        ApplyColor();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        ApplyColor();
        transform.localScale = baseScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        transform.localScale = baseScale * PressScale;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        transform.localScale = baseScale;
    }

    private void ApplyColor()
    {
        if (targetGraphic == null) return;

        if (button != null && !button.interactable)
        {
            targetGraphic.color = normalColor;
            return;
        }

        targetGraphic.color = isHovering
            ? new Color(normalColor.r * HoverDarkenFactor, normalColor.g * HoverDarkenFactor, normalColor.b * HoverDarkenFactor, normalColor.a)
            : normalColor;
    }
}
