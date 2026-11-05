using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using MautAssistent;

public static class ConnectorSignalIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        NavCurve Line(float from,float to,int signal)=>new() {
            StartPosition=new(0,0,from),EndPosition=new(0,0,to),Length=to-from,
            StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),
            EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),
            AllowedVehicles=AllowedVehicles.AllVehicles,SemaphoreId=signal};
        var curves=new List<NavCurve>{Line(0,1,1),Line(1,20,-1)};
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Descriptor=new() {NavCurves=curves};prefab.Prefab=new() {Uid=870};
        var path=new PrefabPath(prefab,new Node {Uid=871},new Node {Uid=872},curves,Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        assert(!ConnectorStart.IsTruckConnector(path),"An upcoming connector with a signal remains ineligible");
        assert(!ConnectorStart.IsTruckConnector(path,2.9),"A passed signal still requires the complete 2 m clearance");
        assert(ConnectorStart.IsTruckConnector(path,15.9),"A fully passed signal cannot block a start on the remaining clear connector");
        var refined=PrefabRefinement.Refine(path);
        assert(ConnectorStart.IsTruckConnector(refined,15.9),"Refinement retains the passed-signal rule");
        assert(curves[0].SemaphoreId==1&&refined.Curves.Any(c=>c.SemaphoreId==1),"Signal metadata must remain intact");
        curves[1].SemaphoreId=2;
        assert(!ConnectorStart.IsTruckConnector(path,15.9),"A second signal ahead still blocks the start");
        curves[1].SemaphoreId=-1;curves[0].AllowedVehicles=AllowedVehicles.SmallVehicles;
        assert(!ConnectorStart.IsTruckConnector(path,15.9),"Vehicle restrictions cannot be bypassed on a passed section");
        assert(!ConnectorStart.IsTruckConnector(path,double.NaN)&&!ConnectorStart.IsTruckConnector(path,21),"Invalid progress cannot bypass the check");
    }
}
