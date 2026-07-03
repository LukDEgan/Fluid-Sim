Shader "Custom/ParticleShader"
{
    Properties
    {
        _ColorA ("Slow Color", Color) = (0, 0, 1, 1)
        _ColorB ("Fast Color", Color) = (1, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            StructuredBuffer<float2> Positions;
			StructuredBuffer<float2> Velocities;

			float scale;
			float4 colA;
			Texture2D<float4> ColourMap;
			SamplerState linear_clamp_sampler;
			float velocityMax;

            fixed4 _ColorA;
            fixed4 _ColorB;
            struct appdata{
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct v2f
			{
				
				float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 colour : TEXCOORD1;
			};

            v2f vert(appdata v, uint instanceID : SV_InstanceID){
                v2f o;

                float2 particlePos = Positions[instanceID];
                float2 velocity = Velocities[instanceID];
                float speed = length(velocity);
                float speedT = saturate(speed / velocityMax);
                float colT = speedT;

                float2 worldPos = particlePos + v.vertex.xy * scale;

                o.pos = UnityObjectToClipPos(float4(worldPos, 0, 1));
                o.uv = v.uv;
                o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(colT, 0.5), 0);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 centreOffset = (i.uv.xy - 0.5) * 2;
				float sqrDst = dot(centreOffset, centreOffset);
				float delta = fwidth(sqrt(sqrDst));
				float alpha = 1 - smoothstep(1 - delta, 1 + delta, sqrDst);

				float3 colour = i.colour;
				return float4(colour, alpha);
            }

            ENDCG
        }
    }
}
