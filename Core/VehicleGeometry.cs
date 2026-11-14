using System.Buffers.Binary;
using System.Numerics;

namespace MautAssistent;

public sealed record VehicleBody(Vector3 Position,Quaternion Rotation,Vector3 Minimum,Vector3 Maximum)
{
    public IEnumerable<Vector3> Corners()=>Enumerable.Range(0,8).Select(i=>Position+Vector3.Transform(new Vector3(
        (i&1)==0?Minimum.X:Maximum.X,(i&2)==0?Minimum.Y:Maximum.Y,(i&4)==0?Minimum.Z:Maximum.Z),Quaternion.Normalize(Rotation)));
}
public sealed record VehicleGeometry(uint ProcessId,ulong Session,ulong Sequence,long UtcMs,VehicleBody Truck,
    bool TrailerKnown,VehicleBody? Trailer)
{
    public bool ClearanceApproved=>false; // Observation alone is never driving permission.
    public bool CargoCoverageKnown=>false;
    public object DescribeFor(Vector3 telemetryOrigin,double heading)
    {
        double gap=Vector3.Distance(telemetryOrigin,Truck.Position);
        var forward=TrackingGuard.TruckForward(heading);
        double dot=Vector3.Dot(Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(Truck.Rotation)),forward);
        bool comparable=double.IsFinite(heading)&&gap<=.5&&dot>=.98;
        static object Body(VehicleBody b)=>new {
            position=new{x=b.Position.X,y=b.Position.Y,z=b.Position.Z},
            rotation=new{x=b.Rotation.X,y=b.Rotation.Y,z=b.Rotation.Z,w=b.Rotation.W},
            minimum=new{x=b.Minimum.X,y=b.Minimum.Y,z=b.Minimum.Z},
            maximum=new{x=b.Maximum.X,y=b.Maximum.Y,z=b.Maximum.Z}};
        return new {source="TruckersMP-SDK model bounds",ProcessId,Session,Sequence,UtcMs,TrailerKnown,
            trailerPresent=Trailer!=null,ClearanceApproved,CargoCoverageKnown,poseGap=gap,headingDot=dot,
            truckModel=Body(Truck),trailerModel=Trailer!=null?Body(Trailer):null,
            truckSize=new{x=Truck.Maximum.X-Truck.Minimum.X,y=Truck.Maximum.Y-Truck.Minimum.Y,z=Truck.Maximum.Z-Truck.Minimum.Z},
            frontReach=comparable?Truck.Corners().Max(p=>Vector3.Dot(p-telemetryOrigin,forward)):(double?)null,
            referenceMatched=comparable};
    }
}
public static class VehicleGeometryProtocol
{
    public const int Size=256;
    public static VehicleGeometry? Decode(ReadOnlySpan<byte> data,long nowMs)
    {
        if(data.Length!=Size||BinaryPrimitives.ReadUInt32LittleEndian(data)!=0x3147544d||
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..])!=1||BinaryPrimitives.ReadInt32LittleEndian(data[8..])!=Size)return null;
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        long timestamp=BinaryPrimitives.ReadInt64LittleEndian(data[16..]);
        uint pid=BinaryPrimitives.ReadUInt32LittleEndian(data[24..]),flags=BinaryPrimitives.ReadUInt32LittleEndian(data[28..]);
        ulong session=BinaryPrimitives.ReadUInt64LittleEndian(data[32..]);
        // Use a widened subtraction so malformed extreme timestamps cannot wrap.
        decimal age=(decimal)nowMs-timestamp;
        if((sequence&1)!=0||sequence==0||pid==0||session==0||timestamp<0||nowMs<0||age is < -100 or >300||
            (flags&~7U)!=0||(flags&1)==0||(flags&2)!=0&&(flags&4)==0)return null;
        var truck=Body(data[40..104]);var trailer=(flags&2)!=0?Body(data[104..168]):null;
        if(truck==null||(flags&2)!=0&&trailer==null)return null;
        return new(pid,session,sequence,timestamp,truck,(flags&4)!=0,trailer);
    }
    static VehicleBody? Body(ReadOnlySpan<byte> d)
    {
        var position=new Vector3((float)BinaryPrimitives.ReadDoubleLittleEndian(d),(float)BinaryPrimitives.ReadDoubleLittleEndian(d[8..]),(float)BinaryPrimitives.ReadDoubleLittleEndian(d[16..]));
        var rotation=new Quaternion(BinaryPrimitives.ReadSingleLittleEndian(d[24..]),BinaryPrimitives.ReadSingleLittleEndian(d[28..]),BinaryPrimitives.ReadSingleLittleEndian(d[32..]),BinaryPrimitives.ReadSingleLittleEndian(d[36..]));
        var minimum=new Vector3(BinaryPrimitives.ReadSingleLittleEndian(d[40..]),BinaryPrimitives.ReadSingleLittleEndian(d[44..]),BinaryPrimitives.ReadSingleLittleEndian(d[48..]));
        var maximum=new Vector3(BinaryPrimitives.ReadSingleLittleEndian(d[52..]),BinaryPrimitives.ReadSingleLittleEndian(d[56..]),BinaryPrimitives.ReadSingleLittleEndian(d[60..]));
        static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
        var extent=maximum-minimum;
        if(!Finite(position)||position.Length()>1e7||!Finite(minimum)||!Finite(maximum)||!float.IsFinite(rotation.LengthSquared())||
            Math.Abs(rotation.LengthSquared()-1)>.01||extent.X is <=0 or >50||extent.Y is <=0 or >50||extent.Z is <=0 or >50)return null;
        return new(position,rotation,minimum,maximum);
    }
}
