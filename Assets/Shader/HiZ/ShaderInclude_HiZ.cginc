#ifndef __HIZ_INCLUDE__
#define __HIZ_INCLUDE__

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct Input
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
};

struct Varyings
{
    float4 vertex : SV_POSITION;
    float2 uv : TEXCOORD0;
};

Varyings vertex(Input i)
{
    Varyings output;
    VertexPositionInputs vertexInput = GetVertexPositionInputs(i.vertex.xyz);
    output.vertex = vertexInput.positionCS;
    output.uv = i.uv;

    return output;
}

float4 blit(Varyings input) : SV_Target
{
    float camDepth = SampleSceneDepth(input.uv).r;
    return float4(camDepth, 0, 0 ,0);
}

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
float4 reduce(in Varyings input) : SV_Target
{
    float4 r = _MainTex.GatherRed(sampler_MainTex, input.uv);
    float minimum = min(min(min(r.x, r.y), r.z), r.w);
    return float4(minimum, 1.0, 1.0, 1.0);
}

#endif
