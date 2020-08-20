using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public abstract class LC_GenericTerrain<Cell, Chunk> : MonoBehaviour where Chunk : LC_Chunk<Cell> where Cell : LC_Cell
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
	[SerializeField] protected bool ParallelChunkCellsLoading = true;
	[SerializeField] protected Material RenderMaterial;
	[SerializeField] protected float MaxUpdateTime = 1f / 120f;

	#endregion

	#region Function attributes

	protected int ChunkSize;
	protected Vector3 CurrentRealPos;    // Equivalent to transform.position. Needed for parallel chunk mesh loading.
	protected Vector2Int PlayerChunkPos;
	protected Vector3 HalfChunk;
	protected float ChunkRenderRealDistance;
	protected Dictionary<Vector2Int, Chunk> ChunksLoading;
	protected Dictionary<Vector2Int, Chunk> ChunksBuilt;
	protected Dictionary<Vector2Int, Chunk> CurrentChunks;
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
		ChunksBuilt = new Dictionary<Vector2Int, Chunk>();
		CurrentChunks = new Dictionary<Vector2Int, Chunk>();

		PlayerChunkPos = RealPosToChunk( Player.position );

		DestroyTerrain( true );
		IniTerrain();
	}

	protected virtual void IniTerrain()
	{
		// Always load the current player chunk
		CreateChunk( PlayerChunkPos, true );

		// Load the other chunks
		foreach ( Vector2Int chunkPos in LC_Math.AroundPositions( PlayerChunkPos, ChunkRenderDistance ) )
			CreateChunk( chunkPos );
	}

	#endregion

	#region Chunk creation

	protected virtual void CreateChunk( Vector2Int chunkPos, bool ignoreParallel = false )
	{
		Chunk chunk = CreateChunkInstance( chunkPos );
		chunk.Obj.transform.parent = this.transform;
		chunk.Obj.name = "Chunk_" + chunkPos;
		//chunk.Obj.transform.position = ChunkPosToReal( chunk.Position ); // TODO
		ChunksLoading.Add( chunkPos, chunk );

		if ( ParallelChunkLoading && !ignoreParallel )
		{
			if ( !ParallelChunkCellsLoading )
				chunk.Cells = CreateCells( chunk );

			chunk.ParallelTask = Task.Run( () =>
			{
				if ( ParallelChunkCellsLoading )
					chunk.Cells = CreateCells( chunk );

				CreateMesh( chunk );
				chunk.BuildMesh();

				ChunkBuilt( chunkPos, chunk );
			} );
		}
		else
		{
			chunk.Cells = CreateCells( chunk );
			CreateMesh( chunk );
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

	protected virtual void ChunkBuilt( Vector2Int chunkPos, Chunk chunk )
	{
		if ( ParallelChunkLoading )
		{
			lock ( ChunksLoadingLock )
			{
				ChunksLoading.Remove( chunk.Position );
				ChunksBuilt.Add( chunkPos, chunk );
			}
		}
		else
		{
			CreateChunkMeshObj( chunk );

			ChunksLoading.Remove( chunk.Position );
			CurrentChunks.Add( chunkPos, chunk );
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
			CreateBuiltChunks();

		// Update chunks needed (if remains time)
		if ( DynamicChunkLoading && InMaxUpdateTime() )
			UpdateChunks();
	}

	protected virtual void CheckPlayerCurrentChunk()
	{
		Monitor.Enter( ChunksLoadingLock );
		if ( !CurrentChunks.ContainsKey( PlayerChunkPos ) && !ChunksBuilt.ContainsKey( PlayerChunkPos ) )
		{
			bool isLoading = ChunksLoading.TryGetValue( PlayerChunkPos, out Chunk playerChunk );
			Monitor.Exit( ChunksLoadingLock );
			if ( isLoading )
			{
				playerChunk.ParallelTask.Wait();  // Wait parallel loading to end					
				CreateBuiltChunks();    // Built the chunk (and the others if are needed)
			}
			else
			{
				CreateChunk( PlayerChunkPos, true );    // Ignore parallel because can continue playing without this chunk
			}
		}
		else
			Monitor.Exit( ChunksLoadingLock );
	}

	protected virtual bool InMaxUpdateTime()
	{
		return ( Time.realtimeSinceStartup - UpdateIniTime ) <= MaxUpdateTime;
	}

	protected virtual bool CreateBuiltChunks()
	{
		bool someChunkLoaded = false;

		if ( ChunksBuilt.Count > 0 )
		{
			if ( Monitor.TryEnter( ChunksLoadingLock ) )
			{
				foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksBuilt )
				{
					if ( InMaxUpdateTime() )
					{
						if ( IsChunkNeeded( entry.Key ) )
						{
							CreateChunkMeshObj( entry.Value );
							CurrentChunks.Add( entry.Key, entry.Value );
						}
					}
					else
						break;
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
		Dictionary<Vector2Int, object> chunksNeeded = ComputeChunksNeeded(); // Use a dictionary for faster searchs

		// Check chunks already loaded
		List<Vector2Int> chunksToUnload = new List<Vector2Int>();
		foreach ( KeyValuePair<Vector2Int, Chunk> entry in CurrentChunks )
		{
			// If already loaded, don't reload
			if ( chunksNeeded.ContainsKey( entry.Key ) )
			{
				chunksNeeded.Remove( entry.Key );
			}
			// If don't needed, unload
			else
			{
				chunksToUnload.Add( entry.Key );
				entry.Value.Destroy();
			}
		}

		lock ( ChunksLoadingLock )
		{
			// Ignore chunks that are loading
			foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksLoading )
				if ( chunksNeeded.ContainsKey( entry.Key ) )
					chunksNeeded.Remove( entry.Key );

			// Ignore chunks that are already built
			foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksBuilt )
				if ( chunksNeeded.ContainsKey( entry.Key ) )
					chunksNeeded.Remove( entry.Key );

			// Load the other chunks
			foreach ( KeyValuePair<Vector2Int, object> entry in chunksNeeded )
			{
				if ( InMaxUpdateTime() )
					CreateChunk( entry.Key );
				else
					break;
			}

		}

		// Remove useless chunks
		foreach ( Vector2Int chunkPos in chunksToUnload )
			CurrentChunks.Remove( chunkPos );
	}

	protected virtual Dictionary<Vector2Int, object> ComputeChunksNeeded()
	{
		Dictionary<Vector2Int, object> chunksNeeded = new Dictionary<Vector2Int, object>();

		// Always load the player current chunk		
		chunksNeeded.Add( PlayerChunkPos, null );

		if ( ChunkRenderDistance > 0 )
		{
			int radius = ChunkRenderDistance + 1;

			Vector2Int chunkPos;
			Vector2Int topLeftCorner;
			int yIncrement = 1;
			for ( int currentRadius = 1; currentRadius < radius; currentRadius++ )
			{
				topLeftCorner = PlayerChunkPos + Vector2Int.one * -1 * currentRadius;

				for ( int x = 0; x <= currentRadius * 2; x++ )
				{
					yIncrement = ( x == 0 || x == currentRadius * 2 ) ? 1 : currentRadius * 2;
					for ( int y = 0; y <= currentRadius * 2; y += yIncrement )
					{
						chunkPos = topLeftCorner + new Vector2Int( x, y );

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

	#region Auxiliar

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

	public virtual Vector3Int RealPosToTerrain( Vector3 realPos )
	{
		Vector3 relativePos = realPos - CurrentRealPos + HalfChunk;
		return new Vector3Int( (int)( relativePos.x / CellSize.x ), (int)( relativePos.y / CellSize.y ), (int)( relativePos.z / CellSize.z ) );
	}

	public virtual Vector2Int RealPosToChunk( Vector3 realPos )
	{
		Vector3Int terrainPos = RealPosToTerrain( realPos );

		Vector2Int res = new Vector2Int( terrainPos.x / ChunkSize, terrainPos.z / ChunkSize );

		if ( terrainPos.x < 0 )
			res.x -= 1;
		if ( terrainPos.z < 0 )
			res.y -= 1;

		return res;
	}

	public virtual Vector3 ChunkPosToReal( Vector2Int chunkPosition )
	{
		return CurrentRealPos + new Vector3( chunkPosition.x * ChunkSize * CellSize.x, 0, chunkPosition.y * ChunkSize * CellSize.z );
	}

	public virtual Vector2Int TerrainPosToChunk( Vector2Int terrainPos )
	{
		return new Vector2Int( terrainPos.x / ChunkSize, terrainPos.y / ChunkSize ); ;
	}

	public virtual Chunk GetChunk( Vector2Int terrainPos )
	{
		Vector2Int chunkPos = TerrainPosToChunk( terrainPos );
		return CurrentChunks.ContainsKey( chunkPos ) ? CurrentChunks[chunkPos] : null;
	}

	public virtual Chunk GetChunk( Vector3 realPos )
	{
		return GetChunk( RealPosToTerrain( realPos ) );
	}

	public virtual Cell GetCell( Vector2Int terrainPos )
	{
		Cell cell = null;

		Chunk chunk = GetChunk( terrainPos );
		if ( chunk != null )
		{
			Vector2Int posInChunk = new Vector2Int( LC_Math.Mod( terrainPos.x, ChunkSize ),
				LC_Math.Mod( terrainPos.y, ChunkSize ) );
			cell = chunk.Cells[posInChunk.x, posInChunk.y];
		}

		return cell;
	}

	public virtual Cell GetCell( Vector3 realPos )
	{
		return GetCell( RealPosToTerrain( realPos ) );
	}

	protected virtual bool IsChunkNeeded( Vector2Int chunkPos )
	{
		Vector3 chunkRealPosition = ChunkPosToReal( chunkPos );
		Vector3 offsetToPlayer = chunkRealPosition - Player.position;
		offsetToPlayer.y = 0; // Ignore height offset

		return offsetToPlayer.magnitude <= ChunkRenderRealDistance;
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

			if ( ChunksBuilt != null )
			{
				foreach ( KeyValuePair<Vector2Int, Chunk> entry in ChunksBuilt )
					entry.Value.Destroy();

				ChunksBuilt.Clear();
			}
		}
	}

	#endregion
}