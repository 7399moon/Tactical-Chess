using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// CardSelectionManager의 데이터 바인딩/뽑기(풀링) 로직 partial 파일.
// 카드에 표시할 데이터를 채우고(Populate), 등급 필터에 맞는 증강 카드를 무작위로 뽑는 역할을 담당한다.
public partial class CardSelectionManager
{
    #region 카드 데이터 바인딩
    // 프로모션 카드 UI 데이터 바인딩
    private void PopulatePromotionCards()
    {
        if (promotionOptions == null) return;

        for (int i = 0; i < cardList.Length; i++)
        {
            CardUI card = cardList[i];
            if (card == null) continue;

            if (i < promotionOptions.Count && promotionOptions[i] != null)
            {
                PromotionOptionData option = promotionOptions[i];
                card.SetVisual(option.icon, option.displayName, option.description);
            }
        }
    }

    // 증강 카드 UI 데이터 바인딩
    private void PopulateAugmentCards()
    {
        List<AugmentData> picked = PickAugments(cardList.Length);
        currentOfferedAugments = picked;

        for (int i = 0; i < cardList.Length; i++)
        {
            CardUI card = cardList[i];
            if (card == null) continue;

            if (i < picked.Count && picked[i] != null)
            {
                card.SetVisual(picked[i].icon, picked[i].displayName, picked[i].description);
            }
        }
    }

    // 이진 선택 카드 UI 데이터 바인딩
    // 2026-09-21 수정: 이진 선택은 카드 4장 중 가운데 2장(인덱스 1, 2)만 사용해 중앙 정렬되어
    // 보이도록 한다(기존엔 왼쪽 2장(0, 1)을 사용해 화면 왼쪽으로 쏠려 보였음). pieceChoiceOptions는
    // cardList와 동일한 길이로 만들어 인덱스 1/2에만 값을 채우고 0/3은 null로 둔다 - 이렇게 하면
    // CloseCardSelection의 Array.IndexOf(cardList, currentSelectedCard) 결과(1 또는 2)를 그대로
    // pieceChoiceOptions의 인덱스로 써도 옵션이 정확히 대응된다(별도 오프셋 계산 불필요).
    private void PopulatePieceChoiceCards(PromotionOptionData optionA, PromotionOptionData optionB)
    {
        pieceChoiceOptions = new PromotionOptionData[cardList.Length];

        if (cardList.Length > 1 && cardList[1] != null && optionA != null)
        {
            cardList[1].SetVisual(optionA.icon, optionA.displayName, optionA.description);
            pieceChoiceOptions[1] = optionA;
        }

        if (cardList.Length > 2 && cardList[2] != null && optionB != null)
        {
            cardList[2].SetVisual(optionB.icon, optionB.displayName, optionB.description);
            pieceChoiceOptions[2] = optionB;
        }
    }
    #endregion

    #region 증강 카드 뽑기
    // 같은 등급 내에서만 무작위 증강 카드를 추출
    //
    // 2026-09-21 수정(1-1): 이 메서드는 White/Black 각자의 클라이언트 프로세스에서 독립적으로
    // 호출된다(증강 선택 화면이 더 이상 공유되지 않는 재설계 이후, 각 클라이언트는 자기 팀의 카드
    // 오퍼만 로컬로 생성한다). 예전에는 "이번 오퍼에 어떤 등급을 쓸지"까지 UnityEngine.Random으로
    // 매 호출마다 독립적으로 뽑았기 때문에, 같은 체크포인트에서도 White 오퍼와 Black 오퍼가 서로
    // 다른 등급으로 나올 수 있었다(신고: "서로 뜨는 증강의 등급이 다르다"). 등급 자체는 두 클라이언트
    // 에서 항상 동일해야 공정하므로, GetCheckpointRarity()로 결정론적으로(현재 체크포인트 시점의
    // GameManager.TurnCount로 시드된 난수) 먼저 등급을 고정한 뒤, 그 등급 안에서 구체적으로 어떤
    // 증강이 걸리는지는 지금처럼 각자 독립적으로 무작위로 뽑는다(등급만 같으면 되고, 개별 카드까지
    // 같을 필요는 없다는 사용자 요구사항에 맞춤).
    //
    // 2026-09-23 수정: 예전에는 결정된 등급의 남은 카드가 count장 미만이면 등급 제한을 아예 풀어서
    // 허용된 등급 전체를 섞어 채웠다 - 그 결과 카드가 부족한 등급의 카드가 다른 등급과 뒤섞여 한
    // 오퍼에 같이 나오는 문제가 있었다("등장할 등급의 카드가 부족하면 다른 등급의 카드가 나옴").
    // 이제 남은 카드가 minRemainingToAppear장(3장 이하) 미만인 등급은 아예 대상에서 제외하고,
    // 결정된 등급이 그 기준에 못 미치면 등급을 섞지 않고 다른 단일 등급으로 대체한다
    // (PickFallbackRarityExcluding 참고). 또한 여기서는 더 이상 usedAugmentIds에 추가하지 않는다 -
    // "제시된" 카드가 아니라 "실제로 선택된" 카드만 소모되어야 선택되지 않은 나머지가 다음에 다시
    // 나올 수 있으므로, 소모 처리는 실제 선택이 확정되는 ApplyAugmentChoice(Selection.cs)로 옮겼다.
    private const int minRemainingToAppear = 4; // "남은 카드가 3장 이하면 뜨지 않음" == 4장 이상이어야 후보

    private List<AugmentData> PickAugments(int count)
    {
        var picked = new List<AugmentData>(count);
        if (augmentDatabase == null || augmentDatabase.allAugments == null) return picked;

        AugmentRarity? chosenRarity = GetCheckpointRarity();

        int remainingInChosen = augmentDatabase.allAugments.Count(
            a => a != null && a.rarity == chosenRarity.Value && !usedAugmentIds.Contains(a.augmentId));

        if (remainingInChosen < minRemainingToAppear)
            chosenRarity = PickFallbackRarityExcluding(chosenRarity.Value);

        if (chosenRarity == null) return picked; // 제공 가능한 증강이 전혀 남지 않음

        List<AugmentData> pool = augmentDatabase.allAugments
            .Where(a => a != null && a.rarity == chosenRarity.Value && !usedAugmentIds.Contains(a.augmentId))
            .ToList();

        while (picked.Count < count && pool.Count > 0)
        {
            int randomIndex = UnityEngine.Random.Range(0, pool.Count);
            picked.Add(pool[randomIndex]);
            pool.RemoveAt(randomIndex);
        }

        return picked;
    }

    // 결정론적으로 고른 등급이 고갈(남은 카드 3장 이하)되었을 때, 등급을 섞지 않고 대체할 단일
    // 등급을 고른다. 남은 카드가 minRemainingToAppear장 이상인 다른 허용 등급이 있으면 그중
    // 하나를 무작위로 고르고, 그것도 없으면 그나마 가장 많이 남은 단일 등급(3장 이하라도)을
    // 사용한다 - 그래도 서로 다른 등급을 한 오퍼에 섞지는 않는다. 제공 가능한 카드가 전혀
    // 없으면 null을 반환한다.
    private AugmentRarity? PickFallbackRarityExcluding(AugmentRarity excluded)
    {
        var remainingByRarity = new Dictionary<AugmentRarity, int>();
        foreach (AugmentRarity rarity in Enum.GetValues(typeof(AugmentRarity)))
        {
            if (!IsRarityAllowed(rarity) || rarity == excluded) continue;

            remainingByRarity[rarity] = augmentDatabase.allAugments.Count(
                a => a != null && a.rarity == rarity && !usedAugmentIds.Contains(a.augmentId));
        }

        var healthy = remainingByRarity.Where(kv => kv.Value >= minRemainingToAppear).Select(kv => kv.Key).ToList();
        if (healthy.Count > 0)
            return healthy[UnityEngine.Random.Range(0, healthy.Count)];

        var best = remainingByRarity.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ToList();
        return best.Count > 0 ? best[0].Key : (AugmentRarity?)null;
    }

    // 이번 증강 체크포인트에서 사용할 등급을 결정론적으로 계산한다. 인스펙터에서 허용된 등급 목록
    // (allowedRarities, 양쪽 클라이언트에서 동일한 씬/프리팹 설정값) 중에서, 체크포인트 시점의
    // GameManager.TurnCount(EndTurn()이 turnCount++를 하기 "전"에 체크포인트를 트리거하므로, 같은
    // 체크포인트 동안에는 양쪽 클라이언트에서 항상 동일한 값)로 시드한 System.Random으로 하나를
    // 뽑는다. 별도의 RPC 없이도 양쪽 클라이언트가 항상 같은 등급을 계산하게 되는 핵심 지점.
    private AugmentRarity GetCheckpointRarity()
    {
        var allowed = new List<AugmentRarity>();
        foreach (AugmentRarity rarity in Enum.GetValues(typeof(AugmentRarity)))
        {
            if (IsRarityAllowed(rarity))
                allowed.Add(rarity);
        }

        if (allowed.Count == 0) return AugmentRarity.Normal;

        int seed = GameManager.Instance != null ? GameManager.Instance.TurnCount : 0;
        var rng = new System.Random(seed);
        int index = rng.Next(allowed.Count);
        return allowed[index];
    }

    // 카드 중복 방지 기록(usedAugmentIds)을 초기화한다. 새 게임 시작 시 호출.
    public void ResetUsedAugments()
    {
        usedAugmentIds.Clear();
        Debug.Log("[CardSelectionManager] 카드 중복 방지 목록이 초기화되었습니다.");
    }

    // 해당 rarity가 인스펙터에서 허용된 마스크에 포함되어 있는지 검사
    private bool IsRarityAllowed(AugmentRarity rarity)
    {
        int rarityBit = 1 << (int)rarity;
        return ((int)allowedRarities & rarityBit) != 0;
    }
    #endregion
}
