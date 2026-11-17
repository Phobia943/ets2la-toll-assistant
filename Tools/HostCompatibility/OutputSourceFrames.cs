using System.Diagnostics;
using ETS2LA.Game.SDK;
namespace MautHostCompatibility;

// A reference-atomic tuple owns a deep copy. The provider's mutable singleton,
// array elements and trailer-parent links can never replace its original clock.
internal sealed partial class OutputTemplate
{
    public static Tuple<long,TrafficData>? MautOwnTrafficFrame;
    public static Tuple<long,ParkedVehicleData>? MautOwnParkedFrame;
    public static Tuple<long,SemaphoreData>? MautOwnSemaphoreFrame;
    public static long MautOwnSourceEpoch;
    public static bool MautOwnOriginFresh(long origin)
    {long now=Stopwatch.GetTimestamp();return origin>0&&origin<=now&&now-origin<=Stopwatch.Frequency/5;}
    public static long MautReadOwnSourceEpoch() { lock(MautGate)return MautOwnSourceEpoch; }
    public static void MautInvalidateOwnSources()
    {
        lock(MautGate) {
            Interlocked.Increment(ref MautOwnSourceEpoch);
            Volatile.Write(ref MautOwnTrafficFrame,null);Volatile.Write(ref MautOwnParkedFrame,null);Volatile.Write(ref MautOwnSemaphoreFrame,null);
            MautTrafficAt=MautParkedAt=MautSemaphoreAt=0;
            MautTrafficData=null;MautParkedData=null;MautSemaphoreData=null;
        }
    }
    static void MautStoreOwnTraffic(TrafficData? data,long origin,long readEpoch) {
        lock(MautGate) {if(readEpoch!=MautOwnSourceEpoch)return;
            Volatile.Write(ref MautOwnTrafficFrame,data!=null&&MautOwnOriginFresh(origin)?Tuple.Create(origin,data):null);}
    }
    static void MautStoreOwnParked(ParkedVehicleData? data,long origin,long readEpoch) {
        lock(MautGate) {if(readEpoch!=MautOwnSourceEpoch)return;
            Volatile.Write(ref MautOwnParkedFrame,data!=null&&MautOwnOriginFresh(origin)?Tuple.Create(origin,data):null);}
    }
    static void MautStoreOwnSemaphore(SemaphoreData? data,long origin,long readEpoch) {
        lock(MautGate) {if(readEpoch!=MautOwnSourceEpoch)return;
            Volatile.Write(ref MautOwnSemaphoreFrame,data!=null&&MautOwnOriginFresh(origin)?Tuple.Create(origin,data):null);}
    }
    static void MautCopyBase(BaseVehicle source,BaseVehicle target)
    {target.Position=source.Position;target.Rotation=source.Rotation;target.Size=source.Size;}
    public static void MautCaptureOwnTraffic(TrafficData? source,long origin,long readEpoch)
    {
        var input=source?.vehicles;
        if(input==null||input.Length>39||!MautOwnOriginFresh(origin)){MautStoreOwnTraffic(null,origin,readEpoch);return;}
        var vehicles=new TrafficVehicle[input.Length];
        for(int i=0;i<input.Length;i++) {
            var value=input[i];var sourceTrailers=value?.trailers;
            if(value==null||sourceTrailers==null||sourceTrailers.Length>3||value.trailer_count<0||value.trailer_count>sourceTrailers.Length)
                {MautStoreOwnTraffic(null,origin,readEpoch);return;}
            var copied=new TrafficVehicle {speed=value.speed,acceleration=value.acceleration,trailer_count=value.trailer_count,
                id=value.id,isTMP=value.isTMP,isTrailer=value.isTrailer,lastPosition=value.lastPosition,lastUpdateTime=value.lastUpdateTime};
            MautCopyBase(value,copied);var trailers=new TrafficTrailer[sourceTrailers.Length];
            for(int t=0;t<trailers.Length;t++) {
                if(sourceTrailers[t]==null){MautStoreOwnTraffic(null,origin,readEpoch);return;}
                var trailer=new TrafficTrailer {parent=copied};MautCopyBase(sourceTrailers[t],trailer);trailers[t]=trailer;
            }
            copied.trailers=trailers;vehicles[i]=copied;
        }
        if(!MautOwnOriginFresh(origin)){MautStoreOwnTraffic(null,origin,readEpoch);return;}
        MautStoreOwnTraffic(new TrafficData {vehicles=vehicles},origin,readEpoch);
    }
    public static void MautCaptureOwnParked(ParkedVehicleData? source,long origin,long readEpoch)
    {
        var input=source?.vehicles;
        if(input==null||input.Count>40||!MautOwnOriginFresh(origin)){MautStoreOwnParked(null,origin,readEpoch);return;}
        var vehicles=new List<ParkedVehicle>(input.Count);
        foreach(var value in input) {
            if(value==null){MautStoreOwnParked(null,origin,readEpoch);return;}
            var copied=new ParkedVehicle {id=value.id,isTrailer=value.isTrailer};MautCopyBase(value,copied);vehicles.Add(copied);
        }
        if(!MautOwnOriginFresh(origin)){MautStoreOwnParked(null,origin,readEpoch);return;}
        MautStoreOwnParked(new ParkedVehicleData {vehicles=vehicles},origin,readEpoch);
    }
    public static void MautCaptureOwnSemaphore(SemaphoreData? source,long origin,long readEpoch)
    {
        var input=source?.semaphores;
        if(input==null||input.Length>40||!MautOwnOriginFresh(origin)){MautStoreOwnSemaphore(null,origin,readEpoch);return;}
        var semaphores=new ETS2LA.Game.SDK.Semaphore[input.Length];
        for(int i=0;i<input.Length;i++) {
            var value=input[i];if(value==null){MautStoreOwnSemaphore(null,origin,readEpoch);return;}
            semaphores[i]=new() {position=value.position,cx=value.cx,cy=value.cy,rotation=value.rotation,
                type=value.type,time_remaining=value.time_remaining,state=value.state,id=value.id};
        }
        if(!MautOwnOriginFresh(origin)){MautStoreOwnSemaphore(null,origin,readEpoch);return;}
        MautStoreOwnSemaphore(new SemaphoreData {semaphores=semaphores},origin,readEpoch);
    }
}
