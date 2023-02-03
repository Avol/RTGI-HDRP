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
		public int ProbeSize = 8;

		public int Upscale = 1;

		[Range(0.0f, 0.5f)]
		public float _RayConeAngle = 0;


		[Range(1, 16)]
		public int RayCount = 32;

		[Range(0.0f, 16.0f)]
		public float Multiplier = 1;

		[Range(0.0f, 1.0f)]
		public float TemporalWeight = 0.5f;

		[Range(0.0f, 0.001f)]
		public float DeltaOffset = 0.01f;



		public float NeighbourBlendDistance = 1;

		[Range(0.0f, 1.0f)]
		public float HarmonicsBlend = 1;

		public bool	 DebugNormals	= false;

		public Light DirLight;

		[Header("Debug")]
		public	RenderTexture	_ProbesPlacementDepthNormal;
		public	RenderTexture	_SSProbes;
		public	RenderTexture	_SSProbesTemporal;
		public	RenderTexture	_SSProbesEncoded;
		public	RenderTexture	_SHAtlas;

		public bool DebugProbesColor;

		private UnityEngine.Rendering.RayTracingAccelerationStructure	_RTStructure;
		
		private Shader													_ProbePlacementShader;
		private Material												_ProbePlacementMaterial;

		private Shader													_ComposeShader;
		private Material												_ComposeMaterial;

		private UnityEngine.Rendering.RayTracingShader					_SSProbesShader;

		private ComputeShader											_SSEncodingShader;
		private ComputeShader											_SSTemporalShader;

		public int Frame;


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
			if (!Application.isPlaying)
				return;

			name	= "HWRT Radiance Caching";

			clearFlags = ClearFlag.None;

			InitRaytracingAccelerationStructure();



			_ProbesPlacementDepthNormal							= new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_ProbesPlacementDepthNormal.enableRandomWrite		= true;
			_ProbesPlacementDepthNormal.filterMode				= FilterMode.Point;
			_ProbesPlacementDepthNormal.Create();

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

			_SHAtlas											= new RenderTexture(Screen.width / ProbeSize * Upscale, Screen.height / ProbeSize * Upscale, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SHAtlas.volumeDepth								= 9;
			_SHAtlas.dimension									= TextureDimension.Tex3D;
			_SHAtlas.enableRandomWrite							= true;
			_SHAtlas.filterMode									= FilterMode.Point;
			_SHAtlas.Create();


			_ProbePlacementShader		= Resources.Load<Shader>("ProbePlacement");
			_ProbePlacementMaterial		= new Material(_ProbePlacementShader);

			_SSProbesShader = Resources.Load<UnityEngine.Rendering.RayTracingShader>("Core/SSProbes");
			_SSProbesShader.SetShaderPass("RTXIntersection");
			_SSProbesShader.SetAccelerationStructure("_RaytracingAccelerationStructure", _RTStructure);

			_SSTemporalShader = Resources.Load<ComputeShader>("Core/SSTemporal");
			_SSTemporalShader.SetTexture(0, "_SSProbes", _SSProbes);
			_SSTemporalShader.SetTexture(0, "_TemporalAccumulation", _SSProbesTemporal);

			_SSEncodingShader = Resources.Load<ComputeShader>("Core/SSEncoding");
			_SSEncodingShader.SetTexture(0, "_TestMergeAll", _SSProbesEncoded);
			_SSEncodingShader.SetTexture(0, "_SSProbes", _SSProbesTemporal);
			_SSEncodingShader.SetTexture(0, "_SHAtlas", _SHAtlas);

			_ComposeShader = Resources.Load<Shader>("Compose");
			_ComposeMaterial = new Material(_ComposeShader);
		}

		protected override void Execute(CustomPassContext ctx)
		{
			if (!Application.isPlaying)
				return;

			Frame++;
			if (Frame >= 1.0f / TemporalWeight)
				Frame = 0;

			_ExtractDepthNormals(ctx);

			_SSProbesGatherPass(ctx);
			_SSProbesTemporalPass(ctx);
			_SSProbesSHPass(ctx);
			_ComposePass(ctx);

			//int dirLightCount = Shader.GetGlobalInt("_DirectionalShadowIndex");
			//Debug.Log(dirLightCount);

			Camera.main.transform.hasChanged = false;
		}

		protected override void Cleanup()
		{
			if (!Application.isPlaying)
				return;

			_RTStructure.Release();

			_ProbesPlacementDepthNormal.Release();
			_SSProbes.Release();
			_SSProbesTemporal.Release();
			_SSProbesEncoded.Release();
			_SHAtlas.Release();
		}

		private void _ExtractDepthNormals(CustomPassContext ctx)
		{
			CoreUtils.SetRenderTarget(ctx.cmd, _ProbesPlacementDepthNormal, ClearFlag.None);
			CoreUtils.DrawFullScreen(ctx.cmd, _ProbePlacementMaterial, ctx.propertyBlock, shaderPassId: 0);
		}

		private void _SSProbesGatherPass(CustomPassContext ctx)
		{
			_SSProbesShader.SetTexture("_ProbesDepthNormal", _ProbesPlacementDepthNormal);
			_SSProbesShader.SetTexture("_SSProbes", _SSProbes);
			//_SSProbesShader.SetVector("_CameraPosition", Camera.main.transform.position);

			_SSProbesShader.SetVector("_CameraUp", Camera.main.transform.up);
			_SSProbesShader.SetVector("_CameraRight", Camera.main.transform.right);
			_SSProbesShader.SetVector("_CameraFront", Camera.main.transform.forward);
			_SSProbesShader.SetFloat("_CameraAspect", Camera.main.aspect);
			_SSProbesShader.SetFloat("_CameraFOV", Mathf.Tan(Camera.main.fieldOfView * Mathf.Deg2Rad * 0.5f) * 2f);
			_SSProbesShader.SetFloat("_CameraNear", Camera.main.nearClipPlane);
			_SSProbesShader.SetFloat("_CameraFar", Camera.main.farClipPlane);

			Matrix4x4 m = Camera.main.transform.localToWorldMatrix;

			Debug.Log("Right: " + Camera.main.transform.right + " Up: " + Camera.main.transform.up + " Forward: " + Camera.main.transform.forward);
			Debug.Log(m.GetColumn(0));

			_SSProbesShader.SetFloat("_Frame", Frame);
			_SSProbesShader.SetFloat("_DeltaOffset", DeltaOffset);

			_SSProbesShader.SetInt("_RayCount", RayCount);
			_SSProbesShader.SetInt("_ProbeSize", ProbeSize);
			_SSProbesShader.SetInt("_Upscale", Upscale);
			_SSProbesShader.SetFloat("_RayConeAngle", _RayConeAngle);
			_SSProbesShader.Dispatch("MyRaygenShader", _SSProbes.width, _SSProbes.height, 1, ctx.hdCamera.camera);

			//ctx.cmd.SetComputeBufferParam(data.contactShadowsCS, data.kernel, HDShaderIDs._DirectionalLightDatas, data.lightLoopLightData.directionalLightData);
		}

		private void _SSProbesTemporalPass(CustomPassContext ctx)
		{
			_SSTemporalShader.SetTexture(0, "_ProbesDepthNormal", _ProbesPlacementDepthNormal);

			_SSTemporalShader.SetVector("_CameraPosition", Camera.main.transform.position);
			_SSTemporalShader.SetVector("_CameraUp", Camera.main.transform.up);
			_SSTemporalShader.SetVector("_CameraRight", Camera.main.transform.right);
			_SSTemporalShader.SetVector("_CameraFront", Camera.main.transform.forward);
			_SSTemporalShader.SetFloat("_CameraAspect", Camera.main.aspect);
			_SSTemporalShader.SetFloat("_CameraFOV", Mathf.Tan(Camera.main.fieldOfView * Mathf.Deg2Rad * 0.5f) * 2f);
			_SSTemporalShader.SetFloat("_CameraNear", Camera.main.nearClipPlane);
			_SSTemporalShader.SetFloat("_CameraFar", Camera.main.farClipPlane);

			_SSTemporalShader.SetInt("_ProbeSize", ProbeSize);
			_SSTemporalShader.SetFloat("_TemporalWeight", TemporalWeight);
			_SSTemporalShader.SetBool("_CameraMoved", Camera.main.transform.hasChanged);
			_SSTemporalShader.SetInt("_Upscale", Upscale);
			_SSTemporalShader.Dispatch(0, _SSProbesTemporal.width, _SSProbesTemporal.height, 1);
		}

		private void _SSProbesSHPass(CustomPassContext ctx)
		{
			_SSEncodingShader.SetVector("_Resolution", new Vector2(Screen.width, Screen.height));
			_SSEncodingShader.SetInt("_ProbeSize", ProbeSize);
			_SSEncodingShader.Dispatch(0, _SHAtlas.width, _SHAtlas.height, 1);
		}

		private void _ComposePass(CustomPassContext ctx)
		{
			ctx.propertyBlock.SetVector("_CameraPosition", Camera.main.transform.position);
			ctx.propertyBlock.SetVector("_CameraUp", Camera.main.transform.up);
			ctx.propertyBlock.SetVector("_CameraRight", Camera.main.transform.right);
			ctx.propertyBlock.SetVector("_CameraFront", Camera.main.transform.forward);
			ctx.propertyBlock.SetFloat("_CameraAspect", Camera.main.aspect);
			ctx.propertyBlock.SetFloat("_CameraFOV", Mathf.Tan(Camera.main.fieldOfView * Mathf.Deg2Rad * 0.5f) * 2f);
			ctx.propertyBlock.SetFloat("_CameraNear", Camera.main.nearClipPlane);
			ctx.propertyBlock.SetFloat("_CameraFar", Camera.main.farClipPlane);

			ctx.propertyBlock.SetInt("_Debug", DebugProbesColor ? 1 : 0);
			ctx.propertyBlock.SetFloat("_NeighbourBlendDistance", NeighbourBlendDistance);
			ctx.propertyBlock.SetVector("_Resolution", new Vector2(Screen.width, Screen.height) * Upscale);
			ctx.propertyBlock.SetTexture("_DepthNormals", _ProbesPlacementDepthNormal);
			ctx.propertyBlock.SetTexture("_SSProbes", _SSProbes);
			ctx.propertyBlock.SetTexture("_SSProbesEncoded", _SSProbesEncoded);
			ctx.propertyBlock.SetTexture("_SHAtlas", _SHAtlas);
			ctx.propertyBlock.SetInt("_ProbeSize", ProbeSize);
			ctx.propertyBlock.SetFloat("_HarmonicsBlend", HarmonicsBlend);
			ctx.propertyBlock.SetFloat("_Multiplier", Multiplier);
			ctx.propertyBlock.SetFloat("_CameraPixelHeight", Camera.main.pixelHeight);
			ctx.propertyBlock.SetFloat("_CameraPixelWidth", Camera.main.pixelWidth);
			ctx.propertyBlock.SetInt("_DebugNormals", DebugNormals ? 1 : 0);

			CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
			CoreUtils.DrawFullScreen(ctx.cmd, _ComposeMaterial, ctx.propertyBlock, DebugProbesColor ? 1 : 0);
		}
	}
}