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
	protected Vector3 HalfChunk;
	protected List<Vector2Int> ChunksLoading;
	protected Dictionary<Vector2Int, Chunk> ChunksBuilt;
	protected Dictionary<Vector2Int, Chunk> CurrentChunks;
	protected int MaxVerticesPerRenderElem = 12;
	protected bool IsWorkFrame = true;

	protected object ChunksLoadingLock = new object();

	#endregion

	#endregion

	#region Initialization

	protected virtual void Start()
	{
		ChunkSize = (int)Mathf.Pow( 2, ChunkSizeLevel );
		CurrentRealPos = transform.position;

		HalfChunk = new Vector3( CellSize.x, 0, CellSize.z ) * ( ChunkSize / 2 );
		ChunksLoading = new List<Vector2Int>();
		ChunksBuilt = new Dictionary<Vector2Int, Chunk>();
		CurrentChunks = new Dictionary<Vector2Int, Chunk>();

		PlayerChunkPos = RealPosToChunk( Player.position );

		DestroyTerrain( true );
		IniTerrain();
	}

	public virtual void DestroyTerrain( bool immediate )
	{
		Transform[] allChildren = GetComponentsInChildren<Transform>();

		if ( allChildren.Length > 0 )
			foreach ( Transform child in allChildren )
			{
				if ( child.gameObject != gameObject )
				{
					if ( immediate )
						DestroyImmediate( child.gameObject );
					else
						Destroy( child.gameObject );
				}

			}

		if ( CurrentChunks != null ) CurrentChunks.Clear();
		// TODO : Check loading and built chunks
	}

	protected virtual void IniTerrain()
	{
		CreateChunk( PlayerChunkPos );

		foreach ( Vector2Int pos in LC_Math.AroundPositions( Vector2Int.zero, ChunkRenderDistance ) )
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
				ChunksLoading.Remove( chunkPos );
				ChunksBuilt.Add( chunkPos, chunk );
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
		// Update CurrentRealPos (needed for parallel functions and others)
		CurrentRealPos = transform.position;

		// Do work 1 of each 2 frames
		if ( IsWorkFrame && ( ParallelChunkLoading || DynamicChunkLoading ) )
		{
			bool someChunkLoaded = false;

			if ( ParallelChunkLoading )
				someChunkLoaded = LoadChunks();

			if ( !someChunkLoaded && DynamicChunkLoading )
				UpdateChunks();
		}
		IsWorkFrame = !IsWorkFrame;
	}

	protected virtual bool LoadChunks()
	{
		bool someChunkLoaded = false;

		if ( ChunksBuilt.Count > 0 )
		{
			bool canAccess = Monitor.TryEnter( ChunksLoadingLock, 0 );
			if ( canAccess )
			{
				foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksBuilt )
				{
					CreateChunkMeshObj( entry.Value );
					CurrentChunks.Add( entry.Key, entry.Value );
				}
				ChunksBuilt.Clear();
				Monitor.Exit( ChunksLoadingLock );

				someChunkLoaded = true;
			}
		}

		return someChunkLoaded;
	}

	protected virtual void UpdateChunks()
	{
		Vector2Int currentPlayerChunkPos = RealPosToChunk( Player.position );
		Dictionary<Vector2Int, object> chunksToLoad = ComputeChunksToLoad( currentPlayerChunkPos );

		// Check chunks already loaded
		List<Vector2Int> chunksToUnload = new List<Vector2Int>();
		foreach ( KeyValuePair<Vector2Int, Chunk> entry in CurrentChunks )
		{
			// If already loaded, don't reload
			if ( chunksToLoad.ContainsKey( entry.Key ) )
			{
				chunksToLoad.Remove( entry.Key );
			}
			// If don't needed, unload
			else
			{
				chunksToUnload.Add( entry.Key );
				Destroy( entry.Value.Obj );
			}
		}

		lock ( ChunksLoadingLock )
		{
			// Ignore chunks that are loading
			foreach ( Vector2Int chunkPos in ChunksLoading )
				if ( chunksToLoad.ContainsKey( chunkPos ) )
					chunksToLoad.Remove( chunkPos );

			// Ignore chunks that are already built
			foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksBuilt )
				if ( chunksToLoad.ContainsKey( entry.Key ) )
					chunksToLoad.Remove( entry.Key );

			// Load new chunks
			foreach ( KeyValuePair<Vector2Int, object> entry in chunksToLoad )
				CreateChunk( entry.Key );
		}

		// Remove useless chunks
		foreach ( Vector2Int chunkPos in chunksToUnload )
			CurrentChunks.Remove( chunkPos );
	}

	protected virtual Dictionary<Vector2Int, object> ComputeChunksToLoad( Vector2Int currentPlayerChunkPos )
	{
		Dictionary<Vector2Int, object> chunksToLoad = new Dictionary<Vector2Int, object>();

		int squareRadius = ChunkRenderDistance + 1;
		Vector2Int topLeftCorner = currentPlayerChunkPos + Vector2Int.one * -1 * squareRadius;
		float ChunkRenderRealDistance = ChunkRenderDistance * ChunkSize * Mathf.Max( CellSize.x, CellSize.z );
		Vector3 chunkRealPosition;
		Vector3 offsetToPlayer;

		Vector2Int pos;
		for ( int x = 0; x <= squareRadius * 2; x++ )
		{
			for ( int y = 0; y <= squareRadius * 2; y++ )
			{
				pos = topLeftCorner + new Vector2Int( x, y );

				chunkRealPosition = ChunkPosToReal( pos );
				offsetToPlayer = chunkRealPosition - Player.position;
				offsetToPlayer.y = 0; // Ignore height offset
				if ( offsetToPlayer.magnitude <= ChunkRenderRealDistance )
					chunksToLoad.Add( pos, null );
			}
		}

		return chunksToLoad;
	}

	#endregion

	#region Auxiliar

	public virtual Vector3 TerrainPosToReal( int x, float height, int z )
	{
		return CurrentRealPos + new Vector3( x * CellSize.x, height * CellSize.y, z * CellSize.z ) - HalfChunk;
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
		Vector3 relativePos = pos - CurrentRealPos + HalfChunk;
		return new Vector3Int( (int)( relativePos.x / CellSize.x ), (int)( relativePos.y / CellSize.y ), (int)( relativePos.z / CellSize.z ) );
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

	public virtual Vector3 ChunkPosToReal( Vector2Int chunkPosition )
	{
		return CurrentRealPos + new Vector3( chunkPosition.x * ChunkSize * CellSize.x, 0, chunkPosition.y * ChunkSize * CellSize.z ) - HalfChunk;
	}

	#endregion
}