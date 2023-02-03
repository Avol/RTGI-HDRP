static const float PI = 3.1415926535897932384626422832795028841971f;
static const float TWO_PI = 6.2831853071795864769252867665590057683943f;
static const float SQRT_OF_ONE_THIRD = 0.5773502691896257645091487805019574556476f;

static const float4 BACKGROUND_COLOR = float4(0, 0, 0, 0);
static const float4 INITIAL_COLOR = float4(1, 1, 1, 0);
static const float INV_PI = 0.3183098861837906715377675267450287240689f;
static const float RED_WAVELENGTH_UM = 0.69f;
static const float BLUE_WAVELENGTH_UM = 0.47f;
static const float GREEN_WAVELENGTH_UM = 0.53f;

static uint rng_state; // the current seed
static const float png_01_convert = (1.0f / 4294967296.0f); // to convert into a 01 distribution

// Magic bit shifting algorithm from George Marsaglia's paper
uint rand_xorshift()
{
	rng_state ^= uint(rng_state << 13);
	rng_state ^= uint(rng_state >> 17);
	rng_state ^= uint(rng_state << 5);
	return rng_state;
}

// Wang hash for randomizing
uint wang_hash(uint seed)
{
	seed = (seed ^ 61) ^ (seed >> 16);
	seed *= 9;
	seed = seed ^ (seed >> 4);
	seed *= 0x27d4eb2d;
	seed = seed ^ (seed >> 15);
	return seed;
}

// Sets the seed of the pseudo-rng calls using the index of the pixel, the iteration number, and the current depth
void ComputeRngSeed(uint index, uint iteration, uint depth) {
	rng_state = uint(wang_hash((1 << 31) | (depth << 22) | iteration) ^ wang_hash(index));
}

// Returns a pseudo-rng float between 0 and 1. Must call ComputeRngSeed at least once.
float Uniform01() {
	return float(rand_xorshift() * png_01_convert);
}

/**
 * Computes a cosine-weighted random direction in a hemisphere.
 * Used for diffuse lighting.
 */
float3 CalculateRandomDirectionInHemisphere(float3 normal) {

	float up = sqrt(Uniform01()); // cos(theta)
	float over = sqrt(1 - up * up); // sin(theta)
	float around = Uniform01() * TWO_PI;

	// Find a direction that is not the normal based off of whether or not the
	// normal's components are all equal to sqrt(1/3) or whether or not at
	// least one component is less than sqrt(1/3). Learned this trick from
	// Peter Kutz.

	float3 directionNotNormal;
	if (abs(normal.x) < SQRT_OF_ONE_THIRD) {
		directionNotNormal = float3(1, 0, 0);
	}
	else if (abs(normal.y) < SQRT_OF_ONE_THIRD) {
		directionNotNormal = float3(0, 1, 0);
	}
	else {
		directionNotNormal = float3(0, 0, 1);
	}

	// Use not-normal direction to generate two perpendicular directions
	float3 perpendicularDirection1 =
		normalize(cross(normal, directionNotNormal));
	float3 perpendicularDirection2 =
		normalize(cross(normal, perpendicularDirection1));

	return up * normal
		+ cos(around) * over * perpendicularDirection1
		+ sin(around) * over * perpendicularDirection2;
}