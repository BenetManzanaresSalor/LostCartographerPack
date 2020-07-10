using System.Collections.Generic;
using UnityEngine;

public class LC_Cell
{
	public Vector2Int TerrainPos;
	public float Height;

	public LC_Cell( Vector2Int terrainPosition, float height )
	{
		TerrainPos = terrainPosition;
		Height = height;
	}
}

public class LC_Chunk
{
	public GameObject Obj;
	public Vector2Int Position;
	public Vector2Int CellsOffset;
	public float[,] HeightsMap;

	public List<Vector3> Vertices;
	public List<int> Triangles;
	public List<Vector2> UVs;
	public Vector3[] Normals;

	public Vector3[] VerticesArray;
	public int[] TrianglesArray;
	public Vector2[] UVsArray;


	public LC_Chunk( GameObject obj, Vector2Int position, int chunkSize )
	{
		Obj = obj;
		Position = position;
		CellsOffset = Position * chunkSize;

		Vertices = new List<Vector3>();
		Triangles = new List<int>();
		UVs = new List<Vector2>();
		Normals = null;
	}

	public Vector2Int TerrainPosToChunk( Vector2Int cellTerrainPos )
	{
		return cellTerrainPos - CellsOffset;
	}

	public Vector2Int ChunkPosToTerrain( Vector2Int cellChunkPos )
	{
		return cellChunkPos + CellsOffset;
	}

	public void BuildMesh()
	{
		// Convert to array
		VerticesArray = Vertices.ToArray();
		TrianglesArray = Triangles.ToArray();
		UVsArray = UVs.ToArray();

		// Reset lists
		Vertices.Clear();
		Triangles.Clear();
		UVs.Clear();
	}
}