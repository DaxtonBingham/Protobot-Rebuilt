using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot {
    public class OverridePartGenerator : PartGenerator {
        public OverrideCatalog.Entry entry;
        private readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        public override List<string> GetParam1Options() { return entry.variants.Select(v=>v.value).ToList(); }
        public override List<string> GetParam2Options() { return new List<string>(); }
        private GameObject Template() {
            string value = UsesParams ? param1.value : entry.variants[0].value;
            GameObject template;
            if (!templates.TryGetValue(value,out template)) {
                var variant=entry.variants.FirstOrDefault(v=>v.value==value);
                if (variant==null) throw new ArgumentException("Unknown Override variant: "+value);
                template=OverrideCatalog.CreateTemplate(entry,variant);
                templates.Add(value,template);
            }
            return template;
        }
        public override Mesh GetMesh() { return Template().GetComponent<MeshFilter>().sharedMesh; }
        public override bool TryGetPartData(out PartData partData) { partData=Template().GetComponent<PartData>(); return true; }
        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var obj=Instantiate(Template(),position,rotation);
            obj.name=entry.name;
            RemoveDataScripts(obj); SetId(obj);
            return obj;
        }
    }
}
