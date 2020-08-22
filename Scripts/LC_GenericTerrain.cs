using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public abstract class LC_GenericTerrain<Chunk, Cell> : MonoBehaviour where Chunk : LC_Chunk<Cell> where Cell : LC_Cell
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
	[SerializeField] protected bool HasCollider = true;
	[SerializeField] protected bool DynamicChunkLoading = true;
	[SerializeField] protected bool ParallelChunkLoading = true;
	[SerializeField] protected Material RenderMaterial;
	[SerializeField] protected float MaxUpdateTime = 1f / ( 60f * 2f );

	#endregion

	#region Function attributes

	protected int ChunkSize;
	protected Vector3 CurrentRealPos;    // Equivalent to transform.position. Needed for parallel chunk mesh loading.
	protected Vector2Int PlayerChunkPos;
	protected Vector3 HalfChunk;
	protected float ChunkRenderRealDistance;
	protected Dictionary<Vector2Int, Chunk> ChunksLoading;
	protected Dictionary<Vector2Int, Chunk> ChunksLoaded;
	protected Dictionary<Vector2Int, Chunk> CurrentChunks;
	protected Dictionary<Vector2Int, Chunk> ChunksLoadingForMap;
	protected Dictionary<Vector2Int, Chunk> ChunksForMap;
	protected int MaxVerticesPerRenderElem = 12;
	protected float UpdateIniTime;

	protected object ChunksLoadingLock = new object();

	#endregion

	#endregion

	#region Initialization

	protected virtual void Start()
	{
		ChunkSize = (int)Mathf.Pow( 2, ChunkSizeLevel );
		CurrentRealPos = transform.position;

		HalfChunk = new Vector3( CellSize.x, 0, CellSize.z ) * ( ChunkSize / 2 );
		ChunksLoading = new Dictionary<Vector2Int, Chunk>();
		ChunksLoaded = new Dictionary<Vector2Int, Chunk>();
		CurrentChunks = new Dictionary<Vector2Int, Chunk>();

		ChunksLoadingForMap = new Dictionary<Vector2Int, Chunk>();
		ChunksForMap = new Dictionary<Vector2Int, Chunk>();

		PlayerChunkPos = RealPosToChunk( Player.position );

		DestroyTerrain( true );
		IniTerrain();
	}

	protected virtual void IniTerrain()
	{
		// Always load the current player chunk
		LoadChunk( PlayerChunkPos, true );

		// Load the other chunks
		foreach ( Vector2Int chunkPos in LC_Math.AroundPositions( PlayerChunkPos, ChunkRenderDistance ) )
			LoadChunk( chunkPos );
	}

	#endregion

	#region Chunk creation

	protected virtual void LoadChunk( Vector2Int chunkPos, bool ignoreParallel = false, bool isForMap = false )
	{
		Chunk chunk = CreateChunkInstance( chunkPos );
		bool inParallel = ParallelChunkLoading && !ignoreParallel;

		if ( isForMap )
			ChunksLoadingForMap.Add( chunkPos, chunk );
		else if ( inParallel )
			ChunksLoading.Add( chunkPos, chunk );

		if ( inParallel )
		{
			chunk.ParallelTask = Task.Run( () =>
			{
				LoadChunkMethod( chunk, ignoreParallel, isForMap );
			} );
		}
		else
		{
			LoadChunkMethod( chunk, ignoreParallel, isForMap );
		}
	}

	protected virtual void LoadChunkMethod( Chunk chunk, bool ignoreParallel, bool isForMap )
	{
		chunk.Cells = CreateCells( chunk );

		if ( !isForMap )
		{
			CreateMesh( chunk );
			chunk.BuildMesh();
		}

		ChunkLoaded( chunk, ignoreParallel, isForMap );
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

	protected virtual void ChunkLoaded( Chunk chunk, bool ignoreParallel, bool isForMap )
	{
		bool inParallel = ParallelChunkLoading && !ignoreParallel;

		if ( inParallel )
			Monitor.Enter( ChunksLoadingLock );

		if ( isForMap )
		{
			ChunksLoadingForMap.Remove( chunk.Position );
			ChunksForMap.Add( chunk.Position, chunk );
		}
		else if ( inParallel )
		{
			ChunksLoading.Remove( chunk.Position );
			ChunksLoaded.Add( chunk.Position, chunk );
		}

		if ( inParallel )
			Monitor.Exit( ChunksLoadingLock );
		else if ( !isForMap )
		{
			CreateChunkMeshObj( chunk );
			CurrentChunks.Add( chunk.Position, chunk );
		}
	}

	#region Mesh

	protected virtual void CreateMesh( Chunk chunk )
	{
		CellsToMesh( chunk );
	}

	protected virtual void CellsToMesh( Chunk chunk )
	{
		for ( int x = 0; x < chunk.Cells.GetLength( 0 ); x++ )
		{
			for ( int z = 0; z < chunk.Cells.GetLength( 1 ); z++ )
			{
				CreateCellMesh( x, z, chunk );
			}
		}
	}

	protected abstract void CreateCellMesh( int chunkX, int chunkZ, Chunk chunk );

	protected virtual void CreateChunkMeshObj( Chunk chunk )
	{
		chunk.Obj = new GameObject();
		chunk.Obj.transform.parent = this.transform;
		chunk.Obj.name = "Chunk_" + chunk.Position;
		//chunk.Obj.transform.position = ChunkPosToReal( chunk.Position ); // TODO

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

	#endregion

	#region Dynamic chunk loading

	protected virtual void Update()
	{
		UpdateIniTime = Time.realtimeSinceStartup;

		// Update useful variables
		CurrentRealPos = transform.position;    // Needed for parallel functions and others
		PlayerChunkPos = RealPosToChunk( Player.position ); // needed for DynamicChunkLoading
		ChunkRenderRealDistance = ChunkRenderDistance * ChunkSize * Mathf.Max( CellSize.x, CellSize.z );    // needed for DynamicChunkLoading

		// Load the current player chunk if isn't loaded
		CheckPlayerCurrentChunk();

		// Check chunk built parallelly (if remains time)
		if ( ParallelChunkLoading && InMaxUpdateTime() )
			CreateLoadedChunks();

		// Update chunks needed (if remains time)
		if ( DynamicChunkLoading && InMaxUpdateTime() )
			UpdateChunks();
	}

	protected virtual void CheckPlayerCurrentChunk()
	{
		Monitor.Enter( ChunksLoadingLock );
		if ( !CurrentChunks.ContainsKey( PlayerChunkPos ) && !ChunksLoaded.ContainsKey( PlayerChunkPos ) )
		{
			bool isLoading = ChunksLoading.TryGetValue( PlayerChunkPos, out Chunk playerChunk );
			Monitor.Exit( ChunksLoadingLock );
			if ( isLoading )
			{
				playerChunk.ParallelTask.Wait();  // Wait parallel loading to end					
				CreateLoadedChunks();    // Built the chunk (and the others if are needed)
			}
			else
			{
				LoadChunk( PlayerChunkPos, true );    // Ignore parallel because can continue playing without this chunk
			}
		}
		else
			Monitor.Exit( ChunksLoadingLock );
	}

	protected virtual bool InMaxUpdateTime()
	{
		return ( Time.realtimeSinceStartup - UpdateIniTime ) <= MaxUpdateTime;
	}

	protected virtual void CreateLoadedChunks()
	{
		if ( ChunksLoaded.Count > 0 )
		{
			lock ( ChunksLoadingLock )
			{
				foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksLoaded )
				{
					if ( InMaxUpdateTime() )
					{
						if ( IsChunkNeeded( entry.Key ) )
						{
							CreateChunkMeshObj( entry.Value );
							CurrentChunks.Add( entry.Key, entry.Value );
						}
						else
						{
							entry.Value.Destroy();
						}
					}
					else
						break;
				}
				ChunksLoaded.Clear();
			}
		}
	}

	protected virtual void UpdateChunks()
	{
		Dictionary<Vector2Int, object> chunksNeeded = ComputeChunksNeeded(); // Use a dictionary for faster searchs

		// Check chunks already created
		List<Vector2Int> chunksToDestroy = new List<Vector2Int>();
		foreach ( KeyValuePair<Vector2Int, Chunk> entry in CurrentChunks )
		{
			// If already created, don't reload
			if ( chunksNeeded.ContainsKey( entry.Key ) )
			{
				chunksNeeded.Remove( entry.Key );
			}
			// If don't needed, unload
			else
			{
				chunksToDestroy.Add( entry.Key );
				entry.Value.Destroy();
			}
		}

		// Remove chunks don't needed
		foreach ( Vector2Int chunkPos in chunksToDestroy )
			CurrentChunks.Remove( chunkPos );

		lock ( ChunksLoadingLock )
		{
			// Ignore chunks that are loading
			foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksLoading )
				if ( chunksNeeded.ContainsKey( entry.Key ) )
					chunksNeeded.Remove( entry.Key );

			// Ignore chunks that are already loaded
			foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksLoaded )
				if ( chunksNeeded.ContainsKey( entry.Key ) )
					chunksNeeded.Remove( entry.Key );

			// Load the other chunks
			foreach ( KeyValuePair<Vector2Int, object> entry in chunksNeeded )
			{
				if ( InMaxUpdateTime() )
					LoadChunk( entry.Key );
				else
					break;
			}
		}
	}

	protected virtual Dictionary<Vector2Int, object> ComputeChunksNeeded()
	{
		Dictionary<Vector2Int, object> chunksNeeded = new Dictionary<Vector2Int, object>();

		// Always load the player current chunk		
		chunksNeeded.Add( PlayerChunkPos, null );

		if ( ChunkRenderDistance > 0 )
		{
			int radius = ChunkRenderDistance + 1;

			Vector2Int topLeftCorner;
			Vector2Int chunkPos = new Vector2Int();
			int yIncrement = 1;
			for ( int currentRadius = 1; currentRadius < radius; currentRadius++ )
			{
				topLeftCorner = PlayerChunkPos + Vector2Int.one * -1 * currentRadius;

				for ( int x = 0; x <= currentRadius * 2; x++ )
				{
					yIncrement = ( x == 0 || x == currentRadius * 2 ) ? 1 : currentRadius * 2;
					for ( int y = 0; y <= currentRadius * 2; y += yIncrement )
					{
						chunkPos.x = topLeftCorner.x + x;
						chunkPos.y = topLeftCorner.y + y;

						// If isn't PlayerChunkPos (because is always loaded) and is needed
						if ( chunkPos != PlayerChunkPos && IsChunkNeeded( chunkPos ) )
							chunksNeeded.Add( chunkPos, null );
					}
				}
			}
		}

		return chunksNeeded;
	}

	#endregion

	#region Mapping

	public virtual void UpdateChunksForMap( Vector2Int bottomLeftPos, Vector2Int topRightPos, System.Func<bool> inMaxUpdateTime )
	{
		Vector2Int bottomLeftChunkPos = TerrainPosToChunk( bottomLeftPos );
		Vector2Int topRightChunkPos = TerrainPosToChunk( topRightPos );
		Vector2Int mapSize = topRightChunkPos - bottomLeftChunkPos;

		// Destroy the don't needed chunks
		List<Vector2Int> chunksToDestroy = new List<Vector2Int>();
		foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksForMap )
			if ( entry.Key.x < bottomLeftChunkPos.x || entry.Key.x > topRightChunkPos.x ||
				entry.Key.y < bottomLeftChunkPos.y || entry.Key.y > topRightChunkPos.y )
			{
				entry.Value.Destroy();
				chunksToDestroy.Add( entry.Key );
			}

		foreach ( Vector2Int pos in chunksToDestroy )
			ChunksForMap.Remove( pos );

		// Load the chunks needed for map
		Vector2Int chunkPos = new Vector2Int();
		for ( int x = 0; x <= mapSize.x; x++ )
		{
			for ( int y = 0; y <= mapSize.y; y++ )
			{
				chunkPos.x = bottomLeftChunkPos.x + x;
				chunkPos.y = bottomLeftChunkPos.y + y;

				// If no loaded neither loading
				if ( !CurrentChunks.ContainsKey( chunkPos ) &&
					!ChunksLoaded.ContainsKey( chunkPos ) &&
					!ChunksLoading.ContainsKey( chunkPos ) &&
					!ChunksForMap.ContainsKey( chunkPos ) &&
					!ChunksLoadingForMap.ContainsKey( chunkPos ) )
				{
					LoadChunk( chunkPos, ParallelChunkLoading, true );
				}
			}
		}
	}

	#endregion

	#region External use

	public virtual Vector3 TerrainPosToReal( int x, float height, int z )
	{
		return CurrentRealPos + new Vector3( x * CellSize.x, height * CellSize.y, z * CellSize.z ) - HalfChunk;
	}

	public virtual Vector3 TerrainPosToReal( Vector2Int terrainPos, float height )
	{
		return TerrainPosToReal( terrainPos.x, height, terrainPos.y );
	}

	public virtual Vector3 TerrainPosToReal( Cell cell )
	{
		return TerrainPosToReal( cell.TerrainPos, cell.Height );
	}

	public virtual Vector3 ChunkPosToReal( Vector2Int chunkPosition )
	{
		return CurrentRealPos + new Vector3( chunkPosition.x * ChunkSize * CellSize.x, 0, chunkPosition.y * ChunkSize * CellSize.z );
	}

	public virtual Vector3Int RealPosToTerrain( Vector3 realPos )
	{
		Vector3 relativePos = realPos - CurrentRealPos + HalfChunk;
		return new Vector3Int( (int)( relativePos.x / CellSize.x ), (int)( relativePos.y / CellSize.y ), (int)( relativePos.z / CellSize.z ) );
	}

	public virtual Vector3Int GetPlayerTerrainPos()
	{
		return RealPosToTerrain( Player.position );
	}

	public virtual Vector2Int TerrainPosToChunk( Vector2Int terrainPos )
	{
		Vector2Int res = new Vector2Int( terrainPos.x / ChunkSize, terrainPos.y / ChunkSize );

		if ( terrainPos.x < 0 )
			res.x -= 1;
		if ( terrainPos.y < 0 )
			res.y -= 1;

		return res;
	}

	public virtual Vector2Int TerrainPosToChunk( Vector3Int terrainPos )
	{
		return TerrainPosToChunk( new Vector2Int( terrainPos.x, terrainPos.z ) );
	}

	public virtual Vector2Int RealPosToChunk( Vector3 realPos )
	{
		Vector3Int terrainPos = RealPosToTerrain( realPos );
		return TerrainPosToChunk( terrainPos );
	}

	public virtual Chunk GetChunk( Vector2Int terrainPos )
	{
		Vector2Int chunkPos = TerrainPosToChunk( terrainPos );
		return CurrentChunks.ContainsKey( chunkPos ) ? CurrentChunks[chunkPos] : null;
	}

	public virtual Chunk GetChunk( Vector3Int terrainPos )
	{
		return GetChunk( new Vector2Int( terrainPos.x, terrainPos.z ) );
	}

	public virtual Chunk GetChunk( Vector3 realPos )
	{
		return GetChunk( RealPosToTerrain( realPos ) );
	}

	public virtual Cell GetCell( Vector2Int terrainPos, bool isForMap = false )
	{
		Cell cell = null;

		Chunk chunk = GetChunk( terrainPos );

		// Check ChunksForMap
		if ( chunk == null && isForMap )
		{
			Vector2Int chunkPos = TerrainPosToChunk( terrainPos );
			ChunksForMap.TryGetValue( chunkPos, out chunk );
		}

		// Get cell from the chunk if it exists
		if ( chunk != null )
		{
			// Adjust for the module operation
			if ( terrainPos.x < 0 )
				terrainPos.x--;
			if ( terrainPos.y < 0 )
				terrainPos.y--;

			Vector2Int posInChunk = new Vector2Int( LC_Math.Mod( terrainPos.x, ChunkSize ),
				LC_Math.Mod( terrainPos.y, ChunkSize ) );

			cell = chunk.Cells[posInChunk.x, posInChunk.y];
		}

		return cell;
	}

	public virtual Cell GetCell( Vector3 realPos, bool isForMap = false )
	{
		return GetCell( RealPosToTerrain( realPos ), isForMap );
	}

	public virtual bool IsChunkNeeded( Vector2Int chunkPos )
	{
		bool isNeeded = chunkPos == PlayerChunkPos;

		// If isn't the player current chunk
		if ( !isNeeded )
		{
			Vector3 chunkRealPosition = ChunkPosToReal( chunkPos );
			Vector3 offsetToPlayer = chunkRealPosition - Player.position;
			offsetToPlayer.y = 0; // Ignore height offset

			isNeeded = offsetToPlayer.magnitude <= ChunkRenderRealDistance;
		}

		return isNeeded;
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

		if ( CurrentChunks != null )
		{
			foreach ( KeyValuePair<Vector2Int, Chunk> entry in CurrentChunks )
				entry.Value.Destroy();

			CurrentChunks.Clear();
		}

		lock ( ChunksLoadingLock )
		{

			if ( ChunksLoading != null )
			{
				foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksLoading )
					entry.Value.Destroy();

				ChunksLoading.Clear();
			}

			if ( ChunksLoaded != null )
			{
				foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksLoaded )
					entry.Value.Destroy();

				ChunksLoaded.Clear();
			}
		}
	}

	#endregion
}