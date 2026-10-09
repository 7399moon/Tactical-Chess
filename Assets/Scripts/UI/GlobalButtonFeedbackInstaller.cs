using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 씬이 로드될 때마다 그 씬에 있는 모든 UnityEngine.UI.Button에 UIButtonFeedback을 자동으로 붙여준다.
// 이렇게 하면 버튼마다 에디터에서 일일이 컴포넌트를 추가하지 않아도 "모든 버튼"에 호버/클릭
// 피드백(호버 시 어두워짐, 클릭 시 0.95배로 작아짐)이 일괄 적용된다.
//
// 비활성화된 상태로 씬에 이미 존재하는 버튼(설정 패널, 카드 선택 패널 등 평소엔 꺼져 있다가 나중에
// 켜지는 패널)도 FindObjectsByType(...Include)로 함께 찾아 미리 붙여둔다 - 다만 런타임에 코드로
// Instantiate되는 버튼(예: 동적으로 생성되는 목록 항목)까지는 이 설치 한 번으로 잡아내지 못한다.
public static class GlobalButtonFeedbackInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        AttachToSceneButtons();
        SceneManager.sceneLoaded += (scene, mode) => AttachToSceneButtons();
    }

    private static void AttachToSceneButtons()
    {
        // 스킬 버튼(나이트/비숍/룩/퀸/킹)은 자체 상태 표현과 겹쳐 기존 느낌이 달라진다는 피드백을
        // 받고 전역 피드백 대상에서 제외했다 - SkillUIManager.SkillButtonsExcludedFromGlobalFeedback 참고.
        var excluded = new HashSet<Button>();
        var skillManagers = Object.FindObjectsByType<SkillUIManager>(FindObjectsInactive.Include);
        foreach (var sm in skillManagers)
            foreach (var b in sm.SkillButtonsExcludedFromGlobalFeedback())
                excluded.Add(b);

        var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include);
        foreach (var b in buttons)
        {
            if (b == null || excluded.Contains(b)) continue;
            if (b.GetComponent<UIButtonFeedback>() == null)
                b.gameObject.AddComponent<UIButtonFeedback>();
        }
    }
}
