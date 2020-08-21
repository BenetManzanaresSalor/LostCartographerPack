using UnityEngine;

public abstract class LC_GenericMap<Terrain, Chunk, Cell> : MonoBehaviour where Terrain : LC_GenericTerrain<Chunk, Cell> where Chunk : LC_Chunk<Cell> where Cell : LC_Cell
{
	#region Attributes

	#region Settings

	[SerializeField] protected Terrain TerrainToMap;
	[SerializeField] protected Vector2Int CellsWidthAndHeight = new Vector2Int( 20, 20 );
	[SerializeField] protected Vector2Int TextureWidthAndHeight = new Vector2Int( 200, 200 );
	[SerializeField] protected bool UseMipMaps = false;

	#endregion

	#region Function attributes

	protected Texture2D MapTexture;
	protected Color[] TextureColors;

	#endregion

	#endregion

	#region Initialization

	protected virtual void Start()
	{
		MapTexture = new Texture2D( TextureWidthAndHeight.x, TextureWidthAndHeight.y );
		TextureColors = new Color[MapTexture.width * MapTexture.height];

		BindTexture();
	}

	/// <summary>
	/// Bind the texture with the renderer (for example, a RawImage)
	/// </summary>
	protected abstract void BindTexture();

	#endregion

	#region Texture computation

	protected virtual void Update()
	{
		ComputePixels();
	}

	protected virtual void ComputePixels()
	{
		Vector3Int playerTerrainPos = TerrainToMap.GetPlayerTerrainPos();
		Vector2Int bottomLeftCorner = new Vector2Int( playerTerrainPos.x - ( CellsWidthAndHeight.x / 2 ), playerTerrainPos.z - ( CellsWidthAndHeight.y / 2 ) );
		Vector2Int pixelsPerCell = TextureWidthAndHeight.Div( CellsWidthAndHeight );

		Vector2Int cellPos = new Vector2Int();
		Cell cell;
		Color color;
		int row, column;
		for ( int x = 0; x < CellsWidthAndHeight.x; x++ )
		{
			for ( int y = 0; y < CellsWidthAndHeight.y; y++ )
			{
				cellPos.x = bottomLeftCorner.x + x;
				cellPos.y = bottomLeftCorner.y + y;
				cell = TerrainToMap.GetCell( cellPos );

				color = GetColorPerCell( cell );
				for ( int i = 0; i < pixelsPerCell.y; i++ )
				{
					row = pixelsPerCell.y * y + i;
					for ( int j = 0; j < pixelsPerCell.x; j++ )
					{
						column = pixelsPerCell.x * x + j;
						TextureColors[row * MapTexture.height + column] = color;
					}
				}
			}
		}

		MapTexture.SetPixels( TextureColors );
		MapTexture.Apply( UseMipMaps );
	}

	protected abstract Color GetColorPerCell( Cell cell );

	#endregion
}
