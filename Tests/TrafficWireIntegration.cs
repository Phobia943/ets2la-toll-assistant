using System.Numerics;
using ETS2LA.Game.SDK;
using MautAssistent;
public static class TrafficWireIntegration
{
    public static void Run(Action<bool,string> check)
    {
        foreach(float yaw in new[]{0f,MathF.PI/2,-MathF.PI/2,MathF.PI}) {
            var physical=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
            // Actual producer writes qw,qx,qy,qz; actual provider directly
            // constructs System.Numerics.Quaternion from those four floats.
            byte[] wire=new byte[16];var values=new[]{physical.W,physical.X,physical.Y,physical.Z};
            for(int i=0;i<4;i++)BitConverter.GetBytes(values[i]).CopyTo(wire,4*i);
            var body=new ParkedVehicle {Position=new(100,4,200),Size=new(2.6f,4,12),
                Rotation=new(BitConverter.ToSingle(wire,0),BitConverter.ToSingle(wire,4),BitConverter.ToSingle(wire,8),BitConverter.ToSingle(wire,12))};
            var copied=TollPlugin.CopyBody(body,10);var corners=body.GetCornersOnGround();
            Vector3[] local=[new(-1.3f,-2,-6),new(1.3f,-2,-6),new(1.3f,-2,6),new(-1.3f,-2,6)];
            for(int i=0;i<4;i++)check(Vector3.Distance(corners[i],body.Position+Vector3.Transform(local[i],copied.Rotation))<.001,
                $"WXYZ byte orientation matches the pinned footprint, yaw={yaw}, corner={i}");
            var front=(corners[0]+corners[1]-corners[2]-corners[3])/2;
            check(Vector3.Dot(Vector3.Normalize(front),Vector3.Transform(-Vector3.UnitZ,copied.Rotation))>.99,
                "Direction credit uses the same actual front edge as the provider footprint");
        }
        var empty=new TrafficVehicle();empty.trailers=Enumerable.Range(0,3).Select(_=>new TrafficTrailer(){parent=empty}).ToArray();
        check(TollPlugin.IsEmptyTrafficSlot(empty)&&TollPlugin.IsEmptyParkedSlot(new()),"Exact fixed-capacity zero slots are vacant");
        empty.id=2;check(!TollPlugin.IsEmptyTrafficSlot(empty),"Malformed active traffic is not a vacant slot");
        empty.id=0;empty.trailers[2].Size=Vector3.One;check(!TollPlugin.IsEmptyTrafficSlot(empty),"A nonzero hidden trailer cannot be dropped as empty");
        empty.trailer_count=1;check(!TollPlugin.DeclaredTrailersPresent(empty),"An announced but empty first trailer cannot disappear as unused capacity");
        check(!TollPlugin.IsEmptyParkedSlot(new(){id=2}),"Malformed active parked entry stays unknown");
    }
}
