#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
struct ProtobotInstance { float4x4 world, inverse; float4 center, extents; };
StructuredBuffer<ProtobotInstance> _ProtobotInstances;
StructuredBuffer<uint> _ProtobotVisible;
#endif
void ProtobotSetupInstance() {
    #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        uint id = _ProtobotVisible[unity_InstanceID];
        unity_ObjectToWorld = _ProtobotInstances[id].world;
        unity_WorldToObject = _ProtobotInstances[id].inverse;
    #endif
}
