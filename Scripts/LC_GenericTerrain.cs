using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public abstract class LC_GenericTerrain<Cell, Chunk> : MonoBehaviour where Chunk : LC_Chunk where Cell : LC_Cell
{
	#region Attributes

	#region Constants

	public const int MaxVerticesByMesh = 65536;

	#endregion

	#region Settings	

	[Header( "Global settings" )]
	[SerializeField] protected Transform Player;
	[SerializeField] protected Vector3 CellSize = Vector3.one;
	[SerializeField] [Range( 1, 8 )] protected int ChunkSizeLevel = 4;
	[SerializeField] [Range( 0, 64 )] protected int ChunkRenderDistance = 4;
	[SerializeField] protected bool DynamicChunkLoading = true;
	[SerializeField] protected bool ParallelChunkLoading = true;
	[SerializeField] protected bool ParallelChunkCellsLoading = true;
	[SerializeField] protected Material RenderMaterial;
	[SerializeField] protected bool HasCollider = true;

	#endregion

	#region Function attributes

	protected int ChunkSize;
	protected Vector3 CurrentRealPos;    // Equivalent to transform.position. Needed for parallel chunk mesh loading.
	protected Vector2Int PlayerChunkPos;
	protected float ChunkRenderRealDistance;
	protected List<Vector2Int> ChunksLoading;
	protected Dictionary<Vector2Int, Chunk> ChunksBuilt;
	protected Dictionary<Vector2Int, Chunk> CurrentChunks;
	protected int MaxVerticesPerRenderElem = 12;

	protected object ChunksLoadingLock = new object();
	protected object ChunksBuiltLock = new object();

	#endregion

	#endregion

	#region Initialization

	protected virtual void Start()
	{
		ChunkSize = (int)Mathf.Pow( 2, ChunkSizeLevel );
		CurrentRealPos = transform.position;

		ChunkRenderRealDistance = ChunkRenderDistance * ChunkSize * Mathf.Max( CellSize.x, CellSize.z );
		ChunksLoading = new List<Vector2Int>();
		ChunksBuilt = new Dictionary<Vector2Int, Chunk>();
		CurrentChunks = new Dictionary<Vector2Int, Chunk>();

		PlayerChunkPos = RealPosToChunk( Player.position );

		IniTerrain();
	}

	protected virtual void IniTerrain()
	{
		CreateChunk( PlayerChunkPos );

		foreach ( Vector2Int pos in LC_Math.AroundPositions( Vector2Int.zero, (uint)ChunkRenderDistance ) )
		{
			CreateChunk( PlayerChunkPos + pos );
		}
	}

	protected virtual void CreateChunk( Vector2Int chunkPos )
	{
		ChunksLoading.Add( chunkPos );

		Chunk chunk = CreateChunkInstance( chunkPos );
		chunk.Obj.transform.parent = this.transform;
		chunk.Obj.name = "Chunk_" + chunkPos;
		//chunk.Obj.transform.position = TerrainPosToReal( chunk.CellsOffset, 0 ); // TODO : Check position offset

		Cell[,] cells = null;
		if ( !ParallelChunkCellsLoading || !ParallelChunkLoading )
			cells = CreateCells( chunk );

		if ( ParallelChunkLoading )
		{
			Task task = Task.Run( () =>
			{
				if ( ParallelChunkCellsLoading )
					cells = CreateCells( chunk );

				CreateMesh( chunk, cells );
				chunk.BuildMesh();

				ChunkBuilt( chunkPos, chunk );
			} );
		}
		else
		{
			CreateMesh( chunk, cells );
			chunk.BuildMesh();

			ChunkBuilt( chunkPos, chunk );
		}
	}

	protected abstract Chunk CreateChunkInstance( Vector2Int chunkPos );

	protected virtual Cell[,] CreateCells( Chunk chunk )
	{
		Cell[,] cells = new Cell[ChunkSize + 1, ChunkSize + 1]; // +1 for edges

		for ( int x = 0; x < cells.GetLength( 0 ); x++ )
			for ( int z = 0; z < cells.GetLength( 1 ); z++ )
				cells[x, z] = CreateCell( x, z, chunk );

		return cells;
	}

	public abstract Cell CreateCell( int chunkX, int chunkZ, Chunk chunk );

	protected virtual void CreateMesh( Chunk chunk, Cell[,] cells )
	{
		CellsToMesh( chunk, cells );
	}

	protected virtual void ChunkBuilt( Vector2Int chunkPos, Chunk chunk )
	{
		if ( ParallelChunkLoading )
		{
			lock ( ChunksLoadingLock )
			{
				lock ( ChunksBuiltLock )
				{
					ChunksLoading.Remove( chunkPos );
					ChunksBuilt.Add( chunkPos, chunk );
				}
			}
		}
		else
		{
			CreateChunkMeshObj( chunk );

			ChunksLoading.Remove( chunkPos );
			CurrentChunks.Add( chunkPos, chunk );
		}
	}

	#endregion

	#region Mesh

	protected virtual void CellsToMesh( Chunk chunk, Cell[,] cells )
	{
		for ( int x = 0; x < cells.GetLength( 0 ); x++ )
		{
			for ( int z = 0; z < cells.GetLength( 1 ); z++ )
			{
				CreateCellMesh( x, z, chunk, cells );
			}
		}
	}

	protected abstract void CreateCellMesh( int chunkX, int chunkZ, Chunk chunk, Cell[,] cells );

	protected virtual void CreateChunkMeshObj( Chunk chunk )
	{
		Mesh mesh = new Mesh
		{
			vertices = chunk.VerticesArray,
			triangles = chunk.TrianglesArray,
			uv = chunk.UVsArray
		};
		mesh.RecalculateBounds();

		if ( chunk.Normals != null )
			mesh.normals = chunk.Normals;
		else
			mesh.RecalculateNormals();

		mesh.Optimize();

		MeshFilter renderMeshFilter = chunk.Obj.AddComponent<MeshFilter>();
		renderMeshFilter.mesh = mesh;

		chunk.Obj.AddComponent<MeshRenderer>().material = RenderMaterial;

		if ( HasCollider )
		{
			MeshCollider renderMeshCollider = chunk.Obj.AddComponent<MeshCollider>();
			renderMeshCollider.sharedMesh = mesh;
		}
	}

	#endregion

	#region Dynamic chunk loading

	protected virtual void Update()
	{
		CurrentRealPos = transform.position; // TODO : Check if is needed

		if ( ParallelChunkLoading )
			LoadChunks();

		if ( DynamicChunkLoading )
			UpdateChunks();
	}

	protected virtual void LoadChunks()
	{
		if ( ChunksBuilt.Count > 0 )
		{
			bool canAccess = Monitor.TryEnter( ChunksBuiltLock, 0 );
			if ( canAccess )
			{
				foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksBuilt )
				{
					CreateChunkMeshObj( entry.Value );
					CurrentChunks.Add( entry.Key, entry.Value );
				}
				ChunksBuilt.Clear();

				Monitor.Exit( ChunksBuiltLock );
			}
		}
	}

	protected virtual void UpdateChunks()
	{
		Vector2Int newPlayerChunkPos = RealPosToChunk( Player.position );
		Vector2Int offset = newPlayerChunkPos - PlayerChunkPos;

		// If chunk pos changed
		if ( offset.magnitude > 0 )
		{
			PlayerChunkPos = newPlayerChunkPos;

			List<Vector2Int> chunksToLoad = LC_Math.AroundPositions( newPlayerChunkPos, (uint)ChunkRenderDistance );
			chunksToLoad.Add( newPlayerChunkPos );
			List<Vector2Int> chunksToUnload = new List<Vector2Int>();

			// Check chunks already loaded
			Chunk chunk;
			Vector2Int chunkPos;
			int index;
			foreach ( KeyValuePair<Vector2Int, Chunk> entry in CurrentChunks )
			{
				chunkPos = entry.Key;
				chunk = entry.Value;
				index = chunksToLoad.IndexOf( chunkPos );

				// If already loaded, don't reload
				if ( index >= 0 )
				{
					chunksToLoad.RemoveAt( index );
				}
				// If don't needed, unload
				else
				{
					chunksToUnload.Add( chunkPos );
					Destroy( chunk.Obj );
				}
			}

			// Ignore chunks that are already loading
			lock ( ChunksLoadingLock )
			{
				foreach ( Vector2Int pos in ChunksLoading )
				{
					index = chunksToLoad.IndexOf( pos );
					if ( index >= 0 )
					{
						chunksToLoad.RemoveAt( index );
					}
				}
			}

			// Load new chunks
			foreach ( Vector2Int pos in chunksToLoad )
			{
				CreateChunk( pos );
			}

			// Remove useless chunks
			foreach ( Vector2Int pos in chunksToUnload )
			{
				CurrentChunks.Remove( pos );
			}
		}
	}

	#endregion

	#region Auxiliar

	public virtual Vector3 TerrainPosToReal( int x, float height, int z )
	{
		return CurrentRealPos + new Vector3( x * CellSize.x, height * CellSize.y, z * CellSize.z );
	}

	public virtual Vector3 TerrainPosToReal( Vector2Int pos, float height )
	{
		return TerrainPosToReal( pos.x, height, pos.y );
	}

	public virtual Vector3 TerrainPosToReal( Cell cell )
	{
		return TerrainPosToReal( cell.TerrainPos, cell.Height );
	}

	public virtual Vector3Int RealPosToTerrain( Vector3 pos )
	{
		Vector3 offsetPos = pos - CurrentRealPos;
		return new Vector3Int( (int)( offsetPos.x / CellSize.x ), (int)( offsetPos.y / CellSize.y ), (int)( offsetPos.z / CellSize.z ) );
	}

	public virtual Vector2Int RealPosToChunk( Vector3 pos )
	{
		Vector3Int terrainPos = RealPosToTerrain( pos );

		Vector2Int res = new Vector2Int( terrainPos.x / ChunkSize, terrainPos.z / ChunkSize );

		if ( terrainPos.x < 0 )
			res.x -= 1;
		if ( terrainPos.z < 0 )
			res.y -= 1;

		return res;
	}

	#endregion
}