using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 우노 카드 종류 50개(색상 4 x 12종 + 와일드 2종)의 UnoCardData 에셋과 UnoCardDatabase 에셋을
// Assets/Data 아래에 한 번에 만들어 주는 에디터 도구. UNO Cards 스프라이트 시트에서 이름으로 스프라이트를 찾아 연결한다.
// 이미 에셋이 있으면 새로 만들지 않고 값만 갱신하므로, 몇 번을 실행해도 안전하다 (GUID가 유지되어 참조가 깨지지 않는다).
public static class UnoCardAssetGenerator
{
    private const string SpriteSheetPath = "Assets/Icons/UNO Card/UNO Cards.png";
    private const string CardFolder = "Assets/Data/Uno";
    private const string DatabasePath = "Assets/Data/UnoCardDatabase.asset";

    private const int ColorCardCopies = 2;   // 색상 카드: 종류당 2장 (4색 x 12종 x 2 = 96장)
    private const int WildCardCopies = 4;    // 와일드 계열: 종류당 4장 (2종 x 4 = 8장)

    private static readonly UnoColor[] PlayColors = { UnoColor.Red, UnoColor.Yellow, UnoColor.Green, UnoColor.Blue };
    private static readonly string[] ColorNamesKo = { "빨강", "노랑", "초록", "파랑" };

    [MenuItem("체스 증강/우노 카드 에셋 생성·갱신")]
    public static void Generate()
    {
        Dictionary<string, Sprite> sprites = LoadSprites();
        if (sprites.Count == 0)
        {
            Debug.LogError($"[UnoCardAssetGenerator] {SpriteSheetPath}에서 스프라이트를 찾지 못했습니다.");
            return;
        }

        EnsureFolder("Assets/Data", "Uno");

        var created = new List<UnoCardData>();

        // 색상 카드: 숫자 0~8, 스킵, +2, 리버스 (숫자 9는 사용하지 않는다)
        for (int c = 0; c < PlayColors.Length; c++)
        {
            UnoColor color = PlayColors[c];
            string colorKo = ColorNamesKo[c];

            for (int n = 0; n <= 8; n++)
            {
                created.Add(Upsert(sprites, $"{color}_{n}", color, UnoKind.Number, n, ColorCardCopies,
                    $"{colorKo} {n}", NumberDescription(n), $"UNO_{color}_{n}", null));
            }

            created.Add(Upsert(sprites, $"{color}_Skip", color, UnoKind.Skip, 0, ColorCardCopies,
                $"{colorKo} 스킵", "체스 이동 1회 후, 상대의 턴 전체를 건너뛰고 내가 다시 카드를 냅니다.", $"UNO_{color}_Skip", null));
            created.Add(Upsert(sprites, $"{color}_Draw2", color, UnoKind.Draw2, 0, ColorCardCopies,
                $"{colorKo} +2", "상대에게 +2를 부과하고 체스 이동 1회를 합니다. 상대는 +2 또는 와일드 +4로 반격할 수 있습니다.", $"UNO_{color}_Draw2", null));
            created.Add(Upsert(sprites, $"{color}_Reverse", color, UnoKind.Reverse, 0, ColorCardCopies,
                $"{colorKo} 리버스", "나와 상대의 손패를 교환한 뒤 체스 이동 1회를 합니다.", $"UNO_{color}_Reverse", null));
        }

        // 와일드 계열: 색상을 고른 뒤에는 UNO_Wild_{색}, UNO_Wild_Draw4_{색} 스프라이트로 바뀐다
        created.Add(Upsert(sprites, "Wild", UnoColor.Wild, UnoKind.Wild, 0, WildCardCopies,
            "와일드", "색상을 고르고 체스 이동 1회를 합니다.", "UNO_Wild", "UNO_Wild_"));
        created.Add(Upsert(sprites, "WildDraw4", UnoColor.Wild, UnoKind.WildDraw4, 0, WildCardCopies,
            "와일드 +4", "상대에게 +4를 부과하고 색상을 고른 뒤 체스 이동 1회를 합니다. 상대는 와일드 +4로만 반격할 수 있습니다.", "UNO_Wild_Draw4", "UNO_Wild_Draw4_"));

        // 데이터베이스 에셋 생성/갱신
        var database = AssetDatabase.LoadAssetAtPath<UnoCardDatabase>(DatabasePath);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<UnoCardDatabase>();
            AssetDatabase.CreateAsset(database, DatabasePath);
        }
        database.allCards = created;
        EditorUtility.SetDirty(database);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[UnoCardAssetGenerator] 카드 종류 {created.Count}개, 총 {database.TotalCardCount}장 (DB: {DatabasePath})");
    }

    // 숫자 카드 효과 설명 (1~4 이동, 5~8 부활, 0 손패 리셋)
    private static string NumberDescription(int n)
    {
        if (n == 0)
            return "손패를 모두 덱에 섞고 같은 장수만큼 다시 뽑은 뒤 체스 이동 1회를 합니다.";
        if (n <= 4)
            return $"서로 다른 기물을 {n}번 움직입니다. 최소 1번은 이동해야 합니다.";
        return $"잡힌 내 기물 1개를 골라 내 진영 첫 두 줄의 {n}번째 또는 {n - 4}번째 세로 줄 빈칸에 부활시킵니다. 부활 후 이동은 없고, 폰은 2랭크에만 부활할 수 있습니다.";
    }

    // 카드 종류 하나의 에셋을 만들거나(없을 때) 값을 갱신한다
    private static UnoCardData Upsert(Dictionary<string, Sprite> sprites, string id, UnoColor color, UnoKind kind, int number,
        int copies, string displayName, string description, string spriteName, string chosenPrefix)
    {
        string path = $"{CardFolder}/{id}.asset";
        var data = AssetDatabase.LoadAssetAtPath<UnoCardData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<UnoCardData>();
            AssetDatabase.CreateAsset(data, path);
        }

        data.cardId = id;
        data.color = color;
        data.kind = kind;
        data.number = number;
        data.copies = copies;
        data.displayName = displayName;
        data.description = description;
        data.sprite = FindSprite(sprites, spriteName);

        if (chosenPrefix != null)
        {
            data.chosenColorSprites = new Sprite[PlayColors.Length];
            for (int i = 0; i < PlayColors.Length; i++)
                data.chosenColorSprites[i] = FindSprite(sprites, chosenPrefix + PlayColors[i]);
        }
        else
        {
            data.chosenColorSprites = new Sprite[0];
        }

        EditorUtility.SetDirty(data);
        return data;
    }

    private static Sprite FindSprite(Dictionary<string, Sprite> sprites, string name)
    {
        if (sprites.TryGetValue(name, out Sprite sprite)) return sprite;
        Debug.LogError($"[UnoCardAssetGenerator] 스프라이트를 찾지 못했습니다: {name}");
        return null;
    }

    private static Dictionary<string, Sprite> LoadSprites()
    {
        var result = new Dictionary<string, Sprite>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SpriteSheetPath))
        {
            if (asset is Sprite sprite)
                result[sprite.name] = sprite;
        }
        return result;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }
}
