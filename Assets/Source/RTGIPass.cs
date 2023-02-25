// https://gpuopen.com/download/publications/GPUOpen2022_GI1_0.pdf

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEditor.Build;
using System;

namespace Avol.RTGI
{
	class RTGIPass : CustomPass
	{
		[Header("Settings")]
		public bool ExecuteInSceneView = true;

		public int		ProbeSize		= 8;
		public int		Upscale			= 1;
		public float	MaxRayDistance	= 100;

		[Range(0.0f, 1.0f)]
		public float	BounceDistance	= 1;

		[Range(1, 16)]
		public int RayCount = 1;

		[Range(0.0f, 10000f)]
		public float Exposure = 1;

		[Range(0.0f, 1.0f)]
		public	float	TemporalWeight = 0.5f;

		[Header("Debug Filters")]
		public bool		TraceJitter							= true;
		public bool		TemporalFilter						= true;
		public bool		SpatialFilter						= true;
		public bool		ReuseTemporalAndSpatialHistory		= true;
		public bool		ImportanceSampling					= true;

		[Header("Debug Distance Plane Weighting")]
		public bool		DistancePlaneWeightingReprojection	= true;
		public bool		AngleWeightingReprojection			= true;
		public bool		DistancePlaneWeightingCompose		= true;

		[Header("Visualize Debug")]
		public bool		VisualizeDistancePlaneWeighting		= false;
		public bool		VisualizeSSProbes					= false;

		[Header("Debug Probes")]
		public	RenderTexture	_HistoryNormalDepth;

		public	RenderTexture	_SSProbesRadiance;
		public	RenderTexture	_SSProbesReprojected;
		public	RenderTexture	_SSProbesReprojected2;
		public	RenderTexture	_SSProbesLightingPDF;

		public	RenderTexture	_SSProbesSHAtlasR;
		public	RenderTexture	_SSProbesSHAtlasG;
		public	RenderTexture	_SSProbesSHAtlasB;

		public bool DebugProbesColor;

		private UnityEngine.Rendering.RayTracingAccelerationStructure	_RTStructure;
		
		private Shader													_ComposeShader;
		private Material												_ComposeMaterial;

		private UnityEngine.Rendering.RayTracingShader					_SSProbesShader;

		private ComputeShader											_SSEncodingShader;
		private ComputeShader											_SSTemporalShader;
		private ComputeShader											_SSImportanceSamplingShader;

		private Shader													_CopyNormalDepthShader;
		private Material												_CopyNormalDepthMaterial;

		public int Frame;

		private Matrix4x4												_HistoryIPVMatrix;

		private bool													_SwapReprojectedTexture;


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


			// create probe layout resolution in incremenets of ProbeSize.
			int x = Screen.width	% ProbeSize;
			int y = Screen.height	% ProbeSize;
			Vector2Int probeLayoutResolution = new Vector2Int(Screen.width + (x != 0 ? ProbeSize - x : 0),
															  Screen.height + (y != 0 ? ProbeSize - y : 0));

			_HistoryNormalDepth									= new RenderTexture(Screen.width, Screen.height, 1, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Default);
			_HistoryNormalDepth.enableRandomWrite				= true;
			_HistoryNormalDepth.filterMode						= FilterMode.Point;
			_HistoryNormalDepth.Create();

			_SSProbesRadiance									= new RenderTexture(probeLayoutResolution.x, probeLayoutResolution.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesRadiance.enableRandomWrite					= true;
			_SSProbesRadiance.filterMode						= FilterMode.Point;
			_SSProbesRadiance.Create();

			_SSProbesReprojected								= new RenderTexture(probeLayoutResolution.x, probeLayoutResolution.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesReprojected.enableRandomWrite				= true;
			_SSProbesReprojected.filterMode						= FilterMode.Point;
			_SSProbesReprojected.Create();

			_SSProbesReprojected2								= new RenderTexture(probeLayoutResolution.x, probeLayoutResolution.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesReprojected2.enableRandomWrite				= true;
			_SSProbesReprojected2.filterMode					= FilterMode.Point;
			_SSProbesReprojected2.Create();

			_SSProbesLightingPDF								= new RenderTexture(probeLayoutResolution.x, probeLayoutResolution.y, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default);
			_SSProbesLightingPDF.enableRandomWrite				= true;
			_SSProbesLightingPDF.filterMode						= FilterMode.Point;
			_SSProbesLightingPDF.Create();
			

			_SSProbesSHAtlasR									= new RenderTexture(probeLayoutResolution.x / ProbeSize, probeLayoutResolution.y / ProbeSize, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default);
			_SSProbesSHAtlasR.volumeDepth						= 9;
			_SSProbesSHAtlasR.dimension							= TextureDimension.Tex3D;
			_SSProbesSHAtlasR.enableRandomWrite					= true;
			_SSProbesSHAtlasR.filterMode						= FilterMode.Point;
			_SSProbesSHAtlasR.wrapMode							= TextureWrapMode.Clamp;
			_SSProbesSHAtlasR.Create();

			_SSProbesSHAtlasG									= new RenderTexture(probeLayoutResolution.x / ProbeSize, probeLayoutResolution.y / ProbeSize, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default);
			_SSProbesSHAtlasG.volumeDepth						= 9;
			_SSProbesSHAtlasG.dimension							= TextureDimension.Tex3D;
			_SSProbesSHAtlasG.enableRandomWrite					= true;
			_SSProbesSHAtlasG.filterMode						= FilterMode.Point;
			_SSProbesSHAtlasG.wrapMode							= TextureWrapMode.Clamp;
			_SSProbesSHAtlasG.Create();

			_SSProbesSHAtlasB									= new RenderTexture(probeLayoutResolution.x / ProbeSize, probeLayoutResolution.y / ProbeSize, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default);
			_SSProbesSHAtlasB.volumeDepth						= 9;
			_SSProbesSHAtlasB.dimension							= TextureDimension.Tex3D;
			_SSProbesSHAtlasB.enableRandomWrite					= true;
			_SSProbesSHAtlasB.filterMode						= FilterMode.Point;
			_SSProbesSHAtlasB.wrapMode							= TextureWrapMode.Clamp;
			_SSProbesSHAtlasB.Create();


			_SSProbesShader = Resources.Load<UnityEngine.Rendering.RayTracingShader>("Core/SSProbes");
			_SSProbesShader.SetShaderPass("RTXIntersection");
			_SSProbesShader.SetAccelerationStructure("_RaytracingAccelerationStructure", _RTStructure);

			_SSTemporalShader				= Resources.Load<ComputeShader>("Core/SSReprojection");
			_SSEncodingShader				= Resources.Load<ComputeShader>("Core/SSEncoding");
			_SSImportanceSamplingShader		= Resources.Load<ComputeShader>("Core/SSImportanceSampling");

			_ComposeShader				= Resources.Load<Shader>("Compose");
			_ComposeMaterial			= new Material(_ComposeShader);

			_CopyNormalDepthShader		= Resources.Load<Shader>("CopyNormalDepth");
			_CopyNormalDepthMaterial	= new Material(_CopyNormalDepthShader);
		}


		protected override void Execute(CustomPassContext ctx)
		{
			if (ImportanceSampling)
				_SSImportanceSampling(ctx);

			_SSProbesGatherPass(ctx);

			if (TemporalFilter)
				_SSProbesTemporalReprojectionPass(ctx);

			if (SpatialFilter)
				_SSProbesSpatialReprojectionPass(ctx);

			_SSProbesEcodePass(ctx);
			_ComposePass(ctx);

			_StoreHistoryNormalDepth(ctx);
			

			_HistoryIPVMatrix = (GL.GetGPUProjectionMatrix(ctx.hdCamera.camera.projectionMatrix, true) * ctx.hdCamera.camera.worldToCameraMatrix).inverse;

			if (TemporalFilter)
			{
				Frame++;
				if (Frame >= 1.0f / TemporalWeight)
					Frame = 0;
			}
			else
			{
				Frame = 0;
			}

			if (ReuseTemporalAndSpatialHistory)
				_SwapReprojectedTexture = !_SwapReprojectedTexture;
		}

		protected override void Cleanup()
		{
			Debug.Log("Cleanup");

			_RTStructure.Release();

			_HistoryNormalDepth.Release();

			_SSProbesRadiance.Release();
			_SSProbesReprojected.Release();
			_SSProbesReprojected2.Release();
			_SSProbesLightingPDF.Release();

			_SSProbesSHAtlasR.Release();
			_SSProbesSHAtlasG.Release();
			_SSProbesSHAtlasB.Release();

			_HistoryNormalDepth.DiscardContents();

			_SSProbesRadiance.DiscardContents();
			_SSProbesReprojected.DiscardContents();
			_SSProbesReprojected2.DiscardContents();
			_SSProbesLightingPDF.DiscardContents();

			_SSProbesSHAtlasR.DiscardContents();
			_SSProbesSHAtlasG.DiscardContents();
			_SSProbesSHAtlasB.DiscardContents();


			_HistoryNormalDepth		= null;

			_SSProbesRadiance		= null;
			_SSProbesReprojected	= null;
			_SSProbesReprojected2	= null;
			_SSProbesLightingPDF	= null;

			_SSProbesSHAtlasR	= null;
			_SSProbesSHAtlasG	= null;
			_SSProbesSHAtlasB	= null;

			CoreUtils.Destroy(_ComposeMaterial);
		}

		private void _StoreHistoryNormalDepth(CustomPassContext ctx)
		{
			CoreUtils.DrawFullScreen(ctx.cmd, _CopyNormalDepthMaterial, _HistoryNormalDepth, ctx.propertyBlock, shaderPassId: 0);
		}

		private void _SSProbesGatherPass(CustomPassContext ctx)
		{
			_SSProbesShader.SetTexture("_SSProbes", _SSProbesRadiance);
			_SSProbesShader.SetTexture("_SSProbesLightingPDF", _SSProbesLightingPDF);
			_SSProbesShader.SetBool("_ImportanceSampling", ImportanceSampling);
			
			_SSProbesShader.SetInt("_Frame", Frame);
			_SSProbesShader.SetInt("_RayCount", RayCount);
			_SSProbesShader.SetInt("_ProbeSize", ProbeSize);
			_SSProbesShader.SetInt("_Upscale", Upscale);
			_SSProbesShader.SetFloat("_RayConeAngle", 2.0f / ProbeSize);
			_SSProbesShader.SetFloat("_MaxRayDistance", MaxRayDistance);
			_SSProbesShader.SetBool("_TraceJitter", TraceJitter);
			_SSProbesShader.SetVector("_ScreenResolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));
			_SSProbesShader.SetFloat("_BounceDistance", BounceDistance);
			_SSProbesShader.SetFloat("_TemporalWeight", TemporalWeight);

			_SSProbesShader.Dispatch("MyRaygenShader", _SSProbesRadiance.width, _SSProbesRadiance.height, 1, ctx.hdCamera.camera);
			//ctx.cmd.DispatchRays(_SSProbesShader, "MyRaygenShader", (uint)_SSProbes.width, (uint)_SSProbes.height, 1, ctx.hdCamera.camera);
		}

		private void _SSProbesTemporalReprojectionPass(CustomPassContext ctx)
		{
			_SSTemporalShader.SetTexture(0, "_HistoryNormalDepth", _HistoryNormalDepth);
			_SSTemporalShader.SetTexture(0, "_SSProbes", _SSProbesRadiance);
			_SSTemporalShader.SetTexture(0, "_TemporalAccumulation", _SwapReprojectedTexture ? _SSProbesReprojected2 : _SSProbesReprojected);

			_SSTemporalShader.SetInt("_ProbeSize", ProbeSize);
			_SSTemporalShader.SetFloat("_TemporalWeight", TemporalWeight);
			_SSTemporalShader.SetInt("_Upscale", Upscale);
			_SSTemporalShader.SetFloat("_RayConeAngle", 2.0f / ProbeSize);

			ctx.cmd.SetComputeIntParam(_SSTemporalShader, "_ProbeSize", ProbeSize);
			ctx.cmd.SetComputeFloatParam(_SSTemporalShader, "_TemporalWeight", TemporalWeight);
			ctx.cmd.SetComputeIntParam(_SSTemporalShader, "_Upscale", Upscale);

			ctx.cmd.SetComputeFloatParam(_SSTemporalShader, "_MaxRayDistance", MaxRayDistance);
			ctx.cmd.SetComputeVectorParam(_SSTemporalShader, "_ProbeLayoutResolution", new Vector2(_SSProbesReprojected.width, _SSProbesReprojected.height));
			ctx.cmd.SetComputeVectorParam(_SSTemporalShader, "_ScreenResolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));
			ctx.cmd.SetComputeMatrixParam(_SSTemporalShader, "_HistoryIVPMatrix", _HistoryIPVMatrix);

			ctx.cmd.DispatchCompute(_SSTemporalShader, 0, _SSProbesReprojected.width, _SSProbesReprojected.height, 1);
		}

		private void _SSProbesSpatialReprojectionPass(CustomPassContext ctx)
		{
			_SSTemporalShader.SetTexture(1, "_TemporalAccumulation", TemporalFilter ? _SwapReprojectedTexture ? _SSProbesReprojected2 : _SSProbesReprojected : _SSProbesRadiance);
			_SSTemporalShader.SetTexture(1, "_SpatialAccumulation", _SwapReprojectedTexture ? _SSProbesReprojected : _SSProbesReprojected2);

			_SSTemporalShader.SetFloat("_MaxRayDistance", MaxRayDistance);
			_SSTemporalShader.SetInt("_ProbeSize", ProbeSize);
			_SSTemporalShader.SetFloat("_TemporalWeight", TemporalWeight);
			_SSTemporalShader.SetInt("_Upscale", Upscale);
			_SSTemporalShader.SetVector("_Resolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));

			_SSTemporalShader.SetInt("_DistancePlaneWeighting", DistancePlaneWeightingReprojection ? 1 : 0);
			_SSTemporalShader.SetInt("_AngleWeighting", AngleWeightingReprojection ? 1 : 0);

			ctx.cmd.DispatchCompute(_SSTemporalShader, 1, _SSProbesReprojected.width, _SSProbesReprojected.height, 1);
		}

		private void _SSProbesEcodePass(CustomPassContext ctx)
		{
			ctx.cmd.SetComputeTextureParam(_SSEncodingShader, 0, "_SSProbes", SpatialFilter ? _SwapReprojectedTexture ? _SSProbesReprojected : _SSProbesReprojected2 : TemporalFilter ? _SSProbesReprojected : _SSProbesRadiance);
			ctx.cmd.SetComputeTextureParam(_SSEncodingShader, 0, "_SHAtlasR", _SSProbesSHAtlasR);
			ctx.cmd.SetComputeTextureParam(_SSEncodingShader, 0, "_SHAtlasG", _SSProbesSHAtlasG);
			ctx.cmd.SetComputeTextureParam(_SSEncodingShader, 0, "_SHAtlasB", _SSProbesSHAtlasB);

			ctx.cmd.SetComputeIntParam(_SSEncodingShader, "_ProbeSize", ProbeSize);
			ctx.cmd.DispatchCompute(_SSEncodingShader, 0, _SSProbesSHAtlasR.width, _SSProbesSHAtlasR.height, 1);
		}

		private void _SSImportanceSampling(CustomPassContext ctx)
		{
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 0, "_SSProbes", SpatialFilter ? _SwapReprojectedTexture ? _SSProbesReprojected : _SSProbesReprojected2 : TemporalFilter ? _SSProbesReprojected : _SSProbesRadiance);
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 0, "_SSProbesLightingPDF", _SSProbesLightingPDF);
			ctx.cmd.SetComputeIntParam(_SSImportanceSamplingShader, "_ProbeSize", ProbeSize);
			ctx.cmd.DispatchCompute(_SSImportanceSamplingShader, 0, _SSProbesLightingPDF.width, _SSProbesLightingPDF.height, 1);
		}

		private void _ComposePass(CustomPassContext ctx)
		{
			ctx.propertyBlock.SetInt("_Debug", DebugProbesColor ? 1 : 0);

			ctx.propertyBlock.SetVector("_ProbeLayoutResolution", new Vector2(_SSProbesReprojected.width, _SSProbesReprojected.height));
			ctx.propertyBlock.SetVector("_ScreenResolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight) * Upscale);

			ctx.propertyBlock.SetTexture("_SSProbes", _SSProbesRadiance);

			ctx.propertyBlock.SetTexture("_SHAtlasR", _SSProbesSHAtlasR);
			ctx.propertyBlock.SetTexture("_SHAtlasG", _SSProbesSHAtlasG);
			ctx.propertyBlock.SetTexture("_SHAtlasB", _SSProbesSHAtlasB);

			ctx.propertyBlock.SetInt("_ProbeSize", ProbeSize);
			ctx.propertyBlock.SetFloat("_Exposure", Exposure);

			ctx.propertyBlock.SetInt("_DistancePlaneWeighting", DistancePlaneWeightingCompose ? 1 : 0);
			ctx.propertyBlock.SetInt("_VisualizeDistancePlaneWeighting", VisualizeDistancePlaneWeighting ? 1 : 0);
			ctx.propertyBlock.SetInt("_VisualizeProbes", VisualizeSSProbes ? 1 : 0);

			HDUtils.DrawFullScreen(ctx.cmd, _ComposeMaterial, ctx.cameraColorBuffer, ctx.propertyBlock, shaderPassId: DebugProbesColor ? 1 : 0);
		}
	}
}