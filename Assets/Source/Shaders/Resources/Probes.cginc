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

static float2 sidePixels[5] =
{
	float2(0, 0),
	float2(-1, 0),
	float2(0, 1),
	float2(0, -1),
	float2(1, 0),
};


// Clamps UV to smaller resolution.
// @ positionCS = screen space UV coordinate.
float2 ClampCoordinate(float2 positionNDC, float2 resolution, int downscale)
{
	float2	t = floor(positionNDC * resolution);
	float2	m = t % downscale;
	float2	d = positionNDC - m / resolution;
	return d;
}

// Clamps UV to smaller resolution.
// @ positionCS = screen space UV coordinate.
float2 ClampCoordinateBilinearCenter(uint2 positionCS, float2 resolution, uint downscale)
{
	float2	m = positionCS - positionCS % downscale + downscale / 2;
	float2	d = m / resolution;
	return d;
}

//  Compare probe vs pixel distance and normal.
//
int DistancePlaneWeighting(float3 fromNormal, float3 fromWorldPosition, float fromDepth, float3 toWorldPosition)
{
	float4	probeScenePlane = float4(fromNormal, dot(fromWorldPosition, fromNormal));

	float	planeDistance = abs(dot(float4(toWorldPosition, -1), probeScenePlane));
	float	relativeDepthDifference = planeDistance / fromDepth;

	return exp2(-100000 * (relativeDepthDifference * relativeDepthDifference)) > .1 ? 1.0 : 0.0;
}

//  Compare probe vs pixel distance and normal.
//
float DistancePlaneWeighting2(float3 fromNormal, float3 fromWorldPosition, float fromDepth, float3 toWorldPosition)
{
	float4	probeScenePlane = float4(fromNormal, dot(fromWorldPosition, fromNormal));

	float	planeDistance = abs(dot(float4(toWorldPosition, -1), probeScenePlane));
	float	relativeDepthDifference = planeDistance / fromDepth;

	return exp2(-100000 * (relativeDepthDifference * relativeDepthDifference));
}