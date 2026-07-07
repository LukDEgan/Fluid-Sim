Shader "Custom/ParticleShader3D"
{
    Properties
    {
        _ColorA ("Slow Color", Color) = (0, 0, 1, 1)
        _ColorB ("Fast Color", Color) = (1, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        ZWrite On
        Cull Back

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            StructuredBuffer<float3> Positions;
            StructuredBuffer<float3> Velocities;

            float scale;
            float velocityMax;

            Texture2D<float4> ColourMap;
            SamplerState linear_clamp_sampler;

            fixed4 _ColorA;
            fixed4 _ColorB;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 colour : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;

                float3 particlePos = Positions[instanceID];
                float3 velocity = Velocities[instanceID];

                float speed = length(velocity);
                float colT = saturate(speed / max(velocityMax, 0.0001));

                float3 worldPos = particlePos + v.vertex.xyz * scale;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.normalWS = normalize(v.normal);
                o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(colT, 0.5), 0).rgb;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 lightDir = normalize(float3(0.4, 0.8, 0.3));
                float diffuse = saturate(dot(normalize(i.normalWS), lightDir));
                float lighting = diffuse * 0.65 + 0.35;

                return float4(i.colour * lighting, 1.0);
            }

            ENDCG
        }
    }
}
