using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Avol.IndirectFlux
{
	public class Filtering
	{
		private IndirectFlux	_IndirectFlux;

		private ComputeShader	_SSFilteringShader;

		private Matrix4x4		_HistoryIPVMatrix;

		public Filtering(IndirectFlux indirectFlux)
		{
			_IndirectFlux		= indirectFlux;
		}

		public void Setup()
		{
			_SSFilteringShader = Resources.Load<ComputeShader>("Core/SSFiltering");
		}

		public void Cleanup()
		{

		}

		public void SSProbesTemporalReprojectionPass(CustomPassContext ctx, RenderTexture historyNormalDepth, RenderTexture SSProbesSource, RenderTexture SSProbesTarget)
		{
			ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 0, "_HistoryNormalDepth", historyNormalDepth);
			ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 0, "_SSProbes", SSProbesSource);
			ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 0, "_TemporalAccumulation", SSProbesTarget);

			ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_TemporalWeight", _IndirectFlux.TemporalWeight);
			ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_RayConeAngle", 2.0f / _IndirectFlux.SSProbeSize);

			ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_ProbeSize", _IndirectFlux.SSProbeSize);
			ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_TemporalWeight", _IndirectFlux.TemporalWeight);

			ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_MaxRayDistance", _IndirectFlux.MaxRayDistance);
			ctx.cmd.SetComputeVectorParam(_SSFilteringShader, "_ProbeLayoutResolution", (Vector2)_IndirectFlux.SSProbeLayoutResolution);
			ctx.cmd.SetComputeVectorParam(_SSFilteringShader, "_ScreenResolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));
			ctx.cmd.SetComputeMatrixParam(_SSFilteringShader, "_HistoryIVPMatrix", _HistoryIPVMatrix);

			ctx.cmd.DispatchCompute(_SSFilteringShader, 0, _IndirectFlux.SSProbeLayoutResolution.x, _IndirectFlux.SSProbeLayoutResolution.y, 1);

			_HistoryIPVMatrix = (GL.GetGPUProjectionMatrix(ctx.hdCamera.camera.projectionMatrix, true) * ctx.hdCamera.camera.worldToCameraMatrix).inverse;
		}

		public void SSProbesSpatialReprojectionPass(CustomPassContext ctx, RenderTexture SSProbesSource, RenderTexture SSProbesTarget)
		{
			{
				ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 1, "_TemporalAccumulation", SSProbesSource);
				ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 1, "_SpatialAccumulation", SSProbesTarget);

				ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_MaxRayDistance", _IndirectFlux.MaxRayDistance);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_ProbeSize", _IndirectFlux.SSProbeSize);
				ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_TemporalWeight", _IndirectFlux.TemporalWeight);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_Upscale", _IndirectFlux.Upscale);
				ctx.cmd.SetComputeVectorParam(_SSFilteringShader, "_Resolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));

				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_DistancePlaneWeighting", _IndirectFlux.DistancePlaneWeightingReprojection ? 1 : 0);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_AngleWeighting", _IndirectFlux.AngleWeightingReprojection ? 1 : 0);

				ctx.cmd.DispatchCompute(_SSFilteringShader, 1, _IndirectFlux.SSProbeLayoutResolution.x, _IndirectFlux.SSProbeLayoutResolution.y, 1);
			}


			/*{
				ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 1, "_TemporalAccumulation", SSProbesTarget);
				ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 1, "_SpatialAccumulation", SSProbesSource);

				ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_MaxRayDistance", _IndirectFlux.MaxRayDistance);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_ProbeSize", _IndirectFlux.ProbeSize);
				ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_TemporalWeight", _IndirectFlux.TemporalWeight);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_Upscale", _IndirectFlux.Upscale);
				ctx.cmd.SetComputeVectorParam(_SSFilteringShader, "_Resolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));

				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_DistancePlaneWeighting", _IndirectFlux.DistancePlaneWeightingReprojection ? 1 : 0);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_AngleWeighting", _IndirectFlux.AngleWeightingReprojection ? 1 : 0);

				ctx.cmd.DispatchCompute(_SSFilteringShader, 1, _IndirectFlux.ProbeLayoutResolution.x, _IndirectFlux.ProbeLayoutResolution.y, 1);
			}

			{
				ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 1, "_TemporalAccumulation", SSProbesSource);
				ctx.cmd.SetComputeTextureParam(_SSFilteringShader, 1, "_SpatialAccumulation", SSProbesTarget);

				ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_MaxRayDistance", _IndirectFlux.MaxRayDistance);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_ProbeSize", _IndirectFlux.ProbeSize);
				ctx.cmd.SetComputeFloatParam(_SSFilteringShader, "_TemporalWeight", _IndirectFlux.TemporalWeight);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_Upscale", _IndirectFlux.Upscale);
				ctx.cmd.SetComputeVectorParam(_SSFilteringShader, "_Resolution", new Vector2(ctx.hdCamera.actualWidth, ctx.hdCamera.actualHeight));

				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_DistancePlaneWeighting", _IndirectFlux.DistancePlaneWeightingReprojection ? 1 : 0);
				ctx.cmd.SetComputeIntParam(_SSFilteringShader, "_AngleWeighting", _IndirectFlux.AngleWeightingReprojection ? 1 : 0);

				ctx.cmd.DispatchCompute(_SSFilteringShader, 1, _IndirectFlux.ProbeLayoutResolution.x, _IndirectFlux.ProbeLayoutResolution.y, 1);
			}*/
		}
	}
}
