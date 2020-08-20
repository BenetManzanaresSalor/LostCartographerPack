Shader "Custom/HeightShader"
{
	Properties
	{
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard fullforwardshadows

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		const static int maxColorCount = 8;

		float minHeight;
		float maxHeight;
		int n_colors;
		float3 colors[maxColorCount];

		struct Input
		{
			float3 worldPos;
		};

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		float inverseLerp(float a, float b, float value) {
			return saturate((value - a) / (b - a));
		}

		void surf(Input IN, inout SurfaceOutputStandard o)
		{
			float heightPercentage = inverseLerp(minHeight, maxHeight, IN.worldPos.y);
			float colorFloatIndex = heightPercentage * (n_colors - 1);
			int colorIndex = (int)colorFloatIndex;
			float indexDecimals = colorFloatIndex - (float)colorIndex;

			o.Albedo = (1 - indexDecimals) * colors[colorIndex] + indexDecimals * colors[colorIndex + 1];
		}
		ENDCG
	}
		FallBack "Diffuse"
}
