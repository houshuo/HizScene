Shader "ProceduralDraw/Unlit" {
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
    }
        SubShader{
            Tags {"LightMode" = "ForwardBase" }

            Pass {
            CGPROGRAM
            #include "UnityCG.cginc"
            #include "../Structs.hlsl"
            #pragma target 5.0  
            #pragma vertex vertex_shader
            #pragma fragment fragment_shader

            StructuredBuffer<Vertex> _Vertices;
            StructuredBuffer<uint> _Indices;

            struct v2f {
                float4 pos : SV_POSITION;
                uint instance_ID : TEXCOORD0;
            };

            v2f vertex_shader(uint id : SV_VertexID, uint inst : SV_InstanceID)
            {
                v2f o;
                Vertex vertex = _Vertices[id];
                float4 vertex_position = float4(vertex.position,1.0f);
                o.pos = mul(UNITY_MATRIX_VP, vertex_position);
                o.instance_ID = id;
                return o;
            }

            fixed4 fragment_shader(v2f i) : SV_Target
            {
                return float4((i.instance_ID & 255) / 255.0, (i.instance_ID >> 8 & 255) / 255.0, (i.instance_ID >> 16 & 255) / 255.0, 1);
            }

            ENDCG
        }
    }
}