Shader "FullScreen/Compose"
{
	HLSLINCLUDE

	#pragma vertex Vert

	#pragma target 4.5
	#pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

	#include "./Octahedron.cginc"
	#include "./SH2.cginc" 
	#include "./Probes.cginc" 

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

	uniform				bool		_DiffuseGI;
	uniform				bool		_Reflections;
	uniform				bool		_VisualizeWorldProbes;
	uniform				bool		_TrilinearOffset;
	uniform				bool		_TrilinearOffset2;
	uniform				bool		_NormalWeight;


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

	//
	//
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

	//
	//
	bool checkProbeBlendMatch(float2 probeNDC, float2 dir, float4 sceneNormalDepth, float3 sceneWorldPosition)
	{
		float2	neighbourProbeNDC				= probeNDC + (dir / _ProbeLayoutResolution * _ProbeSize);

		if (neighbourProbeNDC.x < 0 || neighbourProbeNDC.y < 0 ||
			neighbourProbeNDC.x > 1 || neighbourProbeNDC.y > 1)
			return 1;

		float3	neighbourWorldPosition			= 0;
		float4	neighbourProbeNormalDepth		= GetNormalDepth(neighbourProbeNDC * _ProbeLayoutResolution, neighbourWorldPosition);

		float	neighbourProbeDistanceWeight	= DistancePlaneWeighting2(sceneNormalDepth.xyz, sceneWorldPosition, sceneNormalDepth.w, neighbourWorldPosition);

		if (_NormalWeight)
		{
			float normalWeight = max(0, dot(sceneNormalDepth.xyz, neighbourProbeNormalDepth.xyz));
			if (neighbourProbeDistanceWeight > normalWeight)
				neighbourProbeDistanceWeight = normalWeight;
		}

		return neighbourProbeDistanceWeight < 0.1f;
	}

    // There are also a lot of utility function you can use inside Common.hlsl and Color.hlsl,
    // you can check them out in the source code of the core SRP package.
    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
		// this pixel data.
		float2	positionCS			= varyings.positionCS.xy;
		float2	positionNDC			= positionCS / _ScreenResolution;
		float3	sceneWorldPosition	= 0;
		float4  sceneNormalDepth	= GetNormalDepth(positionCS, sceneWorldPosition);

		if (_VisualizeWorldProbes)
			return GetWorldProbePixel(positionNDC, positionCS);

		if (_VisualizeProbes)
			return tex2D(_SSProbes, positionNDC);

		if (_VisualizeSurfaceBRDF)
			return tex2D(_SurfaceBRDF, positionNDC);

		if (_VisualizeLightningPDF)
			return tex2D(_LightningPDF, positionNDC);

		// todo: multiply by camera range.
		if (sceneNormalDepth.w >= _ProjectionParams.z)
			return float4(0, 0, 0, 1);
		
		// this probe data.
		float2	probeCS						= positionCS - positionCS % _ProbeSize;
		float2	probeNDC					= ClampCoordinate(positionNDC, _ProbeLayoutResolution, _ProbeSize);
		float3	probeWorldPosition			= 0;
		float4  probeNormalDepth			= GetNormalDepth(probeNDC * _ProbeLayoutResolution, probeWorldPosition);

		// ------- weight probes for distance & normal to find best matching one ----- //
		if (_DistancePlaneWeighting)
		{
			float distanceWeight	= DistancePlaneWeighting2(sceneNormalDepth.xyz, sceneWorldPosition, sceneNormalDepth.w, probeWorldPosition);


			float normalWeight = max(0, dot(sceneNormalDepth.xyz, probeNormalDepth.xyz));
			if (_NormalWeight)
			{
				//float normalWeight = max(0, dot(sceneNormalDepth.xyz, probeNormalDepth.xyz));
				if (distanceWeight > normalWeight)
					distanceWeight = normalWeight;
			}

			if (distanceWeight <= 0.1 || normalWeight < 0.5f)
			{
				if (_VisualizeDistancePlaneWeighting)
					return 1;

				float	closestProbeDistance	= 1;
				float2	closestPositionNDC		= probeNDC;
				float	biggestWeight			= distanceWeight;

				for (int i = 1; i < 9; i++)
				{
					float2	neighbourProbeNDC					= probeNDC + closestPixels[i] / _ProbeLayoutResolution * _ProbeSize;

					// out of screen
					if (neighbourProbeNDC.x < 0 || neighbourProbeNDC.y < 0 ||
						neighbourProbeNDC.x > 1 || neighbourProbeNDC.y > 1)
						continue;

					float3	neighbourWorldPosition				= 0;
					float4	neighbourProbeNormalDepth			= GetNormalDepth(neighbourProbeNDC * _ProbeLayoutResolution, neighbourWorldPosition);

					float	neighbourProbeDistanceWeight		= DistancePlaneWeighting2(sceneNormalDepth.xyz, sceneWorldPosition, sceneNormalDepth.w, neighbourWorldPosition);

					if (_NormalWeight)
					{
						float	normalWeight = max(0, dot(sceneNormalDepth.xyz, neighbourProbeNormalDepth.xyz));
						if (neighbourProbeDistanceWeight > normalWeight)
							neighbourProbeDistanceWeight = normalWeight;
					}

					if (biggestWeight < neighbourProbeDistanceWeight)
					{
						biggestWeight		= neighbourProbeDistanceWeight;
						closestPositionNDC	= neighbourProbeNDC;
					}
				}

				positionNDC = closestPositionNDC;

				if (_TrilinearOffset)
					positionNDC = ClampCoordinateBilinearCenter(closestPositionNDC * _ProbeLayoutResolution, _ProbeLayoutResolution, _ProbeSize);
			}
			else
			{
				//  TODO: when this pixel is on correct distance plane:
				//		 test if direction of bilinear sampling distance plane is the same to blend.

				if (_TrilinearOffset)
				{
					if (_TrilinearOffset2)
					{
						float2 offsetNDC = probeNDC + _ProbeSize / 2.0f / _ProbeLayoutResolution - positionNDC;
						uint2 missmatch = uint2(0, 0);

						// left, right
						if (offsetNDC.x > 0)
						{
							if (checkProbeBlendMatch(probeNDC, float2(-1, 0), sceneNormalDepth, sceneWorldPosition))
								missmatch.x = 1;
						}
						else
						{
							if (checkProbeBlendMatch(probeNDC, float2(1, 0), sceneNormalDepth, sceneWorldPosition))
								missmatch.x = 1;
						}

						// down, up
						if (offsetNDC.y > 0)
						{
							if (checkProbeBlendMatch(probeNDC, float2(0, -1), sceneNormalDepth, sceneWorldPosition))
								missmatch.y = 1;
						}
						else
						{
							if (checkProbeBlendMatch(probeNDC, float2(0, 1), sceneNormalDepth, sceneWorldPosition))
								missmatch.y = 1;
						}

						// down left
						if (offsetNDC.y > 0 && offsetNDC.x > 0)
						{
							if (checkProbeBlendMatch(probeNDC, float2(-1, -1), sceneNormalDepth, sceneWorldPosition))
								missmatch = uint2(1, 1);
						}

						// up left
						if (offsetNDC.y <= 0 && offsetNDC.x > 0)
						{
							if (checkProbeBlendMatch(probeNDC, float2(-1, 1), sceneNormalDepth, sceneWorldPosition))
								missmatch = uint2(1, 1);
						}

						// up right
						if (offsetNDC.y <= 0 && offsetNDC.x <= 0)
						{
							if (checkProbeBlendMatch(probeNDC, float2(1, 1), sceneNormalDepth, sceneWorldPosition))
								missmatch = uint2(1, 1);
						}

						// down right
						if (offsetNDC.y > 0 && offsetNDC.x <= 0)
						{
							if (checkProbeBlendMatch(probeNDC, float2(1, -1), sceneNormalDepth, sceneWorldPosition))
								missmatch = uint2(1, 1);
						}




						if (missmatch.x != 0 || missmatch.y != 0)
						{
							if (_VisualizeDistancePlaneWeighting)
								return 1;

							float2 clampedPositionNDC = ClampCoordinateBilinearCenter(positionCS, _ProbeLayoutResolution, _ProbeSize);
							//positionNDC = clampedPositionNDC;
							positionNDC.x = missmatch.x == 1 ? clampedPositionNDC.x : positionNDC.x;
							positionNDC.y = missmatch.y == 1 ? clampedPositionNDC.y : positionNDC.y;
						}
					}
				}
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


		float4 gBufferAlbedo	= max(0.01, LOAD_TEXTURE2D_X(_GBufferTexture0, positionCS));	// albedo, ao
		float4 gBufferSurface	= LOAD_TEXTURE2D_X(_GBufferTexture1, positionCS);	// normal, roughness
		float4 gBufferSurface2	= LOAD_TEXTURE2D_X(_GBufferTexture2, positionCS);	// z = metallic.

		float	gBufferRoughness	= gBufferSurface.w;
		float	gBufferMetallic		= gBufferSurface2.z;

		float3	radiance			= calcIrradiance(sceneNormalDepth.xyz, sh9Color) * gBufferAlbedo.rgb * gBufferAlbedo.a;



		float3	viewVector			= normalize(sceneWorldPosition - _WorldSpaceCameraPos);
		float3	reflectVector		= reflect(viewVector, normalize(sceneNormalDepth.xyz));
		float2	octaUV				= OctahedronUV(reflectVector) / _ProbeLayoutResolution * _ProbeSize;

		float3	reflection			= calcIrradiance(reflectVector, sh9Color) * gBufferAlbedo.a;



		/*
		float3 baseColor = lerp(diffuseColor.rgb, specularColor.rgb, metallic);
		float roughness = saturate(roughnessValue);
		float3 diffuseComponent = baseColor * (1.0 - metallic);
		float3 specularComponent = specularColor.rgb;
		float3 reflectionComponent = lerp(specularComponent, baseColor, roughness * roughness);
		float3 finalColor = lerp(diffuseComponent, reflectionComponent, metallic);
		*/


		if (_DiffuseGI && _Reflections)
		{
			return float4(lerp(radiance, reflection, gBufferMetallic), 1) * PI * _Exposure;
		}
		else if (_DiffuseGI && !_Reflections)
		{
			return float4(radiance, 1) * _Exposure;
		}
		else if (!_DiffuseGI && _Reflections)
		{
			return float4(lerp(0, reflection, gBufferMetallic), 1) * PI * _Exposure;
		}
		else
		{
			return 0;
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