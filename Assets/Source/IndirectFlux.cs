using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Avol.IndirectFlux
{
	public class IndirectFlux : CustomPass
	{
		[Header("Settings")]
		public bool		ExecuteInSceneView		= false;
		protected override bool executeInSceneView => ExecuteInSceneView;


		public int		Upscale					= 1;
		public float	MaxRayDistance			= 100;

		[Range(0.0f, 1.0f)]
		public float	BounceDistance			= 1;

		[Range(1, 16)]
		public int		RayCount				= 1;

		[Range(0.0f, 10000f)]
		public float	Exposure				= 10;

		[Range(0.0f, 1.0f)]
		public	float	TemporalWeight			= 0.25f;

		[Header("Debug Filters")]
		public bool		TraceJitter							= true;
		public bool		TemporalFilter						= true;
		public bool		SpatialFilter						= true;
		public bool		EnableImportanceSampling			= true;

		[Header("Debug Features")]
		public bool		DiffuseGI							= false;
		public bool		Reflections							= false;
		public bool		UseShadowMaps						= false;

		[Header("Debug Distance Plane Weighting")]
		public bool		DistancePlaneWeightingReprojection	= true;
		public bool		AngleWeightingReprojection			= true;
		public bool		DistancePlaneWeightingCompose		= true;
		public bool		TrilinearOffset						= true;
		public bool		TrilinearOffset2					= true;
		public bool		NormalWeight						= true;

		[Header("Visualize Debug")]
		public bool		VisualizeDistancePlaneWeighting		= false;
		public bool		VisualizeSSProbes					= false;
		public bool		VisualizeSurfaceBRDF				= false;
		public bool		VisualizeLightningPDF				= false;
		public bool		VisualizeWorldProbes				= false;

		

		[Header("Debug Probes")]
		public	RenderTexture	_HistoryNormalDepth;

		public	RenderTexture	_SSProbesRadiance;
		public	RenderTexture	_SSProbesRadianceUnpacked;

		public	RenderTexture	_SSProbesReprojected;
		public	RenderTexture	_SSProbesReprojected2;

		public	RenderTexture	_WSProbesAtlas;
		public	RenderTexture	_WSProbesRadiance;


		public bool TestTracingLanes;

		public bool DebugProbesColor;

		private Shader													_ComposeShader;
		private Material												_ComposeMaterial;


		private Shader													_CopyNormalDepthShader;
		private Material												_CopyNormalDepthMaterial;


		public int _Frame;


		[HideInInspector]	public int		SSProbeSize		= 8;
		[HideInInspector]	public int		WSProbeSize		= 64;

		public		Vector2Int		SSProbeLayoutResolution		{ private set; get; }
		public		Vector2Int		WSProbeLayoutResolution		{ private set; get; }

		public		HWRT					HWRT;
		public		Filtering				Filtering;
		public		ImportanceSampling		ImportanceSampling;
		public		Encoding				Encoding;



		// It can be used to configure render targets and their clear state. Also t o create temporary render target textures.
		// When empty this render pass will render to the active camera render target.
		// You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
		// The render pipeline will ensure target setup and clearing happens in an performance manner.
		protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
		{
			name		= "Indirect Flux";

			clearFlags	= ClearFlag.None;

			// create probe layout resolution in incremenets of ProbeSize.
			{
				int x = Screen.width	% SSProbeSize;
				int y = Screen.height	% SSProbeSize;
				SSProbeLayoutResolution	= new Vector2Int(Screen.width + (x != 0 ? SSProbeSize - x : 0),
														 Screen.height + (y != 0 ? SSProbeSize - y : 0));
			}

			{
				int x = Screen.width	% WSProbeSize;
				int y = Screen.height	% WSProbeSize;
				WSProbeLayoutResolution	= new Vector2Int(Screen.width + (x != 0 ? SSProbeSize - x : 0),
														 Screen.height + (y != 0 ? SSProbeSize - y : 0));
			}

			_HistoryNormalDepth									= new RenderTexture(Screen.width, Screen.height, 1, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_HistoryNormalDepth.enableRandomWrite				= true;
			_HistoryNormalDepth.filterMode						= FilterMode.Point;
			_HistoryNormalDepth.anisoLevel						= 0;
			_HistoryNormalDepth.Create();

			_SSProbesRadiance									= new RenderTexture(SSProbeLayoutResolution.x, SSProbeLayoutResolution.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesRadiance.enableRandomWrite					= true;
			_SSProbesRadiance.filterMode						= FilterMode.Bilinear;
			_SSProbesRadiance.anisoLevel						= 0;
			_SSProbesRadiance.Create();

			_SSProbesRadianceUnpacked							= new RenderTexture(SSProbeLayoutResolution.x, SSProbeLayoutResolution.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesRadianceUnpacked.enableRandomWrite			= true;
			_SSProbesRadianceUnpacked.filterMode				= FilterMode.Bilinear;
			_SSProbesRadianceUnpacked.anisoLevel				= 0;
			_SSProbesRadianceUnpacked.Create();

			_SSProbesReprojected								= new RenderTexture(SSProbeLayoutResolution.x, SSProbeLayoutResolution.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesReprojected.enableRandomWrite				= true;
			_SSProbesReprojected.filterMode						= FilterMode.Point;
			_SSProbesReprojected.anisoLevel						= 0;
			_SSProbesReprojected.Create();

			_SSProbesReprojected2								= new RenderTexture(SSProbeLayoutResolution.x, SSProbeLayoutResolution.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_SSProbesReprojected2.enableRandomWrite				= true;
			_SSProbesReprojected2.filterMode					= FilterMode.Point;
			_SSProbesReprojected2.anisoLevel					= 0;
			_SSProbesReprojected2.Create();


			_WSProbesAtlas										= new RenderTexture(WSProbeLayoutResolution.x / WSProbeSize, WSProbeLayoutResolution.y / WSProbeSize, 0, RenderTextureFormat.R16, RenderTextureReadWrite.Default);
			_WSProbesAtlas.enableRandomWrite					= true;
			_WSProbesAtlas.filterMode							= FilterMode.Point;
			_WSProbesAtlas.anisoLevel							= 0;
			_WSProbesAtlas.Create();

			_WSProbesRadiance									= new RenderTexture(WSProbeLayoutResolution.x, WSProbeLayoutResolution.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
			_WSProbesRadiance.enableRandomWrite					= true;
			_WSProbesRadiance.filterMode						= FilterMode.Point;
			_WSProbesRadiance.anisoLevel						= 0;
			_WSProbesRadiance.useMipMap							= true;
			_WSProbesRadiance.autoGenerateMips					= false;
			_WSProbesRadiance.Create();


			_ComposeShader				= Resources.Load<Shader>("Compose");
			_ComposeMaterial			= new Material(_ComposeShader);

			_CopyNormalDepthShader		= Resources.Load<Shader>("CopyNormalDepth");
			_CopyNormalDepthMaterial	= new Material(_CopyNormalDepthShader);

			HWRT		= new HWRT(this);
			HWRT.Setup();

			Filtering	= new Filtering(this);	
			Filtering.Setup();

			ImportanceSampling = new ImportanceSampling(this);
			ImportanceSampling.Setup();

			Encoding	= new Encoding(this);
			Encoding.Setup();
		}

		protected override void Execute(CustomPassContext ctx)
		{
			HWRT.WSProbes(ctx);
			
			if (TemporalFilter)
			{
				_Frame++;
				if (_Frame >= 1.0f / TemporalWeight)
					_Frame = 0;
			}
			else
			{
				_Frame = 0;
			}

			if (EnableImportanceSampling)
			{
				ImportanceSampling.SSImportanceSamplingLightningPDF(ctx, TemporalFilter ? _SSProbesReprojected : _SSProbesRadianceUnpacked);
				ImportanceSampling.SSImportanceSamplingSurfaceBRDF(ctx, SpatialFilter ? _SSProbesReprojected2 : TemporalFilter ? _SSProbesReprojected : _SSProbesRadiance);
				ImportanceSampling.SSImportanceSamplingPackRays(ctx);
			}

			HWRT.SSProbesGatherPass(ctx);

			if (EnableImportanceSampling)
				ImportanceSampling.SSImportanceSamplingUnpackRadiance(ctx);

			if (TemporalFilter)
			{
				Filtering.SSProbesTemporalReprojectionPass(ctx, _HistoryNormalDepth,
																EnableImportanceSampling ? _SSProbesRadianceUnpacked : _SSProbesRadiance,
																_SSProbesReprojected);
			}

			if (SpatialFilter)
			{
				Filtering.SSProbesSpatialReprojectionPass(ctx,
															TemporalFilter ? _SSProbesReprojected : _SSProbesRadiance,
															_SSProbesReprojected2);
			}

			Encoding.SSProbesEcodePass(ctx, 
				SpatialFilter ? _SSProbesReprojected2 : TemporalFilter ? _SSProbesReprojected : EnableImportanceSampling ? _SSProbesRadianceUnpacked : _SSProbesRadiance);


			_ComposePass(ctx);

			_StoreHistoryNormalDepth(ctx);
		}

		protected override void Cleanup()
		{
			_HistoryNormalDepth.Release();

			_SSProbesRadiance.Release();
			_SSProbesRadianceUnpacked.Release();
			_SSProbesReprojected.Release();
			_SSProbesReprojected2.Release();

			_WSProbesAtlas.Release();
			_WSProbesRadiance.Release();


			_HistoryNormalDepth.DiscardContents();

			_SSProbesRadiance.DiscardContents();
			_SSProbesRadianceUnpacked.DiscardContents();
			_SSProbesReprojected.DiscardContents();
			_SSProbesReprojected2.DiscardContents();

			_WSProbesAtlas.DiscardContents();
			_WSProbesRadiance.DiscardContents();

			_HistoryNormalDepth			= null;

			_SSProbesRadiance			= null;
			_SSProbesRadianceUnpacked	= null;
			_SSProbesReprojected		= null;
			_SSProbesReprojected2		= null;

			_WSProbesAtlas				= null;
			_WSProbesRadiance			= null;

			CoreUtils.Destroy(_ComposeMaterial);

			HWRT.Cleanup();
			Filtering.Cleanup();
			ImportanceSampling.Cleanup();
			Encoding.Cleanup();
		}

		private void _StoreHistoryNormalDepth(CustomPassContext ctx)
		{
			CoreUtils.DrawFullScreen(ctx.cmd, _CopyNormalDepthMaterial, _HistoryNormalDepth, ctx.propertyBlock, shaderPassId: 0);

			//HDUtils.DrawFullScreen(ctx.cmd, new Rect(0, 0, ctx.hdCamera.camera.pixelWidth, ctx.hdCamera.camera.pixelHeight), _CopyNormalDepthMaterial, _HistoryNormalDepth, ctx.propertyBlock, shaderPassId: 0);
		}

		private void _ComposePass(CustomPassContext ctx)
		{
			ctx.propertyBlock.SetInt("_Debug", DebugProbesColor ? 1 : 0);

			ctx.propertyBlock.SetVector("_ProbeLayoutResolution", new Vector2(_SSProbesReprojected.width, _SSProbesReprojected.height));
			ctx.propertyBlock.SetVector("_ScreenResolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));

			ctx.propertyBlock.SetTexture("_SSProbes", EnableImportanceSampling ? _SSProbesRadianceUnpacked : _SSProbesRadiance);
			ctx.propertyBlock.SetTexture("_SSProbesFiltered", SpatialFilter ? _SSProbesReprojected2 : TemporalFilter ? _SSProbesReprojected : EnableImportanceSampling ? _SSProbesRadianceUnpacked : _SSProbesRadiance);

			ctx.propertyBlock.SetTexture("_SurfaceBRDF", ImportanceSampling.SSProbesSurfaceBRDF);
			ctx.propertyBlock.SetTexture("_LightningPDF", ImportanceSampling.SSProbesLightingPDF);

			ctx.propertyBlock.SetTexture("_SHAtlasR", Encoding.SSProbesSHAtlasR);
			ctx.propertyBlock.SetTexture("_SHAtlasG", Encoding.SSProbesSHAtlasG);
			ctx.propertyBlock.SetTexture("_SHAtlasB", Encoding.SSProbesSHAtlasB);

			ctx.propertyBlock.SetInt("_ProbeSize", SSProbeSize);
			ctx.propertyBlock.SetFloat("_Exposure", Exposure);

			ctx.propertyBlock.SetInt("_DistancePlaneWeighting", DistancePlaneWeightingCompose ? 1 : 0);
			ctx.propertyBlock.SetInt("_VisualizeDistancePlaneWeighting", VisualizeDistancePlaneWeighting ? 1 : 0);
			ctx.propertyBlock.SetInt("_VisualizeProbes", VisualizeSSProbes ? 1 : 0);

			ctx.propertyBlock.SetInt("_VisualizeSurfaceBRDF", VisualizeSurfaceBRDF ? 1 : 0);
			ctx.propertyBlock.SetInt("_VisualizeLightningPDF", VisualizeLightningPDF ? 1 : 0);

			

			ctx.propertyBlock.SetInt("_DiffuseGI", DiffuseGI ? 1 : 0);
			ctx.propertyBlock.SetInt("_Reflections", Reflections ? 1 : 0);


			ctx.propertyBlock.SetInt("_VisualizeWorldProbes", VisualizeWorldProbes ? 1 : 0);
			ctx.propertyBlock.SetInt("_TrilinearOffset", TrilinearOffset ? 1 : 0);
			ctx.propertyBlock.SetInt("_TrilinearOffset2", TrilinearOffset2 ? 1 : 0);

			ctx.propertyBlock.SetInt("_NormalWeight", NormalWeight ? 1 : 0);


			HDUtils.DrawFullScreen(ctx.cmd, _ComposeMaterial, ctx.cameraColorBuffer, ctx.propertyBlock, shaderPassId: DebugProbesColor ? 1 : 0);
		}
	}
}