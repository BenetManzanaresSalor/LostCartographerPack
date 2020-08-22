using UnityEngine;
using UnityEngine.UI;

[RequireComponent( typeof( RawImage ) )]
public class LC_Map : LC_GenericMap<LC_Terrain, LC_Chunk<LC_Cell>, LC_Cell>
{
	#region Attributes

	#region Settings

	[Header( "Render settings" )]
	[SerializeField] protected Color[] Colors;

	#endregion

	#region Function attributes

	protected RawImage Renderer;

	#endregion

	#endregion

	#region Initialization

	protected override void BindTexture()
	{
		Renderer = GetComponent<RawImage>();
		Renderer.texture = MapTexture;
	}

	#endregion

	#region Texture computation

	protected override Color GetColorPerCell( LC_Cell cell )
	{
		Color color;

		if ( cell == null )
		{
			color = Color.black;
		}
		else
		{
			float heightPercentage = Mathf.Clamp( Mathf.InverseLerp( 0, TerrainToMap.MaxHeight, cell.Height ), 0, 1f );
			float colorFloatIndex = heightPercentage * ( Colors.Length - 1 );
			int colorIndex = Mathf.RoundToInt( colorFloatIndex );
			color = Colors[colorIndex];

			/*int colorIndex = (int)colorFloatIndex;
			float indexDecimals = colorFloatIndex - colorIndex;
			color = ( 1 - indexDecimals ) * Colors[colorIndex] + indexDecimals * Colors[colorIndex + 1];*/
		}

		return color;
	}

	#endregion
}
