using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 스킬/상태 효과로 특정 행동이 막히거나, 플레이어에게 꼭 알려줘야 할 상황(지휘 2회 이동 등)이
// 발생했을 때 게임 화면 중앙에 짧게 떴다가 사라지는 안내 메시지를 담당하는 매니저.
// Debug.Log(콘솔)와 달리 플레이어가 실제로 보는 화면 UI이므로, 메시지 문구는 "[Skill]" 같은
// 내부 태그 없이 플레이어가 바로 이해할 수 있는 문장으로만 넘겨야 한다.
public class CenterAnnouncer : MonoBehaviour
{
    public static CenterAnnouncer Instance { get; private set; }

    #region 인스펙터 설정값
    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Text messageText;

    [Header("Timing")]
    [SerializeField] private float fadeInDuration = 0.15f;
    [SerializeField] private float holdDuration = 1.3f;
    [SerializeField] private float fadeOutDuration = 0.4f;
    #endregion

    #region 내부 상태
    // 대기 중인 메시지 목록. 같은 메시지가 연달아 쌓이는 것만 방지하고(리스트 맨 뒤와 비교),
    // 그 외에는 들어온 순서대로 하나씩 보여준다.
    private readonly List<string> pendingMessages = new List<string>();
    private Coroutine activeRoutine;
    #endregion

    #region 유니티 생명주기
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }
    #endregion

    #region 공개 API
    // 메시지를 화면 중앙에 표시. 이미 다른 메시지를 표시/대기 중이면 끝나는 대로 순서대로 보여준다.
    public static void Show(string message)
    {
        if (Instance == null || string.IsNullOrEmpty(message)) return;
        Instance.Enqueue(message);
    }
    #endregion

    #region 내부 처리
    private void Enqueue(string message)
    {
        // 바로 직전에 대기열에 들어온 것과 완전히 같은 메시지면 중복으로 쌓지 않는다
        // (예: 같은 프레임에 비슷한 판정이 여러 번 호출되는 경우).
        if (pendingMessages.Count > 0 && pendingMessages[pendingMessages.Count - 1] == message)
            return;

        pendingMessages.Add(message);

        if (activeRoutine == null)
            activeRoutine = StartCoroutine(ProcessQueue());
    }

    private IEnumerator ProcessQueue()
    {
        while (pendingMessages.Count > 0)
        {
            string message = pendingMessages[0];
            pendingMessages.RemoveAt(0);

            if (messageText != null)
                messageText.text = message;

            yield return Fade(0f, 1f, fadeInDuration);
            yield return new WaitForSeconds(holdDuration);
            yield return Fade(1f, 0f, fadeOutDuration);
        }

        activeRoutine = null;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (canvasGroup == null) yield break;

        if (duration <= 0f)
        {
            canvasGroup.alpha = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        canvasGroup.alpha = to;
    }
    #endregion
}
