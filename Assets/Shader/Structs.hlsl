#ifndef __INDIRECT_INCLUDE__
#define __INDIRECT_INCLUDE__

struct Vertex
{
	float3 position;
	float3 normal;
	float3 tangent;
};

struct AABB
{
	float3 center;
	float3 extent;
};

struct IndirectArgument
{
	uint indexCount;
	uint instanceCount;
	uint startIndexLocation;
	uint baseVertexLocation;
	uint startInstanceLocation;
};

#endif