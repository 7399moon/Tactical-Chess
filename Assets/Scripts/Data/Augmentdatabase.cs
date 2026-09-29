using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 모든 AugmentData ScriptableObject 에셋을 한 곳에 모아두는 중앙 저장소 겸,
// 희귀도/ID로 원하는 증강 데이터를 조회할 수 있는 검색 기능을 제공하는 데이터베이스 클래스.
[CreateAssetMenu(fileName = "AugmentDatabase", menuName = "체스 증강/증강 데이터베이스")]
public class AugmentDatabase : ScriptableObject
{
    [Header("전체 증강 목록")]
    public List<AugmentData> allAugments = new List<AugmentData>();

    #region 조회 메서드
    // 지정된 희귀도(Rarity)에 해당하는 증강 데이터 목록을 조회하여 반환
    public List<AugmentData> GetByRarity(AugmentRarity rarity)
    {
        var result = new List<AugmentData>();
        for (int i = 0; i < allAugments.Count; i++)
        {
            if (allAugments[i] != null && allAugments[i].rarity == rarity)
                result.Add(allAugments[i]);
        }
        return result;
    }

    // 고유 ID(augmentId)로 특정 증강 데이터 에셋을 검색
    public AugmentData GetById(string augmentId)
    {
        for (int i = 0; i < allAugments.Count; i++)
        {
            if (allAugments[i] != null && allAugments[i].augmentId == augmentId)
                return allAugments[i];
        }
        return null;
    }
    #endregion

    #region 에디터 전용 도구
#if UNITY_EDITOR
    // 프로젝트 내의 모든 AugmentData 에셋을 자동으로 검색하여 리스트에 등록 (에디터 컨텍스트 메뉴 전용)
    [ContextMenu("프로젝트에서 모든 AugmentData 자동 수집")]
    private void CollectAllFromProject()
    {
        allAugments.Clear();

        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AugmentData");
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            AugmentData data = UnityEditor.AssetDatabase.LoadAssetAtPath<AugmentData>(path);
            if (data != null)
                allAugments.Add(data);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"AugmentDatabase: {allAugments.Count}개의 AugmentData를 자동으로 채워 넣었습니다.");
    }
#endif
    #endregion
}
