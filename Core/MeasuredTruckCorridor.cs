using System.Numerics;
namespace MautAssistent;

// Positive model-intrusion evidence at concrete selected-route poses of the
// actual UNPADDED measured truck box. The 0.25m samples do not prove continuous
// collision absence. No clearance/tracking/motion uncertainty confers emergency
// authority here; those belong to the separate ordinary-caution prediction.
public sealed class MeasuredTruckCorridor
{
    readonly PreparedBox[] poses;
    readonly V3 minimum,maximum;
    readonly double currentAlong;
    public int SampleCount=>poses.Length;
    public double HorizonMetres {get;}
    MeasuredTruckCorridor(PreparedBox[] prepared,V3 min,V3 max,double current,double horizon)
    {poses=prepared;minimum=min;maximum=max;currentAlong=current;HorizonMetres=horizon;}

    public static MeasuredTruckCorridor? Create(VehicleBody truck,RouteTrace road,Vector3 own,double speed)
    {
        if(truck==null||road==null||!double.IsFinite(speed)||speed<0||!Finite(own)||!Finite(truck.Position)||!Finite(truck.Minimum)||!Finite(truck.Maximum)||
            truck.Maximum.X<=truck.Minimum.X||truck.Maximum.Y<=truck.Minimum.Y||truck.Maximum.Z<=truck.Minimum.Z||
            !float.IsFinite(truck.Rotation.LengthSquared())||Math.Abs(truck.Rotation.LengthSquared()-1)>.01)return null;
        var projected=road.ProjectForTracking(truck.Position);var current=road.ProjectForTracking(own);
        if(!double.IsFinite(projected.Along)||!double.IsFinite(current.Along))return null;
        double horizon=Math.Clamp(speed*speed/5+speed*.5+10,15,200),end=Math.Min(road.Length,projected.Along+horizon);
        if(end<=projected.Along||!road.TrySlice(projected.Along,end,.25,out var points,out _))return null;
        var first=points[0];var anchor=truck.Position-first.Position;
        var initialQ=Quaternion.Normalize(truck.Rotation);double initialYaw=ArticulatedRigPrediction.Yaw(initialQ);
        var localCenter=((V3)truck.Minimum+(V3)truck.Maximum)*.5;var half=((V3)truck.Maximum-(V3)truck.Minimum)*.5;
        var prepared=new PreparedBox[points.Count];V3 min=new(double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity),
            max=new(double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity);
        for(int i=0;i<points.Count;i++) {
            var point=points[i];double yaw=i==0?initialYaw:Math.Atan2(-point.Direction.X,-point.Direction.Z);
            var q=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(yaw-initialYaw))*initialQ);
            var box=Prepare((V3)(point.Position+anchor),localCenter,half,q,point.Along);
            prepared[i]=box;min=V3.Min(min,box.Minimum);max=V3.Max(max,box.Maximum);
        }
        return new(prepared,min,max,current.Along,end-projected.Along);
    }

    public double? Distance(SceneBody body,double age)
    {
        if(body==null||(body.Flags&2)==0||(body.Flags&~3U)!=0||!float.IsFinite(body.Speed)||body.Speed is <0 or >.1f||
            !double.IsFinite(age)||age is <0 or >.2||!Finite(body.Center)||!Finite(body.HalfExtents)||
            body.HalfExtents.X<=0||body.HalfExtents.Y<=0||body.HalfExtents.Z<=0||
            !float.IsFinite(body.Rotation.LengthSquared())||Math.Abs(body.Rotation.LengthSquared()-1)>.001)return null;
        var actor=Prepare((V3)body.Center,default,(V3)body.HalfExtents,Quaternion.Normalize(body.Rotation),0);
        if(!Overlap(minimum,maximum,actor.Minimum,actor.Maximum))return null;
        // No route projection, sampling, heap collection or pose allocation is
        // performed per actor. The prepared immutable route is reusable.
        for(int i=0;i<poses.Length;i++) {
            ref readonly var pose=ref poses[i];
            if(Overlap(pose.Minimum,pose.Maximum,actor.Minimum,actor.Maximum)&&Intersects(pose,actor))
                return Math.Max(0,pose.Along-currentAlong);
        }
        return null;
    }

    readonly record struct V3(double X,double Y,double Z)
    {
        public static explicit operator V3(Vector3 v)=>new(v.X,v.Y,v.Z);
        public static V3 operator +(V3 a,V3 b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static V3 operator -(V3 a,V3 b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static V3 operator *(V3 a,double b)=>new(a.X*b,a.Y*b,a.Z*b);
        internal static V3 Min(V3 a,V3 b)=>new(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Min(a.Z,b.Z));
        internal static V3 Max(V3 a,V3 b)=>new(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y),Math.Max(a.Z,b.Z));
        internal static V3 Abs(V3 v)=>new(Math.Abs(v.X),Math.Abs(v.Y),Math.Abs(v.Z));
    }
    readonly record struct PreparedBox(V3 Center,V3 Half,V3 X,V3 Y,V3 Z,V3 Minimum,V3 Maximum,double Along)
    {internal V3 Axis(int index)=>index==0?X:index==1?Y:Z;}
    static PreparedBox Prepare(V3 root,V3 localCenter,V3 half,Quaternion q,double along)
    {
        var x=(V3)Vector3.Transform(Vector3.UnitX,q);var y=(V3)Vector3.Transform(Vector3.UnitY,q);var z=(V3)Vector3.Transform(Vector3.UnitZ,q);
        var center=root+x*localCenter.X+y*localCenter.Y+z*localCenter.Z;
        var extent=V3.Abs(x)*half.X+V3.Abs(y)*half.Y+V3.Abs(z)*half.Z;
        return new(center,half,x,y,z,center-extent,center+extent,along);
    }
    static bool Intersects(in PreparedBox own,in PreparedBox actor)
    {
        var delta=actor.Center-own.Center;
        for(int i=0;i<3;i++)if(Separates(own.Axis(i),delta,own,actor)||Separates(actor.Axis(i),delta,own,actor))return false;
        for(int i=0;i<3;i++)for(int j=0;j<3;j++)if(Separates(Cross(own.Axis(i),actor.Axis(j)),delta,own,actor))return false;
        return true;
    }
    static bool Separates(V3 axis,V3 delta,in PreparedBox a,in PreparedBox b)
    {
        if(Dot(axis,axis)<1e-20)return false;
        double support=Math.Abs(Dot(axis,a.X))*a.Half.X+Math.Abs(Dot(axis,a.Y))*a.Half.Y+Math.Abs(Dot(axis,a.Z))*a.Half.Z+
            Math.Abs(Dot(axis,b.X))*b.Half.X+Math.Abs(Dot(axis,b.Y))*b.Half.Y+Math.Abs(Dot(axis,b.Z))*b.Half.Z;
        return Math.Abs(Dot(axis,delta))>support; // exact model boxes; no planning padding
    }
    static V3 Cross(V3 a,V3 b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    static double Dot(V3 a,V3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static bool Overlap(V3 a,V3 b,V3 c,V3 d)=>a.X<=d.X&&b.X>=c.X&&a.Y<=d.Y&&b.Y>=c.Y&&a.Z<=d.Z&&b.Z>=c.Z;
    static bool Finite(Vector3 p)=>SceneSnapshotProtocol.Finite(p)&&Math.Max(Math.Abs(p.X),Math.Max(Math.Abs(p.Y),Math.Abs(p.Z)))<=1e7;
}
