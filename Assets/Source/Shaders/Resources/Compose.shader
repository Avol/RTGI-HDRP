Shader "FullScreen/Compose"
{
	HLSLINCLUDE

	#pragma vertex Vert

	#pragma target 4.5
	#pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

	#include "./Octahedron.cginc"
	#include "./SH2.cginc" 

	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"

	// The PositionInputs struct allow you to retrieve a lot of useful information for your fullScreenShader:
	// struct PositionInputs
	// {
	//     float3 positionWS;  // World space position (could be camera-relative)
	//     float2 positionNDC; // Normalized screen coordinates within the viewport    : [0, 1) (with the half-pixel offset)
	//     uint2  positionSS;  // Screen space pixel coordinates                       : [0, NumPixels)
	//     uint2  tileCoord;   // Screen tile coordinates                              : [0, NumTiles)
	//     float  deviceDepth; // Depth from the depth buffer                          : [0, 1] (typically reversed)
	//     float  linearDepth; // View space Z coordinate                              : [Near, Far]
	// };

	// To sample custom buffers, you have access to these functions:
	// But be careful, on most platforms you can't sample to the bound color buffer. It means that you
	// can't use the SampleCustomColor when the pass color buffer is set to custom (and same for camera the buffer).
	// float4 CustomPassSampleCustomColor(float2 uv);
	// float4 CustomPassLoadCustomColor(uint2 pixelCoords);
	// float LoadCustomDepth(uint2 pixelCoords);
	// float SampleCustomDepth(float2 uv);

	uniform sampler2D _SSProbes;
	uniform sampler2D _SSProbesFiltered;
	uniform sampler2D _SurfaceBRDF;
	uniform sampler2D _LightningPDF;

	uniform sampler3D _SHAtlasR;
	uniform sampler3D _SHAtlasG;
	uniform sampler3D _SHAtlasB;

	TEXTURE2D_X(_GBufferTexture0);
	TEXTURE2D_X(_GBufferTexture1);
	TEXTURE2D_X(_GBufferTexture2);

	uniform				float		_Exposure;

	uniform				float2		_ScreenResolution;
	uniform				float2		_ProbeLayoutResolution;

	uniform				int			_Debug;

	uniform				int			_ProbeSize;
	uniform				int			_DistancePlaneWeighting;

	uniform				int			_VisualizeDistancePlaneWeighting;

	uniform				int			_VisualizeProbes;
	uniform				int			_VisualizeSurfaceBRDF;
	uniform				int			_VisualizeLightningPDF;

	uniform				int			_Reflections;
	uniform				bool		_VisualizeWorldProbes;
	uniform				bool		_TrilinearOffset;

	static float2 closestPixels[9] =
	{
		float2(0, 0),
		float2(-1, 1),
		float2(0, 1),
		float2(1, 1),
		float2(1, 0),
		float2(1, -1),
		float2(0, -1),
		float2(-1, -1),
		float2(-1, 0),
	};

	// Retrieves world space normal and linear depth.
	// @ positionCS = screen space UV coordinate.
	float4 GetNormalDepth(float2 positionCS, out float3 worldPosition)
	{
		float4 normalDepth = 0;

		// load normal
		NormalData normalData;
		DecodeFromNormalBuffer(positionCS, normalData);
		normalDepth.xyz = normalData.normalWS;

		// load depth
		float			depth = LoadCameraDepth(positionCS);
		PositionInputs	posInput = GetPositionInput(positionCS, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
		normalDepth.w = posInput.linearDepth;

		worldPosition = posInput.positionWS + _WorldSpaceCameraPos;

		return normalDepth;
	}

	// Clamps UV to smaller resolution.
	// @ positionCS = screen space UV coordinate.
	float2 ClampCoordinate(float2 positionNDC, float2 resolution, int downscale)
	{
		float2	t = floor(positionNDC * resolution);
		float2	m = t % downscale;
		float2	d = positionNDC - m / resolution;
		return d;
	}

	// Clamps UV to smaller resolution.
	// @ positionCS = screen space UV coordinate.
	float2 ClampCoordinateBilinearCenter(float2 positionCS, float2 resolution, int downscale)
	{
		float2	m = positionCS - positionCS % downscale + downscale / 2;
		float2	d = m / resolution;
		return d;
	}

	//  Compare probe vs pixel distance and normal.
	//
	int DistancePlaneWeighting(float3 fromNormal, float3 fromWorldPosition, float fromDepth, float3 toWorldPosition)
	{
		float4	probeScenePlane				= float4(fromNormal, dot(fromWorldPosition, fromNormal));

		float	planeDistance				= abs(dot(float4(toWorldPosition, -1), probeScenePlane));
		float	relativeDepthDifference		= planeDistance / fromDepth;

		return exp2(-1000000 * (relativeDepthDifference * relativeDepthDifference)) > .1 ? 1.0 : 0.0;
	}

	//  Compare probe vs pixel distance and normal.
	//
	float DistancePlaneWeighting2(float3 fromNormal, float3 fromWorldPosition, float fromDepth, float3 toWorldPosition)
	{
		float4	probeScenePlane				= float4(fromNormal, dot(fromWorldPosition, fromNormal));

		float	planeDistance				= abs(dot(float4(toWorldPosition, -1), probeScenePlane));
		float	relativeDepthDifference		= planeDistance / fromDepth;

		return exp2(-1000000 * (relativeDepthDifference * relativeDepthDifference));
	}

	int GetWorldProbePixel(float2 positionNDC, float2 positionCS)
	{
		float3 worldPosition;
		GetNormalDepth(positionCS, worldPosition);

		if (abs(worldPosition.x) % 1 < 0.1f &&
			abs(worldPosition.y) % 1 < 0.1f &&
			abs(worldPosition.z) % 1 < 0.1f)
			return 1;

		return 0;
	}

    // There are also a lot of utility function you can use inside Common.hlsl and Color.hlsl,
    // you can check them out in the source code of the core SRP package.
    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
		// this pixel data.
		float2	positionCS			= varyings.positionCS.xy;
		float2	positionNDC			= positionCS / _ScreenResolution;
		float3	worldPosition		= 0;
		float4  pixelNormalDepth	= GetNormalDepth(positionCS, worldPosition);

		if (_VisualizeWorldProbes)
			return GetWorldProbePixel(positionNDC, positionCS);

		if (_VisualizeProbes)
			return tex2D(_SSProbes, positionNDC);

		if (_VisualizeSurfaceBRDF)
			return tex2D(_SurfaceBRDF, positionNDC);

		if (_VisualizeLightningPDF)
			return tex2D(_LightningPDF, positionNDC);

		// todo: multiply by camera range.
		if (pixelNormalDepth.w >= _ProjectionParams.z)
			return float4(0, 0, 0, 1);
		
		// this probe data.
		float2	probeNDC					= ClampCoordinate(positionNDC, _ProbeLayoutResolution, _ProbeSize);
		float3	probeWorldPosition			= 0;
		float4  probeNormalDepth			= GetNormalDepth(probeNDC * _ProbeLayoutResolution, probeWorldPosition);

		// ------- weight probes for distance normal to find best matching one ----- //
		if (_DistancePlaneWeighting)
		{
			float distanceWeight	= DistancePlaneWeighting2(probeNormalDepth.xyz, probeWorldPosition, probeNormalDepth.w, worldPosition);

			if (distanceWeight <= 0.1)
			{
				if (_VisualizeDistancePlaneWeighting)
					return 1;

				float	closestProbeDistance	= 1;
				float2	closestPositionNDC		= probeNDC;
				float	biggestWeight			= distanceWeight;

				for (int i = 1; i < 9; i++)
				{
					float2	neighbourProbeNDC					= probeNDC + closestPixels[i] / _ProbeLayoutResolution * _ProbeSize;

					float3	neighbourWorldPosition				= 0;
					float4	neighbourProbeNormalDepth			= GetNormalDepth(neighbourProbeNDC * _ProbeLayoutResolution, neighbourWorldPosition);

					float	neighbourProbeDistanceWeight		= DistancePlaneWeighting2(neighbourProbeNormalDepth.xyz, neighbourWorldPosition, neighbourProbeNormalDepth.w, worldPosition);

					if (biggestWeight < neighbourProbeDistanceWeight)
					{
						biggestWeight		= neighbourProbeDistanceWeight;
						closestPositionNDC	= neighbourProbeNDC;
					}
					/*if (neighbourProbeDistanceWeight == 1)
					{
						float probeDistanceToPixel = length(positionNDC - neighbourProbeNDC);
						if (probeDistanceToPixel < closestProbeDistance)
						{
							closestProbeDistance	= probeDistanceToPixel;
							closestPositionNDC		= neighbourProbeNDC;
						}
					}*/
				}

				positionNDC = closestPositionNDC;

				if (_TrilinearOffset)
					positionNDC = closestPositionNDC + _ProbeSize / 2.0f / _ProbeLayoutResolution;
			}
			else
			{
				//  TODO: when this pixel is on correct distance plane:
				//		 test if direction of bilinear sampling distance plane is the same to blend.


				//positionNDC = probeNDC;

				//if (_TrilinearOffset)
				//	positionNDC += _ProbeSize / 2.0f / _ProbeLayoutResolution;
			}
		}

		else
		{
			//positionNDC = probeNDC;
			if (_TrilinearOffset)
				positionNDC = ClampCoordinateBilinearCenter(positionCS, _ProbeLayoutResolution, _ProbeSize);
		}

		// -------- sample diffuse harmonics ------------- //

		SH9Color sh9Color;
		sh9Color.sh0 = float3(tex3D(_SHAtlasR, float3(positionNDC, 0 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 0 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 0 / 8.0)).r * 2 - 1);
		sh9Color.sh1 = float3(tex3D(_SHAtlasR, float3(positionNDC, 1 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 1 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 1 / 8.0)).r * 2 - 1);
		sh9Color.sh2 = float3(tex3D(_SHAtlasR, float3(positionNDC, 2 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 2 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 2 / 8.0)).r * 2 - 1);
		sh9Color.sh3 = float3(tex3D(_SHAtlasR, float3(positionNDC, 3 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 3 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 3 / 8.0)).r * 2 - 1);
		sh9Color.sh4 = float3(tex3D(_SHAtlasR, float3(positionNDC, 4 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 4 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 4 / 8.0)).r * 2 - 1);
		sh9Color.sh5 = float3(tex3D(_SHAtlasR, float3(positionNDC, 5 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 5 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 5 / 8.0)).r * 2 - 1);
		sh9Color.sh6 = float3(tex3D(_SHAtlasR, float3(positionNDC, 6 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 6 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 6 / 8.0)).r * 2 - 1);
		sh9Color.sh7 = float3(tex3D(_SHAtlasR, float3(positionNDC, 7 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 7 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 7 / 8.0)).r * 2 - 1);
		sh9Color.sh8 = float3(tex3D(_SHAtlasR, float3(positionNDC, 8 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasG, float3(positionNDC, 8 / 8.0)).r * 2 - 1,  tex3D(_SHAtlasB, float3(positionNDC, 8 / 8.0)).r * 2 - 1);


		float4 gBufferAlbedo = LOAD_TEXTURE2D_X(_GBufferTexture0, positionCS);
		gBufferAlbedo = max(0.01, gBufferAlbedo);

		//float3	exposure	= /*GetCurrentExposureMultiplier() * */_Exposure;
		//float	exposureMult = GetCurrentExposureMultiplier();

		float3	radiance = calcIrradiance(pixelNormalDepth.xyz, sh9Color) * gBufferAlbedo.rgb * _Exposure;// *exposure;


		if (_Reflections)
		{
			float	gBufferRoughness		= LOAD_TEXTURE2D_X(_GBufferTexture1, positionCS).w;

			float3	viewVector			= normalize(worldPosition - _WorldSpaceCameraPos);
			float3	reflectVector		= reflect(viewVector, normalize(pixelNormalDepth.xyz));
			float2	octaUV				= OctahedronUV(reflectVector) / _ProbeLayoutResolution * _ProbeSize;

			//float3	reflection = tex2D(_SSProbesFiltered, probeNDC + octaUV).rgb;
			float3	reflection			= calcIrradiance(reflectVector, sh9Color) * _Exposure;

			return float4(lerp(reflection, radiance, gBufferRoughness), 1);
		}
		else
		{
			return float4(radiance, 1);
		}
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Compose"

            ZWrite Off
            ZTest Off
            Blend One One
            Cull Off

            HLSLPROGRAM
                #pragma fragment FullScreenPass
            ENDHLSL
        }

		Pass
		{
			Name "ComposeDebug"

			ZWrite Off
			ZTest Off
			Blend One Zero
			Cull Off

			HLSLPROGRAM
				#pragma fragment FullScreenPass
			ENDHLSL
		}
    }
	Fallback Off
}