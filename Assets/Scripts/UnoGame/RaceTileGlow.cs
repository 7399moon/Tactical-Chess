using UnityEngine;

// UNO 경쟁에서 눌러야 하는 칸 위에 올리는 빛나는 표시: 진한 금색 바탕 + 테두리에서 번져 나가는 빛, 천천히 깜박인다.
public class RaceTileGlow : MonoBehaviour
{
    private const float GlowScale = 1.7f;   // 타일보다 얼마나 크게 그릴지 (테두리 바깥으로 번지는 빛 몫)
    private static Sprite sprite;

    private SpriteRenderer sr;
    private Vector3 baseScale;

    // 보드 타일 위에 표시를 만든다
    public static RaceTileGlow Create(GameObject tile)
    {
        var renderer = tile.GetComponent<Renderer>();
        Bounds b = renderer != null ? renderer.bounds : new Bounds(tile.transform.position, Vector3.one);
        float size = Mathf.Max(b.size.x, b.size.z);

        var go = new GameObject("Race Tile Glow");
        go.transform.position = new Vector3(b.center.x, b.max.y + 0.01f, b.center.z);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힌다

        var glow = go.AddComponent<RaceTileGlow>();
        glow.sr = go.AddComponent<SpriteRenderer>();
        glow.sr.sprite = GetSprite();
        glow.sr.sortingOrder = 50;
        float unit = size * GlowScale / glow.sr.sprite.bounds.size.x;
        glow.baseScale = Vector3.one * unit;
        go.transform.localScale = glow.baseScale;
        return glow;
    }

    private void Update()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
        sr.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.75f, 1f, pulse));
        transform.localScale = baseScale * Mathf.Lerp(0.97f, 1.04f, pulse);
    }

    // 안쪽은 진한 금색, 타일 경계에서 가장 밝고 바깥으로 갈수록 옅어지는 빛
    private static Sprite GetSprite()
    {
        if (sprite != null) return sprite;

        const int N = 256;
        float half = 0.5f / GlowScale;                       // 타일 반폭 (uv 단위)
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var gold = new Color(1f, 0.62f, 0f);
        var bright = new Color(1f, 1f, 0.7f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N - 0.5f;
                float d = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) - half; // 음수 = 타일 안쪽, 양수 = 바깥
                Color c; float a;
                if (d <= 0f)
                {
                    float inner = Mathf.Clamp01(-d / 0.05f);           // 경계에서 안쪽으로 갈수록 0 -> 1
                    c = Color.Lerp(bright, gold, inner);
                    a = Mathf.Lerp(1f, 0.85f, inner);
                }
                else
                {
                    float fall = Mathf.Exp(-d / 0.07f);               // 바깥으로 번지는 빛
                    c = new Color(1f, 0.7f, 0.05f); // 밝은 타일 위에서도 보이는 진한 금색
                    a = fall * Mathf.Clamp01((0.5f - half - d) / 0.03f + 0.2f); // 스프라이트 끝에서 부드럽게 사라진다
                }
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, Mathf.Clamp01(a)));
            }
        tex.Apply();
        sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        return sprite;
    }
}
