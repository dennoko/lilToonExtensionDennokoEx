// Editor-only blit shader used by DennokoEx's build-time mask packer.
// Composites four single-channel masks into one RGBA texture (each mask -> .r of its source):
//   R = Reflection 2nd   G = Rim 2nd   B = Normal 3rd   A = Normal 1st
// Sampling honours each source's import settings (sRGB/linear) exactly as the runtime shader
// did before packing, and the result is written to a linear render target, so values are faithful.
Shader "Hidden/dennokoworks/DennokoEx/MaskPacker"
{
    Properties
    {
        _TexR ("R", 2D) = "white" {}
        _TexG ("G", 2D) = "white" {}
        _TexB ("B", 2D) = "white" {}
        _TexA ("A", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TexR;
            sampler2D _TexG;
            sampler2D _TexB;
            sampler2D _TexA;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(
                    tex2D(_TexR, i.uv).r,
                    tex2D(_TexG, i.uv).r,
                    tex2D(_TexB, i.uv).r,
                    tex2D(_TexA, i.uv).r);
            }
            ENDCG
        }
    }
    Fallback Off
}
