// Battery look: metal caps at both ends, the label texture wrapped around the body
// (or, with "Use label texture" off, plain colours: yellow band at the + end, black body).
// The BatteryLook script sets _AxisStart (+ end) and _AxisEnd (- end) from the mesh,
// so the CAD mesh needs no UVs.
Shader "ARManual/Battery"
{
    Properties
    {
        [NoScaleOffset] _LabelTex ("Label (left edge = + end, wraps around)", 2D) = "white" {}
        [Toggle] _UseLabel ("Use label texture", Float) = 1
        [Toggle] _MirrorLabel ("Mirror label (if the text reads backwards)", Float) = 0
        _LabelTurn ("Turn label around the battery", Range(0, 1)) = 0
        _BodyColor ("Body", Color) = (0.04, 0.04, 0.04, 1)
        _BandColor ("Band at the + end", Color) = (0.98, 0.82, 0.0, 1)
        _MetalColor ("Metal caps", Color) = (0.82, 0.82, 0.84, 1)
        _BandLength ("Band length (part of the battery)", Range(0, 1)) = 0.27
        _CapLength ("Metal cap length (part of the battery)", Range(0, 0.2)) = 0.03
        _Smoothness ("Smoothness", Range(0, 1)) = 0.8
        _MetalMetallic ("Metal caps: metallic", Range(0, 1)) = 0.6
        [HideInInspector] _AxisStart ("Axis start (+ end, object space)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AxisEnd ("Axis end (- end, object space)", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_LabelTex);
            SAMPLER(sampler_LabelTex);

            CBUFFER_START(UnityPerMaterial)
                float _UseLabel;
                float _MirrorLabel;
                float _LabelTurn;
                half4 _BodyColor;
                half4 _BandColor;
                half4 _MetalColor;
                float _BandLength;
                float _CapLength;
                float _Smoothness;
                float _MetalMetallic;
                float4 _AxisStart;
                float4 _AxisEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 0 at the + end, 1 at the - end.
                float3 axis = _AxisEnd.xyz - _AxisStart.xyz;
                float t = saturate(dot(input.positionOS - _AxisStart.xyz, axis) / max(dot(axis, axis), 1e-8));

                half3 albedo = _BodyColor.rgb;
                half metallic = 0;
                if (t < _CapLength || t > 1 - _CapLength)
                {
                    albedo = _MetalColor.rgb;
                    metallic = _MetalMetallic;
                }
                else if (_UseLabel > 0.5)
                {
                    // Wrap the label around the battery: u along the axis (between the caps),
                    // v = angle around the axis. The mesh needs no UVs.
                    float3 dir = axis * rsqrt(max(dot(axis, axis), 1e-12));
                    float3 helper = abs(dir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                    float3 side1 = normalize(cross(dir, helper));
                    float3 side2 = cross(dir, side1);
                    float3 p = input.positionOS - _AxisStart.xyz;

                    float u = (t - _CapLength) / max(1 - 2 * _CapLength, 1e-4);
                    float v = atan2(dot(p, side2), dot(p, side1)) / (2 * PI) + 0.5;
                    if (_MirrorLabel > 0.5) v = 1 - v;
                    v = frac(v + _LabelTurn);

                    // The angle jumps from 1 to 0 at one seam; take the smaller of two
                    // derivatives so the texture filtering doesn't draw a line there.
                    float2 uv = float2(u, v);
                    float v2 = frac(v + 0.5);
                    float2 dx = ddx(uv), dy = ddy(uv);
                    float dx2 = ddx(v2), dy2 = ddy(v2);
                    dx.y = abs(dx.y) < abs(dx2) ? dx.y : dx2;
                    dy.y = abs(dy.y) < abs(dy2) ? dy.y : dy2;

                    albedo = SAMPLE_TEXTURE2D_GRAD(_LabelTex, sampler_LabelTex, uv, dx, dy).rgb;
                }
                else if (t < _CapLength + _BandLength)
                {
                    albedo = _BandColor.rgb;
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = metallic;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.normalTS = half3(0, 0, 1);

                return UniversalFragmentPBR(inputData, surface);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
