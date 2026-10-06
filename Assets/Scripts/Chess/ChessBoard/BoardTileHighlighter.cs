using System.Collections.Generic;
using UnityEngine;

// 체스판 타일의 머티리얼 색상 변경 및 시각적 강조(이동/캡처/체크/체크메이트/호버)를 전담하는 클래스.
public class BoardTileHighlighter : MonoBehaviour
{
    public static BoardTileHighlighter Instance { get; private set; }

    #region 열거형
    public enum TileHighlightState { None, Move, Capture }
    #endregion

    #region 인스펙터 설정값
    [Header("References")]
    [SerializeField] private ChessBoard board;

    [Header("Highlight Color Settings")]
    [SerializeField] private Color moveTint = new Color(1.0f, 1.3f, 0.2f);
    [SerializeField] private Color captureTint = new Color(1.4f, 0.25f, 0.25f);
    [SerializeField] private float hoverDimFactor = 0.6f;
    #endregion

    #region 내부 상태 필드
    private TileHighlightState[,] tileHighlightStates;
    private Material[,] tileMaterialInstances;
    // 타일 기본색 캐시: RefreshTileColor가 호출될 때마다 Material.color(네이티브 호출)를 읽지 않도록 초기화 시 한 번만 읽는다.
    private Color whiteTileColor;
    private Color blackTileColor;

    private Vector2Int checkedKingTile = -Vector2Int.one;
    private readonly List<Vector2Int> checkmateAttackerTiles = new List<Vector2Int>();

    // 체크 및 체크메이트 강조용 고정 색상 상수
    private static readonly Color CheckTint = new Color(1.6f, 0.2f, 0.2f, 1.0f);
    private static readonly Color CheckmateAttackerTint = new Color(1.6f, 0.9f, 0.1f, 1.0f);
    #endregion

    #region 유니티 생명주기 및 초기화
    // 싱글턴 인스턴스 등록
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 타일 렌더러 캐싱 및 머티리얼 인스턴스 배열 생성
    public void Initialize(ChessBoard chessBoard)
    {
        board = chessBoard;
        tileHighlightStates = new TileHighlightState[ChessBoard.TileCountX, ChessBoard.TileCountY];
        tileMaterialInstances = new Material[ChessBoard.TileCountX, ChessBoard.TileCountY];
        whiteTileColor = board.TileMaterialWhite.color;
        blackTileColor = board.TileMaterialBlack.color;

        for (int x = 0; x < ChessBoard.TileCountX; x++)
        {
            for (int y = 0; y < ChessBoard.TileCountY; y++)
            {
                MeshRenderer renderer = board.GetTileObject(x, y).GetComponent<MeshRenderer>();
                tileMaterialInstances[x, y] = renderer.material;
            }
        }
    }
    #endregion

    #region 하이라이트 상태 조작
    // 지정된 타일의 이동/공격 하이라이트 상태를 설정
    public void SetHighlightState(int x, int y, TileHighlightState state)
    {
        tileHighlightStates[x, y] = state;
    }

    // 지정된 타일의 상태(이동, 공격, 체크, 호버 등)를 종합하여 최종 머티리얼 색상을 갱신
    public void RefreshTileColor(Vector2Int pos, Vector2Int currentHover)
    {
        if (board == null || tileMaterialInstances == null) return;

        // 체스판 체크판 무늬 기본 색상 계산
        Color baseColor = (((pos.x + pos.y) & 1) == 0) ? whiteTileColor : blackTileColor;
        Color result = baseColor;

        // 1. 이동 / 공격 타일 하이라이트 적용
        if (tileHighlightStates[pos.x, pos.y] == TileHighlightState.Move)
        {
            result.r *= moveTint.r;
            result.g *= moveTint.g;
            result.b *= moveTint.b;
        }
        else if (tileHighlightStates[pos.x, pos.y] == TileHighlightState.Capture)
        {
            result.r *= captureTint.r;
            result.g *= captureTint.g;
            result.b *= captureTint.b;
        }

        // 2. 체크 상태인 킹 타일 강조
        if (pos == checkedKingTile)
        {
            result.r *= CheckTint.r;
            result.g *= CheckTint.g;
            result.b *= CheckTint.b;
        }

        // 3. 체크메이트를 유발한 공격 기물 타일 강조
        if (checkmateAttackerTiles.Contains(pos))
        {
            result.r *= CheckmateAttackerTint.r;
            result.g *= CheckmateAttackerTint.g;
            result.b *= CheckmateAttackerTint.b;
        }

        // 4. 마우스 호버 연출 (다른 강조가 없을 때만 적용)
        if (currentHover == pos && tileHighlightStates[pos.x, pos.y] == TileHighlightState.None)
        {
            result *= hoverDimFactor;
        }

        // 최종 머티리얼 색상 적용
        tileMaterialInstances[pos.x, pos.y].color = result;
    }

    // 전달받은 타일 목록에 이동 가능 하이라이트를 즉시 적용
    public void HighlightCustomTiles(List<Vector2Int> tiles)
    {
        ClearCustomHighlights();

        for (int i = 0; i < tiles.Count; i++)
        {
            Vector2Int pos = tiles[i];
            if (pos.x >= 0 && pos.x < ChessBoard.TileCountX && pos.y >= 0 && pos.y < ChessBoard.TileCountY)
            {
                tileHighlightStates[pos.x, pos.y] = TileHighlightState.Move;
                RefreshTileColor(pos, -Vector2Int.one);
            }
        }
    }

    // 모든 custom 타일 하이라이트를 초기화
    // 2026-10-03 수정: 턴 시작 시 안전망으로 ChessInteractionManager.ClearCustomHighlights()를
    // 거쳐 이 메서드가 호출되도록 했는데, 게임 첫 턴(GameManager.Start() -> StartTurn(0))은
    // 스크립트 실행 순서상 Initialize()가 아직 호출되기 전(=tileHighlightStates가 null인 상태)에
    // 실행될 수 있어 NullReferenceException이 발생했다. 초기화 전이면 지울 하이라이트도 없으므로
    // 그냥 조용히 반환한다.
    public void ClearCustomHighlights()
    {
        if (tileHighlightStates == null) return;

        for (int x = 0; x < ChessBoard.TileCountX; x++)
        {
            for (int y = 0; y < ChessBoard.TileCountY; y++)
            {
                if (tileHighlightStates[x, y] != TileHighlightState.None)
                {
                    tileHighlightStates[x, y] = TileHighlightState.None;
                    RefreshTileColor(new Vector2Int(x, y), -Vector2Int.one);
                }
            }
        }
    }
    #endregion

    #region 체크 / 체크메이트 강조
    // 체크 상태에 빠진 킹의 위치를 설정하고 시각 효과를 갱신
    public void SetCheckedKingTile(Vector2Int pos)
    {
        Vector2Int oldChecked = checkedKingTile;
        checkedKingTile = pos;

        if (oldChecked != -Vector2Int.one && oldChecked != checkedKingTile)
            RefreshTileColor(oldChecked, -Vector2Int.one);

        if (checkedKingTile != -Vector2Int.one)
            RefreshTileColor(checkedKingTile, -Vector2Int.one);
    }

    // 체크메이트를 공격한 기물들의 타일을 강조
    public void HighlightCheckmateAttackers(List<Vector2Int> attackerTiles)
    {
        checkmateAttackerTiles.Clear();
        if (attackerTiles != null)
            checkmateAttackerTiles.AddRange(attackerTiles);

        for (int i = 0; i < checkmateAttackerTiles.Count; i++)
            RefreshTileColor(checkmateAttackerTiles[i], -Vector2Int.one);
    }
    #endregion
}
