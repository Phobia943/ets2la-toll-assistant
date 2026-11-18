using System.Diagnostics;
using ETS2LA.Game.SDK;
using System.Numerics;

namespace MautAssistent;
public sealed partial class TollPlugin
{
    static readonly System.Reflection.FieldInfo? ownTrafficFrame=typeof(ETS2LA.Game.Output.GameOutput).GetField("MautOwnTrafficFrame"),
        ownParkedFrame=typeof(ETS2LA.Game.Output.GameOutput).GetField("MautOwnParkedFrame"),
        ownSemaphoreFrame=typeof(ETS2LA.Game.Output.GameOutput).GetField("MautOwnSemaphoreFrame");
    static readonly System.Reflection.MethodInfo? readOwnSourceEpoch=typeof(ETS2LA.Game.Output.GameOutput).GetMethod("MautReadOwnSourceEpoch");
    static long CaptureSourceEpoch()=>readOwnSourceEpoch?.Invoke(null,null) is long epoch?epoch:-1;
    sealed record SourceLease(long Epoch,long Traffic,long Parked,long Signals,long Gates,long GateEpoch);
    SourceLease? longitudinalSourceLease;
    long longitudinalObservationEpoch;
    long longitudinalGateOrigin,longitudinalGateEpoch;
    bool SourceLeaseCurrent(SourceLease lease)=>CaptureSourceEpoch() is long epoch&&epoch==lease.Epoch&&epoch==lease.GateEpoch&&
        FreshSource(lease.Traffic)&&FreshSource(lease.Parked)&&FreshSource(lease.Signals)&&FreshSource(lease.Gates)&&
        ownTrafficFrame?.GetValue(null)!=null&&ownParkedFrame?.GetValue(null)!=null&&ownSemaphoreFrame?.GetValue(null)!=null;
    object? longitudinalTrafficEvidence;
    RailCrossingProfiles? railCrossingProfiles;
    readonly List<object> longitudinalSignalEvidence=[];
    // Immutable copies keep the selected command, its reasons and observed
    // objects together. Host stamps attest successful provider reads, not the
    // time at which this consumer happened to inspect an old object.
    TrafficSpeedDecision ReadLongitudinalSurroundings()
    {
        longitudinalTrafficEvidence=null;
        longitudinalSourceLease=null;
        if(path==null||safetyTrace==null)return new(-.35f,false,"Keine aktuelle Verkehrsroute");
        long sourceEpoch=longitudinalObservationEpoch=CaptureSourceEpoch();
        var trafficFrame=ownTrafficFrame?.GetValue(null) as Tuple<long,TrafficData>;
        var parkedFrame=ownParkedFrame?.GetValue(null) as Tuple<long,ParkedVehicleData>;
        var liveTraffic=trafficFrame?.Item2;
        var liveParked=parkedFrame?.Item2;
        long trafficStamp=trafficFrame?.Item1??0,parkedStamp=parkedFrame?.Item1??0;
        if(liveTraffic?.vehicles==null||liveParked?.vehicles==null||!FreshSource(trafficStamp)||!FreshSource(parkedStamp))
            return new(-.35f,false,"Warte auf aktuelle Verkehrs- und Parkfahrzeugdaten");
        var bodies=new List<TrafficBody>();
        foreach(var vehicle in liveTraffic.vehicles) {
            if(vehicle==null)return new(-.35f,false,"Ungültiger Verkehrseintrag");
            if(IsEmptyTrafficSlot(vehicle))continue;
            bodies.Add(CopyBody(vehicle,vehicle.speed));
            if(!DeclaredTrailersPresent(vehicle))return new(-.35f,false,"Unvollständige Verkehrsgespanne");
            foreach(var trailer in vehicle.trailers??[]) {
                if(trailer==null)return new(-.35f,false,"Fehlender Verkehrsauflieger");
                if(IsZeroBody(trailer))continue;
                bodies.Add(CopyBody(trailer,vehicle.speed));
            }
        }
        foreach(var vehicle in liveParked.vehicles) {
            if(vehicle==null)return new(-.35f,false,"Fehlender Parkfahrzeugeintrag");
            if(IsEmptyParkedSlot(vehicle))continue;
            bodies.Add(CopyBody(vehicle,0));
        }
        if(!FreshSource(trafficStamp)||!FreshSource(parkedStamp))return new(-.35f,false,"Verkehrsabfrage abgelaufen");
        var decision=TrafficSpeedPolicy.Evaluate(safetyTrace,position,Math.Max(0,speed),bodies);
        var light=ReadTrafficLights(out long signalStamp);
        var lease=new SourceLease(sourceEpoch,trafficStamp,parkedStamp,signalStamp,longitudinalGateOrigin,longitudinalGateEpoch);
        if(!SourceLeaseCurrent(lease))return new(-.35f,false,"Verkehrs- oder Signalabfrage abgelaufen");
        longitudinalSourceLease=lease;
        longitudinalTrafficEvidence=new {trafficStamp,parkedStamp,bodyCount=bodies.Count,decision.Gap,decision.ClosingSpeed,
            trafficLimit=FiniteLimit(decision.Acceleration),decision.Reason,
            limitingBody=decision.LimitingBody is { } body?new{x=body.Position.X,y=body.Position.Y,z=body.Position.Z,
                width=body.Size.X,height=body.Size.Y,length=body.Size.Z,body.Speed}:null,
            signalReason=light.Reason,light.StopIdentity,light.StopDistance,signals=longitudinalSignalEvidence.ToArray()};
        return new(Minimum(decision.Acceleration,light.Acceleration),decision.Clear&&light.Clear,
            !light.Clear?light.Reason:decision.Reason,
            Math.Min(decision.StopDistance??double.PositiveInfinity,light.StopDistance??double.PositiveInfinity) is var stop&&double.IsFinite(stop)?stop:null,
            light.StopDistance<=decision.StopDistance||decision.StopDistance==null?light.StopIdentity:decision.StopIdentity);
    }
    internal static TrafficBody CopyBody(BaseVehicle body,double speed)
    {
        // The verified provider exposes producer WXYZ bytes as XYZW fields.
        // Match its documented footprint transform, never use that raw value
        // as a world quaternion. The byte fixtures compare all four corners.
        var euler=TruckLib.MathExtensions.ToEuler(Quaternion.Conjugate(body.Rotation));
        var rotation=Quaternion.CreateFromYawPitchRoll(-euler.Y+MathF.PI,-euler.Z+MathF.PI,-euler.X);
        if(!float.IsFinite(body.Rotation.X)||!float.IsFinite(body.Rotation.Y)||!float.IsFinite(body.Rotation.Z)||
            !float.IsFinite(body.Rotation.W)||Math.Abs(body.Rotation.LengthSquared()-1)>.05)rotation=new(float.NaN,0,0,1);
        return new(body.Position,rotation,body.Size,speed);
    }
    static bool IsZeroBody(BaseVehicle body)=>body.Position==Vector3.Zero&&body.Size==Vector3.Zero&&body.Rotation==default;
    internal static bool IsEmptyParkedSlot(ParkedVehicle body)=>body.id==0&&!body.isTrailer&&body.Position==Vector3.Zero&&
        body.Size==Vector3.Zero&&body.Rotation==default;
    internal static bool IsEmptyTrafficSlot(TrafficVehicle body)=>body.id==0&&!body.isTrailer&&!body.isTMP&&
        body.speed==0&&body.acceleration==0&&body.trailer_count==0&&IsZeroBody(body)&&
        body.trailers!=null&&body.trailers.Length==3&&body.trailers.All(t=>t!=null&&IsZeroBody(t));
    internal static bool DeclaredTrailersPresent(TrafficVehicle body)=>body.trailer_count>=0&&
        body.trailer_count<=(body.trailers?.Length??0)&&
        (body.trailers??[]).Take(body.trailer_count).All(t=>t!=null&&!IsZeroBody(t));
    static bool FreshSource(long stamp)=>stamp>0&&Stopwatch.GetTimestamp()-stamp is var age&&age>=0&&age<=Stopwatch.Frequency*.2;

    TrafficSpeedDecision ReadTrafficLights(out long signalStamp)
    {
        longitudinalSignalEvidence.Clear();
        signalStamp=0;
        if(path==null||safetyTrace==null)return new(-.35f,false,"Keine Ampelroute");
        var signalFrame=ownSemaphoreFrame?.GetValue(null) as Tuple<long,SemaphoreData>;
        if(signalFrame?.Item2?.semaphores==null||!FreshSource(signalFrame.Item1))return new(-.35f,false,"Signalzustände veraltet");
        signalStamp=signalFrame.Item1;
        return EvaluateTrafficLights(path,safetyTrace,position,signalFrame.Item2.semaphores,router.SelectedBarrier,longitudinalSignalEvidence,railCrossingProfiles?.Read());
    }
    internal static TrafficSpeedDecision EvaluateTrafficLights(PathLib.PlannedPathData path,RouteTrace safetyTrace,Vector3 position,
        ETS2LA.Game.SDK.Semaphore[] signals,TollBarrierPlan? selectedBarrier,List<object>? observations=null,
        IReadOnlyDictionary<string,RailSignalProfile>? railProfiles=null)
    {
        double own=safetyTrace.ProjectForTracking(position).Along;
        double? stop=null;string? key=null;string stopReason="Haltende oder unbestätigte Ampel auf der Route";
        foreach(var item in path.PlannedPath.Where(item=>item.prefabPath!=null)) {
        bool ownBarrier=TrafficSignalEvidence.OwnsBarrier(item,selectedBarrier);
        var mappedSignals=item.prefabPath!.GetSemaphores().ToArray();
        var crossings=new Dictionary<(uint Id,Vector3 Anchor),RailCrossingDecision?>();
        var handledCrossings=new HashSet<(uint Id,Vector3 Anchor)>();
        foreach(var signal in mappedSignals) {
            var mappedType=signal.Semaphore.Type;
            if(mappedType is TruckLib.Models.Ppd.SemaphoreType.ModelOnly or TruckLib.Models.Ppd.SemaphoreType.BarrierManualTimed or
                TruckLib.Models.Ppd.SemaphoreType.BarrierDistanceActivated or TruckLib.Models.Ppd.SemaphoreType.BarrierGas or
                TruckLib.Models.Ppd.SemaphoreType.BarrierAutomatic)continue;
            var anchor=signal.GetWorldOrientedPoint().Position;
            var start=signal.GetCurveStartWorldCoordinates().Position;
            var projection=safetyTrace.ProjectForTracking(start);
            double distance=projection.Along-own-5;
            var crossingKey=(signal.Semaphore.SemaphoreId,anchor);
            if(!crossings.TryGetValue(crossingKey,out var crossing)) {
                crossing=RailCrossingEvidence.Evaluate(item.prefabPath,signal.Semaphore,railProfiles,signals);
                crossings[crossingKey]=crossing;
            }
            if(crossing!=null) {
                if(!handledCrossings.Add(crossingKey))continue;
                // A controller can be referenced by several consecutive nav
                // curves. Its stop belongs to the approach, not the track exit.
                projection=mappedSignals.Where(s=>s.Semaphore.SemaphoreId==crossingKey.Item1&&s.GetWorldOrientedPoint().Position==anchor)
                    .Select(s=>safetyTrace.ProjectForTracking(s.GetCurveStartWorldCoordinates().Position))
                    .OrderBy(p=>p.Along).First();
                distance=projection.Along-own-5;
                if(distance< -2||projection.Offset>2||Math.Abs(projection.HeightOffset)>2||projection.Along>=safetyTrace.Length-.01)continue;
                if(observations is {Count:<64})observations.Add(new {prefab=item.prefabPath.Prefab.Prefab?.Uid,
                    id=signal.Semaphore.SemaphoreId,mappedType=mappedType.ToString(),profile=signal.Semaphore.Profile.ToString(),
                    distance,crossing.Clear,crossing.Reason,expected=crossing.Expected.Select(p=>new{x=p.X,y=p.Y,z=p.Z}),states=crossing.States});
                if(!crossing.Clear&&(stop==null||Math.Max(0,distance)<stop)) {
                    stop=Math.Max(0,distance);
                    key=FormattableString.Invariant($"crossing:{item.prefabPath.Prefab.Prefab?.Uid}:{signal.Semaphore.SemaphoreId}");
                    stopReason=crossing.Reason;
                }
                continue;
            }
            if(distance< -2||projection.Offset>2||Math.Abs(projection.HeightOffset)>2||projection.Along>=safetyTrace.Length-.01)continue;
            var matches=signals.Where(g=>g.id==signal.Semaphore.SemaphoreId&&Vector3.Distance(g.GetWorldCoordinates(),anchor)<=1).ToArray();
            bool? spawnEnabled=TrafficSignalEvidence.SpawnEnabled(item.prefabPath.Prefab.Prefab);
            bool omitted=TrafficSignalEvidence.OmitUnspawned(spawnEnabled,matches.Length>0);
            if(observations is {Count:<64})observations.Add(new {
                prefab=item.prefabPath.Prefab.Prefab?.Uid,id=signal.Semaphore.SemaphoreId,
                mappedType=mappedType.ToString(),profile=signal.Semaphore.Profile.ToString(),spawnEnabled,
                anchor=new{x=anchor.X,y=anchor.Y,z=anchor.Z},curveStart=new{x=start.X,y=start.Y,z=start.Z},
                distance,omitted,live=matches.Select(g=>new{type=g.type.ToString(),g.state,g.id}).ToArray()});
            // A disabled map instance is not an unobserved red light. A real
            // matching SDK light overrules the map flag; missing/unknown flags
            // never create a green permission. Toll gates keep their own policy.
            if(omitted)continue;
            // Gates have exactly one authority: the mapped toll barrier binding.
            // Never substitute a closer SDK post from an adjacent/opposing lane.
            if(ownBarrier&&selectedBarrier!.GateId==signal.Semaphore.SemaphoreId)continue;
            if(matches.Length>0&&matches.All(g=>g.type==SemaphoreType.TRAFFICLIGHT&&g.state is 0 or 8))continue;
            if(stop==null||Math.Max(0,distance)<stop) {
                stop=Math.Max(0,distance);
                key=FormattableString.Invariant($"light:{signal.Semaphore.SemaphoreId}:{anchor.X:F1}:{anchor.Z:F1}");
                stopReason="Haltende oder unbestätigte Ampel auf der Route";
            }
        }
        }
        return stop==null?new(float.NaN,true,"Keine haltende Ampel auf der Route"):
            new(float.NaN,false,stopReason,stop,key);
    }
}
