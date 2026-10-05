using UnityEngine;
using UnityEngine.UI;

// 상단 닉네임 표시와 환경설정 버튼 사이에 휴전 협정 남은 턴 수를 표시한다.
// GameManager.armisticeTurns가 0이면 텍스트를 숨기고, 0보다 크면 "휴전 협정 종료까지 N턴"으로 표시한다.
public class ArmisticeStatusUI : MonoBehaviour
{
    [SerializeField] private Text armisticeText;

    private int shownTurns = -1;

    private void Update()
    {
        if (GameManager.Instance == null || armisticeText == null) return;

        int turns = GameManager.Instance.armisticeTurns;
        if (turns == shownTurns) return;
        shownTurns = turns;

        armisticeText.enabled = turns > 0;
        if (turns > 0)
            armisticeText.text = $"휴전 협정 종료까지 {turns}턴";
    }
}
