using UnityEngine;

public abstract class LC_GenericMap<Terrain, Chunk, Cell> : MonoBehaviour where Terrain : LC_GenericTerrain<Chunk, Cell> where Chunk : LC_Chunk<Cell> where Cell : LC_Cell
{
	#region Attributes

	#region Settings

	[Header( "General settings" )]
	[SerializeField] protected Terrain TerrainToMap;
	[SerializeField] protected RenderTexture TargetTexture;
	[SerializeField] protected Vector2Int NumCellsWidthAndHeight = new Vector2Int( 20, 20 );
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
	protected Color32[] TextureColors;
	protected float UpdateIniTime;
	protected Vector2Int HalfMapOffset;
	protected Vector2Int CurrentCellPosInTex;

	#endregion

	#endregion

	#region Initialization

	protected virtual void Start()
	{
		MapTexture = new Texture2D( TextureWidthAndHeight.x, TextureWidthAndHeight.y );
		TextureColors = new Color32[MapTexture.width * MapTexture.height];
		CurrentCellPosInTex = Vector2Int.zero;
	}

	#endregion

	#region Texture computation

	protected virtual void Update()
	{
		if ( Time.frameCount % FramesBtwUpdates == 0 )
		{
			UpdateIniTime = Time.realtimeSinceStartup;
			HalfMapOffset = new Vector2Int( NumCellsWidthAndHeight.x / 2, NumCellsWidthAndHeight.y / 2 ); // Update HalfMapOffset for map generation
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
		Vector2Int cellsToGet = NumCellsWidthAndHeight.Div( ResolutionDivider );
		Vector2Int pixelsPerCell = TextureWidthAndHeight.Div( cellsToGet );

		// Compute pixels
		Vector2Int cellPosInTexture = new Vector2Int();
		Vector2Int cellPosInTerrain = new Vector2Int();
		Cell cell;
		Color color;
		int row, column;
		for ( int x = 0; x < cellsToGet.x && InMaxUpdateTime(); x++ )
		{
			for ( int y = 0; y < cellsToGet.y && InMaxUpdateTime(); y++ )
			{
				cellPosInTexture.x = ( CurrentCellPosInTex.x + x ) % cellsToGet.x;
				cellPosInTexture.y = ( CurrentCellPosInTex.y + y ) % cellsToGet.y;
				cellPosInTerrain = bottomLeftCorner + cellPosInTexture * ResolutionDivider;

				cell = TerrainToMap.GetCell( cellPosInTerrain, MapNonLoadedChunks );
				color = GetColorPerCell( cell );
				for ( int i = 0; i < pixelsPerCell.y; i++ )
				{
					row = cellPosInTexture.y * pixelsPerCell.y + i;
					for ( int j = 0; j < pixelsPerCell.x; j++ )
					{
						column = cellPosInTexture.x * pixelsPerCell.x + j;
						TextureColors[row * MapTexture.height + column] = color;
					}
				}
			}
		}

		// Update CurrentCellPosInTex
		CurrentCellPosInTex = cellPosInTexture;

		// Set texture
		MapTexture.SetPixels32( TextureColors );
		MapTexture.Apply();
		Graphics.Blit( MapTexture, TargetTexture );
	}

	protected abstract Color32 GetColorPerCell( Cell cell );

	protected virtual void UpdateTerrainToMapChunks()
	{
		Vector2Int bottomLeftCorner = ReferencePos - HalfMapOffset;
		Vector2Int topRightCorner = ReferencePos + HalfMapOffset;
		TerrainToMap.UpdateChunksForMap( bottomLeftCorner, topRightCorner, InMaxUpdateTime );
	}

	#endregion
}
