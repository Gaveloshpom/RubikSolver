// Draws the faces of the cube for any size N. The sticker colors live in a 6N x N texture
// (face f occupies columns f*N .. f*N+N-1, one texel per sticker) and the mesh UVs are in sticker
// units, so the shader picks the texel of the sticker under the pixel and draws a rounded sticker
// with a dark gap around it. Interior faces (the cut faces while a layer turns) set _Body = 1.
// It does its own simple lighting and never samples shadow maps, which URP Lit does and which
// Chrome's WebGL rejects (nothing lit gets drawn there).
Shader "Rubik/CubeFace"
{
    Properties
    {
        _MainTex ("Sticker colors (6N x N)", 2D) = "white" {}
        _BodyColor ("Body", Color) = (0.06, 0.06, 0.07, 1)
        _StickerSize ("Sticker size (share of a cell)", Range(0.5, 1)) = 0.88
        _Radius ("Corner radius (share of a sticker)", Range(0, 0.5)) = 0.18
        _Body ("Body only", Float) = 0
        _Ambient ("Ambient", Range(0, 1)) = 0.42
        _Specular ("Specular", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;   // (1/width, 1/height, width, height)
            float4 _BodyColor;
            float _StickerSize;
            float _Radius;
            float _Body;
            float _Ambient;
            float _Specular;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;        // in stickers: integer part = texel, fraction = place in the sticker
                float3 normal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 albedo = _BodyColor.rgb;
                float specular = 0.05;

                if (_Body < 0.5)
                {
                    float2 cell = floor(i.uv);
                    float3 sticker = tex2D(_MainTex, (cell + 0.5) * _MainTex_TexelSize.xy).rgb;

                    // signed distance to a rounded square sticker centred in the cell
                    float halfSize = _StickerSize * 0.5;
                    float radius = _Radius * halfSize;
                    float2 q = abs(frac(i.uv) - 0.5) - (halfSize - radius);
                    float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;

                    // antialias the edge; once a sticker is only a few pixels wide the gaps cannot be
                    // drawn cleanly (they shimmer), so fade them out and show plain colors
                    float cellsPerPixel = fwidth(i.uv.x) + fwidth(i.uv.y);
                    float mask = 1.0 - smoothstep(-cellsPerPixel * 0.5, cellsPerPixel * 0.5, dist);
                    mask = lerp(mask, 1.0, saturate((cellsPerPixel - 0.25) / 0.35));

                    albedo = lerp(_BodyColor.rgb, sticker, mask);
                    specular = lerp(0.05, _Specular, mask);
                }

                float3 n = normalize(i.normal);
                float3 toLight = normalize(float3(0.45, 0.8, -0.4));   // roughly the scene's directional light
                float3 toCamera = normalize(_WorldSpaceCameraPos - i.worldPos);
                float diffuse = saturate(dot(n, toLight));
                float fill = saturate(dot(n, toCamera)) * 0.25;         // so no face turns black from any angle
                float spec = pow(saturate(dot(n, normalize(toLight + toCamera))), 48) * specular;
                return float4(albedo * (_Ambient + diffuse * 0.55 + fill) + spec, 1);
            }
            ENDCG
        }
    }
}
