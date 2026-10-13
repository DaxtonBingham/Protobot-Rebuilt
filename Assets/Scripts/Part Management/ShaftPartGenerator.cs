using System;
using System.Collections;
using System.Collections.Generic;
using Parts_List;
using Protobot;
using UnityEngine;
using UnityEngine.UIElements;

public class ShaftPartGenerator : PartGenerator {
    [SerializeField] private GameObject normalShaftInch;
    [SerializeField] private GameObject hsShaftInch;
    
    private List<string> Types => new List<string>(){"Normal", "High Strength"};
    public override List<string> GetParam1Options() => Types;

    public override List<string> GetParam2Options() => new List<string>{" "};

    public override Mesh GetMesh() {
        Mesh refMesh;

        if (param1.value == "Normal") {
            refMesh = normalShaftInch.GetComponent<MeshFilter>().sharedMesh;
            param2.customLimits.y = 12;
        }
        else {
            refMesh = hsShaftInch.GetComponent<MeshFilter>().sharedMesh;
            param2.customLimits.y = 24;
        }

        Mesh mesh = new Mesh() {
            vertices = refMesh.vertices,
            triangles = refMesh.triangles,
            tangents = refMesh.tangents,
            normals = refMesh.normals
        };

        float zScale = PartParameterValue.Parse(param2.value);

        var vertices = mesh.vertices;

        for (int i = 0; i < mesh.vertexCount; i++) {
            var vert = vertices[i];
            vert.z *= zScale;

            vertices[i] = vert;
        }
        
        mesh.SetVertices(vertices);
        
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }

    public override GameObject Generate(Vector3 position, Quaternion rotation) {
        GameObject temp = normalShaftInch;
        if (param1.value == "High Strength") 
        {
            temp = hsShaftInch;
        }

        Vector3 scale = temp.transform.localScale;
        scale.z = PartParameterValue.Parse(param2.value);

        GameObject newObj = Instantiate(temp, position, rotation);
        newObj.transform.localScale = scale;
        string shaftType;
        var partList = newObj.AddComponent<PartName>();

        if(param1.value == "High Strength") 
        { 
            shaftType = "\" HS Shaft"; 
            partList.weightInGrams = scale.z * 6.5f;
        } else 
        { 
            shaftType = "\" Shaft"; 
            partList.weightInGrams = scale.z * 1.9f;
        }

        partList.name = PartParameterValue.Format(scale.z) + shaftType;

        SetId(newObj);
        RemoveDataScripts(newObj);

        return newObj;
    }
}
