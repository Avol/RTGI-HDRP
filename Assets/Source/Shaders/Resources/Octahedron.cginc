float3 UVtoOctahedron(float2 uv) 
{
	// Unpack the 0...1 range to the -1...1 unit square.
	float3 position = float3(2.0f * (uv - 0.5f), 0);

	// "Lift" the middle of the square to +1 z, and let it fall off linearly
	// to z = 0 along the Manhattan metric diamond (absolute.x + absolute.y == 1),
	// and to z = -1 at the corners where position.x and .y are both = +-1.
	float2 absolute = abs(position.xy);
	position.z = 1.0f - absolute.x - absolute.y;

	// "Tuck in" the corners by reflecting the xy position along the line y = 1 - x
	// (in quadrant 1), and its mirrored image in the other quadrants.
	if (position.z < 0) {
		position.xy = sign(position.xy)
			* float2(1.0f - absolute.y, 1.0f - absolute.x);
	}

	return normalize(position);
}

float2 OctahedronUV(float3 direction) 
{
	direction = normalize(direction);

	float3 octant = sign(direction);

	// Scale the vector so |x| + |y| + |z| = 1 (surface of octahedron).
	float sum = dot(direction, octant);
	float3 octahedron = direction / sum;

	// "Untuck" the corners using the same reflection across the diagonal as before.
	// (A reflection is its own inverse transformation).
	if (octahedron.z < 0) {
		float3 absolute = abs(octahedron);
		octahedron.xy = octant.xy
			* float2(1.0f - absolute.y, 1.0f - absolute.x);
	}

	return octahedron.xy * 0.5f + 0.5f;
}

// Computes the spherical excess (solid angle) of a spherical triangle with vertices A, B, C as unit length vectors
// https://en.wikipedia.org/wiki/Spherical_trigonometry#Area_and_spherical_excess
float ComputeSphericalExcess(float3 A, float3 B, float3 C) {
	float CosAB = dot(A, B);
	float SinAB = 1.0f - CosAB * CosAB;
	float CosBC = dot(B, C);
	float SinBC = 1.0f - CosBC * CosBC;
	float CosCA = dot(C, A);
	float CosC = CosCA - CosAB * CosBC;
	float SinC = sqrt(SinAB * SinBC - CosC * CosC);
	float Inv = (1.0f - CosAB) * (1.0f - CosBC);
	return 2.0f * atan2(SinC, sqrt((SinAB * SinBC * (1.0f + CosBC) * (1.0f + CosAB)) / Inv) + CosC);
}

// TexelCoord should be centered on the octahedral texel, in the range [.5f, .5f + Resolution - 1]
float OctahedralSolidAngle(float2 TexelCoord, float InvResolution)
{
	float3 Direction10 = UVtoOctahedron((TexelCoord + float2(.5f, -.5f) * InvResolution) * 2.0f - 1.0f);
	float3 Direction01 = UVtoOctahedron((TexelCoord + float2(-.5f, .5f) * InvResolution) * 2.0f - 1.0f);

	float SolidAngle0 = ComputeSphericalExcess(
		UVtoOctahedron((TexelCoord + float2(-.5f, -.5f) * InvResolution) * 2.0f - 1.0f),
		Direction10,
		Direction01);

	float SolidAngle1 = ComputeSphericalExcess(
		UVtoOctahedron((TexelCoord + float2(.5f, .5f) * InvResolution) * 2.0f - 1.0f),
		Direction01,
		Direction10);

	return SolidAngle0 + SolidAngle1;
}

uint PackRay(uint2 octaUV)
{
	return octaUV.x + octaUV.y * 8;
}

uint2 UnpackRay(uint rayID)
{
	return uint2(rayID % 8, floor(rayID / 8.0f));
}
