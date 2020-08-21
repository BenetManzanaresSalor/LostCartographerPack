using UnityEditor;
using UnityEngine;

public class LC_Terrain : LC_GenericTerrain<LC_Chunk<LC_Cell>, LC_Cell>
{
	#region Attributes

	#region Settings	

	[SerializeField] public bool AutoUpdate = false;    // If true, regenerates the terrain when any setting is changed

	[Header( "Random generation settings" )]
	[SerializeField] protected float HeightsMapDivisor = 25f;
	[SerializeField] public float MaxHeight = 10f;
	[SerializeField] protected bool RandomMapSeed = true;
	[SerializeField] protected int MapSeed;
	[SerializeField] [Range( 0, 64 )] protected int Octaves = 5;
	[SerializeField] [Range( 0, 1 )] protected float Persistance = 0.5f;
	[SerializeField] protected float Lacunarity = 2f;

	[Header( "Additional render settings" )]
	[SerializeField] protected Vector2Int TextureColumnsAndRows = Vector2Int.one;
	[SerializeField] [Range( 1, 4 )] protected float TextureMarginRelation = 3;

	#endregion

	#region Function attributes

	protected System.Random RandomGenerator;

	protected int NumTextures;
	protected Vector2 TextureSize;
	protected Vector2 TextureMargin;

	#endregion

	#endregion

	#region Initialization

	protected override void Start()
	{
		NumTextures = TextureColumnsAndRows.x * TextureColumnsAndRows.y;
		TextureSize = new Vector2( 1f / TextureColumnsAndRows.x, 1f / TextureColumnsAndRows.y );
		TextureMargin = TextureSize / TextureMarginRelation;

		RandomGenerator = new System.Random();
		if ( RandomMapSeed )
			MapSeed = RandomGenerator.Next();

		base.Start();
	}

	#endregion

	#region Chunk creation

	protected override LC_Chunk<LC_Cell> CreateChunkInstance( Vector2Int chunkPos )
	{
		return new LC_Chunk<LC_Cell>( chunkPos, ChunkSize );
	}

	protected override LC_Cell[,] CreateCells( LC_Chunk<LC_Cell> chunk )
	{
		chunk.HeightsMap = CreateChunkHeightsMap( chunk.Position );
		return base.CreateCells( chunk );
	}

	protected virtual float[,] CreateChunkHeightsMap( Vector2Int chunkPos )
	{
		return LC_Math.PerlinNoiseMap(
			new Vector2Int( ChunkSize + 3, ChunkSize + 3 ), // +1 for top-right edges and +2 for normals computation
			MapSeed,
			Octaves, Persistance, Lacunarity,
			new Vector2( 0, MaxHeight ),
			HeightsMapDivisor,
			( chunkPos.x - 1 ) * ChunkSize,   // -1 for normals computation (get neighbour chunk edge heights)
			( chunkPos.y - 1 ) * ChunkSize,   // -1 for normals computation (get neighbour chunk edge heights)
			true );
	}

	public override LC_Cell CreateCell( int chunkX, int chunkZ, LC_Chunk<LC_Cell> chunk )
	{
		return new LC_Cell( new Vector2Int( chunk.CellsOffset.x + chunkX, chunk.CellsOffset.y + chunkZ ),
			chunk.HeightsMap[chunkX + 1, chunkZ + 1] ); // +1 to compensate the offset for normals computation
	}

	#region Mesh

	protected override void CellsToMesh( LC_Chunk<LC_Cell> chunk )
	{
		base.CellsToMesh( chunk );
		CalculateNormals( chunk );
	}

	protected override void CreateCellMesh( int chunkX, int chunkZ, LC_Chunk<LC_Cell> chunk )
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

	protected virtual void GetUVs( Vector2Int chunkPos, out Vector2 ini, out Vector2 end, LC_Chunk<LC_Cell> chunk )
	{
		Vector2Int texPos = GetTexPos( chunk.Cells[chunkPos.x, chunkPos.y], chunk );

		end = new Vector2( ( texPos.x + 1f ) / TextureColumnsAndRows.x, ( texPos.y + 1f ) / TextureColumnsAndRows.y ) - TextureMargin;
		ini = end - TextureMargin;
	}

	protected virtual Vector2Int GetTexPos( LC_Cell cell, LC_Chunk<LC_Cell> chunk )
	{
		float value = Mathf.InverseLerp( 0, MaxHeight, cell.Height );
		int texInd = Mathf.RoundToInt( value * ( NumTextures - 1 ) );
		int x = texInd / TextureColumnsAndRows.y;
		int y = texInd % TextureColumnsAndRows.y;

		return new Vector2Int( x, y );
	}

	protected virtual void CalculateNormals( LC_Chunk<LC_Cell> chunk )
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

		// TODO : top-left and bottom-right

		for ( i = 0; i < chunk.Normals.Length; i++ )
			chunk.Normals[i].Normalize();
	}

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

	[CustomEditor( typeof( LC_Terrain ) )]
	internal class LevelScriptEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			LC_Terrain myTarget = (LC_Terrain)target;

			bool hasChanged = DrawDefaultInspector();

			if ( ( myTarget.AutoUpdate && hasChanged ) || GUILayout.Button( "Generate" ) )
			{
				// Disable parallel settings
				bool parallelChunk = myTarget.ParallelChunkLoading;
				bool parallelCells = myTarget.ParallelChunkCellsLoading;
				myTarget.ParallelChunkLoading = false;
				myTarget.ParallelChunkCellsLoading = false;

				// Generate
				myTarget.Start();

				// Restore parallel settings
				myTarget.ParallelChunkLoading = parallelChunk;
				myTarget.ParallelChunkCellsLoading = parallelCells;
			}

			if ( GUILayout.Button( "Destroy" ) )
				myTarget.DestroyTerrain( true );
		}
	}
}