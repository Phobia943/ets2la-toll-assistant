using System.Numerics;

namespace MautAssistent;

// A map gate's explicit activation area binds the approach. This is not an
// animated-arm mesh or a clearance certificate, and grants no interaction.
public sealed record MapGateArea(ulong Uid,Vector3 Anchor,Quaternion Rotation,
    Vector3 Activation,Quaternion ActivationRotation,float Width,float Length);
public sealed record MapGateCandidate(ulong Uid,Vector3 Anchor,Quaternion Rotation,double PlaneAlong,double StopAlong);
public static class MapGateApproach
{
    public const double FrontReserve=5;
    public static bool Valid(MapGateArea g)=>g.Uid!=0&&Finite(g.Anchor)&&Finite(g.Activation)&&Rotation(g.Rotation)&&Rotation(g.ActivationRotation)&&
        float.IsFinite(g.Width)&&g.Width is >=2 and <=10&&float.IsFinite(g.Length)&&g.Length is >=2 and <=30;
    static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)&&Math.Abs(p.X)<=262144&&Math.Abs(p.Y)<=10000&&Math.Abs(p.Z)<=262144;
    static bool Rotation(Quaternion q)=>float.IsFinite(q.LengthSquared())&&Math.Abs(q.LengthSquared()-1)<.001;
    public static MapGateCandidate? Bind(MapGateArea gate,IReadOnlyList<TraceSlicePoint> points,double own)
    {
        if(!Valid(gate)||points.Count is <2 or >1000||!double.IsFinite(own)||own<0)return null;
        var forward=-Vector3.Transform(Vector3.UnitZ,gate.ActivationRotation);
        var across=Vector3.Transform(Vector3.UnitX,gate.ActivationRotation);
        if(Math.Abs(forward.Y)>.03||Math.Abs(across.Y)>.03||
            Vector3.Dot(forward,-Vector3.Transform(Vector3.UnitZ,gate.Rotation))<.99)return null;
        var delta=gate.Anchor-gate.Activation;double plane=Vector3.Dot(delta,forward);
        // An activation area on the far side, another deck or a distant gate
        // cannot lend authority. The post may stand beside the driving line.
        if(plane is <1 or >12||plane>gate.Length/2-.25||Math.Abs(delta.Y)>1||Math.Abs(Vector3.Dot(delta,across))>gate.Width/2+2)return null;
        Vector3 Local(Vector3 p){p-=gate.Activation;return new(Vector3.Dot(p,across),p.Y,Vector3.Dot(p,forward));}
        bool admitted=false;int crossings=0;double crossingAlong=0;
        for(int i=1;i<points.Count;i++) {
            var a=points[i-1];var b=points[i];var x=Local(a.Position);var y=Local(b.Position);
            double step=b.Along-a.Along;
            if(!double.IsFinite(step)||step is <=0 or >1||!Finite(a.Position)||!Finite(b.Position)||
                !Finite(a.Direction)||!Finite(b.Direction)||
                a.Direction.LengthSquared() is <.9f or >1.1f||b.Direction.LengthSquared() is <.9f or >1.1f||
                Math.Abs(step-Vector3.Distance(a.Position,b.Position))>.05)return null;
            bool inside=Math.Abs(x.X)<=gate.Width/2-.25&&Math.Abs(x.Y)<=1&&Math.Abs(x.Z)<=gate.Length/2;
            // The actual directed route must enter the explicitly owned area,
            // then continue through the gate plane. Merely passing its post,
            // or turning away after touching the activation area, is not enough.
            if(!admitted&&inside&&x.Z<plane&&Vector3.Dot(a.Direction,forward)>.95)admitted=true;
            if(admitted&&crossings==0) {
                if(Math.Abs(y.X)>gate.Width/2-.25||Math.Abs(y.Y)>1||y.Z<=x.Z||Vector3.Dot(b.Direction,forward)<.95)return null;
                if(x.Z<=plane&&y.Z>=plane) {
                    double t=(plane-x.Z)/(y.Z-x.Z);crossingAlong=a.Along+t*step;
                    if(crossingAlong<own||crossingAlong>=points[^1].Along-.01)return null;
                    crossings++;
                }
            }else if(crossings>0&&x.Z<plane&&y.Z>=plane)return null; // loop/reused crossing
        }
        return crossings==1?new(gate.Uid,gate.Anchor,gate.Rotation,crossingAlong,Math.Max(0,crossingAlong-FrontReserve)):null;
    }
}
