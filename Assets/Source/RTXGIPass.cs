// https://gpuopen.com/download/publications/GPUOpen2022_GI1_0.pdf
// https://github.com/EpicGames/UnrealEngine/blob/release/Engine/Shaders/Private/Lumen/LumenScreenProbeGather.usf

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using System.Collections.Generic;

namespace Avol.RTXGI
{
	class RTXGIPass : CustomPass
	{
		[Header("Settings")]
		public bool ExecuteInSceneView = true;

		public int ProbeSize = 8;

		public int Upscale = 1;

		[Range(0.0f, 0.5f)]
		public float _RayConeAngle = 0;


		[Range(1, 16)]
		public int RayCount = 32;

		[Range(0.0f, 10000f)]
		public float Exposure = 1;

		[Range(0.0f, 1.0f)]
		public float TemporalWeight = 0.5f;


		public float NeighbourBlendDistance = 1;

		[Range(0.0f, 1.0f)]
		public float HarmonicsBlend = 1;

		public bool	 DebugNormals	= false;


		[Header("Debug")]
		public	RenderTexture	_SSProbes;
		public	RenderTexture	_SSProbesTemporal;
		public	RenderTexture	_SSProbesEncoded;
		public	RenderTexture	_SHAtlas;

		public bool DebugProbesColor;

		private UnityEngine.Rendering.RayTracingAccelerationStructure	_RTStructure;
		
		private Shader													_ComposeShader;
		private Material												_ComposeMaterial;

		private UnityEngine.Rendering.RayTracingShader					_SSProbesShader;

		private ComputeShader											_SSEncodingShader;
		private ComputeShader											_SSTemporalShader;

		public int Frame;


		protected override bool executeInSceneView => ExecuteInSceneView;

		private void InitRaytracingAccelerationStructure()
		{
			UnityEngine.Rendering.RayTracingAccelerationStructure.Settings settings = new UnityEngine.Rendering.RayTracingAccelerationStructure.Settings();
			// include all layers
			settings.layerMask = ~0;
			// enable automatic updates
			settings.managementMode = UnityEngine.Rendering.RayTracingAccelerationStructure.ManagementMode.Automatic;
			// include all renderer types
			settings.rayTracingModeMask = UnityEngine.Rendering.RayTracingAccelerationStructure.RayTracingModeMask.Everything;

			_RTStructure = new UnityEngine.Rendering.RayTracingAccelerationStructure(settings);

			// collect all objects in scene and add them to raytracing scene
			Renderer[] renderers = GameObject.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			foreach (Renderer r in renderers)
				_RTStructure.AddInstance(r, new RayTracingSubMeshFlags[] { RayTracingSubMeshFlags.UniqueAnyHitCalls });

			// build raytracing scene
			_RTStructure.Build();
		}

		// It can be used to configure render targets and their clear state. Also t o create temporary render target textures.
		// When empty this render pass will render to the active camera render target.
		// You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
		// The render pipeline will ensure target setup and clearing happens in an performance manner.
		protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
		{
			Debug.Log("Setup");

			name	= "HWRT Radiance Caching";

			clearFlags = ClearFlag.None;

			InitRaytracingAccelerationStructure();

			_SSProbes											= new RenderTexture(Screen.width * Upscale, Screen.height * Upscale, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbes.enableRandomWrite							= true;
			_SSProbes.filterMode								= FilterMode.Point;
			_SSProbes.Create();

			_SSProbesTemporal									= new RenderTexture(Screen.width * Upscale, Screen.height * Upscale, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesTemporal.enableRandomWrite					= true;
			_SSProbesTemporal.filterMode						= FilterMode.Point;
			_SSProbesTemporal.Create();

			// test thing.
			_SSProbesEncoded									= new RenderTexture(Screen.width / ProbeSize * Upscale, Screen.height / ProbeSize * Upscale, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesEncoded.enableRandomWrite					= true;
			_SSProbesEncoded.filterMode							= FilterMode.Point;
			_SSProbesEncoded.Create();

			_SHAtlas											= new RenderTexture(Screen.width / ProbeSize * Upscale, Screen.height / ProbeSize * Upscale, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Default);
			_SHAtlas.volumeDepth								= 9;
			_SHAtlas.dimension									= TextureDimension.Tex3D;
			_SHAtlas.enableRandomWrite							= true;
			_SHAtlas.filterMode									= FilterMode.Trilinear;
			_SHAtlas.wrapMode									= TextureWrapMode.Clamp;
			_SHAtlas.Create();


			_SSProbesShader = Resources.Load<UnityEngine.Rendering.RayTracingShader>("Core/SSProbes");
			_SSProbesShader.SetShaderPass("RTXIntersection");
			_SSProbesShader.SetAccelerationStructure("_RaytracingAccelerationStructure", _RTStructure);

			_SSTemporalShader = Resources.Load<ComputeShader>("Core/SSTemporal");
			_SSEncodingShader = Resources.Load<ComputeShader>("Core/SSEncoding");


			_ComposeShader = Resources.Load<Shader>("Compose");
			_ComposeMaterial = new Material(_ComposeShader);
		}

		protected override void Execute(CustomPassContext ctx)
		{
			Frame++;
			if (Frame >= 1.0f / TemporalWeight)
				Frame = 0;

			_SSProbesGatherPass(ctx);
			_SSProbesTemporalPass(ctx);
			_SSProbesEcodePass(ctx);
			_ComposePass(ctx);


			ctx.hdCamera.camera.transform.hasChanged = false;
		}

		protected override void Cleanup()
		{
			Debug.Log("Cleanup");

			_RTStructure.Release();

			_SSProbes.Release();
			_SSProbesTemporal.Release();
			_SSProbesEncoded.Release();
			_SHAtlas.Release();

			_SSProbes			= null;
			_SSProbesTemporal	= null;
			_SSProbesEncoded	= null;
			_SHAtlas			= null;

			CoreUtils.Destroy(_ComposeMaterial);
		}

		private void _SSProbesGatherPass(CustomPassContext ctx)
		{
			_SSProbesShader.SetTexture("_SSProbes", _SSProbes);

			_SSProbesShader.SetFloat("_Frame", Frame);

			_SSProbesShader.SetInt("_RayCount", RayCount);
			_SSProbesShader.SetInt("_ProbeSize", ProbeSize);
			_SSProbesShader.SetInt("_Upscale", Upscale);
			_SSProbesShader.SetFloat("_RayConeAngle", _RayConeAngle);

			ctx.cmd.DispatchRays(_SSProbesShader, "MyRaygenShader", (uint)_SSProbes.width, (uint)_SSProbes.height, 1, ctx.hdCamera.camera);
		}

		private void _SSProbesTemporalPass(CustomPassContext ctx)
		{
			_SSTemporalShader.SetTexture(0, "_SSProbes", _SSProbes);
			_SSTemporalShader.SetTexture(0, "_TemporalAccumulation", _SSProbesTemporal);
			_SSTemporalShader.SetInt("_ProbeSize", ProbeSize);
			_SSTemporalShader.SetFloat("_TemporalWeight", TemporalWeight);
			_SSTemporalShader.SetBool("_CameraMoved", ctx.hdCamera.camera.transform.hasChanged);
			_SSTemporalShader.SetInt("_Upscale", Upscale);
	
			ctx.cmd.DispatchCompute(_SSTemporalShader, 0, _SSProbesTemporal.width, _SSProbesTemporal.height, 1);
		}

		private void _SSProbesEcodePass(CustomPassContext ctx)
		{
			_SSEncodingShader.SetTexture(0, "_TestMergeAll", _SSProbesEncoded);
			_SSEncodingShader.SetTexture(0, "_SSProbes", _SSProbesTemporal);
			_SSEncodingShader.SetTexture(0, "_SHAtlas", _SHAtlas);
			_SSEncodingShader.SetVector("_Resolution", new Vector2(Screen.width, Screen.height));
			_SSEncodingShader.SetInt("_ProbeSize", ProbeSize);

			ctx.cmd.DispatchCompute(_SSEncodingShader, 0, _SHAtlas.width, _SHAtlas.height, 1);
		}

		private void _ComposePass(CustomPassContext ctx)
		{
			ctx.propertyBlock.SetInt("_Debug", DebugProbesColor ? 1 : 0);
			ctx.propertyBlock.SetFloat("_NeighbourBlendDistance", NeighbourBlendDistance);
			ctx.propertyBlock.SetVector("_Resolution", new Vector2(Screen.width, Screen.height) * Upscale);
			ctx.propertyBlock.SetTexture("_SSProbes", _SSProbes);
			ctx.propertyBlock.SetTexture("_SSProbesEncoded", _SSProbesEncoded);
			ctx.propertyBlock.SetTexture("_SHAtlas", _SHAtlas);
			ctx.propertyBlock.SetInt("_ProbeSize", ProbeSize);
			ctx.propertyBlock.SetFloat("_HarmonicsBlend", HarmonicsBlend);
			ctx.propertyBlock.SetFloat("_Exposure", Exposure);
			ctx.propertyBlock.SetFloat("_CameraPixelHeight", ctx.hdCamera.camera.pixelHeight);
			ctx.propertyBlock.SetFloat("_CameraPixelWidth", ctx.hdCamera.camera.pixelWidth);
			ctx.propertyBlock.SetInt("_DebugNormals", DebugNormals ? 1 : 0);


			ctx.propertyBlock.SetVector("_CameraPosition", ctx.hdCamera.camera.transform.position);
			ctx.propertyBlock.SetVector("_CameraUp", ctx.hdCamera.camera.transform.up);
			ctx.propertyBlock.SetVector("_CameraRight", ctx.hdCamera.camera.transform.right);
			ctx.propertyBlock.SetVector("_CameraFront", ctx.hdCamera.camera.transform.forward);
			ctx.propertyBlock.SetFloat("_CameraAspect", ctx.hdCamera.camera.aspect);
			ctx.propertyBlock.SetFloat("_CameraFOV", Mathf.Tan(ctx.hdCamera.camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * 2f);
			ctx.propertyBlock.SetFloat("_CameraNear", ctx.hdCamera.camera.nearClipPlane);
			ctx.propertyBlock.SetFloat("_CameraFar", ctx.hdCamera.camera.farClipPlane);
			//ctx.propertyBlock.SetTexture("_GBufferTexture0", Shader.GetGlobalTexture("_GBufferTexture0"));

			HDUtils.DrawFullScreen(ctx.cmd, _ComposeMaterial, ctx.cameraColorBuffer, ctx.propertyBlock, shaderPassId: DebugProbesColor ? 1 : 0);
		}
	}
}