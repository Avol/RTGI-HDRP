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
	uniform sampler3D _SHAtlas;

	TEXTURE2D_X(_GBufferTexture0);

	uniform				float3		_CameraPosition;
	uniform				float3		_CameraUp;
	uniform				float3		_CameraRight;
	uniform				float3		_CameraFront;
	uniform				float		_CameraAspect;
	uniform				float		_CameraFOV;
	uniform				float		_CameraNear;
	uniform				float		_CameraFar;

	uniform				float		_Exposure;

	uniform	float2		_Resolution;

	uniform int			_Debug;

	uniform				int			_ProbeSize;
	uniform				int			_DebugNormals;



	float2 PixelSizeInWorldSpace(float linearDepth)
	{
		float cameraDistance	= linearDepth * (_CameraFar - _CameraNear);
		float height			= _CameraFOV * cameraDistance;
		float width				= (height / _Resolution.y) * _Resolution.x;

		return float2(width, height) / float2(_Resolution.y, _Resolution.x);
	}

	/*float3 getWorldPos(float2 screenUV, float depth)
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

	*/

	// Transforms camera coordinate to world space position.
	// @ m = inverse projection view matrix of the camera.
	float3 worldToScreen(float4x4 m, float3 wNormal)
	{
		float3 sNormal = mul((float3x3)m, wNormal);
		return sNormal;
	}

	// Retrieves world space normal and linear depth.
	// @ positionCS = screen space UV coordinate.
	float4 getNormalDepth(float2 positionCS)
	{
		float4 normalDepth = 0;

		// load normal
		NormalData normalData;
		DecodeFromNormalBuffer(positionCS, normalData);
		normalDepth.xyz = normalData.normalWS;

		// load depth
		float			depth			= LoadCameraDepth(positionCS);
		PositionInputs	posInput		= GetPositionInput(positionCS, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
		normalDepth.w = posInput.linearDepth;

		return normalDepth;
	}

	// Clamps UV to smaller resolution.
	// @ positionCS = screen space UV coordinate.
	float2 clampCoordinate(float2 positionNDC, float2 resolution, int downscale)
	{
		float2	t = floor(positionNDC * resolution);
		float2	m = t % downscale;
		float2	d = positionNDC - m / resolution;
		return d;
	}

	/*
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
	}*/

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

    // There are also a lot of utility function you can use inside Common.hlsl and Color.hlsl,
    // you can check them out in the source code of the core SRP package.
    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
		// this pixel data.
		float2	positionCS			= varyings.positionCS.xy;
		float2	positionNDC			= positionCS / _Resolution;
		float4  pixelNormalDepth	= getNormalDepth(positionCS);

		if (pixelNormalDepth.w == 0)
			return 0;
		
		// this probe data.
		float2	probeNDC					= clampCoordinate(positionNDC, _Resolution, _ProbeSize);
		float4  probeNormalDepth			= getNormalDepth(probeNDC * _Resolution);
		float	probeToPixelNormalAngle		= dot(probeNormalDepth.xyz, pixelNormalDepth.xyz);



		// --------------- probe plane ----------------- //
		// TODO:	get corner depths
		//			get avarage & min max. median? if between median and avarage then pass it through?
		//			ignore something too far from avarage?
		/*float2	worldSpacePixelSize = PixelSizeInWorldSpace(probeNormalDepth.w);
		float3	probePlaneNormal			= pixelNormalDepth.xyz;

		probePlaneNormal *= worldSpacePixelSize.x;
		float3	screenNormal	= worldToScreen(UNITY_MATRIX_V, probePlaneNormal);

		float3	tangent;
		float3	bitangent;

		float3	c1 = cross(screenNormal, float3(0.0, 0.0, 1.0));
		float3	c2 = cross(screenNormal, float3(0.0, 1.0, 0.0));

		if (length(c1) > length(c2))	tangent = c1;
		else							tangent = c2;

		tangent		= normalize(tangent);
		bitangent	= cross(screenNormal.xyz, tangent);
		bitangent	= normalize(bitangent);

		float	depthDiff		= probeNormalDepth.w - pixelNormalDepth.w;

		float3	scaledNormal	= bitangent;
		if (abs(tangent.z) > abs(bitangent.z))
			scaledNormal = tangent;

		scaledNormal *= worldSpacePixelSize.x;
		bool	depthDiffers	= abs(depthDiff) >= abs(scaledNormal.z);
		//if (depthDiffers)
		//	return 1;

		// ------------- find better normal, if not too far ------------------
		float closestAngle		= probeToPixelNormalAngle;
		float closestDepth		= abs(depthDiff) - abs(scaledNormal.z);

		if (probeToPixelNormalAngle < 0.95f || depthDiffers)
		{
			[unroll]
			for (int i = 1; i < 9; i++)
			{
				float2	sideProbeNDC					= probeNDC + closestPixels[i] / _Resolution * _ProbeSize;
				float4	sideProbeNormalDepth			= getNormalDepth(sideProbeNDC * _Resolution);
				float	sideProbeToPixelNormalAngle		= dot(sideProbeNormalDepth.xyz, pixelNormalDepth.xyz);
				float	sideProbeDepthDiff				= abs(sideProbeNormalDepth.w - pixelNormalDepth.w);
				
				if (sideProbeToPixelNormalAngle > closestAngle ||
					(sideProbeToPixelNormalAngle == closestAngle && sideProbeDepthDiff < closestDepth))
				{
					closestAngle	= sideProbeToPixelNormalAngle;
					probeNDC		= sideProbeNDC;
					closestDepth	= sideProbeDepthDiff;
				}
			}
		}*/

		// -------- sample diffuse harmonics ------------- //

		SH9Color sh9Color;
		sh9Color.sh0 = tex3D(_SHAtlas, float3(positionNDC, 0 / 8.0)).rgb * 2 - 1;
		sh9Color.sh1 = tex3D(_SHAtlas, float3(positionNDC, 1 / 8.0)).rgb * 2 - 1;
		sh9Color.sh2 = tex3D(_SHAtlas, float3(positionNDC, 2 / 8.0)).rgb * 2 - 1;
		sh9Color.sh3 = tex3D(_SHAtlas, float3(positionNDC, 3 / 8.0)).rgb * 2 - 1;
		sh9Color.sh4 = tex3D(_SHAtlas, float3(positionNDC, 4 / 8.0)).rgb * 2 - 1;
		sh9Color.sh5 = tex3D(_SHAtlas, float3(positionNDC, 5 / 8.0)).rgb * 2 - 1;
		sh9Color.sh6 = tex3D(_SHAtlas, float3(positionNDC, 6 / 8.0)).rgb * 2 - 1;
		sh9Color.sh7 = tex3D(_SHAtlas, float3(positionNDC, 7 / 8.0)).rgb * 2 - 1;
		sh9Color.sh8 = tex3D(_SHAtlas, float3(positionNDC, 8 / 8.0)).rgb * 2 - 1;


		float4 gBufferAlbedo = LOAD_TEXTURE2D_X(_GBufferTexture0, positionCS);
		gBufferAlbedo = max(0.01, gBufferAlbedo);

		float3 exposure = /*GetCurrentExposureMultiplier() * */_Exposure;

		float3 radiance = calcIrradiance(pixelNormalDepth.xyz, sh9Color) * gBufferAlbedo.rgb * exposure;
		return float4(radiance, 1);

		// sample 4 directions to cover BRDF using SH9.
		// removes tiling artifacts and blends probes better.
		/* {
			float3 tangent; 
			float3 bitangent;

			float3 c1 = cross(pixelNormalDepth.xyz, float3(0.0, 0.0, 1.0));
			float3 c2 = cross(pixelNormalDepth.xyz, float3(0.0, 1.0, 0.0));

			if (length(c1) > length(c2))	tangent = c1;
			else							tangent = c2;

			tangent = normalize(tangent);
			bitangent = cross(pixelNormalDepth.xyz, tangent);
			bitangent = normalize(bitangent);

			const float angle		= 0.5f;
			const float invAngle	= 1.0f - angle;

			float3 n0 = normalize(pixelNormalDepth.xyz * invAngle + tangent * angle + bitangent * angle);
			float3 n1 = normalize(pixelNormalDepth.xyz * invAngle - tangent * angle + bitangent * angle);
			float3 n2 = normalize(pixelNormalDepth.xyz * invAngle - tangent * angle - bitangent * angle);
			float3 n3 = normalize(pixelNormalDepth.xyz * invAngle + tangent * angle - bitangent * angle);

			float3	radiance0	= calcIrradiance(pixelNormalDepth.xyz, sh9Color);
			float3	radiance1	= calcIrradiance(n0, sh9Color);
			float3	radiance2	= calcIrradiance(n1, sh9Color);
			float3	radiance3	= calcIrradiance(n2, sh9Color);
			float3	radiance4	= calcIrradiance(n3, sh9Color);

			float3	radiance		= (radiance0 + radiance1 + radiance2 + radiance3 + radiance4) * 0.2f;
			float3	radianceFinal	= min(1, radiance * _Multiplier);
			float3	SSProbes		= tex2D(_SSProbesEncoded, positionNDC).rgb;

			return float4(SSProbes, 1);
		}*/
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