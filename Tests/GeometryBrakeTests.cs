using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using MautAssistent;

public static class GeometryBrakeTests
{
    public static int Replay(string path)
    {
        var guard=new BrakeResponseGuard();int rows=0,coherent=0;
        var warnings=new List<BrakeResponseFailure>();
        foreach(var line in File.ReadLines(path)) {
            using var doc=JsonDocument.Parse(line);var d=doc.RootElement;rows++;
            bool good=!d.GetProperty("sdkChangedDuringRead").GetBoolean()&&!d.GetProperty("commandChangedDuringRead").GetBoolean();
            if(good)coherent++;
            // Legacy captures have no reliable producer command age or assist
            // enable bit. This is a conditional signal replay, not live proof.
            var result=guard.Observe(new(d.GetProperty("t").GetDouble(),d.GetProperty("sdk").GetUInt64(),
                d.GetProperty("sim").GetUInt64(),true,good&&d.GetProperty("active").GetInt32()!=0,
                d.GetProperty("paused").GetInt32()!=0,d.GetProperty("speed").GetDouble(),good?0:double.PositiveInfinity,
                // The legacy output buffer already stores positive brake;
                // ACC's upstream abackward event uses negative pedal output.
                Math.Clamp(d.GetProperty("legacyBrake").GetSingle(),0,1),d.GetProperty("gameBrake").GetSingle()));
            if(result!=null)warnings.Add(result);
        }
        Console.WriteLine(JsonSerializer.Serialize(new {rows,coherent,warnings,
            limitations=new[]{"Legacy command age assumed zero; original producer timestamps unavailable.",
                "Assist enable state unavailable; observed requests are not an atomic snapshot.",
                "First latched warning only; no physical stopping-distance or focus-cause proof."}},new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
    public static byte[] GeometryFrame(long timestamp=1000)
    {
        var bytes=new byte[256];var d=bytes.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(d,0x3147544d);
        BinaryPrimitives.WriteUInt32LittleEndian(d[4..],1);
        BinaryPrimitives.WriteUInt32LittleEndian(d[8..],256);
        BinaryPrimitives.WriteUInt32LittleEndian(d[12..],2);
        BinaryPrimitives.WriteInt64LittleEndian(d[16..],timestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(d[24..],123);
        BinaryPrimitives.WriteUInt32LittleEndian(d[28..],7);
        BinaryPrimitives.WriteUInt64LittleEndian(d[32..],456);
        foreach(int start in new[]{40,104}) {
            BinaryPrimitives.WriteDoubleLittleEndian(d[start..],10);
            BinaryPrimitives.WriteDoubleLittleEndian(d[(start+8)..],2);
            BinaryPrimitives.WriteDoubleLittleEndian(d[(start+16)..],20);
            BinaryPrimitives.WriteSingleLittleEndian(d[(start+36)..],1);
            foreach(var (offset,value) in new[]{(40,-1f),(44,0f),(48,-5f),(52,1f),(56,4f),(60,3f)})
                BinaryPrimitives.WriteSingleLittleEndian(d[(start+offset)..],value);
        }
        return bytes;
    }
    public static void Protocol(Action<bool,string> assert)
    {
        var valid=GeometryFrame();var shape=VehicleGeometryProtocol.Decode(valid,1000)!;
        assert(shape is {TrailerKnown:true,Trailer:not null}&&!shape.ClearanceApproved&&!shape.CargoCoverageKnown,
            "Valid model bounds are observation only, with explicit trailer state");
        using var described=JsonDocument.Parse(JsonSerializer.Serialize(shape.DescribeFor(new(10,2,20),0)));
        assert(described.RootElement.GetProperty("referenceMatched").GetBoolean()&&
            described.RootElement.GetProperty("frontReach").GetDouble()==5,"Matching pose permits diagnostic front reach");
        using var mismatch=JsonDocument.Parse(JsonSerializer.Serialize(shape.DescribeFor(new(10,2,30),0)));
        assert(mismatch.RootElement.GetProperty("frontReach").ValueKind==JsonValueKind.Null,"Different origins cannot invent bumper distance");
        using var wrongHeading=JsonDocument.Parse(JsonSerializer.Serialize(shape.DescribeFor(new(10,2,20),.5)));
        assert(wrongHeading.RootElement.GetProperty("frontReach").ValueKind==JsonValueKind.Null,"Different axes cannot invent bumper distance");
        foreach(var (offset,value) in new[]{(0,0U),(4,2U),(8,255U),(12,1U),(12,0U),(24,0U),(28,3U),(28,15U),(32,0U)}) {
            var invalid=(byte[])valid.Clone();BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(offset),value);
            assert(VehicleGeometryProtocol.Decode(invalid,1000)==null,$"Malformed protocol field {offset} is rejected");
        }
        foreach(var now in new[]{1301L,899L,long.MaxValue,long.MinValue})
            assert(VehicleGeometryProtocol.Decode(valid,now)==null,"Stale, future or extreme clocks are rejected");
        assert(VehicleGeometryProtocol.Decode(valid.AsSpan(0,255),1000)==null,"Truncated frames are rejected");
        foreach(int offset in new[]{40,76,96,168-8}) {
            var invalid=(byte[])valid.Clone();
            if(offset==40)BinaryPrimitives.WriteDoubleLittleEndian(invalid.AsSpan(offset),double.NaN);
            else BinaryPrimitives.WriteSingleLittleEndian(invalid.AsSpan(offset),float.NaN);
            assert(VehicleGeometryProtocol.Decode(invalid,1000)==null,"Invalid bounds, rotation or position are rejected");
        }
        var absent=(byte[])valid.Clone();BinaryPrimitives.WriteUInt32LittleEndian(absent.AsSpan(28),5);
        assert(VehicleGeometryProtocol.Decode(absent,1000) is {TrailerKnown:true,Trailer:null},"Explicit absence is retained");
        BinaryPrimitives.WriteUInt32LittleEndian(absent.AsSpan(28),1);
        assert(VehicleGeometryProtocol.Decode(absent,1000) is {TrailerKnown:false,Trailer:null},"Unavailable trailer data cannot become known absence");
    }
    public static void Brake(Action<bool,string> assert)
    {
        BrakeResponseFrame Frame(int i)=>new(i*.05,(ulong)(1000000+i*50000),(ulong)(1000000+i*50000),true,true,false,20,.01,.5f,0);
        var guard=new BrakeResponseGuard();
        for(int i=0;i<20;i++)assert(guard.Observe(Frame(i))==null,"Short input lag cannot raise a persistent failure");
        assert(guard.Observe(Frame(20)) is {MissingFor:>=1},"Continuous coherent missing braking must be reported");
        assert(guard.Observe(Frame(21))==null,"A session produces only one warning");
        guard.Observe(Frame(22) with {Enabled=false});
        assert(guard.Failure==null,"Manual disable clears diagnostic state");
        foreach(var alter in new Func<BrakeResponseFrame,BrakeResponseFrame>[] {
            s=>s with {Paused=true},s=>s with {Active=false},s=>s with {CommandAge=.151},
            s=>s with {CommandAge=-.01},s=>s with {Speed=0},s=>s with {RequestedBrake=.19f},
            s=>s with {GameBrake=.1f},s=>s with {RequestedBrake=float.NaN},s=>s with {GameBrake=float.NaN},
            s=>s with {SimulationTime=1000000},s=>s with {GameTime=1000000},s=>s with {Arrived=s.Arrived*10}
        }) {
            guard.Reset();for(int i=0;i<50;i++)assert(guard.Observe(alter(Frame(i)))==null,
                "Paused, invalid, stale, effective or weak braking cannot prove a continuous missing response");
        }
        guard.Reset();
        for(int i=0;i<18;i++)guard.Observe(Frame(i));
        guard.Observe(Frame(18) with {GameBrake=.4f});
        for(int i=19;i<38;i++)assert(guard.Observe(Frame(i))==null,"A real brake response resets the missing interval");
        assert(guard.Observe(Frame(39))!=null,"A later new continuous failure remains detectable");
    }
    public static void NativeFixture(Action<bool,string> assert)
    {
        string? path=Environment.GetEnvironmentVariable("MAUT_GEOMETRY_FIXTURE");
        if(string.IsNullOrWhiteSpace(path))return;
        var bytes=File.ReadAllBytes(path);long timestamp=BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16));
        var native=VehicleGeometryProtocol.Decode(bytes,timestamp);
        assert(native is {TrailerKnown:true,Trailer:not null}&&native.Truck.Position==new Vector3(10,2,20)&&
            native.Truck.Minimum==new Vector3(-1,0,-5)&&native.Truck.Maximum==new Vector3(1,4,3),
            "Actual C-produced fixture must decode without layout or quaternion mismatch");
    }
}
