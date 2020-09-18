using UnityEditor;
using UnityEngine;

/// <summary>
/// Render types for the LC_Terrain.
/// </summary>
public enum LC_Terrain_RenderType : int
{
	DEFAULT_UVs = 0,
	HEIGHT_DISCRETE = 1,
	HEIGHT_CONTINUOUS = 2
};

/// <summary>
/// Default procedural terrain of Lost Cartographer Pack.
/// </summary>
public class LC_Terrain : LC_GenericTerrain<LC_Chunk<LC_Cell>, LC_Cell>
{
	#region Attributes

	#region Settings

	[SerializeField]
	[Tooltip( "If true, regenerates the terrain when any setting is changed." )]
	public bool AutoUpdate = false;

	[Header( "Random generation settings" )]
	[SerializeField]
	[Tooltip( "Divisor used at heights computation to smooth the terrain." )]
	protected float HeightsDivisor = 25f;
	[SerializeField]
	[Tooltip( "Maxium height for any cell.\nThe minimum height is always 0." )]
	public float MaxHeight = 10f;
	[SerializeField]
	[Tooltip( "If use a random seed for each terrain generation." )]
	protected bool UseRandomSeed = true;
	[SerializeField]
	[Tooltip( "Current seed of the terrain." )]
	protected int Seed;
	[SerializeField]
	[Tooltip( "Number of octaves used to random heights computation." )]// TODO
	[Range( 0, 64 )]
	protected int Octaves = 5;
	[SerializeField]
	[Tooltip( "Persistance used to random heights computation." )]// TODO
	[Range( 0, 1 )]
	protected float Persistance = 0.5f;
	[SerializeField]
	[Tooltip( "Lacunarity used to random heights computation." )]// TODO
	protected float Lacunarity = 2f;

	[Header( "Additional render settings" )]
	[SerializeField]
	[Tooltip( "Render type used at terrain mesh." )]
	protected LC_Terrain_RenderType RenderType;
	[SerializeField]
	[Tooltip( "Only if RenderType is DEFAULT_UVs.\nNumber of columns and rows of the material texture atlas." )]
	protected Vector2Int TextureColumnsAndRows = Vector2Int.one;
	[SerializeField]
	[Tooltip( "Only if RenderType is DEFAULT_UVs.\nMargin fraction of every subtexture of the texture atlas to delete in order to avoid strange edges." )]
	[Min( 1 )]
	protected float TextureMarginRelation = 8;
	[SerializeField]
	[Tooltip( "Only if RenderType is HEIGHT_DISCRETE or HEIGHT_CONTINUOUS.\nGradient of colors used at LC_Shader." )]
	protected Color[] HeightShaderColors;

	#endregion

	#region Function attributes

	protected System.Random RandomGenerator;

	protected int NumTextures;
	protected Vector2 TextureReservedSize;
	protected Vector2 TextureSize;
	protected Vector2 TextureMargin;

	#endregion

	#endregion

	#region Initialization

	protected override void Start()
	{
		NumTextures = TextureColumnsAndRows.x * TextureColumnsAndRows.y;
		TextureReservedSize = new Vector2( 1f / TextureColumnsAndRows.x, 1f / TextureColumnsAndRows.y );
		TextureMargin = TextureReservedSize / TextureMarginRelation;
		TextureSize = TextureReservedSize - 2 * TextureMargin;

		RandomGenerator = new System.Random();
		if ( UseRandomSeed )
			Seed = RandomGenerator.Next();

		SetRenderMaterial();

		base.Start();
	}

	/// <summary>
	/// If the material uses LC_Shader, comunicates the information required.
	/// </summary>
	protected void SetRenderMaterial()
	{
		// If RenderMaterial uses LC_Shader
		RenderMaterial.SetInt( "LC_RenderType", (int)RenderType );
		if ( RenderType != LC_Terrain_RenderType.DEFAULT_UVs )
		{
			RenderMaterial.SetFloat( "minHeight", transform.position.y );
			RenderMaterial.SetFloat( "maxHeight", transform.position.y + MaxHeight );
			RenderMaterial.SetInt( "numColors", HeightShaderColors.Length );
			RenderMaterial.SetColorArray( "colors", HeightShaderColors );
		}
	}

	#endregion

	#region Chunk creation

	protected override LC_Chunk<LC_Cell> CreateChunkInstance( Vector2Int chunkPos )
	{
		return new LC_Chunk<LC_Cell>( chunkPos, ChunkSize );
	}

	/// <summary>
	/// Create the cells of a chunk using a heights map.
	/// </summary>
	/// <param name="chunk"></param>
	protected override void CreateCells( LC_Chunk<LC_Cell> chunk )
	{
		chunk.HeightsMap = CreateChunkHeightsMap( chunk.Position );

		LC_Cell[,] cells = new LC_Cell[ChunkSize + 1, ChunkSize + 1]; // +1 for edges

		for ( int x = 0; x < cells.GetLength( 0 ); x++ )
			for ( int z = 0; z < cells.GetLength( 1 ); z++ )
				cells[x, z] = CreateCell( x, z, chunk );

		chunk.Cells = cells;
	}

	/// <summary>
	/// Compute the heights map using LC_Math.PerlinNoiseMap.
	/// </summary>
	/// <param name="chunkPos">Position of the chunk</param>
	/// <returns></returns>
	protected virtual float[,] CreateChunkHeightsMap( Vector2Int chunkPos )
	{
		return LC_Math.PerlinNoiseMap(
			new Vector2Int( ChunkSize + 3, ChunkSize + 3 ), // +1 for top-right edges and +2 for normals computation
			Seed,
			Octaves, Persistance, Lacunarity,
			new Vector2( 0, MaxHeight ),
			HeightsDivisor,
			( chunkPos.x - 1 ) * ChunkSize,   // -1 for normals computation (get neighbour chunk edge heights)
			( chunkPos.y - 1 ) * ChunkSize,   // -1 for normals computation (get neighbour chunk edge heights)
			true );
	}

	/// <summary>
	/// Create a cell of a chunk using the coordinates and the chunk.HeightsMap. 
	/// </summary>
	/// <param name="chunkX"></param>
	/// <param name="chunkZ"></param>
	/// <param name="chunk"></param>
	/// <returns></returns>
	public virtual LC_Cell CreateCell( int chunkX, int chunkZ, LC_Chunk<LC_Cell> chunk )
	{
		return new LC_Cell( new Vector2Int( chunk.CellsOffset.x + chunkX, chunk.CellsOffset.y + chunkZ ),
			chunk.HeightsMap[chunkX + 1, chunkZ + 1] ); // +1 to compensate the offset for normals computation
	}

	#region Mesh

	/// <summary>
	/// <para>Compute the mesh incrementally with the mesh data for each cell.</para>
	/// <para>Next, calculate the normals of each vertex to avoid seams between chunks.</para>
	/// </summary>
	/// <param name="chunk"></param>
	protected override void ComputeMesh( LC_Chunk<LC_Cell> chunk )
	{
		for ( int x = 0; x < chunk.Cells.GetLength( 0 ); x++ )
		{
			for ( int z = 0; z < chunk.Cells.GetLength( 1 ); z++ )
			{
				CreateCellMesh( x, z, chunk );
			}
		}

		ComputeNormals( chunk );
	}

	/// <summary>
	/// Create mesh data of a specific cell of a chunk (vertices, triangles and UVs) and add it to the chunk lists.
	/// </summary>
	/// <param name="chunkX"></param>
	/// <param name="chunkZ"></param>
	/// <param name="chunk"></param>
	protected virtual void CreateCellMesh( int chunkX, int chunkZ, LC_Chunk<LC_Cell> chunk )
	{
		LC_Cell cell = chunk.Cells[chunkX, chunkZ];
		Vector3 realPos = TerrainPosToReal( cell );

		// Vertices
		chunk.Vertices.Add( realPos );

		// Triangles (if isn't edge)
		if ( chunkX < ChunkSize && chunkZ < ChunkSize )
		{
			int vertexI = chunk.Vertices.Count - 1;

			chunk.Triangles.Add( vertexI );
			chunk.Triangles.Add( vertexI + 1 );
			chunk.Triangles.Add( vertexI + chunk.Cells.GetLength( 0 ) + 1 );

			chunk.Triangles.Add( vertexI );
			chunk.Triangles.Add( vertexI + chunk.Cells.GetLength( 0 ) + 1 );
			chunk.Triangles.Add( vertexI + chunk.Cells.GetLength( 0 ) );
		}

		// UVs
		GetUVs( new Vector2Int( chunkX, chunkZ ), out Vector2 iniUV, out Vector2 endUV, chunk );
		chunk.UVs.Add( iniUV );
	}

	/// <summary>
	/// Compute the UVs square of a specific cell.
	/// </summary>
	/// <param name="posAtChunk">Position of the cell at the chunk.</param>
	/// <param name="ini">Initial position of the UV square(bottom left)</param>
	/// <param name="end">Final position of the UV square(top right)</param>
	/// <param name="chunk"></param>
	protected virtual void GetUVs( Vector2Int posAtChunk, out Vector2 ini, out Vector2 end, LC_Chunk<LC_Cell> chunk )
	{
		Vector2Int texPos = GetTexPos( chunk.Cells[posAtChunk.x, posAtChunk.y], chunk );
		ini = TextureReservedSize * texPos + TextureMargin;
		end = ini + TextureSize;
	}

	/// <summary>
	/// Compute the texture position using the TextureColumnsAndRows and the cell height.
	/// </summary>
	/// <param name="cell"></param>
	/// <param name="chunk"></param>
	/// <returns></returns>
	protected virtual Vector2Int GetTexPos( LC_Cell cell, LC_Chunk<LC_Cell> chunk )
	{
		float value = Mathf.InverseLerp( 0, MaxHeight, cell.Height );
		int texInd = Mathf.RoundToInt( value * ( NumTextures - 1 ) );
		int x = texInd % TextureColumnsAndRows.x;
		int y = texInd / TextureColumnsAndRows.x;

		return new Vector2Int( x, y );
	}

	/// <summary>
	/// <para>Compute the normals for each vertex of the mesh, checking the edges cells of other chunks.</para>
	/// <para>This process is needed to avoid illumination differences between contiguous cells of different chunks (seams).</para>
	/// </summary>
	/// <param name="chunk"></param>
	protected virtual void ComputeNormals( LC_Chunk<LC_Cell> chunk )
	{
		chunk.Normals = new Vector3[( ChunkSize + 1 ) * ( ChunkSize + 1 )];
		int i, triangleIdx, x, z;
		Vector2Int vertexTerrainPos;
		Vector3 a, b, c, normal;

		// Normal for each vertex
		for ( i = 0; i < chunk.Vertices.Count; i++ )
		{
			LC_Math.IndexToCoords( i, ChunkSize + 1, out x, out z );

			// If isn't edge
			if ( x < ChunkSize && z < ChunkSize )
			{
				triangleIdx = LC_Math.CoordsToIndex( x, z, ChunkSize ) * 6;
				CalculateTrianglesNormals( triangleIdx, chunk );
				CalculateTrianglesNormals( triangleIdx + 3, chunk );
			}
			// If is edge
			if ( x == 0 || z == 0 || x >= ChunkSize || z >= ChunkSize )
			{
				vertexTerrainPos = chunk.ChunkPosToTerrain( new Vector2Int( x, z ) );
				a = chunk.Vertices[i];

				if ( x == 0 || z == 0 )
				{
					b = TerrainPosToReal( vertexTerrainPos.x - 1, chunk.HeightsMap[x, z + 1], vertexTerrainPos.y ); // x - 1, z
					c = TerrainPosToReal( vertexTerrainPos.x - 1, chunk.HeightsMap[x, z], vertexTerrainPos.y - 1 ); // x - 1, z - 1
					normal = -Vector3.Cross( b - a, c - a );
					chunk.Normals[i] += normal;
					if ( x != 0 )
						chunk.Normals[i - ( ChunkSize + 1 )] += normal;

					b = c;  // x - 1, z - 1
					c = TerrainPosToReal( vertexTerrainPos.x, chunk.HeightsMap[x + 1, z], vertexTerrainPos.y - 1 ); // x, z - 1
					normal = -Vector3.Cross( b - a, c - a );
					chunk.Normals[i] += normal;
					if ( z != 0 )
						chunk.Normals[i - 1] += normal;
				}
				if ( x == ChunkSize || z == ChunkSize )
				{
					b = TerrainPosToReal( vertexTerrainPos.x + 1, chunk.HeightsMap[x + 2, z + 1], vertexTerrainPos.y ); // x + 1, z
					c = TerrainPosToReal( vertexTerrainPos.x + 1, chunk.HeightsMap[x + 2, z + 2], vertexTerrainPos.y + 1 ); // x + 1, z + 1
					normal = -Vector3.Cross( b - a, c - a );
					chunk.Normals[i] += normal;
					if ( x < ChunkSize )
						chunk.Normals[i + ( ChunkSize + 1 )] += normal;

					b = c; // x + 1, z + 1
					c = TerrainPosToReal( vertexTerrainPos.x, chunk.HeightsMap[x + 1, z + 2], vertexTerrainPos.y + 1 ); // x, z + 1
					normal = -Vector3.Cross( b - a, c - a );
					chunk.Normals[i] += normal;
					if ( z < ChunkSize )
						chunk.Normals[i + 1] += normal;
				}
			}
		}

		for ( i = 0; i < chunk.Normals.Length; i++ )
			chunk.Normals[i].Normalize();
	}

	/// <summary>
	/// Compute the normal of a specific triangle of the chunk mesh and update the normals of the corresponding vertices.
	/// </summary>
	/// <param name="firstTriangleIdx"></param>
	/// <param name="chunk"></param>
	protected virtual void CalculateTrianglesNormals( int firstTriangleIdx, LC_Chunk<LC_Cell> chunk )
	{
		int idxA, idxB, idxC;
		Vector3 normal;

		idxA = chunk.Triangles[firstTriangleIdx];
		idxB = chunk.Triangles[firstTriangleIdx + 1];
		idxC = chunk.Triangles[firstTriangleIdx + 2];

		normal = Vector3.Cross( chunk.Vertices[idxB] - chunk.Vertices[idxA],
			chunk.Vertices[idxC] - chunk.Vertices[idxA] );
		chunk.Normals[idxA] += normal;
		chunk.Normals[idxB] += normal;
		chunk.Normals[idxC] += normal;
	}

	#endregion

	#endregion

	/// <summary>
	/// Auxiliar class used to allow AutoUpdate functionality and the Generate and Destroy buttons.
	/// </summary>
	[CustomEditor( typeof( LC_Terrain ) )]
	internal class LevelScriptEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			LC_Terrain myTarget = (LC_Terrain)target;

			bool hasChanged = DrawDefaultInspector();

			if ( ( myTarget.AutoUpdate && hasChanged ) || GUILayout.Button( "Generate" ) )
			{
				// Update RenderMaterial
				myTarget.SetRenderMaterial();

				// Disable parallel settings
				bool parallelChunk = myTarget.ParallelChunkLoading;
				myTarget.ParallelChunkLoading = false;

				// Generate
				myTarget.Start();

				// Restore parallel settings
				myTarget.ParallelChunkLoading = parallelChunk;
			}

			if ( GUILayout.Button( "Destroy" ) )
				myTarget.DestroyTerrain( true );
		}
	}
}