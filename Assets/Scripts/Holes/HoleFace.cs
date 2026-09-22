using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    public class HoleFace : MonoBehaviour {
        public Vector3 direction;
        public Vector3 position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public Quaternion LookRotation => Quaternion.LookRotation(direction, transform.up);
        public HoleData hole;

        [CacheComponent] private MeshFilter meshFilter;

        private void OnEnable() => ViewportPresentation.Changed();
        private void OnDisable() => ViewportPresentation.Changed();

        public void Set(HoleData newHole, Vector3 newDir) {
            var pose = transform;
            var rotation = Quaternion.LookRotation(-newDir, newHole.rotation * Vector3.up);
            var newPos = newHole.position + newDir * (newHole.depth / 2);
            var scale = new Vector3(newHole.size.x, newHole.size.y, 0.001f);
            bool moved = pose.rotation != rotation || pose.position != newPos;
            bool resized = pose.localScale != scale;
            bool reshaped = meshFilter.sharedMesh != newHole.shape;
            if (hole != newHole || direction != newDir || moved || resized || reshaped) ViewportPresentation.Changed();
            if (moved) pose.SetPositionAndRotation(newPos, rotation);
            if (resized) pose.localScale = scale;
            if (reshaped) meshFilter.sharedMesh = newHole.shape;
            direction = newDir;
            hole = newHole;
        }

        public void Set(HoleFace newHoleFace) => Set(newHoleFace.hole, newHoleFace.direction);
    }
}
