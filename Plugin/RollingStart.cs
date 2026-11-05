using System.Numerics;
using ETS2LA.Shared;
using ETS2LA.State;
using PathLib;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    const double RollingStartTimeout=4;
    readonly Func<IPlugin,PlannedPathData,PlannedPathData,Vector3,double,bool> installStart;
    bool rollingStartPending,rollingStartCalculating;
    int rollingStartGeneration;
    double rollingStartAt;
    ulong rollingStartNavigation;
    DrivingMode rollingStartMode;
    string rollingStartStatus="Noch kein Start während der Fahrt angefordert.";

    // F8 is an explicit request. Never turn a preparation tick into a driving request.
    // The host remains disabled until a checked plan can be installed on its own tick.
    bool RequestRollingStart()
    {
        double now=clock.Elapsed.TotalSeconds;
        if (!_IsRunning || faultBrake.Latched || brakeHeld || now<takeoverUntil || !SensorsFresh(now) || !double.IsFinite(speed) || speed<=.1 ||
            (!active && !automaticPreparation.Requested) || readPlanner() is not { } planner ||
            ConnectorStart.IsLoadingMap(planner) || readAcc()==null)
            return false;
        active=false;
        ResetDriving();
        preview=previewCalculating=false;
        previewGeneration++;
        previewRouter=null;
        startCheck=null;
        rollingStartPending=true;
        rollingStartCalculating=false;
        rollingStartGeneration++;
        rollingStartAt=now;
        rollingStartNavigation=ConnectorStart.NavigationKey(planner);
        rollingStartMode=State.DrivingMode;
        preparationReason=rollingStartStatus="Start angefordert – noch selbst steuern, während die Route geprüft wird. F9 bricht ab.";
        return true;
    }

    void CancelRollingStart()
    {
        rollingStartPending=rollingStartCalculating=false;
        rollingStartGeneration++;
    }

    void FailRollingStart(string reason)
    {
        CancelRollingStart();
        State.EnableAssists=false;
        active=false;
        ResetDriving();
        automaticPreparation.Request();
        preparationReason=rollingStartStatus=reason;
        // A refused request while driving manually is not a tracking failure.
        // It must not emit a brake or steering command.
        notify("Start nicht übernommen – selbst weiterfahren",reason);
    }

    void CompleteRollingStart(int request,IPlugin? planner,PlannedPathData published,
        Vector3 origin,double heading,Vector3[] history)
    {
        PlannedPathData? planned=null;
        string? failure=null;
        try { if(planner!=null) planned=probeRoute(planner,origin,heading,history); }
        catch(Exception ex) { failure=ex.GetBaseException().Message; }
        lock(sync)
        {
            DrainInputFrames();
            if(!rollingStartPending || request!=rollingStartGeneration)return;
            double now=clock.Elapsed.TotalSeconds;
            if(!_IsRunning || faultBrake.Latched || State.EnableAssists || !SensorsFresh(now) ||
                now-rollingStartAt>RollingStartTimeout || !double.IsFinite(speed) || speed<0 ||
                State.DrivingMode!=rollingStartMode || !ReferenceEquals(path,published) ||
                !ReferenceEquals(planner,readPlanner()) || rollingStartNavigation!=ConnectorStart.NavigationKey(planner))
            {
                FailRollingStart("Startprüfung abgebrochen: Spieldaten, Route oder Fahrmodus haben sich geändert.");
                return;
            }
            if(planned==null || planned.HasError)
            {
                FailRollingStart(failure??planned?.ErrorMessage??"Keine eindeutige Route für den Start gefunden.");
                return;
            }
            try
            {
                // The truck can move during the probe. Validate at the latest
                // telemetry position, with a horizon based on the current speed.
                var testRouter=router;
                testRouter.Discover(planned,position,gates,speed);
                testRouter.Apply(planned,position,out var issue,speed);
                ExitHandoff.RepairRoadConnections(planned,position);
                var trace=RouteMatcher.SampleForTracking(planned,position,speed);
                issue??=new TrackingGuard().Check(trace,position,headingTurns,speed);
                var projection=trace.ProjectForTracking(position);
                if(projection.Offset>1.25 || Vector3.Dot(TrackingGuard.TruckForward(headingTurns),trace.DirectionAt(projection.Along))<.9)
                    issue??="Lkw und geplante Fahrspur sind noch nicht ausreichend ausgerichtet.";
                if(trace.Length-projection.Along<TrackingGuard.RequiredLookahead(speed))
                    issue??="Zu wenig zusammenhängende Strecke für den Start bei diesem Tempo.";
                // The native planner prunes only one passed item per tick.
                // A moving probe must not hand it an already distant first item.
                if(!ConnectorStart.TrimPassedStart(planned,position))
                    issue??="Der aktuelle Routenanfang passt nicht mehr zur Lkw-Position.";
                // Do not engage into an already necessary takeover or sudden stop.
                var gate=RouteMatcher.Select(planned,position,gates,null);
                var initial=new TollController(options).Step(new(now,speed,true,true,gate,
                    testRouter.SelectedGate is int selected && testRouter.PaymentFor(selected)==TollPayment.Manual));
                if(testRouter.StopBoundary.HasValue || initial.RequiresTakeover)
                    issue??="Vor der Mautstelle bitte zuerst manuell übernehmen und sicher anhalten.";
                if(issue!=null) { FailRollingStart(issue);return; }
                var acc=readAcc();
                if(acc==null) { FailRollingStart("Tempomat ist nicht bereit.");return; }
                bridge.Attach(acc,Evaluate,()=>Status);
                if(bridge.Constraint==null) { FailRollingStart("Tempomat wird noch initialisiert.");return; }
                // All potentially failing geometry checks precede the native commit.
                // The verified adapter refuses foreign threads/builds or a nonempty plan.
                if(planner==null || !installStart(planner,planned,published,position,headingTurns))
                {
                    FailRollingStart("Die geprüfte Route konnte nicht an Pathfinding übergeben werden.");return;
                }
                CancelRollingStart();
                active=true;
                automaticPreparation.Cancel();
                ResetDriving();controller.Reset();passage.Reset();tracked=null;candidate=null;
                safetyTrace=trace;
                pathAt=now;
                decision=initial;
                evaluatedAt=now;
                decisionArmed=true;
                State.EnableAssists=true;
                preparationReason="";
                rollingStartStatus="Route geprüft – Fahrassistenz während der Fahrt gestartet.";
                SafetyWatchdog(now);
                HandlePath(published);
            }
            catch(Exception ex)
            {
                if(active && State.EnableAssists) Disarm("Startübergabe fehlgeschlagen: "+ex.GetBaseException().Message);
                else FailRollingStart(ex.GetBaseException().Message);
            }
        }
    }
}
