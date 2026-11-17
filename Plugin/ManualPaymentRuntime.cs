using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using ETS2LA.Game.Output;
using ETS2LA.Game.SDK;
using ETS2LA.State;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    sealed record PaymentGuard(PaymentZoneProof Zone,int GateId,Vector3[][] Components,long PathGeneration,long IssuedAt,long Attempt);
    sealed record PaymentReservation(PaymentKey Key,Vector3 Anchor,long Epoch,long SourceEpoch,string Token,Task<PaymentJournalAck> Work,bool Cancelled=false);
    sealed record PaymentRemoval(string Token,Task<PaymentJournalAck> Work);
    sealed record PaymentRuntimeSnapshot(PaymentDecision Decision,PaymentZoneProof? Zone,long Attempt,long SourceAt,GameInputContextSnapshot Context);
    static readonly MethodInfo? setPaymentLease=typeof(GameOutput).GetMethod("MautSetPaymentLease"),
        cancelPaymentLease=typeof(GameOutput).GetMethod("MautCancelPaymentAttempt"),
        registerPaymentPermission=typeof(GameOutput).GetMethod("MautRegisterPaymentPermission"),
        clearPaymentPermission=typeof(GameOutput).GetMethod("MautClearPaymentPermission");
    static readonly FieldInfo? paymentPermission=typeof(GameOutput).GetField("MautPaymentPermission");
    Func<long,bool>? paymentCallback;
    // Passive DI sinks for isolated product-adapter tests; production leaves
    // these null and uses only the sealed host methods/context reader.
    Action<long,long,long,long,long>? paymentLeaseSink=null;
    Action<long>? paymentCancelSink=null;
    Func<GameInputContextSnapshot>? paymentContextReader=null;
    GameInputContextSnapshot PaymentContext()=>paymentContextReader?.Invoke()??ReadInputContext();
    readonly Dictionary<string,PaymentJournalEntry> paymentVisits=new(StringComparer.Ordinal);
    readonly Dictionary<string,double> paymentDepartures=new(StringComparer.Ordinal);
    readonly Dictionary<string,PaymentRemoval> paymentDepartureWrites=new(StringComparer.Ordinal);
    readonly Dictionary<string,PaymentReservation> paymentReservationWrites=new(StringComparer.Ordinal);
    PaymentJournal? paymentJournal;
    Task<PaymentJournalSnapshot>? paymentJournalLoad;
    PaymentReservation? paymentReservation;
    string PaymentJournalPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ETS2LA-Mautassistent","payment-visits.json");
    ManualPayment? manualPayment;
    PaymentGuard? paymentGuard;
    ManualPaymentRouteZone? paymentRouteZone;
    PaymentRuntimeSnapshot? paymentSnapshot;
    GateFrame? latestDeliveredGates;
    long paymentAttempt,paymentPathGeneration,paymentAppliedPathGeneration;
    TelemetryFrame? paymentDepartureFrame;
    int paymentDeparturePid;
    string paymentStatus="Automatische Zahlungsbestätigung wartet auf eine bestätigte manuelle Mautspur.";
    public string PaymentStatus=>Volatile.Read(ref paymentStatus);

    void StartManualPayment()
    {
        if(!requireHostCompatibility)return;
        paymentJournal??=new(PaymentJournalPath);
        paymentJournalLoad??=paymentJournal.LoadAsync();
        paymentCallback??=PaymentCommitAllowed;
        registerPaymentPermission?.Invoke(null,[paymentCallback]);
    }
    void PersistPaymentAttempt(string gate)
    {
        var request=paymentReservation;
        if(request==null||request.Cancelled||request.Key.GateIdentity!=gate||!request.Work.IsCompletedSuccessfully)
            throw new InvalidOperationException("Zahlversuch noch nicht dauerhaft vorbereitet");
        var ack=request.Work.Result; // Completed-only read; no blocking I/O.
        if(!ack.Durable||!ack.Applied||ack.RequestToken!=request.Token||ack.StoredReservationToken!=request.Token)
            throw new InvalidOperationException("Keine neue bestätigte Zahlungsreservierung");
    }
    void PersistPaymentDeparture(string gate)
    {
        if(!paymentDepartureWrites.TryGetValue(gate,out var removal)||!removal.Work.IsCompletedSuccessfully||
            !removal.Work.Result.Durable||!removal.Work.Result.Applied||
            !paymentVisits.TryGetValue(gate,out var visit)||visit.AttemptToken!=removal.Token)
            throw new InvalidOperationException("Abgang noch nicht gespeichert");
        paymentVisits.Remove(gate);paymentDepartures.Remove(gate);
    }
    void CancelPaymentReservation()
    {
        if(paymentReservation is { } request) {
            paymentReservation=request with{Cancelled=true};
            manualPayment?.RestoreConsumed([request.Key.GateIdentity]);
        }
    }
    void CancelPaymentOutput(long epoch)
    {
        long attempt=paymentAttempt;Volatile.Write(ref paymentGuard,null);paymentAttempt=0;
        if(paymentCancelSink!=null)paymentCancelSink(epoch);else cancelPaymentLease?.Invoke(null,[epoch,attempt]);
    }
    void StopManualPayment()
    {
        // Revoke the physical permission before waiting for plugin bookkeeping.
        Volatile.Write(ref paymentGuard,null);
        if(Volatile.Read(ref paymentCallback) is { } callback)clearPaymentPermission?.Invoke(null,[callback]);
        cancelPaymentLease?.Invoke(null,[CaptureLongitudinalEpoch(),paymentAttempt]);
        lock(sync) {CancelPaymentReservation();manualPayment?.Cancel();paymentAttempt=0;}
    }
    void RunManualPayment(long epoch,long issuedAt)
    {
        try {UpdateManualPayment(epoch,issuedAt);}
        catch(Exception ex) when(ex is not OutOfMemoryException) {
            long attempt=paymentAttempt;Volatile.Write(ref paymentGuard,null);manualPayment?.Cancel();paymentAttempt=0;
            paymentStatus="Zahlungsbestätigung nicht verfügbar: "+ex.GetType().Name+". Bitte manuell bezahlen.";
            // A failed optional interaction never requests an emergency stop.
            try {cancelPaymentLease?.Invoke(null,[epoch,attempt]);}catch { }
        }
    }
    void UpdateManualPayment(long epoch,long issuedAt)
    {
        if((setPaymentLease==null||paymentPermission==null)&&paymentLeaseSink==null||paymentJournal==null)return;
        if(manualPayment==null) {
            if(paymentJournalLoad?.IsCompletedSuccessfully!=true) {paymentStatus="Zahlungsprotokoll wird vorbereitet; noch keine Interaktion.";return;}
            var loaded=paymentJournalLoad.Result;
            if(!loaded.Available) {paymentStatus="Zahlungsprotokoll nicht verfügbar – bitte manuell bezahlen. "+loaded.Issue;return;}
            foreach(var entry in loaded.Entries)paymentVisits[entry.Gate]=entry;
            manualPayment=new(PersistPaymentAttempt,PersistPaymentDeparture);manualPayment.RestoreConsumed(paymentVisits.Keys);
        }
        foreach(var pending in paymentReservationWrites.Values.Where(p=>p.Work.IsCompleted).ToArray()) {
            if(pending.Work.IsCompletedSuccessfully&&pending.Work.Result.Durable&&pending.Work.Result.StoredReservationToken is {Length:>0} token)
                paymentVisits[pending.Key.GateIdentity]=new(pending.Key.GateIdentity,pending.Anchor.X,pending.Anchor.Y,pending.Anchor.Z,token);
            paymentReservationWrites.Remove(pending.Token);
        }
        foreach(var pair in paymentDepartureWrites.Where(p=>p.Value.Work.IsCompleted).ToArray()) {
            manualPayment.ForgetDepartedGate(pair.Key);paymentDepartureWrites.Remove(pair.Key);
        }
        double now=clock.Elapsed.TotalSeconds;
        long appliedRoute=Volatile.Read(ref paymentAppliedPathGeneration);
        var frame=appliedTelemetry;var context=PaymentContext();
        bool enabled=_IsRunning&&active&&!preview&&State.EnableAssists&&State.DrivingMode==DrivingMode.FullSelfDriving&&
            !faultBrake.Latched&&!brakeHeld&&now>=takeoverUntil;
        if(frame!=null&&SensorsFresh(now))ForgetDepartedPaymentVisits(frame,context);
        else {paymentDepartures.Clear();paymentDepartureFrame=null;}
        if(!enabled||frame==null||!SensorsFresh(now)||now-pathAt>=.2||path is not {HasError:false}||
            Volatile.Read(ref paymentPathGeneration)!=appliedRoute) {
            CancelPaymentReservation();manualPayment.Cancel();CancelPaymentOutput(epoch);return;
        }
        paymentRouteZone=router.ManualPaymentZone(options.StopOffsetMetres);
        if(paymentRouteZone is not { } zone) {
            if(router.SelectedGate is int selected&&router.PaymentFor(selected)==TollPayment.Manual)
                paymentStatus="Zahlzone am sicheren Haltepunkt nicht eindeutig bestätigt. Bitte mit F9 übernehmen und manuell bezahlen.";
            else paymentStatus="Automatische Zahlungsbestätigung wartet auf eine bestätigte manuelle Mautspur.";
            CancelPaymentReservation();manualPayment.Cancel();CancelPaymentOutput(epoch);return;
        }
        var live=TollSignals.ReadLive(gates,zone.GateId,zone.Anchor);
        bool bound=barrierBinding?.LiveBarrierBound==true&&live!=null&&barrierBinding.LiveGate?.Key==RouteMatcher.Key(live)&&
            router.SelectedBarrier is { } barrier&&barrier.GateId==zone.GateId&&Vector3.Distance(barrier.Anchor,zone.Anchor)<.1;
        var state=live?.state is >=0 and <=3?(BarrierState)live.state:BarrierState.Unknown;
        double sourceTime=Math.Min(frame.Arrived,gatesAt);
        bool foreground=context.IsFreshGameForeground(Stopwatch.GetTimestamp(),Stopwatch.Frequency);
        bool eligible=foreground&&bound&&frame.Active&&!frame.Paused&&state==BarrierState.Closed&&
            Math.Abs(frame.Speed)<=.1&&zone.Zone.Contains(frame.Position,TrackingGuard.TruckForward(frame.Heading));
        if(paymentReservation is { } oldRequest&&(oldRequest.Epoch!=epoch||oldRequest.SourceEpoch!=gatesSourceEpoch||
            oldRequest.Key!=zone.Zone.Key||!eligible))CancelPaymentReservation();
        if(eligible&&!manualPayment.ConsumedGateIds.Contains(zone.Zone.Key.GateIdentity)) {
            if(paymentReservation==null||paymentReservation.Key.GateIdentity!=zone.Zone.Key.GateIdentity) {
                string token=Guid.NewGuid().ToString("N");
                paymentReservation=new(zone.Zone.Key,zone.Anchor,epoch,gatesSourceEpoch,token,paymentJournal.ReserveAsync(zone.Zone.Key.GateIdentity,zone.Anchor,token));
                paymentReservationWrites[token]=paymentReservation;
            }
            var request=paymentReservation;
            if(!request.Work.IsCompleted) {
                manualPayment.Cancel();CancelPaymentOutput(epoch);paymentStatus="Zahlversuch wird gespeichert; Lkw wartet ohne Interaktion.";return;
            }
            if(request.Cancelled||!request.Work.IsCompletedSuccessfully||!request.Work.Result.Durable||!request.Work.Result.Applied||
                request.Work.Result.RequestToken!=request.Token||request.Work.Result.StoredReservationToken!=request.Token) {
                manualPayment.RestoreConsumed([zone.Zone.Key.GateIdentity]);
                paymentStatus="Zahlversuch nicht neu freigegeben – bitte manuell bezahlen.";
            }
        }
        var observation=new PaymentObservation(zone.Zone.Key,epoch,now,sourceTime,frame.Position,
            TrackingGuard.TruckForward(frame.Heading),frame.Speed,enabled,frame.Active,frame.Paused,foreground,
            router.PaymentFor(zone.GateId)==TollPayment.Manual,bound,state);
        var result=manualPayment.Step(observation,zone.Zone);
        paymentStatus=result.Reason;
        if(result.Activate)paymentAttempt=issuedAt;
        long sourceAt=Math.Min(frame.Origin,Math.Min(gatesOrigin,context.CapturedAt));
        if(paymentAttempt>0&&Stopwatch.GetTimestamp()-paymentAttempt<Stopwatch.Frequency/10&&
            result.State is PaymentState.ActivationIssued or PaymentState.WaitingForGate&&
            foreground&&bound&&state==BarrierState.Closed&&Math.Abs(frame.Speed)<=.1&&zone.Zone.Contains(frame.Position,TrackingGuard.TruckForward(frame.Heading))) {
            var components=TollSignals.SnapshotComponents(zone.GateId,zone.Anchor);
            if(components!=null) {
                Volatile.Write(ref paymentGuard,new(zone.Zone,zone.GateId,components,appliedRoute,issuedAt,paymentAttempt));
                if(paymentLeaseSink!=null)paymentLeaseSink(paymentAttempt,epoch,gatesSourceEpoch,issuedAt,sourceAt);
                else setPaymentLease!.Invoke(null,[paymentAttempt,epoch,gatesSourceEpoch,issuedAt,sourceAt]);
            } else CancelPaymentOutput(epoch);
        } else CancelPaymentOutput(epoch);
        paymentSnapshot=new(result,zone.Zone,paymentAttempt,sourceAt,context);
    }
    // Called under the host gate: immutable snapshots/Win32 reads only, no
    // plugin/route locks, process enumeration, disk access or SDK calls.
    bool PaymentCommitAllowed(long attempt)
    {
        var proof=Volatile.Read(ref paymentGuard);var frame=Volatile.Read(ref latestDeliveredTelemetry);
        var gateFrame=Volatile.Read(ref latestDeliveredGates);long now=Stopwatch.GetTimestamp();
        bool Fresh(long at)=>at>0&&at<=now&&now-at<=Stopwatch.Frequency/5;
        if(proof==null||proof.Attempt!=attempt||frame==null||gateFrame==null||!Fresh(proof.IssuedAt)||!Fresh(frame.Origin)||!Fresh(gateFrame.Origin)||
            Volatile.Read(ref paymentPathGeneration)!=proof.PathGeneration||Volatile.Read(ref paymentAppliedPathGeneration)!=proof.PathGeneration||!frame.Active||frame.Paused||
            !double.IsFinite(frame.Speed)||Math.Abs(frame.Speed)>.1||brakeHeld||faultBrake.Latched||!State.EnableAssists||State.DrivingMode!=DrivingMode.FullSelfDriving||
            !proof.Zone.Contains(frame.Position,TrackingGuard.TruckForward(frame.Heading))||
            !PaymentContext().IsFreshGameForeground(Stopwatch.GetTimestamp(),Stopwatch.Frequency))return false;
        foreach(var component in proof.Components) {
            bool found=false;
            foreach(var gate in gateFrame.Gates)if(gate.type==SemaphoreType.GATE&&gate.id==proof.GateId&&
                component.Any(p=>Vector3.Distance(p,gate.GetWorldCoordinates())<=1)) {
                if(gate.state!=1)return false;found=true;
            }
            if(!found)return false;
        }
        return proof.Components.Length>0;
    }
    void ForgetDepartedPaymentVisits(TelemetryFrame frame,GameInputContextSnapshot context)
    {
        // Departure is actual fresh observation >100m away for two seconds.
        // Gaps, teleport-like jumps, stopped simulation or process changes
        // restart that proof; F8/focus return at the same booth never re-arms.
        var old=paymentDepartureFrame;paymentDepartureFrame=frame;
        bool continuous=old!=null&&context.GamePid>0&&context.GamePid==paymentDeparturePid&&frame.Active&&!frame.Paused&&
            frame.Arrived>old.Arrived&&frame.Arrived-old.Arrived<=.2&&frame.Physics.SimulatedTime>old.Physics.SimulatedTime&&
            Vector3.Distance(frame.Position,old.Position)<=2+Math.Max(Math.Abs(frame.Speed),Math.Abs(old.Speed))*(frame.Arrived-old.Arrived)*2;
        paymentDeparturePid=context.GamePid;
        if(!continuous) {paymentDepartures.Clear();return;}
        foreach(var visit in paymentVisits.Values.ToArray()) {
            if(Vector3.Distance(frame.Position,new(visit.X,visit.Y,visit.Z))<=100) {paymentDepartures.Remove(visit.Gate);continue;}
            if(!paymentDepartures.TryGetValue(visit.Gate,out double since))paymentDepartures[visit.Gate]=frame.Arrived;
            else if(frame.Arrived-since>=2&&!paymentDepartureWrites.ContainsKey(visit.Gate)) {
                paymentDepartureWrites[visit.Gate]=new(visit.AttemptToken,paymentJournal!.RemoveAsync(visit.Gate,visit.AttemptToken,Guid.NewGuid().ToString("N")));
                if(paymentReservation?.Key.GateIdentity==visit.Gate)paymentReservation=null;
            }
        }
    }
}
