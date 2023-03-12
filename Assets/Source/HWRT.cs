using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Avol.IndirectFlux
{
	public class HWRT
	{
		private readonly IndirectFlux					_IndirectFlux;

		private RayTracingShader						_SSProbesShader;
		private ComputeShader							_WSProbesShader;
		private RayTracingAccelerationStructure			_RTStructure;

		public HWRT(IndirectFlux indirectFlux)
		{
			_IndirectFlux = indirectFlux;
		}

		public void Setup()
		{
			InitRaytracingAccelerationStructure();

			_SSProbesShader = Resources.Load<RayTracingShader>("Core/SSProbes");
			_SSProbesShader.SetShaderPass("RTXIntersection");
			_SSProbesShader.SetAccelerationStructure("_RaytracingAccelerationStructure", _RTStructure);

			_WSProbesShader = Resources.Load<ComputeShader>("Core/WSEncoding");
		}

		public void Cleanup()
		{
			_RTStructure.Release();
		}

		private void InitRaytracingAccelerationStructure()
		{
			//RequestAccelerationStructure(ctx.hdCamera);

			RayTracingAccelerationStructure.Settings settings = new RayTracingAccelerationStructure.Settings();

			settings.layerMask				= ~0;
			settings.managementMode			= RayTracingAccelerationStructure.ManagementMode.Automatic;
			settings.rayTracingModeMask		= RayTracingAccelerationStructure.RayTracingModeMask.Everything;

			_RTStructure = new RayTracingAccelerationStructure(settings);

			// collect all objects in scene and add them to raytracing scene
			Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			foreach (Renderer r in renderers)
				_RTStructure.AddInstance(r, new RayTracingSubMeshFlags[] { RayTracingSubMeshFlags.UniqueAnyHitCalls });

			// build raytracing scene
			_RTStructure.Build();
		}


		public void WSProbes(CustomPassContext ctx)
		{
			ctx.cmd.SetComputeTextureParam(_WSProbesShader, 0, "_WSProbes0", _IndirectFlux._WSProbesRadiance, 0);
			ctx.cmd.SetComputeTextureParam(_WSProbesShader, 0, "_WSProbes1", _IndirectFlux._WSProbesRadiance, 1);
			ctx.cmd.SetComputeTextureParam(_WSProbesShader, 0, "_WSProbes2", _IndirectFlux._WSProbesRadiance, 2);

			ctx.cmd.DispatchCompute(_WSProbesShader, 0, _IndirectFlux._WSProbesRadiance.width, _IndirectFlux._WSProbesRadiance.height, 1);
		}

		public void SSProbesGatherPass(CustomPassContext ctx)
		{
			ctx.cmd.SetRayTracingIntParam(_SSProbesShader, "_TestTracingLanes", _IndirectFlux.TestTracingLanes ? 1 : 0);
			ctx.cmd.SetRayTracingTextureParam(_SSProbesShader, "_SSProbesRayAtlas", _IndirectFlux.ImportanceSampling.SSProbesRayAtlas);
			ctx.cmd.SetRayTracingTextureParam(_SSProbesShader, "_SSProbes", _IndirectFlux._SSProbesRadiance);
			ctx.cmd.SetRayTracingTextureParam(_SSProbesShader, "_SSProbesLightingPDF", _IndirectFlux.ImportanceSampling.SSProbesLightingPDF);
			ctx.cmd.SetRayTracingIntParam(_SSProbesShader, "_ImportanceSampling", _IndirectFlux.EnableImportanceSampling ? 1 : 0);

			ctx.cmd.SetRayTracingIntParam(_SSProbesShader, "_RayCount", _IndirectFlux.RayCount);
			ctx.cmd.SetRayTracingIntParam(_SSProbesShader, "_ProbeSize", _IndirectFlux.SSProbeSize);
			ctx.cmd.SetRayTracingIntParam(_SSProbesShader, "_Upscale", _IndirectFlux.Upscale);
			ctx.cmd.SetRayTracingFloatParam(_SSProbesShader, "_RayConeAngle", 2.0f / _IndirectFlux.SSProbeSize);
			ctx.cmd.SetRayTracingFloatParam(_SSProbesShader, "_MaxRayDistance", _IndirectFlux.MaxRayDistance);
			ctx.cmd.SetRayTracingIntParam(_SSProbesShader, "_TraceJitter", _IndirectFlux.TraceJitter ? 1 : 0);
			ctx.cmd.SetRayTracingVectorParam(_SSProbesShader, "_ScreenResolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));
			ctx.cmd.SetRayTracingFloatParam(_SSProbesShader, "_BounceDistance", _IndirectFlux.BounceDistance);
			ctx.cmd.SetRayTracingFloatParam(_SSProbesShader, "_TemporalWeight", _IndirectFlux.TemporalWeight);
			ctx.cmd.SetRayTracingFloatParam(_SSProbesShader, "_Exposure", _IndirectFlux.Exposure);

			ctx.cmd.SetRayTracingIntParam(_SSProbesShader, "_UseShadowMaps", _IndirectFlux.UseShadowMaps ? 1 : 0);

			ctx.cmd.SetRayTracingIntParam(_SSProbesShader, "_Frame", _IndirectFlux._Frame);
			ctx.cmd.DispatchRays(_SSProbesShader, "SSRadiance", (uint)_IndirectFlux._SSProbesRadiance.width, (uint)_IndirectFlux._SSProbesRadiance.height, 1, ctx.hdCamera.camera);
		}

		public void WSProbesGatherPass()
		{

		}
	}
}
