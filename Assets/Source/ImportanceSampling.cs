using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Avol.IndirectFlux
{
	public class ImportanceSampling
	{
		private readonly IndirectFlux				_IndirectFlux;

		private ComputeShader						_SSImportanceSamplingShader;

		public	RenderTexture						SSProbesSurfaceBRDF;
		public	RenderTexture						SSProbesLightingPDF;

		public	RenderTexture						SSProbesRayAtlas;
		public	RenderTexture						SSProbesRayAtlasLoc;

		public ImportanceSampling(IndirectFlux indirectFlux)
		{
			_IndirectFlux = indirectFlux;
		}

		public void Setup()
		{
			_SSImportanceSamplingShader						= Resources.Load<ComputeShader>("Core/SSImportanceSampling");

			SSProbesSurfaceBRDF								= new RenderTexture(_IndirectFlux.SSProbeLayoutResolution.x, _IndirectFlux.SSProbeLayoutResolution.y, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Default);
			SSProbesSurfaceBRDF.enableRandomWrite			= true;
			SSProbesSurfaceBRDF.filterMode					= FilterMode.Point;
			SSProbesSurfaceBRDF.anisoLevel					= 0;
			SSProbesSurfaceBRDF.Create();

			SSProbesLightingPDF								= new RenderTexture(_IndirectFlux.SSProbeLayoutResolution.x, _IndirectFlux.SSProbeLayoutResolution.y, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Default);
			SSProbesLightingPDF.enableRandomWrite			= true;
			SSProbesLightingPDF.filterMode					= FilterMode.Point;
			SSProbesLightingPDF.anisoLevel					= 0;
			SSProbesLightingPDF.Create();

			SSProbesRayAtlas								= new RenderTexture(_IndirectFlux.SSProbeLayoutResolution.x, _IndirectFlux.SSProbeLayoutResolution.y, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Default);
			SSProbesRayAtlas.enableRandomWrite				= true;
			SSProbesRayAtlas.filterMode						= FilterMode.Point;
			SSProbesRayAtlas.anisoLevel						= 1;
			SSProbesRayAtlas.Create();

			SSProbesRayAtlasLoc								= new RenderTexture(_IndirectFlux.SSProbeLayoutResolution.x, _IndirectFlux.SSProbeLayoutResolution.y, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Default);
			SSProbesRayAtlasLoc.enableRandomWrite			= true;
			SSProbesRayAtlasLoc.filterMode					= FilterMode.Point;
			SSProbesRayAtlasLoc.anisoLevel					= 1;
			SSProbesRayAtlasLoc.Create();
		}

		public void Cleanup()
		{
			SSProbesSurfaceBRDF.DiscardContents();
			SSProbesLightingPDF.DiscardContents();
			SSProbesRayAtlas.DiscardContents();
			SSProbesRayAtlasLoc.DiscardContents();

			SSProbesSurfaceBRDF.Release();
			SSProbesLightingPDF.Release();
			SSProbesRayAtlas.Release();
			SSProbesRayAtlasLoc.Release();

			SSProbesSurfaceBRDF		= null;
			SSProbesLightingPDF		= null;
			SSProbesRayAtlas		= null;
			SSProbesRayAtlasLoc		= null;
		}

		public void SSImportanceSamplingSurfaceBRDF(CustomPassContext ctx, RenderTexture SSProbesSource)
		{
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 0, "_SSProbes", SSProbesSource);
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 0, "_SSProbesSurfaceBRDF", SSProbesSurfaceBRDF);
			ctx.cmd.SetComputeIntParam(_SSImportanceSamplingShader, "_ProbeSize", _IndirectFlux.SSProbeSize);
			ctx.cmd.DispatchCompute(_SSImportanceSamplingShader, 0, SSProbesSurfaceBRDF.width, SSProbesSurfaceBRDF.height, 1);
		}

		public void SSImportanceSamplingLightningPDF(CustomPassContext ctx, RenderTexture SSProbesSource)
		{
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 1, "_SSProbes", SSProbesSource);
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 1, "_SSProbesLightingPDF", SSProbesLightingPDF);
			ctx.cmd.SetComputeIntParam(_SSImportanceSamplingShader, "_ProbeSize", _IndirectFlux.SSProbeSize);
			ctx.cmd.DispatchCompute(_SSImportanceSamplingShader, 1, SSProbesLightingPDF.width, SSProbesLightingPDF.height, 1);
		}

		public void SSImportanceSamplingPackRays(CustomPassContext ctx)
		{
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 2, "_SSProbesSurfaceBRDF",			SSProbesSurfaceBRDF);
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 2, "_SSProbesLightingPDF",			SSProbesLightingPDF);
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 2, "_SSProbesRayAtlas",				SSProbesRayAtlas);
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 2, "_SSProbesRayAtlasLoc",			SSProbesRayAtlasLoc);

			ctx.cmd.SetComputeIntParam(_SSImportanceSamplingShader, "_ProbeSize", _IndirectFlux.SSProbeSize);
			ctx.cmd.DispatchCompute(_SSImportanceSamplingShader, 2, SSProbesRayAtlas.width, SSProbesRayAtlas.height, 1);
		}

		public void SSImportanceSamplingUnpackRadiance(CustomPassContext ctx)
		{
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 3, "_SSProbes",						_IndirectFlux._SSProbesRadiance);
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 3, "_SSProbesUnpacked",				_IndirectFlux._SSProbesRadianceUnpacked);

			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 3, "_SSProbesRayAtlas",				_IndirectFlux.ImportanceSampling.SSProbesRayAtlas);
			ctx.cmd.SetComputeTextureParam(_SSImportanceSamplingShader, 3, "_SSProbesRayAtlasLoc",			_IndirectFlux.ImportanceSampling.SSProbesRayAtlasLoc);
			
			ctx.cmd.SetComputeIntParam(_SSImportanceSamplingShader, "_ProbeSize", _IndirectFlux.SSProbeSize);
			ctx.cmd.DispatchCompute(_SSImportanceSamplingShader, 3, _IndirectFlux.ImportanceSampling.SSProbesRayAtlas.width, _IndirectFlux.ImportanceSampling.SSProbesRayAtlas.height, 1);
		}
	}
}
