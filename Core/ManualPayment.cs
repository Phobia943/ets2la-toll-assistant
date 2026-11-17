using System.Numerics;

namespace MautAssistent;

public sealed record PaymentKey(string GateIdentity,string RouteRevision,string ZoneRevision);

/// <summary>A caller-verified, current model trigger, in WORLD coordinates. It
/// describes inclusion of the game's activation reference point, not rig clearance.</summary>
public sealed record PaymentZoneProof(PaymentKey Key,Vector3 Center,Vector3 Forward,
    double HalfLength,double HalfWidth,double VerticalTolerance,double HeadingCosine)
{
    public bool Valid => ValidKey(Key)&&Finite(Center)&&Finite(Forward)&&
        Math.Abs(Forward.LengthSquared()-1)<.001&&Math.Abs(Forward.Y)<=.1&&
        double.IsFinite(HalfLength)&&HalfLength>0&&HalfLength<=25&&
        double.IsFinite(HalfWidth)&&HalfWidth>0&&HalfWidth<=10&&
        double.IsFinite(VerticalTolerance)&&VerticalTolerance>0&&VerticalTolerance<=1&&
        double.IsFinite(HeadingCosine)&&HeadingCosine>=.95&&HeadingCosine<=1;

    public bool Contains(Vector3 position,Vector3 forward)
    {
        if(!Valid||!Finite(position)||!Finite(forward)||Math.Abs(forward.LengthSquared()-1)>.001||Math.Abs(forward.Y)>.2)return false;
        var direction=Vector3.Normalize(new Vector3(Forward.X,0,Forward.Z));
        var heading=Vector3.Normalize(new Vector3(forward.X,0,forward.Z));
        var right=new Vector3(direction.Z,0,-direction.X);
        var delta=position-Center;
        return Math.Abs(delta.Y)<=VerticalTolerance&&Math.Abs(Vector3.Dot(delta,direction))<=HalfLength&&
            Math.Abs(Vector3.Dot(delta,right))<=HalfWidth&&Vector3.Dot(heading,direction)>=HeadingCosine;
    }
    internal static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    internal static bool ValidKey(PaymentKey? key)=>key!=null&&
        new[]{key.GateIdentity,key.RouteRevision,key.ZoneRevision}.All(s=>!string.IsNullOrWhiteSpace(s)&&s.Length<=4096);
}

public sealed record PaymentObservation(PaymentKey Key,long Epoch,double Now,double OriginalAt,
    Vector3 Position,Vector3 Forward,double Speed,bool Enabled,bool Active,bool Paused,bool Foreground,
    bool ManualKnown,bool OwnGateBound,BarrierState OwnGateState);

public enum PaymentState { Idle,Inhibited,Stabilizing,ActivationIssued,WaitingForGate,GateOpen,TimedOut,Cancelled,Consumed }
public sealed record PaymentDecision(PaymentState State,bool Activate,string Reason);

/// <summary>One serialized caller. A single intent is NOT delivery/payment acknowledgement.
/// The host must recheck epoch, original freshness, foreground and exact zone immediately
/// before its bounded semantic activate pulse. This class never authorizes vehicle motion.
/// Consumed identities are retained across Cancel/F9/epoch/revision changes; persistence
/// across application restarts is the caller's responsibility.</summary>
public sealed class ManualPayment
{
    public const double MaximumAge=.2,StoppedWindow=.4,WaitTimeout=10;
    const int MaximumConsumed=4096;
    readonly HashSet<string> consumed=new(StringComparer.Ordinal);
    readonly Action<string>? persistConsumedGate;
    readonly Action<string>? persistForgottenGate;
    PaymentKey? current;
    long epoch;
    double? lastNow,lastOriginal,stoppedSince,openSince,issuedAt;
    bool exhausted;

    public ManualPayment(Action<string>? persistConsumedGate=null,Action<string>? persistForgottenGate=null)
    {this.persistConsumedGate=persistConsumedGate;this.persistForgottenGate=persistForgottenGate;}
    public string[] ConsumedGateIds=>consumed.Order(StringComparer.Ordinal).ToArray();
    public void RestoreConsumed(IEnumerable<string> identities)
    {
        foreach(string value in identities) {
            if(string.IsNullOrWhiteSpace(value)||value.Length>4096) {exhausted=true;continue;}
            if(consumed.Contains(value))continue;
            if(consumed.Count>=MaximumConsumed) {exhausted=true;continue;}
            consumed.Add(value);
        }
    }
    public void Cancel() {current=null;epoch=0;lastNow=lastOriginal=stoppedSince=openSince=issuedAt=null;}
    /// <summary>Only the caller may certify fresh consecutive observations for
    /// at least2s beyond100m from this booth. A jump or process/source change
    /// restarts that observation period; a subsequent visit is a new encounter,
    /// including a return after recovery/teleport. F9/F8, focus or restart alone
    /// at the same booth never satisfy this condition. This is not proof of a
    /// continuously driven journey from the original payment position.</summary>
    public bool ForgetDepartedGate(string identity)
    {
        if(!consumed.Contains(identity))return false;
        try {persistForgottenGate?.Invoke(identity);}catch {return false;}
        consumed.Remove(identity);
        if(current?.GateIdentity==identity)Cancel();
        return true;
    }
    PaymentDecision Result(PaymentState state,string reason)=>new(state,false,reason);
    PaymentDecision Inhibit(string reason) {Cancel();return Result(PaymentState.Inhibited,reason);}
    bool Consume(string identity)
    {
        if(consumed.Contains(identity))return true;
        if(exhausted||consumed.Count>=MaximumConsumed) {exhausted=true;return false;}
        // Consume BEFORE external persistence or returning the intent. A failing
        // callback cannot cause an automatic retry in this process.
        consumed.Add(identity);
        try {persistConsumedGate?.Invoke(identity);return true;}catch {return false;}
    }
    public PaymentDecision Step(PaymentObservation input,PaymentZoneProof? zone)
    {
        if(!PaymentZoneProof.ValidKey(input.Key)||input.Epoch<=0||!double.IsFinite(input.Now)||input.Now<0||
            !double.IsFinite(input.OriginalAt)||input.OriginalAt<0||input.OriginalAt>input.Now||input.Now-input.OriginalAt>MaximumAge||
            !double.IsFinite(input.Speed)||!PaymentZoneProof.Finite(input.Position)||!PaymentZoneProof.Finite(input.Forward))
            return Inhibit("Ungültige oder veraltete Zahlquellen");
        if(!input.Enabled||!input.Active||input.Paused||!input.Foreground||!input.ManualKnown||!input.OwnGateBound||
            !Enum.IsDefined(input.OwnGateState)||zone==null||!zone.Valid||zone.Key!=input.Key)
            return Inhibit("Zahlkontext oder eigene bestätigte Mautzone fehlt");
        if(current!=input.Key||epoch!=input.Epoch) {Cancel();current=input.Key;epoch=input.Epoch;}
        if(lastNow is double priorNow&&(input.Now<priorNow||input.OriginalAt<lastOriginal))
            return Inhibit("Zahlquellenzeit ist zurückgesprungen");
        if(lastNow is double previous&&input.Now-previous>MaximumAge)stoppedSince=openSince=null;
        lastNow=input.Now;lastOriginal=input.OriginalAt;

        if(issuedAt!=null&&(Math.Abs(input.Speed)>.1||!zone.Contains(input.Position,input.Forward)))
            return Inhibit("Eigene Zahlposition nach einmaligem Versuch verlassen; keine Wiederholung");

        if(input.OwnGateState==BarrierState.Open) {
            stoppedSince=null;openSince??=input.OriginalAt;
            if(input.OriginalAt-openSince.Value>=StoppedWindow) {
                // It may have opened for the preceding vehicle. No activation
                // attempt has happened merely by observing an open barrier.
                issuedAt=null;
                return Result(PaymentState.GateOpen,"Nur die eigene frische Schranke wurde stabil offen beobachtet");
            }
            return Result(PaymentState.WaitingForGate,"Eigene offene Schranke bestätigen; keine Interaktion");
        }
        openSince=null;
        if(issuedAt is double sent) {
            if(input.Now-sent>=WaitTimeout) {issuedAt=null;return Result(PaymentState.TimedOut,"Keine offene eigene Schranke bestätigt; manuell übernehmen");}
            return Result(PaymentState.WaitingForGate,"Einmaliger Zahlversuch ausgegeben; kein automatischer Wiederholungsversuch");
        }
        if(consumed.Contains(input.Key.GateIdentity)||exhausted)
            return Result(PaymentState.Consumed,"Für diese Schranke ist kein weiterer automatischer Zahlversuch erlaubt");
        if(input.OwnGateState!=BarrierState.Closed||Math.Abs(input.Speed)>.1||!zone.Contains(input.Position,input.Forward)) {
            stoppedSince=null;
            return Result(PaymentState.Inhibited,"Kein bestätigter Stillstand in der eigenen geschlossenen Zahlzone");
        }
        stoppedSince??=input.OriginalAt;
        if(input.OriginalAt-stoppedSince.Value<StoppedWindow)return Result(PaymentState.Stabilizing,"Stillstand in bestätigter Zahlzone prüfen");
        if(!Consume(input.Key.GateIdentity))return Result(PaymentState.Consumed,"Zahlversuch nicht sicher speicherbar");
        issuedAt=input.Now;
        return new(PaymentState.ActivationIssued,true,"Einmalige semantische Aktivierung anfordern; Zahlung nicht bestätigt");
    }
}
