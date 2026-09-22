Shader "Hidden/Protobot/Viewport Luma" {
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VertUVTransform
            #pragma fragment Fragment
            #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
            #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Colors.hlsl"
            TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
            float _StopNaN, _KeepAlpha;
            half4 Fragment(VaryingsDefault input) : SV_Target {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texcoord);
                if (_StopNaN > .5 && (any(isnan(color)) || any(isinf(color)))) color = 0;
                #if UNITY_COLORSPACE_GAMMA
                    color = SRGBToLinear(color);
                #endif
                if (_KeepAlpha < .5) color.a = Luminance(saturate(color));
                #if UNITY_COLORSPACE_GAMMA
                    color = LinearToSRGB(color);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
