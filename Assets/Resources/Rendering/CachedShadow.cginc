#ifndef PROTOBOT_CACHED_SHADOW
#define PROTOBOT_CACHED_SHADOW
UNITY_DECLARE_SHADOWMAP(_ProtobotShadowMap);
float4x4 _ProtobotWorldToShadow;
float4 _ProtobotShadowParams; // enabled, strength, texel, soft
half _ProtobotReceiveShadow;
half ProtobotShadow(float3 world) {
    if (_ProtobotShadowParams.x < .5 || _ProtobotReceiveShadow < .5) return 1;
    float4 clip = mul(_ProtobotWorldToShadow, float4(world, 1));
    float3 coord = clip.xyz / clip.w;
    coord.xy = coord.xy * .5 + .5;
    #if UNITY_UV_STARTS_AT_TOP
        coord.y = 1 - coord.y;
    #endif
    if (any(coord.xy < 0) || any(coord.xy > 1) || coord.z < 0 || coord.z > 1) return 1;
    half shadow = 0;
    if (_ProtobotShadowParams.w < .5) shadow = UNITY_SAMPLE_SHADOW(_ProtobotShadowMap, coord);
    else {
        [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
            shadow += UNITY_SAMPLE_SHADOW(_ProtobotShadowMap, coord + float3(x, y, 0) * _ProtobotShadowParams.z);
        shadow /= 9;
    }
    return lerp(1, shadow, _ProtobotShadowParams.y);
}
#endif
