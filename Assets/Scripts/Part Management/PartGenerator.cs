using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using Protobot.Outlining;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot {
    public static class PartParameterValue {
        public static bool TryParse(string text, out float value) {
            // IDs are portable across Windows regional settings. Older versions
            // also wrote decimal commas; accept those without treating dots as thousands.
            bool parsed = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            if (!parsed && text != null && text.IndexOf('.') < 0)
                parsed = float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && !float.IsNaN(value) && !float.IsInfinity(value);
        }
        public static float Parse(string text) {
            if (!TryParse(text, out float value)) throw new FormatException("Invalid part dimension: " + text);
            return value;
        }
        public static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    [Serializable]
    public class Parameter {
        public string name;
        public string value;
        
        [Header("Custom")]
        public bool custom;
        public string customUnit;
        public string customDefault;
        public Vector2 customLimits;
    }
    
    public abstract class PartGenerator : MonoBehaviour {
        public Parameter param1;
        public Parameter param2;
        public bool UsesParams => param1.name.Length > 0;
        public bool UsesTwoParams => param2.name.Length > 0;
        
        public void InitParamValues() {
            if (!UsesParams) return;

            param1.value = param1.custom ? param1.customDefault : GetParam1Options()[0];

            if (UsesTwoParams)
                param2.value = param2.custom ? param2.customDefault : GetParam2Options()[0];
        }
        
        public abstract List<string> GetParam1Options();
        public abstract List<string> GetParam2Options();

        public string GetId() {
            var partType = GetComponent<PartType>();
            var id = partType.id;
            
            if (UsesParams) {
                id += "-" + param1.value;

                if (UsesTwoParams)
                    id += "-" + param2.value;
            }

            return id;
        }

        public void SetId(GameObject obj) {
            var savedObj = obj.GetComponent<SavedObject>();

            if (savedObj == null)
                savedObj = obj.AddComponent<SavedObject>();
            
            savedObj.id = GetId();
            PartHoles.Compact(obj);
            RobotDocument.Synchronize(savedObj);
        }
        public abstract Mesh GetMesh();
        public abstract GameObject Generate(Vector3 position, Quaternion rotation);

        public virtual bool TryGetPartData(out PartData partData) {
            partData = null;
            return false;
        }
        
        public void RemoveDataScripts(GameObject obj) {
            if (obj.TryGetComponent(out PartData partData)) 
                Destroy(partData);
        }
    }
}
