using System;
using System.Collections.Generic;
using UnityEngine;

// 모든 UnoCardData 에셋을 한 곳에 모아두는 중앙 저장소 겸, ID로 카드 데이터를 조회하고
// 덱(카드 목록)을 만들어 주는 데이터베이스 클래스. AugmentDatabase와 같은 구조.
[CreateAssetMenu(fileName = "UnoCardDatabase", menuName = "체스 증강/우노 카드 데이터베이스")]
public class UnoCardDatabase : ScriptableObject
{
    [Header("전체 우노 카드 종류 목록")]
    public List<UnoCardData> allCards = new List<UnoCardData>();

    // cardId -> UnoCardData 조회용 캐시 (UI에서 카드마다 조회해도 선형 탐색이 반복되지 않도록 처음 한 번만 만든다)
    [NonSerialized] private Dictionary<string, UnoCardData> byId;
    // UnoCard 값 -> UnoCardData 조회용 캐시 (card.Id 문자열을 매번 만들지 않고 구조체 키로 바로 찾는다)
    [NonSerialized] private Dictionary<UnoCard, UnoCardData> byCard;

    #region 조회 메서드
    // 덱에 들어가는 전체 카드 장수 (copies 합계)
    public int TotalCardCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < allCards.Count; i++)
            {
                if (allCards[i] != null)
                    total += allCards[i].copies;
            }
            return total;
        }
    }

    // 고유 ID(cardId)로 카드 데이터를 검색. 없으면 null
    public UnoCardData GetById(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return null;

        if (byId == null) BuildCache();
        byId.TryGetValue(cardId, out UnoCardData data);
        return data;
    }

    // 런타임 카드 값(UnoCard)에 해당하는 카드 데이터를 검색
    public UnoCardData Get(UnoCard card)
    {
        if (byCard == null) BuildCache();
        byCard.TryGetValue(card, out UnoCardData data);
        return data;
    }

    // 각 카드 종류를 copies 장수만큼 펼쳐 덱(섞이지 않은 카드 목록)을 만든다
    public List<UnoCard> BuildDeck()
    {
        var deck = new List<UnoCard>(TotalCardCount);
        for (int i = 0; i < allCards.Count; i++)
        {
            UnoCardData data = allCards[i];
            if (data == null) continue;

            UnoCard card = UnoCard.FromData(data);
            for (int c = 0; c < data.copies; c++)
                deck.Add(card);
        }
        return deck;
    }

    private void BuildCache()
    {
        byId = new Dictionary<string, UnoCardData>(allCards.Count);
        byCard = new Dictionary<UnoCard, UnoCardData>(allCards.Count);
        for (int i = 0; i < allCards.Count; i++)
        {
            UnoCardData data = allCards[i];
            if (data == null) continue;

            byCard[UnoCard.FromData(data)] = data;
            if (!string.IsNullOrEmpty(data.cardId))
                byId[data.cardId] = data;
        }
    }

    // 에디터에서 목록을 수정했을 때 캐시를 다시 만들도록 무효화
    private void OnValidate()
    {
        byId = null;
        byCard = null;
    }
    #endregion

    #region 에디터 전용 도구
#if UNITY_EDITOR
    // 프로젝트 내의 모든 UnoCardData 에셋을 자동으로 검색하여 리스트에 등록 (에디터 컨텍스트 메뉴 전용)
    [ContextMenu("프로젝트에서 모든 UnoCardData 자동 수집")]
    private void CollectAllFromProject()
    {
        allCards.Clear();

        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:UnoCardData");
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            UnoCardData data = UnityEditor.AssetDatabase.LoadAssetAtPath<UnoCardData>(path);
            if (data != null)
                allCards.Add(data);
        }

        byId = null;
        byCard = null;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"UnoCardDatabase: {allCards.Count}개의 UnoCardData를 자동으로 채워 넣었습니다.");
    }
#endif
    #endregion
}
