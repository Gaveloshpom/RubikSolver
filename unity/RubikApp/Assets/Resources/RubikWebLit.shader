// Minimal lit shader used for the cube in the web build. URP Lit samples shadow maps, and on WebGL
// Chrome rejects its placeholder shadow textures ("Mismatch between texture format and sampler type"),
// so nothing lit is drawn. This shader does its own lighting and never touches shadow textures.
Shader "Rubik/WebLit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
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

            float4 _BaseColor;
            float _Ambient;
            float _Specular;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 worldPos : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 toLight = normalize(float3(0.45, 0.8, -0.4));   // key light, roughly the scene's directional light
                float3 toCamera = normalize(_WorldSpaceCameraPos - i.worldPos);
                float diffuse = saturate(dot(n, toLight));
                float fill = saturate(dot(n, toCamera)) * 0.25;         // so no face turns black from any angle
                float spec = pow(saturate(dot(n, normalize(toLight + toCamera))), 48) * _Specular;
                return float4(_BaseColor.rgb * (_Ambient + diffuse * 0.55 + fill) + spec, 1);
            }
            ENDCG
        }
    }
}
