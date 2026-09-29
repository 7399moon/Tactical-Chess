using UnityEngine;

// 시작 화면(타이틀) UI 로직. 현재는 게임 종료 버튼 기능만 담당한다.
public class StartMenu : MonoBehaviour
{
    #region 공개 메서드
    // "게임 종료" 버튼에 연결되는 메서드. 빌드에서는 애플리케이션을 종료하고,
    // 에디터에서는 종료 대신 플레이 모드를 정지시킨다.
    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
    #endregion
}
