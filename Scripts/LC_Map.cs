using UnityEngine;
using UnityEngine.UI;

[RequireComponent( typeof( RawImage ) )]
public class LC_Map : LC_GenericMap<LC_Terrain, LC_Chunk<LC_Cell>, LC_Cell>
{
	#region Attributes	

	#region Settings

	[Header( "Additional render settings" )]
	[SerializeField] protected LC_Terrain_RenderType RenderType;
	[SerializeField] protected Color[] Colors;

	#endregion

	#region Function attributes

	protected RawImage Renderer;

	#endregion

	#endregion

	#region Texture computation

	protected override Color32 GetColorPerCell( LC_Cell cell )
	{
		Color32 color;

		if ( cell == null )
		{
			color = Color.black;
		}
		else
		{
			float heightPercentage = Mathf.Clamp( Mathf.InverseLerp( 0, TerrainToMap.MaxHeight, cell.Height ), 0, 0.99f );
			float colorFloatIndex = heightPercentage * ( Colors.Length - 1 );

			switch ( RenderType )
			{
				case LC_Terrain_RenderType.HEIGHT_CONTINUOUS:
					int colorIndex = (int)colorFloatIndex;
					float indexDecimals = colorFloatIndex - colorIndex;
					color = ( 1 - indexDecimals ) * Colors[colorIndex] + indexDecimals * Colors[colorIndex + 1];
					break;
				case LC_Terrain_RenderType.HEIGHT_DISCRETE:
					color = Colors[Mathf.RoundToInt( colorFloatIndex )];
					break;
				default:
					color = Color.black;
					break;
			}
		}

		return color;
	}

	protected override Vector2Int GetReferencePos()
	{
		Vector3Int pos = TerrainToMap.GetPlayerTerrainPos();
		return new Vector2Int( pos.x, pos.z );
	}

	#endregion
}
