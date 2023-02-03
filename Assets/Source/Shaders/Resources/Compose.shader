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

	uniform sampler2D _DepthNormals;
	uniform sampler2D _SSProbes;
	uniform sampler2D _SSProbesEncoded;
	uniform sampler3D _SHAtlas;

	uniform float _Multiplier;
	uniform float _NeighbourBlendDistance;

	uniform float3x3	_CameraMV;

	uniform	float2		_Resolution;

	uniform int			_Debug;

	uniform				float3		_CameraPosition;
	uniform				float3		_CameraUp;
	uniform				float3		_CameraRight;
	uniform				float3		_CameraFront;
	uniform				float		_CameraAspect;
	uniform				float		_CameraFOV;
	uniform				float		_CameraNear;
	uniform				float		_CameraFar;

	uniform				int			_ProbeSize;
	uniform				float		_HarmonicsBlend;
	uniform				int			_DebugNormals;

	//TEXTURE2D_X(_SSProbes);

	float3 getWorldPos(float2 screenUV, float depth)
	{
		// calculate camera ray
		float2 uv = (screenUV - 0.5f) * _CameraFOV;
		uv.x *= _CameraAspect;
		float3 ray = _CameraUp * uv.y + _CameraRight * uv.x + _CameraFront;

		// calculate near & far & ray.
		float3 	farPos = _CameraPosition + ray * _CameraFar;
		float3	nearPos = _CameraPosition;
		float3	worldPos = lerp(nearPos, farPos, depth);

		return worldPos;
	}

	float2 PixelSizeInWorldSpace(float linearDepth)
	{
		float cameraDistance	= linearDepth * (_CameraFar - _CameraNear);
		float height			= _CameraFOV * cameraDistance;
		float width				= (height / _Resolution.y) * _Resolution.x;

		return float2(width, height) / float2(_Resolution.y, _Resolution.x);
	}

	// Transforms camera coordinate to world space position.
	// @ m = inverse projection view matrix of the camera.
	float3 worldToScreen(float4x4 m, float3 wNormal)
	{
		float3 sNormal = mul((float3x3)m, wNormal);
		return sNormal;
	}

	//
	//
	float3 worldPosFromDepth(float2 screenUV, float depth)
	{
		// calculate camera ray
		float2	uv = (screenUV - 0.5) * _CameraFOV;
		uv.x *= _CameraAspect;
		float3	ray = _CameraUp * uv.y + _CameraRight * uv.x + _CameraFront;

		// calculate near & far & ray.
		float3 	farPos		= _CameraPosition + ray * _CameraFar;
		float3	nearPos		= _CameraPosition;
		float3	worldPos	= lerp(nearPos, farPos, depth);

		return worldPos;
	}

    // There are also a lot of utility function you can use inside Common.hlsl and Color.hlsl,
    // you can check them out in the source code of the core SRP package.
    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
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

		//UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);


		// screen depth & normal.
		float	depth			= LoadCameraDepth(varyings.positionCS.xy);
		if (depth == 0)
			return 0;

		PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
		
		float	linearDepth		= posInput.linearDepth / _ProjectionParams.z;
		float3	worldPosition	= getWorldPos(posInput.positionNDC.xy, linearDepth);

		NormalData normalData0;
		DecodeFromNormalBuffer(varyings.positionCS.xy, normalData0);

		float3	normal			= normalData0.normalWS;

		float2	screenUV		= posInput.positionNDC.xy;// * _RTHandleScale.xy;

		float2 t = floor(screenUV * _Resolution);
		float2 m = t % _ProbeSize;
		float2 d = screenUV - m / _Resolution;


		// -------- if pixel lies in probe plane, otherwise choose other probe ------------- //
		float2	nTest				= d;
		float4  nDepthNormal		= tex2D(_DepthNormals, nTest);
		float3	nNormal				= nDepthNormal.xyz * 2.0f - 1.0f;
		float	dotVal				= dot(nNormal, normal);

		//if (_DebugNormals)
		////{
		//	return float4(nNormal * 0.5 + 0.5, 1);
		//}

		//if (dotVal < 0.95f)
		//	return 1;

		float2	worldSpacePixelSize		= PixelSizeInWorldSpace(nDepthNormal.w);
		//float	v						= dot(normal, float3(worldSpacePixelSize.x, 0, 0));
		//float	maxDepth				= nDepthNormal.w * v;


		//normal *= worldSpacePixelSize.y;
		float3	screenNormal	= worldToScreen(UNITY_MATRIX_V, normal);

		float3	tangent;
		float3	bitangent;

		float3	c1 = cross(screenNormal, float3(0.0, 0.0, 1.0));
		float3	c2 = cross(screenNormal, float3(0.0, 1.0, 0.0));

		if (length(c1) > length(c2))	tangent = c1;
		else							tangent = c2;

		tangent		= normalize(tangent);
		bitangent	= cross(screenNormal, tangent);
		bitangent	= normalize(bitangent);



		float	depthDiff		= nDepthNormal.w - linearDepth;

		float3	scaledNormal	= bitangent;
		if (abs(tangent.z) > abs(bitangent.z))
			scaledNormal = tangent;

		scaledNormal *= worldSpacePixelSize.y;

		//return abs(scaledNormal.z * 100);

		//return scaledNormal.z;

		float	biggestDist		= 10000;// abs(depthDiff);
		float2	selectedD		= d;

		bool uncertain = false;

		if (abs(depthDiff) >= abs(scaledNormal.z) || dotVal < 0.95f)
		{
			uncertain = true;

			[unroll]
			for (int i = 0; i < 9; i++)
			{
				float2	nTest = d + closestPixels[i] / _Resolution * _ProbeSize;

				float4  nDepthNormal	= tex2D(_DepthNormals, nTest);
				float3	nNormal			= nDepthNormal.xyz * 2.0f - 1.0f;
				float	dotVal			= dot(nNormal, normal);

				// if normal close enough, find closest distance.
				if (dotVal >= 0.95f)
				{
					//float3 probeWorldPos = worldPosFromDepth();

					float dist = abs(linearDepth - nDepthNormal.w);
					if (dist <= biggestDist)
					{
						biggestDist = dist;
						selectedD	= nTest;
					}
				}
			}
		}


		/*for (int i = 0; i < 9; i++)
		{
			float2	nTest			= d + closestPixels[i] / _Resolution * _ProbeSize;

			float4  nDepthNormal	= tex2D(_DepthNormals, nTest);
			float3	nNormal			= nDepthNormal.xyz * 2.0f - 1.0f;
			//float	dotVal			= dot(nNormal, normal);

			// if normal close enough, find closest distance.
			//if (dotVal >= 0.95f)
			//{
				float3 probeWorldPos = worldPosFromDepth(nTest, linearDepth);
				float3 pixelWorldPos = worldPosFromDepth(screenUV, linearDepth);

				//float dist = abs(linearDepth - nDepthNormal.w);

				float dist = length(probeWorldPos - pixelWorldPos);

				if (dist < biggestDist)
				{
					uncertain = true;
					biggestDist = dist;
					selectedD = nTest;
				}
			//}
		}*/

		

		//if (uncertain)
		//	return 1;

		d = selectedD;


		// -------- sample diffuse harmonics ------------- //

		SH9Color sh9Color;
		sh9Color.sh0 = tex3D(_SHAtlas, float3(d, 0 / 8.0)).rgb * 2 - 1;
		sh9Color.sh1 = tex3D(_SHAtlas, float3(d, 1 / 8.0)).rgb * 2 - 1;
		sh9Color.sh2 = tex3D(_SHAtlas, float3(d, 2 / 8.0)).rgb * 2 - 1;
		sh9Color.sh3 = tex3D(_SHAtlas, float3(d, 3 / 8.0)).rgb * 2 - 1;
		sh9Color.sh4 = tex3D(_SHAtlas, float3(d, 4 / 8.0)).rgb * 2 - 1;
		sh9Color.sh5 = tex3D(_SHAtlas, float3(d, 5 / 8.0)).rgb * 2 - 1;
		sh9Color.sh6 = tex3D(_SHAtlas, float3(d, 6 / 8.0)).rgb * 2 - 1;
		sh9Color.sh7 = tex3D(_SHAtlas, float3(d, 7 / 8.0)).rgb * 2 - 1;
		sh9Color.sh8 = tex3D(_SHAtlas, float3(d, 8 / 8.0)).rgb * 2 - 1;


		// sample 4 directions to cover BRDF using SH9.
		// removes tiling artifacts and blends probes better.
		/*float3 tangent;
		float3 bitangent;

		float3 c1 = cross(normal, float3(0.0, 0.0, 1.0));
		float3 c2 = cross(normal, float3(0.0, 1.0, 0.0));

		if (length(c1) > length(c2))	tangent = c1;
		else							tangent = c2;

		tangent = normalize(tangent);
		bitangent = cross(normal, tangent);
		bitangent = normalize(bitangent);

		const float angle		= 0.5f;
		const float invAngle	= 1.0f - angle;

		float3 n0 = normalize(normal * invAngle + tangent * angle + bitangent * angle);
		float3 n1 = normalize(normal * invAngle - tangent * angle + bitangent * angle);
		float3 n2 = normalize(normal * invAngle - tangent * angle - bitangent * angle);
		float3 n3 = normalize(normal * invAngle + tangent * angle - bitangent * angle);

		float3	radiance0	= calcIrradiance(normal, sh9Color);
		float3	radiance1	= calcIrradiance(n0, sh9Color);
		float3	radiance2	= calcIrradiance(n1, sh9Color);
		float3	radiance3	= calcIrradiance(n2, sh9Color);
		float3	radiance4	= calcIrradiance(n3, sh9Color);

		float3	radiance		= (radiance0 + radiance1 + radiance2 + radiance3 + radiance4) * 0.2f;
		float3	radianceFinal	= min(1, radiance * _Multiplier);
		float3	SSProbes		= tex2D(_SSProbesEncoded, screenUV).rgb;

		return float4(lerp(SSProbes, radianceFinal, _HarmonicsBlend), 1);*/
		////////

		float3	radiance	= calcIrradiance(normal, sh9Color) * _Multiplier;
		float3	SSProbes	= tex2D(_SSProbesEncoded, screenUV).rgb;
		return float4(lerp(SSProbes, radiance, _HarmonicsBlend), 1);
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