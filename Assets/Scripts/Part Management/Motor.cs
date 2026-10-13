using System.Collections;
using System.Collections.Generic;
using Protobot.Transformations;
using UnityEngine;

namespace Protobot {
    public class Motor : MonoBehaviour {
        [SerializeField] private HoleCollider shaftHole;
        [SerializeField] private int shaftHoleIndex = -1;
        internal void BindHole(HoleCollider source, int index) { if (source == shaftHole) shaftHoleIndex = index; }
        private float HsOffset => 0.2775f;
        private float NormOffset => 0.47f;
        
        public Displacement GetShaftDisplacement(float shaftLength, bool highStrength) {
            var collection = GetComponent<PartHoles>();
            var record = collection != null && shaftHoleIndex >= 0 && shaftHoleIndex < collection.Holes.Count ? collection.Holes[shaftHoleIndex] : null;
            HoleWorld.Flush();
            if (record != null ? record.IsOccupied : shaftHole != null && shaftHole.IsOccupied) return null;
            var data = record != null ? record.holeData : shaftHole != null ? shaftHole.holeData : null;
            if (data == null) return null;
            Vector3 normal = data.forward;

            float offset = highStrength ? HsOffset : NormOffset;
            Vector3 pos = data.position + (normal * (shaftLength / 2 - offset));

            return new Displacement(pos, normal);
        }
    }
}