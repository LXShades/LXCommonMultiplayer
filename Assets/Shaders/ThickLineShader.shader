Shader "Unlit/UnityMultiplayerEssentials/ThickLineShader"
{
    Properties
    {
        _LineThickness ("Line thickness", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                fixed thickness : TEXCOORD0;
            };

            struct v2g
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                fixed thickness : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            struct g2f
            {
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Color;
            half _LineThickness;

            v2g vert (appdata v)
            {
                v2g o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.thickness = v.thickness;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            [maxvertexcount(6)]
            void geom(line v2g input[2], inout TriangleStream<g2f> triStream)
            {
                float4 side = normalize(float4(input[1].vertex.y - input[0].vertex.y, input[0].vertex.x - input[1].vertex.x, 0, 0)) * (min(input[0].vertex.w, input[1].vertex.w) / _ScreenParams.x * _LineThickness * input[0].thickness);
                g2f output;
                float4 topLeft = input[0].vertex - side, topRight = input[0].vertex + side, bottomLeft = input[1].vertex - side, bottomRight = input[1].vertex + side;

                output.color = input[0].color;
                output.vertex = topLeft;
                UNITY_TRANSFER_FOG(output, output.vertex);
                triStream.Append(output);

                output.color = input[1].color;
                output.vertex = bottomLeft;
                UNITY_TRANSFER_FOG(output, output.vertex);
                triStream.Append(output);

                output.color = input[1].color;
                output.vertex = bottomRight;
                UNITY_TRANSFER_FOG(output, output.vertex);
                triStream.Append(output);

                output.color = input[0].color;
                output.vertex = topLeft;
                UNITY_TRANSFER_FOG(output, output.vertex);
                triStream.Append(output);

                output.color = input[1].color;
                output.vertex = bottomRight;
                UNITY_TRANSFER_FOG(output, output.vertex);
                triStream.Append(output);

                output.color = input[0].color;
                output.vertex = topRight;
                UNITY_TRANSFER_FOG(output, output.vertex);
                triStream.Append(output);
            }

            fixed4 frag (g2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = i.color;
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
