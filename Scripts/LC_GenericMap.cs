using UnityEngine;

public abstract class LC_GenericMap<Terrain, Chunk, Cell> : MonoBehaviour where Terrain : LC_GenericTerrain<Chunk, Cell> where Chunk : LC_Chunk<Cell> where Cell : LC_Cell
{
	#region Attributes

	#region Settings

	[Header( "General settings" )]
	[SerializeField] protected Terrain TerrainToMap;
	[SerializeField] protected Vector2Int CellsWidthAndHeight = new Vector2Int( 20, 20 );
	[SerializeField] protected Vector2Int ResolutionDivider = new Vector2Int( 1, 1 );
	[SerializeField] protected Vector2Int TextureWidthAndHeight = new Vector2Int( 200, 200 );
	[SerializeField] protected int FramesBtwUpdates = 1;
	[SerializeField] protected float MaxUpdateTime = 1f / ( 60f * 2f );
	[SerializeField] protected bool MapNonLoadedChunks;
	[SerializeField] protected bool UseMipMaps = false;

	#endregion

	#region Function attributes

	protected Vector2Int ReferencePos;
	protected Texture2D MapTexture;
	protected Color[] TextureColors;
	protected Vector2Int HalfMapOffset;
	protected float UpdateIniTime;

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
		if ( Time.frameCount % FramesBtwUpdates == 0 )
		{
			UpdateIniTime = Time.realtimeSinceStartup;
			HalfMapOffset = new Vector2Int( CellsWidthAndHeight.x / 2, CellsWidthAndHeight.y / 2 ); // Update HalfMapOffset for map generation
			ReferencePos = GetReferencePos();

			if ( MapNonLoadedChunks && InMaxUpdateTime() )
				UpdateTerrainToMapChunks();

			if ( InMaxUpdateTime() )
				ComputePixels();
		}
	}

	protected abstract Vector2Int GetReferencePos();

	protected virtual bool InMaxUpdateTime()
	{
		return ( Time.realtimeSinceStartup - UpdateIniTime ) <= MaxUpdateTime;
	}

	protected virtual void ComputePixels()
	{
		Vector2Int bottomLeftCorner = ReferencePos - HalfMapOffset;
		Vector2Int cellsToGet = CellsWidthAndHeight.Div( ResolutionDivider );
		Vector2Int pixelsPerCell = TextureWidthAndHeight.Div( cellsToGet );

		Vector2Int cellPos = new Vector2Int();
		Cell cell;
		Color color;
		int row, column;
		for ( int x = 0; x < cellsToGet.x; x++ )
		{
			for ( int y = 0; y < cellsToGet.y; y++ )
			{
				cellPos.x = bottomLeftCorner.x + x * ResolutionDivider.x;
				cellPos.y = bottomLeftCorner.y + y * ResolutionDivider.y;
				cell = TerrainToMap.GetCell( cellPos, MapNonLoadedChunks );

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

	protected virtual void UpdateTerrainToMapChunks()
	{
		Vector2Int bottomLeftCorner = ReferencePos - HalfMapOffset;
		Vector2Int topRightCorner = ReferencePos + HalfMapOffset;
		TerrainToMap.UpdateChunksForMap( bottomLeftCorner, topRightCorner, InMaxUpdateTime );
	}

	#endregion
}
