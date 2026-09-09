// Blits a center-cropped source into the destination (via _MainTex_ST, exactly like the plain
// Graphics.Blit(source, dest, scale, offset) overload CameraFramePreprocessor used before this),
// then blanks every destination pixel outside a circle around the destination's centre to a flat
// fill colour. This is the background-suppression mask: it keeps the object the crop guide centred
// the user on and removes the corners (and, with a smaller radius, more of the surrounding
// background) from both the embedding input and the AKAZE verification frame, so neither is thrown
// off by whatever is behind the object. See CameraFramePreprocessor and CircularMaskLayout.
Shader "Hidden/ObjecTalk/CircularMask"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _CropST ("Crop scale.xy / offset.zw (source UV)", Vector) = (1, 1, 0, 0)
        _FillColor ("Fill Color", Color) = (0.5, 0.5, 0.5, 1)
        _MaskRadius ("Mask Radius (destination UV)", Float) = 0.5
        _FeatherWidth ("Feather Width (destination UV)", Float) = 0.01
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 sampleUv : TEXCOORD0; // post _MainTex_ST: where to sample the (cropped) source
                float2 destUv : TEXCOORD1;   // raw 0..1 across the destination surface
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            // Center-crop applied in the shader: Graphics.Blit(src, dst, mat) resets _MainTex_ST to
            // identity, so the crop cannot ride on the material's textureScale/Offset. It comes in
            // here explicitly instead.
            float4 _CropST;
            fixed4 _FillColor;
            float _MaskRadius;
            float _FeatherWidth;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.sampleUv = v.texcoord * _CropST.xy + _CropST.zw;
                o.destUv = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.sampleUv);
                float dist = length(i.destUv - float2(0.5, 0.5));
                float keep = smoothstep(_MaskRadius, max(_MaskRadius - _FeatherWidth, 0.0), dist);
                return lerp(_FillColor, col, keep);
            }
            ENDCG
        }
    }
    Fallback Off
}
