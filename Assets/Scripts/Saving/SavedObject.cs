using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    public class SavedObject : MonoBehaviour {
        public string id;
        public string nameId => id.Split('-')[0];
        public string state;
        public string customDefinitionId;
        public string customInstanceId;
        [SerializeField] internal string documentId;
        public RobotPart DocumentPart { get; internal set; }
        internal Transform CachedTransform { get; private set; }
        internal Renderer CachedRenderer { get; private set; }

        private void Awake() { CachedTransform = transform; CachedRenderer = GetComponent<Renderer>(); }
        private void Start() { ShadowRenderProxy.RemoveClones(this); PartHoles.Compact(gameObject); RobotDocument.Synchronize(this); }
        private void OnEnable() => SceneActivity.Register(this);
        private void OnDisable() => SceneActivity.Unregister(this);
        private void OnDestroy() => RobotDocument.Forget(this);
    }
}
