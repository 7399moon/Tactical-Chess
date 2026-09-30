using UnityEngine;

// 증강 선택 제한 시간(MatchSettings.AugmentTimeLimit) 처리 partial 파일.
// 증강 카드 선택 / 이어지는 이진 선택 각 단계마다 30초를 주고, 시간이 끝나면 활성 카드 중 하나를 무작위로 자동 선택한다.
// 남은 시간은 게임 씬의 세로 턴 타임바(GameManager.SetSelectionBar)에 표시한다.
// 카드 선택은 각 클라이언트가 자기 팀 화면에서만 열리므로, 타이머도 자기 화면의 선택에만 작동한다.
public partial class CardSelectionManager
{
    private bool selectTimerArmed;
    private float selectRemaining;

    private void Update()
    {
        bool want = MatchSettings.AugmentEnabled && MatchSettings.AugmentTimeLimit
                    && IsSelecting && currentMode != CardSelectionMode.Promotion && !isClosing;

        if (!want)
        {
            if (selectTimerArmed)
            {
                selectTimerArmed = false;
                GameManager.Instance?.SetSelectionBar(-1f);
            }
            return;
        }

        if (!selectTimerArmed)
        {
            selectTimerArmed = true;
            selectRemaining = MatchSettings.AugmentSelectSeconds;
        }

        selectRemaining -= Time.unscaledDeltaTime;
        GameManager.Instance?.SetSelectionBar(selectRemaining / MatchSettings.AugmentSelectSeconds);

        if (selectRemaining > 0f) return;

        selectTimerArmed = false;
        GameManager.Instance?.SetSelectionBar(-1f);

        // 현재 화면에 떠 있는 카드 중 무작위 1장을 자동 선택한다.
        var candidates = new System.Collections.Generic.List<CardUI>();
        foreach (var card in cardList)
            if (card != null && card.gameObject.activeSelf) candidates.Add(card);
        if (candidates.Count > 0)
            OnCardSelected(candidates[Random.Range(0, candidates.Count)]);
    }
}
