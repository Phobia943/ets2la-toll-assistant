using System.Numerics;
namespace MautAssistent;

public readonly record struct SceneCurve(Vector3 A,Vector3 B,Vector3 C,Vector3 D);
public static class SceneClearance
{
    public static bool OutsideRig(SceneCurve curve,SceneBody body,float radius,float motionReserve)
    {
        if(!float.IsFinite(radius)||radius is <1 or >60||!float.IsFinite(motionReserve)||motionReserve is <0 or >100)return false;
        // An upright cylinder with radius R and vertical [-R,+R] contains the
        // complete rig sphere. Offset the centre by -R before the ordinary
        // cylinder test, whose vertical interval starts at the curve itself.
        var down=new Vector3(0,-radius,0);
        return Outside(new(curve.A+down,curve.B+down,curve.C+down,curve.D+down),
            body with{HalfExtents=body.HalfExtents+new Vector3(motionReserve)},radius*2,radius*2,true);
    }
    // The entire cubic Bezier lies in its control hull. Test separating axes
    // of the actual oriented vehicle box, subdividing until proved separate.
    // Own enclosure is a horizontal disk plus vertical [road,road+height].
    // Inconclusive overlap is occupied; no sampled-point clearance assertion.
    public static bool Outside(SceneCurve curve,SceneBody body,float height,float width,bool rigEnclosure=false)
    {
        if(!float.IsFinite(height)||height<2||height>(rigEnclosure?120:8)||!float.IsFinite(width)||width<2||width>(rigEnclosure?120:5)||
            !SceneSnapshotProtocol.Finite(body.Center)||!SceneSnapshotProtocol.Finite(body.HalfExtents)||
            body.HalfExtents.X<=0||body.HalfExtents.Y<=0||body.HalfExtents.Z<=0||
            !float.IsFinite(body.Rotation.LengthSquared())||Math.Abs(body.Rotation.LengthSquared()-1)>.001||
            new[]{curve.A,curve.B,curve.C,curve.D}.Any(v=>!SceneSnapshotProtocol.Finite(v)))return false;
        var inverse=Quaternion.Inverse(body.Rotation);
        var shift=new Vector3(0,height/2,0);
        Vector3 Local(Vector3 p)=>Vector3.Transform(p+shift-body.Center,inverse);
        var up=Vector3.Transform(Vector3.UnitY,inverse);
        // Transform support of a vertical cylinder into each box axis. Add
        // 20cm numerical/pose reserve; speed/latency gets additional expansion.
        Vector3 reserve=new(
            width/2*MathF.Sqrt(MathF.Max(0,1-up.X*up.X))+height/2*MathF.Abs(up.X)+.2f,
            width/2*MathF.Sqrt(MathF.Max(0,1-up.Y*up.Y))+height/2*MathF.Abs(up.Y)+.2f,
            width/2*MathF.Sqrt(MathF.Max(0,1-up.Z*up.Z))+height/2*MathF.Abs(up.Z)+.2f);
        var extent=body.HalfExtents+reserve;
        bool Separate(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int depth) {
            var min=Vector3.Min(Vector3.Min(a,b),Vector3.Min(c,d));var max=Vector3.Max(Vector3.Max(a,b),Vector3.Max(c,d));
            if(min.X>extent.X||max.X< -extent.X||min.Y>extent.Y||max.Y< -extent.Y||min.Z>extent.Z||max.Z< -extent.Z)return true;
            if(depth==12)return false;
            var ab=(a+b)/2;var bc=(b+c)/2;var cd=(c+d)/2;var abc=(ab+bc)/2;var bcd=(bc+cd)/2;var mid=(abc+bcd)/2;
            return Separate(a,ab,abc,mid,depth+1)&&Separate(mid,bcd,cd,d,depth+1);
        }
        return Separate(Local(curve.A),Local(curve.B),Local(curve.C),Local(curve.D),0);
    }
}
