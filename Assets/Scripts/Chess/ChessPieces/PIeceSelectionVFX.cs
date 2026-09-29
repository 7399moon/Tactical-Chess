using UnityEngine;

// 선택된 기물 하단에 강조 VFX 인스턴스를 생성 및 제어하는 클래스.
public class PieceSelectionVFX : MonoBehaviour
{
    #region 인스펙터 설정값
    [Header("VFX Settings")]
    [SerializeField] private GameObject vfxPrefab;                  // 선택 강조 VFX 프리팹
    [SerializeField] private Vector3 vfxLocalOffset = Vector3.zero; // 기물 발밑 오프셋 위치
    #endregion

    private GameObject vfxInstance;

    #region 공개 메서드
    // 기물의 선택 강조 VFX 표시 여부를 설정
    public void SetVfxActive(bool active)
    {
        if (active)
        {
            if (vfxInstance == null)
                CreateVfxInstance();

            if (vfxInstance != null)
                vfxInstance.SetActive(true);
        }
        else
        {
            if (vfxInstance != null)
                vfxInstance.SetActive(false);
        }
    }

    // 하위 호환성을 유지하기 위한 래퍼 메서드
    public void SetOutline(bool enabled) => SetVfxActive(enabled);
    #endregion

    #region 내부 헬퍼
    // 지정된 프리팹과 오프셋 기반으로 VFX 인스턴스를 동적 생성
    private void CreateVfxInstance()
    {
        if (vfxPrefab == null) return;

        vfxInstance = Instantiate(vfxPrefab, transform);
        vfxInstance.transform.localPosition = vfxLocalOffset;
        vfxInstance.transform.localRotation = Quaternion.identity;
    }
    #endregion
}
