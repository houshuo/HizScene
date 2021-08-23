Shader "IndirectRendering/HiZ/Buffer"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertex
            #pragma fragment blit
            #include "ShaderInclude_HiZ.cginc"
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertex
            #pragma fragment reduce
            #include "ShaderInclude_HiZ.cginc"
            ENDHLSL
        }
    }
}
