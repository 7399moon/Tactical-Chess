using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 패배 연출 공용 도구: 가장자리가 붉게 번지는 비네트 스프라이트(코드 생성)와 카메라 흔들림.
// 모든 모드의 패배 화면(GameOverUI)과 우노 모드의 15장 패배 연출(UnoUI)이 함께 쓴다.
public static class DefeatFx
{
    private static Sprite vignetteSprite;
    private static readonly Dictionary<Transform, Vector3> cameraOrigins = new Dictionary<Transform, Vector3>();

    public static Sprite VignetteSprite()
    {
        if (vignetteSprite != null) return vignetteSprite;

        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                float a = Mathf.Clamp01((d - 0.35f) / 0.65f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        vignetteSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        return vignetteSprite;
    }

    // 켜져 있는 모든 카메라를 duration 동안 흔든다 (끝나면 원래 위치로 복구)
    public static IEnumerator ShakeCameras(float duration, float amplitude)
    {
        RestoreCameras();
        foreach (var cam in Camera.allCameras)
            if (cam != null && cam.enabled) cameraOrigins[cam.transform] = cam.transform.localPosition;

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / duration);
            foreach (var kv in cameraOrigins)
                if (kv.Key != null)
                    kv.Key.localPosition = kv.Value + Random.insideUnitSphere * amplitude * k;
            yield return null;
        }
        RestoreCameras();
    }

    public static void RestoreCameras()
    {
        foreach (var kv in cameraOrigins)
            if (kv.Key != null) kv.Key.localPosition = kv.Value;
        cameraOrigins.Clear();
    }
}
