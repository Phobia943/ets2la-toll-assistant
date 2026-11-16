using System.Numerics;
namespace MautAssistent;
public static class RigReach
{
    // SCS hook.position is model-local. Both native poses must have been
    // independently matched to their contemporaneous SCS origins by the caller.
    // This sphere permits any articulation, pitch or roll, not just yaw.
    public static float Radius(VehicleGeometry rig,Vector3? truckHook,Vector3? trailerHook)
    {
        static bool Valid(VehicleBody b)=>SceneSnapshotProtocol.Finite(b.Position)&&SceneSnapshotProtocol.Finite(b.Minimum)&&
            SceneSnapshotProtocol.Finite(b.Maximum)&&b.Position.Length()<=1e7&&
            float.IsFinite(b.Rotation.LengthSquared())&&Math.Abs(b.Rotation.LengthSquared()-1)<=.001&&
            b.Maximum.X>b.Minimum.X&&b.Maximum.Y>b.Minimum.Y&&b.Maximum.Z>b.Minimum.Z&&
            (b.Maximum-b.Minimum).Length()<=100;
        if(!rig.TrailerKnown||!Valid(rig.Truck)||rig.Trailer!=null&&!Valid(rig.Trailer))return float.NaN;
        static IEnumerable<Vector3> LocalCorners(VehicleBody b)=>Enumerable.Range(0,8).Select(i=>new Vector3(
            (i&1)==0?b.Minimum.X:b.Maximum.X,(i&2)==0?b.Minimum.Y:b.Maximum.Y,(i&4)==0?b.Minimum.Z:b.Maximum.Z));
        float result=LocalCorners(rig.Truck).Max(p=>p.Length());
        if(rig.Trailer is { } trailer) {
            if(!rig.TrailerKnown||truckHook is not Vector3 th||trailerHook is not Vector3 ah||
                !SceneSnapshotProtocol.Finite(th)||!SceneSnapshotProtocol.Finite(ah)||th.Length()>50||ah.Length()>50)return float.NaN;
            Vector3 World(Vector3 p,VehicleBody b)=>b.Position+Vector3.Transform(p,Quaternion.Normalize(b.Rotation));
            if(Vector3.Distance(World(th,rig.Truck),World(ah,trailer))>.25f)return float.NaN;
            result=Math.Max(result,th.Length()+LocalCorners(trailer).Max(p=>Vector3.Distance(p,ah))+.25f);
        }
        // Covers current road-root height/offset registration tolerance and
        // body/model/cargo interpolation reserve. No cached permission results.
        result+=1;
        return float.IsFinite(result)&&result is >1 and <=60?result:float.NaN;
    }
}
