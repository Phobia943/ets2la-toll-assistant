using ETS2LA.Controls;
using ETS2LA.State;
using ETS2LA.Settings.Global;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    int cruiseStartHandled,requestedCruisePreset;
    bool preferredCruiseOwned;
    DrivingMode requestedCruiseMode;
    double baselineCruise;
    string cruisePresetStatus="";
    public string RoadCruiseStatus {get {lock(sync)return cruisePresetStatus;}}
    static CruiseLimits ReadHostCruiseLimits()
    {
        var settings=AssistanceSettings.Current;
        return new(settings.MaximumSpeed,settings.EnabledTrafficRules.Contains(TrafficRule.Speedlimits));
    }
    void RecordCruiseStart(ControlChangeEventArgs input,bool usePreset)
    {
        // F8/F10 while already driving remain the host's normal speed controls.
        if(input.NewValue is not true)return;
        if(Volatile.Read(ref requestedCruisePreset)!=0) {
            preferredCruiseOwned=false;Interlocked.Exchange(ref cruiseStartHandled,1);Interlocked.Exchange(ref requestedCruisePreset,0);return;
        }
        if(supervisor.Checkpoint.Drove||Volatile.Read(ref cruiseStartHandled)!=0) {
            preferredCruiseOwned=false;Volatile.Write(ref requestedCruisePreset,0);return;
        }
        requestedCruiseMode=State.DrivingMode;
        Volatile.Write(ref requestedCruisePreset,usePreset?1:2);
    }
    void ApplyCruiseStartPreference()
    {
        using var operation=faultBrake.BeginOperation();
        int requested=Volatile.Read(ref requestedCruisePreset);
        if(requested==0||Volatile.Read(ref cruiseStartHandled)!=0||!State.EnableAssists||!active||preview||faultBrake.Latched)return;
        if(requested!=1||!options.BriskOpenRoads||State.DrivingMode!=requestedCruiseMode||State.DrivingMode==DrivingMode.LaneAssistOnly) {
            Interlocked.Exchange(ref cruiseStartHandled,1);Interlocked.Exchange(ref requestedCruisePreset,0);return;
        }
        double? target=CruiseLimitPolicy.Preferred(options.RoadCruiseSpeedKph/3.6,readCruiseLimits(),physicsObservation?.SpeedLimitMps??double.NaN);
        if(target==null||!PreferredCorridorReady()) {cruisePresetStatus="Höheres Wunschtempo wartet auf ausreichende Streckenvorschau und bekannte Spielbegrenzungen.";return;}
        faultBrake.RunCurrent(()=> {
            if(!State.EnableAssists||!active||Volatile.Read(ref requestedCruisePreset)!=requested)return;
            baselineCruise=Math.Max(0,State.DesiredSpeed);
            preferredCruiseOwned=target>baselineCruise;
            State.DesiredSpeed=(float)target;
            Interlocked.Exchange(ref cruiseStartHandled,1);Interlocked.Exchange(ref requestedCruisePreset,0);
            cruisePresetStatus=$"Wunschtempo freie Strecke: {State.DesiredSpeed*3.6:F0} km/h. Kurven und andere Begrenzungen bleiben vorrangig.";
        });
    }
    bool PreferredCorridorReady()
    {
        if(!options.BriskOpenRoads||!options.CurvePreviewEnabled||controller.Tracking||router.StopBoundary.HasValue||router.HasShortChange||
            safetyTrace is not {EndsAtGap:false} trace||candidate is { } gate&&gate.Distance<=controller.ApproachHorizon(speed))return false;
        double required=PreferredCruiseCoverage;
        return curvePreview.Read(trace,position,required)?.HasFullCoverage(trace,position,required)==true;
    }
    float LimitForPreferredCruise(float existing)
    {
        if(!preferredCruiseOwned)return existing;
        double? authorized=CruiseLimitPolicy.Preferred(options.RoadCruiseSpeedKph/3.6,readCruiseLimits(),physicsObservation?.SpeedLimitMps??double.NaN);
        if(PreferredCorridorReady()&&authorized is double cap&&speed<=cap)return existing;
        // Losing authorization for an automatic increase must not retain an
        // unbounded positive command. Fall back gradually to the original target.
        double fallbackTarget=PreferredCorridorReady()&&authorized.HasValue?authorized.Value:baselineCruise;
        float fallback=(float)Math.Clamp((fallbackTarget-speed)*.8,-options.ComfortableDeceleration,0);
        return float.IsFinite(existing)?Math.Min(existing,fallback):fallback;
    }
    void ResetCruiseStartPreference()
    {
        Interlocked.Exchange(ref cruiseStartHandled,0);Interlocked.Exchange(ref requestedCruisePreset,0);
        preferredCruiseOwned=false;baselineCruise=0;cruisePresetStatus="";
    }
}
