float4 _Color;

[shader("closesthit")]
void ClosestHit(inout RayPayload rayPayload : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
	rayPayload.color	= _Color.xyz;


	// transform normal to world space
	float3x3 objectToWorld	= (float3x3)ObjectToWorld3x4();

	// compute vertex data on ray/triangle intersection
	IntersectionVertex currentvertex;
	GetCurrentIntersectionVertex(attributeData, currentvertex);

	float3 worldNormal		= normalize(mul(objectToWorld, currentvertex.normalOS));

	rayPayload.normal		= worldNormal;
	rayPayload.distance		= RayTCurrent();
}