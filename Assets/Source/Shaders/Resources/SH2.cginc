//#define PI 3.14159265359

struct SH9
{
	float3 sh0;
	float3 sh1;
	float3 sh2;
	float3 sh3;
	float3 sh4;
	float3 sh5;
	float3 sh6;
	float3 sh7;
	float3 sh8;
};

struct SH9Color
{
	float3 sh0;
	float3 sh1;
	float3 sh2;
	float3 sh3;
	float3 sh4;
	float3 sh5;
	float3 sh6;
	float3 sh7;
	float3 sh8;
};

SH9 genSHCoefficients(float3 N)
{
	SH9 coeffs;

	// Band 0
	coeffs.sh0 = 0.282095f;

	// Band 1
	coeffs.sh1 = 0.488603f * N.y;
	coeffs.sh2 = 0.488603f * N.z;
	coeffs.sh3 = 0.488603f * N.x;

	// Band 2
	coeffs.sh4 = 1.092548f * N.x * N.y;
	coeffs.sh5 = 1.092548f * N.y * N.z;
	coeffs.sh6 = 0.315392f * (3.0f * N.z * N.z - 1.0f);
	coeffs.sh7 = 1.092548f * N.x * N.z;
	coeffs.sh8 = 0.546274f * (N.x * N.x - N.y * N.y);

	return coeffs;
}

SH9Color genLightingCoefficientsForNormal(float3 N, float3 color)
{
	SH9 SHCoefficients = genSHCoefficients(N);
	SH9Color result;

	result.sh0 = color * SHCoefficients.sh0;
	result.sh1 = color * SHCoefficients.sh1;
	result.sh2 = color * SHCoefficients.sh2;
	result.sh3 = color * SHCoefficients.sh3;
	result.sh4 = color * SHCoefficients.sh4;
	result.sh5 = color * SHCoefficients.sh5;
	result.sh6 = color * SHCoefficients.sh6;
	result.sh7 = color * SHCoefficients.sh7;
	result.sh8 = color * SHCoefficients.sh8;

	return result;
}

float3 calcIrradiance(float3 nor, SH9Color sh9Color) 
{
	SH9Color c = sh9Color;

	const float c1 = 0.429043;
	const float c2 = 0.511664;
	const float c3 = 0.743125;
	const float c4 = 0.886227;
	const float c5 = 0.247708;

	float3 l22	= c.sh8;
	float3 l21	= c.sh7;
	float3 l20	= c.sh6;
	float3 l2m1 = c.sh5;
	float3 l2m2 = c.sh4;
	float3 l11  = c.sh3;
	float3 l10  = c.sh2;
	float3 l1m1 = c.sh1;
	float3 l00  = c.sh0;

	float3 radiance =
		c1 * l22 * (nor.x * nor.x - nor.y * nor.y) +
		c3 * l20 * nor.z * nor.z +
		c4 * l00 -
		c5 * l20 +
		2.0 * c1 * l2m2 * nor.x * nor.y +
		2.0 * c1 * l21 * nor.x * nor.z +
		2.0 * c1 * l2m1 * nor.y * nor.z +
		2.0 * c2 * l11 * nor.x +
		2.0 * c2 * l1m1 * nor.y +
		2.0 * c2 * l10 * nor.z;

	return max(0, radiance);
}