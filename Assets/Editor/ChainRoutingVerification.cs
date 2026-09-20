using System;
using System.Collections.Generic;
using UnityEngine;
using Protobot.ChainSystem;
using Protobot;

// Run from the Unity Tools menu or -executeMethod ChainRoutingVerification.Verify.
// Uses the production solver and checks independent analytic lengths and sampled
// continuity/intersections, including regressions from the previous implementation.
public static class ChainRoutingVerification {
    [UnityEditor.MenuItem("Tools/Protobot/Verify chain routing")]
    public static void Verify() {
        int checks = 0;
        Run((passed, label) => {
            if (!passed) throw new InvalidOperationException(label);
            checks++;
        }, message => UnityEngine.Debug.Log(message));
        VerifyBevelContacts((passed, label) => {
            if (!passed) throw new InvalidOperationException(label);
            checks++;
        });
        UnityEngine.Debug.Log("Chain routing: " + checks + " regression checks passed.");
    }
    static void VerifyBevelContacts(Action<bool,string> check) {
        var objects = new List<GameObject>();
        Mesh mesh = null;
        try {
            var left = new GameObject("Routing regression left"); objects.Add(left);
            var right = new GameObject("Routing regression right"); objects.Add(right);
            left.transform.position = new Vector3(-3,0,0); right.transform.position = new Vector3(3,0,0);
            var a = left.AddComponent<ChainEndpoint>(); var b = right.AddComponent<ChainEndpoint>();
            foreach (var endpoint in new[] { a, b }) {
                var serialized = new UnityEditor.SerializedObject(endpoint);
                serialized.FindProperty("pitchRadius").floatValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            // A 1/8-inch square shaft with tiny, densely tessellated corner bevels.
            // The old support tolerance chose a neighbouring bevel and disagreed
            // with its perimeter coordinate, leaving a visible gap in the path.
            var outline = new List<Vector2>();
            for (int corner = 0; corner < 4; corner++) {
                float baseAngle = corner * Mathf.PI * .5f;
                var center = new Vector2(corner == 0 || corner == 3 ? .0525f : -.0525f, corner < 2 ? .0525f : -.0525f);
                for (int step = 0; step <= 24; step++) {
                    float angle = baseAngle + step * Mathf.PI / 48;
                    outline.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .01f);
                }
            }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            foreach (var p in outline) { vertices.Add(new Vector3(p.x,p.y,-1)); vertices.Add(new Vector3(p.x,p.y,1)); }
            for (int i = 0; i < outline.Count; i++) {
                int first = i * 2, next = ((i + 1) % outline.Count) * 2;
                triangles.AddRange(new[] { first,next,first+1, next,next+1,first+1 });
            }
            mesh = new Mesh { name = "Beveled shaft regression" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            var guides = new List<ChainEndpoint>();
            for (int i = 0; i < 3; i++) {
                var part = new GameObject("Routing regression guide"); objects.Add(part);
                part.AddComponent<SavedObject>(); part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var guide = part.AddComponent<ChainGuide>();
                guide.ConfigureManual("runtime-guide",new Vector3((i-1)*1.4f,.5f,0),Quaternion.identity,.1f);
                guides.Add(part.AddComponent<ChainEndpoint>());
            }
            for (int angle = 0; angle <= 90; angle += 15) {
                foreach (var guide in guides) {
                    guide.transform.rotation = Quaternion.AngleAxis(angle,Vector3.forward);
                    guide.GetComponent<ChainGuide>().SetWorldContactHint(Vector3.up);
                }
                check(ChainPathSolver.TrySolve(new[] { a,guides[1],b },ChainStandard.Pitch6p35,.25f,0,out var single,out var error), "Beveled shaft at " + angle + ": " + error);
                check(NoCrossing(single.poses) && Continuous(single.poses), "Beveled shaft continuity at " + angle);
                check(ChainPathSolver.TrySolve(new[] { a,guides[0],guides[1],guides[2],b },ChainStandard.Pitch6p35,.25f,0,out var multiple,out error), "Three straight-run contacts at " + angle + ": " + error);
                check(NoCrossing(multiple.poses) && Continuous(multiple.poses), "Three-contact continuity at " + angle);
            }
        } finally {
            foreach (var obj in objects) UnityEngine.Object.DestroyImmediate(obj);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
    public static void Run(Action<bool,string> check, Action<string> note) {
        List<ChainPathSolver.ChainPose> poses; float length, spacing;
        var a = new Vector3(-3,0,0); var b = new Vector3(3,0,0);
        bool ok = ChainPathSolver.TrySolveLoop(a,1,b,1,Vector3.forward,.25f,0,out poses,out length,out spacing);
        check(ok && Mathf.Abs(length-(12+2*Mathf.PI))<.002f,"Exact equal-sprocket tangent and arc length");
        check(NoCrossing(poses),"Two-sprocket route has no crossing links");
        check(Continuous(poses),"Two-sprocket joins remain tangent");
        var baseline=length;
        ok=ChainPathSolver.TrySolveLoop(a,.4f,b,1.2f,Vector3.forward,.25f,0,out poses,out length,out spacing);
        float delta=.8f; float expected=2*Mathf.Sqrt(36-delta*delta)+Mathf.PI*1.6f+2*delta*Mathf.Asin(delta/6);
        check(ok && Mathf.Abs(length-expected)<.002f,"Unequal-sprocket analytic length");
        check(NoCrossing(poses) && Continuous(poses),"Unequal-sprocket continuous uncrossed route");
        ok=ChainPathSolver.TrySolveLoop(a,1,b,1,Vector3.forward,.25f,1,out poses,out length,out spacing);
        check(ok && Mathf.Abs(length-baseline-1)<.002f,"Slack adds actual path length instead of compressing link pitch");
        check(Mathf.Abs(spacing-.25f)<.003f && NoCrossing(poses),"Slack keeps nominal spacing and a clear loop");
        ok=ChainPathSolver.TrySolveLoop(new[]{a,Vector3.zero,b},new[]{1f,.3f,1f},Vector3.forward,.25f,0,out poses,out length,out spacing);
        check(ok && NoCrossing(poses) && Continuous(poses),"Collinear three-contact crossing regression");
        ok=ChainPathSolver.TrySolveLoop(new[]{Vector3.zero,new Vector3(1,3,0),new Vector3(1,-2,0),new Vector3(-2,-1,0)},new[]{.4f,.9f,.8f,.35f},Vector3.forward,.25f,0,out poses,out length,out spacing);
        check(!ok || (NoCrossing(poses) && Continuous(poses)),"Four-contact tangent reversal regression is rejected or continuous");
        check(!ChainPathSolver.TrySolveLoop(a,1,a,1,Vector3.forward,.25f,0,out poses,out length,out spacing),"Coincident sprockets rejected");
        check(!ChainPathSolver.TrySolveLoop(a,1,a+Vector3.right,1,Vector3.forward,.25f,0,out poses,out length,out spacing),"Overlapping sprockets rejected");
        check(!ChainPathSolver.TrySolveLoop(a,1,b+Vector3.forward,1,Vector3.forward,.25f,0,out poses,out length,out spacing),"Off-plane centers rejected");
        check(!ChainPathSolver.TrySolveLoop(a,1,b,1,Vector3.zero,.25f,0,out poses,out length,out spacing),"Zero normal rejected");
        check(!ChainPathSolver.TrySolveLoop(a,1,b,1,Vector3.forward,float.NaN,0,out poses,out length,out spacing),"Nonfinite pitch rejected");
        check(!ChainPathSolver.TrySolveLoop(a,-1,b,1,Vector3.forward,.25f,0,out poses,out length,out spacing),"Negative radius rejected");
        var q=Quaternion.Euler(42,23,67); var origin=new Vector3(10,-20,5);
        ok=ChainPathSolver.TrySolveLoop(origin+q*a,1,origin+q*b,1,q*Vector3.forward,.25f,0,out poses,out length,out spacing);
        check(ok && Mathf.Abs(length-baseline)<.002f,"Route invariant under 3D rotation and translation");
        bool onPlane=true;foreach(var pose in poses)if(Mathf.Abs(Vector3.Dot(pose.position-origin,q*Vector3.forward))>.001f)onPlane=false;
        check(onPlane,"Rotated route stays in its sprocket plane");
        var clock=System.Diagnostics.Stopwatch.StartNew();
        for(int count=3;count<=24;count+=3) {
            var centers=new Vector3[count];var radii=new float[count];
            for(int i=0;i<count;i++){float angle=2*Mathf.PI*i/count;centers[i]=new Vector3(Mathf.Cos(angle)*12,Mathf.Sin(angle)*9,0);radii[i]=.3f;}
            ok=ChainPathSolver.TrySolveLoop(centers,radii,Vector3.forward,.25f,0,out poses,out length,out spacing);
            check(ok && NoCrossing(poses) && Continuous(poses),count+"-sprocket convex route");
        }
        clock.Stop(); note("Routing regression batch: "+clock.ElapsedMilliseconds+" ms (3â€“24 sprockets)");
        check(clock.ElapsedMilliseconds<5000,"Routing search remains bounded for 24 endpoints");
    }
    static bool Continuous(List<ChainPathSolver.ChainPose> poses) {
        for(int i=0;i<poses.Count;i++){
            var d=(poses[(i+1)%poses.Count].position-poses[i].position).normalized;
            if(Vector3.Dot(d,poses[i].tangent)<.7f)return false;
        }return true;
    }
    static float Cross(Vector3 a,Vector3 b){return a.x*b.y-a.y*b.x;}
    static bool NoCrossing(List<ChainPathSolver.ChainPose> poses) {
        for(int i=0;i<poses.Count;i++)for(int j=i+2;j<poses.Count;j++){
            if(i==0 && j==poses.Count-1)continue;
            var a=poses[i].position;var b=poses[(i+1)%poses.Count].position;var c=poses[j].position;var d=poses[(j+1)%poses.Count].position;
            var u=b-a;var v=d-c;float cross=Cross(u,v);if(Mathf.Abs(cross)<1e-7f)continue;
            float t=Cross(c-a,v)/cross,s=Cross(c-a,u)/cross;if(t>1e-5f&&t<1-1e-5f&&s>1e-5f&&s<1-1e-5f)return false;
        }return true;
    }
}
