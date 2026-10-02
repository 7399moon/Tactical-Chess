using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 개별 카드 UI의 visual 데이터 바인딩, 마우스 포인터 반응(Hover/Click) 및 아웃라인 연출을 담당.
public class CardUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    #region 인스펙터 설정값
    [Header("UI References")]
    [SerializeField] private Outline cardOutline;       // UI Outline 컴포넌트
    [SerializeField] private GameObject checkmarkOverlay; // 선택 확정 시 표시할 체크 표시(선택 사항, 없으면 표시 생략)

    [Header("Card Content")]
    [SerializeField] private Image iconImage;           // 기물/증강 이미지
    [SerializeField] private Text nameText;             // 기물/증강 이름
    [SerializeField] private Text descriptionText;      // 기물/증강 설명

    [Header("Rarity Frames")] // [변경] 등급별 카드 프레임 스프라이트
    [SerializeField] private Image frameImage;          // 카드 루트 Image (비어 있으면 자동 캐싱)
    [SerializeField] private Sprite frameNormal;
    [SerializeField] private Sprite frameRare;
    [SerializeField] private Sprite frameUnique;
    [SerializeField] private Sprite frameLegendary;

    [Header("Outline Colors")]
    [SerializeField] private Color hoverColor = new Color(0f, 1f, 1f, 1f);      // 호버 시 색상 (예: 시안)
    [SerializeField] private Color selectColor = new Color(1f, 0.8f, 0f, 1f);   // 클릭 시 색상 (예: 골드)
    #endregion

    #region 내부 상태 필드
    private Action<CardUI> onClickCallback;               // 카드 선택 시 부모 매니저로 넘길 콜백
    private bool isInteractable = true;                   // 카드 상호작용 가능 여부
    private bool isSelected = false;                       // 이 카드가 선택 확정되어 체크 표시를 유지 중인지 여부
    #endregion

    #region 유니티 생명주기
    private void Awake()
    {
        if (frameImage == null) frameImage = GetComponent<Image>(); // [변경]

        // 아웃라인 자동 캐싱 및 기본 비활성화
        if (cardOutline == null)
            cardOutline = GetComponent<Outline>();

        if (cardOutline != null)
            cardOutline.enabled = false;

        if (checkmarkOverlay != null)
            checkmarkOverlay.SetActive(false);
    }
    #endregion

    #region 초기화 및 콘텐츠 설정
    // 카드 UI의 이미지와 텍스트 데이터를 설정
    // [변경] 등급 미지정(기물/프로모션 카드)은 노말 프레임을 사용한다.
    public void SetVisual(Sprite icon, string cardName, string description)
        => SetVisual(icon, cardName, description, AugmentRarity.Normal);

    // [변경] 등급별 프레임 적용 오버로드
    public void SetVisual(Sprite icon, string cardName, string description, AugmentRarity rarity)
    {
        SetFrame(rarity);
        if (iconImage != null) iconImage.sprite = icon;
        if (nameText != null) nameText.text = cardName;
        if (descriptionText != null) descriptionText.text = description;
    }

    // [변경] 등급에 맞는 프레임 스프라이트로 교체
    public void SetFrame(AugmentRarity rarity)
    {
        if (frameImage == null) frameImage = GetComponent<Image>();
        if (frameImage == null) return;

        Sprite s = rarity switch
        {
            AugmentRarity.Rare => frameRare,
            AugmentRarity.Unique => frameUnique,
            AugmentRarity.Legendary => frameLegendary,
            _ => frameNormal
        };
        if (s != null) frameImage.sprite = s;
    }

    // 카드 초기화 및 클릭 이벤트 콜백을 등록.
    // 고정된 4개의 카드 슬롯을 매 라운드 재사용하므로, 이전 라운드에 남아있던 선택 체크 표시가
    // 그대로 남아있지 않도록 여기서 반드시 선택 상태를 초기화한다.
    public void SetupCard(Action<CardUI> onClick)
    {
        onClickCallback = onClick;
        SetInteractable(true);
        SetSelected(false);

        if (cardOutline != null)
            cardOutline.enabled = false;
    }

    // 카드의 클릭 및 호버 입력 활성화 상태를 변경
    public void SetInteractable(bool value) => isInteractable = value;

    // 이 카드가 "선택 확정" 상태인지 표시한다. true면 체크 표시를 띄우고 아웃라인을 골드색으로 고정한다.
    // 네트워크 대전 중 증강 카드를 선택한 직후, 상대방이 아직 선택하지 않아 애니메이션/패널 종료를
    // 미루는 "대기" 상태를 로컬 화면에 보여주기 위해 사용한다(CardSelectionManager.OnCardSelected 참고).
    public void SetSelected(bool value)
    {
        isSelected = value;

        if (checkmarkOverlay != null)
            checkmarkOverlay.SetActive(value);

        if (cardOutline != null)
        {
            if (value)
            {
                cardOutline.effectColor = selectColor;
                cardOutline.enabled = true;
            }
            else
            {
                cardOutline.enabled = false;
            }
        }
    }
    #endregion

    #region 포인터 이벤트 인터페이스
    // 마우스 호버 시 아웃라인 연출
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isInteractable || isSelected) return;

        if (cardOutline != null)
        {
            cardOutline.effectColor = hoverColor;
            cardOutline.enabled = true;
        }
    }

    // 마우스 호버 이탈 시 아웃라인 해제
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isInteractable || isSelected) return;

        if (cardOutline != null)
            cardOutline.enabled = false;
    }

    // 카드 클릭 시 아웃라인 연출 및 매니저 콜백 호출
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isInteractable) return;

        if (cardOutline != null)
        {
            cardOutline.effectColor = selectColor;
            cardOutline.enabled = true;
        }

        onClickCallback?.Invoke(this);
    }
    #endregion
}
