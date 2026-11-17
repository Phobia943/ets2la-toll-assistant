using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Output;
using ETS2LA.Game.SDK;
using ETS2LA.Shared;
using ETS2LA.State;
using MautAssistent;

// HOST-ISOLATED ONLY: a real planned lane change makes PathLib read
// GameTelemetry.Current. The replay must replace that getter with its private
// DTO first. This fixture intentionally has no ordinary Run registration.
public static class GateSelectionTransitionIntegration
{
    sealed class Planner:Plugin
    {
        NavigationData navigationData=new(){entries=[new NavigationEntry{nodeUid=91234}]};
        public override PluginInformation Info=>new(){Id="test.gate-transition",Name="Transition fixture",Version="1",Description="Synthetic read-only route"};
    }
    public sealed class Fixture
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        public readonly RoutingContinuationIntegration.LateUpgradeFixture Map=RoutingContinuationIntegration.CreateLateUpgradeFixture();
        public readonly ApplicationState State=(ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationState));
        public readonly TollPlugin Plugin;
        public readonly List<ControlEvent> Outputs=[];
        public readonly List<string> Notices=[];
        public Action<Vector3,double>? BeforePath;
        readonly Planner planner=new();
        public readonly ETS2LA.Game.SDK.Semaphore[] Signals;
        public Fixture(Action<ControlEvent>? sink=null,bool useOwnedTraffic=false)
        {
            State.DrivingMode=DrivingMode.FullSelfDriving;State.DesiredSpeed=25;
            Plugin=new(()=>State,(title,body)=>Notices.Add(title+": "+body),o=>{Outputs.Add(o);sink?.Invoke(o);},
                readAcc:()=>throw new Exception("No ACC instance is permitted in the transition fixture"),
                readPayments:Map.Payments,readPlanner:()=>planner,readLaneAssist:()=>null,writeDiagnostic:(_,_)=>{},
                readClearance:Map.Clearance,findClearRoute:Map.SearchRoute,readCruiseLimits:()=>new(0,false),
                readLongitudinalTraffic:useOwnedTraffic?null:()=>new(float.NaN,true,"Explicit synthetic empty traffic"));
            Call("StopDiagnostics");Plugin._IsRunning=true;
            Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});
            Set("subscribed",true);Set("lastWrite",double.MaxValue);Set("lastMotionAt",double.MaxValue);
            Set("active",true);Set("headingTurns",.5d);Set("path",Map.Data);Set("lastAcceptedPlan",Map.Data);
            Set("safetyTrace",RouteMatcher.Sample(Map.Data));
            Signals=Map.Parsed.Descriptor!.Semaphores.Select(s=>new ETS2LA.Game.SDK.Semaphore {
                id=(int)s.SemaphoreId,position=s.Position,state=s.SemaphoreId==4?1:3,type=SemaphoreType.GATE}).ToArray();
            Move(220,0);
            Set("startCheck",new TollPlugin.StartCheck(Map.At(220),.5,ConnectorStart.NavigationKey(planner),null));
            Set("decisionArmed",true);State.EnableAssists=true;
        }
        public void Set(string field,object value)=>typeof(TollPlugin).GetField(field,Hidden)!.SetValue(Plugin,value);
        public T Get<T>(string field)=>(T)typeof(TollPlugin).GetField(field,Hidden)!.GetValue(Plugin)!;
        public object? Call(string method,params object?[] args)=>typeof(TollPlugin).GetMethod(method,Hidden)!.Invoke(Plugin,args);
        public double Now=>Get<Stopwatch>("clock").Elapsed.TotalSeconds;
        public void Move(float remaining,double speed)
        {
            var point=Map.At(remaining);
            BeforePath?.Invoke(point,speed);
            Set("position",point);Set("speed",speed);Set("sdk",true);Set("paused",false);
            Set("gates",Signals);
            foreach(var field in new[]{"telemetryAt","gatesAt","pathAt","gameTimeChangedAt","lastAcceptedPlanAt","evaluatedAt"})Set(field,Now);
        }
        public DriveAction Prime()
        {
            Move(220,0);
            return (DriveAction)Call("SafetyWatchdog",Now)!;
        }
        public void ChooseManual()
        {
            Map.Ready=true;Move(130,77/3.6);
            Call("OnPath",Map.Data);
            Plugin.Tick();
        }
        public void Upgrade()
        {
            Move(65,TollEntry.ShortChangeSpeed);
            Call("OnPath",Map.Data);
        }
        public DriveAction ProbeWatchdog()=>(DriveAction)Call("SafetyWatchdog",Now)!;
        public void Tick()=>Plugin.Tick();
        public int Selected=>Get<TollRouter>("router").SelectedGate??-1;
        public bool ShortChange=>Get<TollRouter>("router").HasShortChange;
        public bool Prepared=>Get<TollRouter>("router").PreparingAutomaticEntry;
        public bool Armed=>Get<bool>("decisionArmed");
        public void Stop()=>Call("StopDiagnostics");
    }
}
