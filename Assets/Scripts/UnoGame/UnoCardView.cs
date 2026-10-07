using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 아래 카드 패(부채꼴)에 놓이는 카드 한 장의 화면 오브젝트.
// 위치/각도/크기는 UnoUI가 정한 "목표값"으로 부드럽게 따라가고, 마우스 입력은 UnoUI에 전달만 한다.
public class UnoCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private const float FollowSpeed = 14f; // 목표값으로 따라가는 속도 (클수록 빠르다)

    public RectTransform Rect { get; private set; }
    public Image Image { get; private set; }
    public Outline Outline { get; private set; }
    public UnoCard Card { get; private set; }

    // UnoUI가 매 프레임 갱신하는 목표값
    public Vector2 TargetPos;
    public float TargetRot;
    public float TargetScale = 1f;

    private UnoUI owner;

    public static UnoCardView Create(UnoUI owner, RectTransform parent, UnoCard card, Sprite sprite, Vector2 size)
    {
        var go = new GameObject("Card " + card.Id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = size;

        var view = go.AddComponent<UnoCardView>();
        view.owner = owner;
        view.Rect = rt;
        view.Card = card;
        view.Image = go.GetComponent<Image>();
        view.Image.sprite = sprite;
        view.Image.raycastTarget = true;

        // 선택(1번 클릭) 표시용 테두리: 평소에는 꺼 둔다
        view.Outline = go.AddComponent<Outline>();
        view.Outline.effectColor = new Color(1f, 0.82f, 0.25f, 1f);
        view.Outline.effectDistance = new Vector2(5f, 5f);
        view.Outline.enabled = false;
        return view;
    }

    // 목표 위치로 즉시 이동 (처음 배치나 뽑은 카드를 덱 위치에서 출발시킬 때)
    public void SnapTo(Vector2 pos, float rot, float scale)
    {
        Rect.anchoredPosition = TargetPos = pos;
        Rect.localRotation = Quaternion.Euler(0, 0, rot);
        TargetRot = rot;
        Rect.localScale = Vector3.one * scale;
        TargetScale = scale;
    }

    public bool Frozen; // 패배 연출 중에는 UnoUI가 직접 움직인다

    private void Update()
    {
        if (Frozen) return;
        float t = 1f - Mathf.Exp(-FollowSpeed * Time.unscaledDeltaTime); // 프레임 속도와 무관한 부드러운 보간
        Rect.anchoredPosition = Vector2.Lerp(Rect.anchoredPosition, TargetPos, t);
        float z = Mathf.LerpAngle(Rect.localEulerAngles.z, TargetRot, t);
        Rect.localRotation = Quaternion.Euler(0, 0, z);
        float s = Mathf.Lerp(Rect.localScale.x, TargetScale, t);
        Rect.localScale = new Vector3(s, s, 1f);
    }

    public void OnPointerEnter(PointerEventData e) => owner.OnCardHover(this, true);
    public void OnPointerExit(PointerEventData e) => owner.OnCardHover(this, false);
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) owner.OnCardClicked(this);
    }
}
