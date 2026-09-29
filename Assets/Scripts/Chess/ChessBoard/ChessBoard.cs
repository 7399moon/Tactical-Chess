using UnityEngine;

// 체스보드의 타일 메쉬 생성, 기물 초기 배치, 프로모션 및 기물 데이터 배열을 관리하는 핵심 클래스.
public class ChessBoard : MonoBehaviour
{
    public static ChessBoard Instance { get; private set; }

    #region 상수
    public const int TileCountX = 8;
    public const int TileCountY = 8;
    #endregion

    #region 인스펙터 설정값
    [Header("Art Settings")]
    [SerializeField] private Material tileMaterialWhite;            // 백색 타일 머티리얼
    [SerializeField] private Material tileMaterialBlack;            // 흑색 타일 머티리얼
    [SerializeField] private float tileSize = 1.0f;                 // 타일 1개의 크기
    [SerializeField] private float yOffset = 0.0f;                  // 체스보드 Y축 높이 오프셋
    [SerializeField] private Vector3 boardCenter = Vector3.zero;    // 체스보드 중심 좌표
    [SerializeField] private GameObject boardFrame;                 // 외곽 프레임 프리팹

    [Header("Prefabs & Materials")]
    [SerializeField] private GameObject[] prefabs;                  // 기물 프리팹 배열
    [SerializeField] private Material pieceMaterial;                // 기물 통합 머티리얼

    [Header("Skill VFX")]
    [SerializeField] private GameObject promotionVfxPrefab;         // 프로모션(승급) 시 재생할 폭죽 VFX 프리팹
    [SerializeField] private float promotionVfxLifetime = 3f;       // 프로모션 VFX 자동 파괴까지의 시간(초)
    #endregion

    #region 내부 상태 필드
    private GameObject[,] tiles;                                    // 8x8 타일 게임오브젝트 배열
    private ChessPieces[,] chessPieces;                             // 8x8 체스 기물 컴포넌트 배열
    private Vector3 bounds;                                         // 체스보드 원점 기준 오프셋
    private GameObject boardFrameInstance;                          // 생성된 외곽 프레임 인스턴스 (표시/숨김 제어용)
    #endregion

    #region 외부 공개 프로퍼티
    public Material TileMaterialWhite => tileMaterialWhite;
    public Material TileMaterialBlack => tileMaterialBlack;
    public float TileSize => tileSize;
    public float YOffset => yOffset;
    public Vector3 Bounds => bounds;
    public ChessPieces[,] Pieces => chessPieces;
    #endregion

    #region 유니티 생명주기
    // 싱글턴 등록 후 타일/프레임 생성과 기물 초기 배치를 수행
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 1. 보드 타일 및 외곽 프레임 생성
        GenerateAllTiles(tileSize, TileCountX, TileCountY);
        GenerateBoardFrame();

        // 2. 초기 기물 생성 및 보드상 위치 정렬
        SpawnAllPieces();
        PositionAllPieces();
    }

    // 파괴 시 싱글턴 참조 해제
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    #endregion

    #region 보드 생성
    // 8x8 체스보드 타일 전체 생성 및 메쉬 계산
    private void GenerateAllTiles(float tileSize, int tileCountX, int tileCountY)
    {
        yOffset += transform.position.y;
        bounds = new Vector3((tileCountX / 2f) * tileSize, 0, (tileCountY / 2f) * tileSize) + boardCenter;

        tiles = new GameObject[tileCountX, tileCountY];
        for (int x = 0; x < tileCountX; x++)
            for (int y = 0; y < tileCountY; y++)
                tiles[x, y] = GenerateSingleTile(tileSize, x, y);
    }

    // 동적 메쉬(Mesh)를 활용하여 단일 타일 오브젝트 생성
    private GameObject GenerateSingleTile(float tileSize, int x, int y)
    {
        GameObject tileObject = new GameObject($"Tile_X{x}_Y{y}");
        tileObject.transform.SetParent(transform, false);

        Mesh mesh = new Mesh();
        MeshFilter meshFilter = tileObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = tileObject.AddComponent<MeshRenderer>();

        meshFilter.mesh = mesh;
        meshRenderer.material = ((x + y) % 2 == 0) ? tileMaterialWhite : tileMaterialBlack;

        // 정점 4개 정의
        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(x * tileSize, yOffset, y * tileSize) - bounds,
            new Vector3(x * tileSize, yOffset, (y + 1) * tileSize) - bounds,
            new Vector3((x + 1) * tileSize, yOffset, y * tileSize) - bounds,
            new Vector3((x + 1) * tileSize, yOffset, (y + 1) * tileSize) - bounds
        };

        mesh.vertices = vertices;
        mesh.triangles = new int[] { 0, 1, 2, 1, 3, 2 };
        mesh.RecalculateNormals();

        tileObject.layer = LayerMask.NameToLayer("Tile");
        tileObject.AddComponent<BoxCollider>();

        return tileObject;
    }

    // 체스보드 외곽 프레임 생성
    private void GenerateBoardFrame()
    {
        if (boardFrame == null) return;
        GameObject frame = Instantiate(boardFrame, transform);
        frame.transform.localPosition = new Vector3(boardCenter.x, transform.position.y - 0.1f, boardCenter.z) - transform.position;
        boardFrameInstance = frame;
    }
    #endregion

    #region 기물 생성 및 배치
    // 게임 시작 시 클래식 체스 규칙에 맞게 백팀/흑팀 기물 생성
    private void SpawnAllPieces()
    {
        chessPieces = new ChessPieces[TileCountX, TileCountY];

        // 백팀 (Team 0)
        chessPieces[0, 0] = SpawnSinglePiece(ChessPieceType.WhiteRook, 0);
        chessPieces[1, 0] = SpawnSinglePiece(ChessPieceType.WhiteKnight, 0);
        chessPieces[2, 0] = SpawnSinglePiece(ChessPieceType.WhiteBishop, 0);
        chessPieces[3, 0] = SpawnSinglePiece(ChessPieceType.WhiteQueen, 0);
        chessPieces[4, 0] = SpawnSinglePiece(ChessPieceType.WhiteKing, 0);
        chessPieces[5, 0] = SpawnSinglePiece(ChessPieceType.WhiteBishop, 0);
        chessPieces[6, 0] = SpawnSinglePiece(ChessPieceType.WhiteKnight, 0);
        chessPieces[7, 0] = SpawnSinglePiece(ChessPieceType.WhiteRook, 0);
        for (int i = 0; i < TileCountX; i++)
            chessPieces[i, 1] = SpawnSinglePiece(ChessPieceType.WhitePawn, 0);

        // 흑팀 (Team 1)
        chessPieces[0, 7] = SpawnSinglePiece(ChessPieceType.BlackRook, 1);
        chessPieces[1, 7] = SpawnSinglePiece(ChessPieceType.BlackKnight, 1);
        chessPieces[2, 7] = SpawnSinglePiece(ChessPieceType.BlackBishop, 1);
        chessPieces[3, 7] = SpawnSinglePiece(ChessPieceType.BlackQueen, 1);
        chessPieces[4, 7] = SpawnSinglePiece(ChessPieceType.BlackKing, 1);
        chessPieces[5, 7] = SpawnSinglePiece(ChessPieceType.BlackBishop, 1);
        chessPieces[6, 7] = SpawnSinglePiece(ChessPieceType.BlackKnight, 1);
        chessPieces[7, 7] = SpawnSinglePiece(ChessPieceType.BlackRook, 1);
        for (int i = 0; i < TileCountX; i++)
            chessPieces[i, 6] = SpawnSinglePiece(ChessPieceType.BlackPawn, 1);
    }

    // 단일 기물 프리팹 생성 및 데이터 설정
    private ChessPieces SpawnSinglePiece(ChessPieceType type, int team)
    {
        GameObject pieceObj = Instantiate(prefabs[(int)type - 1], transform);
        ChessPieces cp = pieceObj.GetComponent<ChessPieces>();
        cp.type = type;
        cp.team = team;

        if (pieceObj.TryGetComponent<MeshRenderer>(out var mr))
            mr.material = pieceMaterial;

        return cp;
    }

    // 배열 데이터의 좌표에 따라 모든 기물의 월드 위치를 동기화
    private void PositionAllPieces()
    {
        for (int x = 0; x < TileCountX; x++)
            for (int y = 0; y < TileCountY; y++)
                if (chessPieces[x, y] != null)
                    PositionSinglePiece(x, y);
    }

    // 단일 기물의 보드 좌표 지정 및 월드 스페이스 위치 조정
    private void PositionSinglePiece(int x, int y)
    {
        chessPieces[x, y].currentX = x;
        chessPieces[x, y].currentY = y;
        chessPieces[x, y].transform.position = GetTileCenter(x, y);
    }
    #endregion

    #region 표시 여부 제어
    // 타일/외곽 프레임/기물 전체의 표시 여부를 한 번에 전환한다.
    // (상대방 입장 대기 중에는 숨기고, 게임 시작 시 다시 보이도록 GameStartController에서 호출)
    public void SetBoardVisible(bool visible)
    {
        if (tiles != null)
        {
            foreach (var tile in tiles)
                tile?.SetActive(visible);
        }

        if (chessPieces != null)
        {
            foreach (var piece in chessPieces)
                piece?.gameObject.SetActive(visible);
        }

        if (boardFrameInstance != null)
            boardFrameInstance.SetActive(visible);
    }
    #endregion

    #region 조회 및 유틸리티
    // 보드 좌표 (x, y)의 월드 스페이스 중심 좌표 계산
    public Vector3 GetTileCenter(int x, int y)
    {
        return new Vector3(x * tileSize, yOffset, y * tileSize) - bounds + new Vector3(tileSize * 0.5f, 0, tileSize * 0.5f);
    }

    // Raycast로 감지된 타일 GameObject를 이용해 (x, y) 배열 좌표 역조회
    public Vector2Int LookupTileIndex(GameObject hitInfo)
    {
        for (int x = 0; x < TileCountX; x++)
            for (int y = 0; y < TileCountY; y++)
                if (tiles[x, y] == hitInfo)
                    return new Vector2Int(x, y);

        return -Vector2Int.one;
    }

    // 해당 위치의 기물을 파괴하고 새 기물 타입으로 교체
    public void PromotePieceAt(int x, int y, ChessPieceType newType, int team)
    {
        if (chessPieces[x, y] != null)
        {
            Destroy(chessPieces[x, y].gameObject);
            chessPieces[x, y] = null;
        }

        ChessPieces newPiece = SpawnSinglePiece(newType, team);
        chessPieces[x, y] = newPiece;
        PositionSinglePiece(x, y);

        SoundManager.Instance?.PlayPromotion();

        // 프로모션(기물 변경) VFX: 폭죽 형태로 한 번 재생 후 자동 파괴
        // 보드 표면에 가려지지 않도록 보드 높이보다 0.1만큼 위에 재생
        if (promotionVfxPrefab != null)
        {
            Vector3 promotionVfxPos = GetTileCenter(x, y) + Vector3.up * 0.1f;
            GameObject vfx = Instantiate(promotionVfxPrefab, promotionVfxPos, Quaternion.identity);
            Destroy(vfx, promotionVfxLifetime);
        }
    }

    // 두 좌표의 기물을 서로 맞바꾼다 (배열 데이터와 월드 좌표 모두 갱신)
    public void SwapPieces(Vector2Int posA, Vector2Int posB)
    {
        ChessPieces pieceA = GetPieceAt(posA.x, posA.y);
        ChessPieces pieceB = GetPieceAt(posB.x, posB.y);

        if (pieceA == null || pieceB == null) return;

        chessPieces[posA.x, posA.y] = pieceB;
        chessPieces[posB.x, posB.y] = pieceA;

        pieceA.currentX = posB.x; pieceA.currentY = posB.y;
        pieceB.currentX = posA.x; pieceB.currentY = posA.y;

        pieceA.transform.position = GetTileCenter(posB.x, posB.y);
        pieceB.transform.position = GetTileCenter(posA.x, posA.y);
    }

    // 좌표별 타일/기물 배열 접근용 getter, setter
    public GameObject GetTileObject(int x, int y) => tiles[x, y];
    public ChessPieces GetPieceAt(int x, int y) => chessPieces[x, y];
    public void SetPieceAt(int x, int y, ChessPieces piece) => chessPieces[x, y] = piece;

    // 새 매치를 시작할 때 보드를 초기 상태로 되돌린다.
    // 이전 매치에서 이동/캡처/프로모션된 기물이 그대로 남아있는 문제를 해결하기 위해,
    // 현재 보드 위의 모든 기물을 파괴하고 SpawnAllPieces/PositionAllPieces를 다시 실행해
    // 클래식 체스 초기 배치로 되돌린다.
    public void ResetBoard()
    {
        if (chessPieces != null)
        {
            foreach (var piece in chessPieces)
            {
                if (piece != null)
                    Destroy(piece.gameObject);
            }
        }

        SpawnAllPieces();
        PositionAllPieces();
    }
    #endregion
}
