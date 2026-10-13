Shader "Protobot/Cached Standard" {
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossMapScale ("Smoothness Scale", Range(0,1)) = 1
        [Enum(Metallic Alpha,0,Albedo Alpha,1)] _SmoothnessTextureChannel ("Smoothness texture channel", Float) = 0
        [Gamma] _Metallic ("Metallic", Range(0,1)) = 0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        [ToggleOff] _SpecularHighlights ("Specular Highlights", Float) = 1
        [ToggleOff] _GlossyReflections ("Glossy Reflections", Float) = 1
        _BumpScale ("Scale", Float) = 1
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _Parallax ("Height Scale", Range(0.005,0.08)) = 0.02
        _ParallaxMap ("Height Map", 2D) = "black" {}
        _OcclusionStrength ("Strength", Range(0,1)) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0,0,0)
        _EmissionMap ("Emission", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMap ("Detail Albedo", 2D) = "grey" {}
        _DetailNormalMapScale ("Scale", Float) = 1
        [Normal] _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        [Enum(UV0,0,UV1,1)] _UVSec ("UV Set", Float) = 0
        [HideInInspector] _Mode ("Mode", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _ProtobotReceiveShadow ("Receive shadow", Float) = 1
    }
    SubShader {
        Tags { "RenderType"="Opaque" "PerformanceChecks"="False" }
        LOD 300
        Pass {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vertBase
            #pragma fragment fragBase
            #pragma multi_compile_local _ _NORMALMAP
            #pragma multi_compile_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma multi_compile_local _ _EMISSION
            #pragma multi_compile_local _ _METALLICGLOSSMAP
            #pragma multi_compile_local _ _DETAIL_MULX2
            #pragma multi_compile_local _ _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma multi_compile_local _ _SPECULARHIGHLIGHTS_OFF
            #pragma multi_compile_local _ _GLOSSYREFLECTIONS_OFF
            #pragma multi_compile_local _ _PARALLAXMAP
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ProtobotSetupInstance
            #define UNITY_SETUP_BRDF_INPUT MetallicSetup
            #include "UnityCG.cginc"
            #include "IndirectInstances.cginc"
            #include "AutoLight.cginc"
            #include "CachedShadow.cginc"
            #ifdef DIRECTIONAL
                #undef UNITY_LIGHT_ATTENUATION
                #define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) half destName = _ProtobotShadowParams.x > .5 ? ProtobotShadow(worldPos) : UNITY_SHADOW_ATTENUATION(input, worldPos);
            #endif
            #include "UnityStandardCoreForward.cginc"
            ENDCG
        }
        UsePass "Standard/FORWARD_DELTA"
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vertShadowCaster
            #pragma fragment fragShadowCaster
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ProtobotSetupInstance
            #pragma multi_compile_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma multi_compile_local _ _METALLICGLOSSMAP
            #pragma multi_compile_local _ _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #define UNITY_SETUP_BRDF_INPUT MetallicSetup
            #include "UnityCG.cginc"
            #include "IndirectInstances.cginc"
            #include "UnityStandardShadow.cginc"
            ENDCG
        }
        UsePass "Standard/META"
    }
    FallBack "Standard"
    CustomEditor "StandardShaderGUI"
}
