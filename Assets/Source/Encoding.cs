using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Avol.IndirectFlux
{
	public class Encoding
	{
		private IndirectFlux	_IndirectFlux;

		private ComputeShader	_SSEncodingShader;

		public	RenderTexture	SSProbesSHAtlasR;
		public	RenderTexture	SSProbesSHAtlasG;
		public	RenderTexture	SSProbesSHAtlasB;

		public Encoding(IndirectFlux indirectFlux)
		{
			_IndirectFlux = indirectFlux;
		}

		public void Setup()
		{
			_SSEncodingShader = Resources.Load<ComputeShader>("Core/SSEncoding");

			SSProbesSHAtlasR									= new RenderTexture(_IndirectFlux.SSProbeLayoutResolution.x / _IndirectFlux.SSProbeSize, _IndirectFlux.SSProbeLayoutResolution.y / _IndirectFlux.SSProbeSize, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default);
			SSProbesSHAtlasR.volumeDepth						= 9;
			SSProbesSHAtlasR.dimension							= TextureDimension.Tex3D;
			SSProbesSHAtlasR.enableRandomWrite					= true;
			SSProbesSHAtlasR.filterMode							= FilterMode.Point;
			SSProbesSHAtlasR.wrapMode							= TextureWrapMode.Clamp;
			SSProbesSHAtlasR.anisoLevel							= 0;
			SSProbesSHAtlasR.Create();

			SSProbesSHAtlasG									= new RenderTexture(_IndirectFlux.SSProbeLayoutResolution.x / _IndirectFlux.SSProbeSize, _IndirectFlux.SSProbeLayoutResolution.y / _IndirectFlux.SSProbeSize, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default);
			SSProbesSHAtlasG.volumeDepth						= 9;
			SSProbesSHAtlasG.dimension							= TextureDimension.Tex3D;
			SSProbesSHAtlasG.enableRandomWrite					= true;
			SSProbesSHAtlasG.filterMode							= FilterMode.Point;
			SSProbesSHAtlasG.wrapMode							= TextureWrapMode.Clamp;
			SSProbesSHAtlasG.anisoLevel							= 0;
			SSProbesSHAtlasG.Create();

			SSProbesSHAtlasB									= new RenderTexture(_IndirectFlux.SSProbeLayoutResolution.x / _IndirectFlux.SSProbeSize, _IndirectFlux.SSProbeLayoutResolution.y / _IndirectFlux.SSProbeSize, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default);
			SSProbesSHAtlasB.volumeDepth						= 9;
			SSProbesSHAtlasB.dimension							= TextureDimension.Tex3D;
			SSProbesSHAtlasB.enableRandomWrite					= true;
			SSProbesSHAtlasB.filterMode							= FilterMode.Point;
			SSProbesSHAtlasB.wrapMode							= TextureWrapMode.Clamp;
			SSProbesSHAtlasB.anisoLevel							= 0;
			SSProbesSHAtlasB.Create();
		}

		public void Cleanup()
		{
			SSProbesSHAtlasR.DiscardContents();
			SSProbesSHAtlasG.DiscardContents();
			SSProbesSHAtlasB.DiscardContents();

			SSProbesSHAtlasR.Release();
			SSProbesSHAtlasG.Release();
			SSProbesSHAtlasB.Release();

			SSProbesSHAtlasR = null;
			SSProbesSHAtlasG = null;
			SSProbesSHAtlasB = null;
		}

		public void SSProbesEcodePass(CustomPassContext ctx, RenderTexture SSProbesSource)
		{
			ctx.cmd.SetComputeTextureParam(_SSEncodingShader, 0, "_SSProbes", SSProbesSource);
			ctx.cmd.SetComputeTextureParam(_SSEncodingShader, 0, "_SHAtlasR", SSProbesSHAtlasR);
			ctx.cmd.SetComputeTextureParam(_SSEncodingShader, 0, "_SHAtlasG", SSProbesSHAtlasG);
			ctx.cmd.SetComputeTextureParam(_SSEncodingShader, 0, "_SHAtlasB", SSProbesSHAtlasB);

			ctx.cmd.SetComputeIntParam(_SSEncodingShader, "_ProbeSize", _IndirectFlux.SSProbeSize);
			ctx.cmd.DispatchCompute(_SSEncodingShader, 0, _IndirectFlux.SSProbeLayoutResolution.x, _IndirectFlux.SSProbeLayoutResolution.y, 1);
		}
	}
}
