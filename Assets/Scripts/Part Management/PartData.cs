using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    public class PartData : MonoBehaviour {
        [Header("Identification")]
        public string param1Value;
        public string param2Value;

        [Header("Holes")]
        [Space(5)]
        [TextArea(0, 100)]
        public string holeData;

        public float PrimaryHoleDepth => PrimaryHole != null ? PrimaryHole.depth : 0;
        public HoleCollider primaryHole;
        [SerializeField] private PartHoles primaryHoleOwner;
        [SerializeField] private int primaryHoleIndex = -1;
        public HoleData PrimaryHole => primaryHoleOwner != null && primaryHoleIndex >= 0 && primaryHoleIndex < primaryHoleOwner.Holes.Count
            ? primaryHoleOwner.Holes[primaryHoleIndex].holeData : primaryHole != null ? primaryHole.holeData : null;
        internal void BindHole(HoleCollider source, PartHoles owner, int index) {
            if (source != primaryHole) return;
            primaryHoleOwner = owner; primaryHoleIndex = index;
        }
        public bool allowCenterInserts;
    }
}
