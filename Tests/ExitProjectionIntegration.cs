using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using MautAssistent;

public static class ExitProjectionIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var curve=new NavCurve {StartPosition=Vector3.Zero,EndPosition=new(60,0,60),
            StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2),
            Length=97.5f,SemaphoreId=12,AllowedVehicles=AllowedVehicles.LargeVehicles};
        var descriptor=new PrefabDescriptor {NavCurves=[curve]};
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Descriptor=descriptor;prefab.Prefab=new Prefab {Uid=300};
        var path=new PrefabPath(prefab,new Node {Uid=301},new Node {Uid=302},[curve],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        float worst=0;
        for(int i=1;i<100;i++) {
            var actual=path.Interpolate(i/100f,0)!.Value.Position;
            var projected=path.Interpolate(path.GetFactorForPoint(actual),0)!.Value.Position;
            worst=Math.Max(worst,Vector3.Distance(actual,projected));
        }
        Console.WriteLine($"  original curved exit: false native path offset {worst:F3} m");
        assert(worst>2.2f,"Regression must reproduce the host's false route loss");
        foreach(var direction in new[]{Direction.Forward,Direction.Backward}) {
            var original=new PrefabPath(prefab,path.StartNode,path.EndNode,[curve],direction,
                Matrix4x4.CreateRotationY(.7f),new Vector3(100,4,200));
            var refined=PrefabRefinement.Refine(original);
            float corrected=0;
            for(int i=0;i<=1000;i++) {
                var actual=original.Interpolate(i/1000f,0)!.Value.Position;
                var projected=refined.Interpolate(refined.GetFactorForPoint(actual),0)!.Value.Position;
                corrected=Math.Max(corrected,Vector3.Distance(actual,projected));
            }
            Console.WriteLine($"  refined {direction} exit: native path offset {corrected:F4} m");
            assert(corrected<.05f,"Refined native projection must follow the actual curved lane");
            assert(Vector3.Distance(original.Interpolate(0,0)!.Value.Position,refined.Interpolate(0,0)!.Value.Position)<.001,"Entry changed");
            assert(Vector3.Distance(original.Interpolate(1,0)!.Value.Position,refined.Interpolate(1,0)!.Value.Position)<.001,"Exit changed");
            assert(refined.Curves.Count(c=>c.SemaphoreId==12)==1,"Gate duplicated or lost");
            assert(refined.Curves.All(c=>c.AllowedVehicles==AllowedVehicles.LargeVehicles),"Vehicle restriction lost");
            assert(ReferenceEquals(PrefabRefinement.Refine(refined),refined),"Refinement must be idempotent");
            assert(ReferenceEquals(PrefabRefinement.OriginalFor(refined),original),"Original topology must remain accessible");
        }
        assert(path.Curves.Count==1 && ReferenceEquals(path.Curves[0],curve),"Do not mutate map geometry");
    }
}
