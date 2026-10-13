using UnityEngine;
using System.Collections;
using System;
using System.Runtime.Serialization;

namespace Protobot.Builds {
    [Serializable]
    public class CameraData {
        [OptionalField] public double xPos, yPos, zPos;
        [OptionalField] public double xRot, yRot, zRot;
        [OptionalField] public double zoom;
        [OptionalField] public bool isOrtho;
    }
}
