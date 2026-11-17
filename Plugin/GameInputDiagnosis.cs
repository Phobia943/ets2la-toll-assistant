namespace MautAssistent;

// Received* is the logical game input, which can include virtual controls.
// Effective* is the simulation result. Neither is a physical controller axis.
internal sealed record PedalDeliverySample(long ObservedAt,long CommandIssuedAt,double CommandThrottle,double CommandBrake,
    double? ReceivedThrottle,double? ReceivedBrake,double? EffectiveThrottle,double? EffectiveBrake,
    double? EffectiveClutch=null,int? Gear=null,int? DashboardGear=null,double? EngineRpm=null,
    bool? DeviceActive=null,long? LastInputFrameAt=null);

internal sealed record GameInputDiagnosisResult(string Code,string Reason,bool? GameForeground,
    bool ReceiptMissing=false,bool SimulationLimited=false);

internal static class GameInputDiagnosis
{
    static bool Pedal(double? value)=>value is { } x&&double.IsFinite(x)&&x>=0&&x<=1;
    static bool Fresh(long stamp,long now,long frequency)=>frequency>0&&stamp>0&&now>=stamp&&now-stamp<=frequency*.2;
    internal static GameInputDiagnosisResult Evaluate(PedalDeliverySample sample,GameInputContextSnapshot? context,long now,long frequency)
    {
        bool? foreground=context is { } c&&c.IsFresh(now,frequency)?c.GameForeground:null;
        GameInputDiagnosisResult Result(string code,string reason,bool missing=false,bool limited=false)=>new(code,reason,foreground,missing,limited);
        if(!Fresh(sample.ObservedAt,now,frequency)||!Fresh(sample.CommandIssuedAt,now,frequency)||
            !Pedal(sample.CommandThrottle)||!Pedal(sample.CommandBrake)||sample.CommandThrottle>0&&sample.CommandBrake>0)
            return Result("Unknown","Keine frische gültige Befehls-/Telemetriepaarung.");
        if(sample.DeviceActive==false)return Result("InputDeviceInactive","Das SDK meldet das Eingabegerät inaktiv; Fensterfokus ist kein Aktivierungsbefehl.");
        if(sample.LastInputFrameAt is { } frame&&!Fresh(frame,now,frequency))
            return Result("InputCallbackStalled","Keine frische Eingabe-Callbackprobe; keine bestätigte Pedalübernahme.");
        if(sample.ObservedAt<sample.CommandIssuedAt)
            return Result("AwaitingObservation","Die Telemetrieprobe stammt noch von vor diesem Pedalbefehl.");
        if(!Pedal(sample.ReceivedThrottle)||!Pedal(sample.ReceivedBrake))
            return Result("ReceiptUnknown","Logischer Pedalempfang ist nicht vollständig gemessen.");
        bool missing=sample.CommandThrottle>=.2&&sample.ReceivedThrottle<.05||sample.CommandBrake>=.2&&sample.ReceivedBrake<.05;
        if(missing)return foreground==false?
            Result("UnfocusedReceiptMissing","Anderes Fenster im Vordergrund und angefordertes Pedal im Spieleingang nicht gemessen; Kausalität nicht bewiesen.",true):
            Result("ReceiptMissing","Angefordertes Pedal im logischen Spieleingang nicht gemessen; Ursache noch unbekannt.",true);
        if(Math.Abs(sample.CommandThrottle-sample.ReceivedThrottle!.Value)>.15||Math.Abs(sample.CommandBrake-sample.ReceivedBrake!.Value)>.15)
            return Result("ReceiptDifferent","Logischer Pedaleingang weicht vom Befehl ab; weitere Eingaben oder Übersteuerung sind damit nicht unterschieden.");
        if(!Pedal(sample.EffectiveThrottle)||!Pedal(sample.EffectiveBrake))
            return Result("EffectUnknown","Logischer Pedalempfang vorhanden; Simulationseffekt nicht vollständig gemessen.");
        bool limited=sample.ReceivedThrottle>=.2&&sample.EffectiveThrottle<.05||sample.ReceivedBrake>=.2&&sample.EffectiveBrake<.05;
        return limited?Result("SimulationLimited","Pedal im Spieleingang vorhanden, Simulationseffekt begrenzt; Schaltung/Kupplung ist damit noch nicht als Ursache bewiesen.",limited:true):
            Result("Observed","Logischen Pedalempfang und Simulation getrennt erfasst; keine eigenständige Fahrfreigabe.");
    }
}
