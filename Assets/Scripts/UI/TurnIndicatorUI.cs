using UnityEngine;
using UnityEngine.UI;

// 상단 턴 수 표시(TurnCount) 위에 "현재 턴 플레이어 닉네임 + 의 턴"을 표시한다.
// GameManager.OnTurnStarted 구독 시점이 불확실할 수 있어 PlayerNameplateUI와 동일하게
// 매 프레임 CurrentTurn 변화를 감지해서만 다시 그리는 방식을 사용한다.
public class TurnIndicatorUI : MonoBehaviour
{
    [SerializeField] private Text indicatorText;

    private int shownTurn = -2;

    private void OnEnable()
    {
        shownTurn = -2;
        Refresh();
    }

    private void Update()
    {
        if (GameManager.Instance == null) return;
        if (GameManager.Instance.CurrentTurn != shownTurn)
            Refresh();
    }

    private void Refresh()
    {
        if (GameManager.Instance == null || indicatorText == null) return;

        int team = GameManager.Instance.CurrentTurn;
        shownTurn = team;

        string name = PlayerProfile.GetTeamNickname(team);
        if (string.IsNullOrEmpty(name))
            name = team == 0 ? "백팀" : "흑팀";

        indicatorText.text = $"{name}의 턴";
    }
}
