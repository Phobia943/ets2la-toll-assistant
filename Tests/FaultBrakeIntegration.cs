using System.Reflection;
using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Controls;
using ETS2LA.Game.Output;
using ETS2LA.Game.Telemetry;
using ETS2LA.Game.Data;
using ETS2LA.State;
using MautAssistent;
using PathLib;
using TruckLib.ScsMap;

public static class FaultBrakeIntegration
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(TollPlugin plugin,string name,object value)=>typeof(TollPlugin).GetField(name,Hidden)!.SetValue(plugin,value);
    static object? Call(TollPlugin plugin,string name,params object?[] args)=>typeof(TollPlugin).GetMethod(name,Hidden)!.Invoke(plugin,args);
    public static void Run(Action<bool,string> assert)
    {
        var state=(ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationState));
        state.DrivingMode=DrivingMode.FullSelfDriving;
        var sent=new List<ControlEvent>();
        var plugin=new TollPlugin(()=>state,(_,_)=>{},sent.Add);
        plugin._IsRunning=true;
        Set(plugin,"lastWrite",double.MaxValue);
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var start=new Node {Uid=501,Position=Vector3.Zero,Rotation=rotation};
        var end=new Node {Uid=502,Position=new(0,0,200),Rotation=rotation};
        var road=(ParsedRoad)RuntimeHelpers.GetUninitializedObject(typeof(ParsedRoad));
        road.Road=new Road {Node=start,ForwardNode=end,Length=200};road.StartNode=start;road.EndNode=end;
        road.LeftLaneOffsetsStart=road.LeftLaneOffsetsEnd=[-2];road.RightLaneOffsetsStart=road.RightLaneOffsetsEnd=[2];
        var point=road.InterpolateLane(.5f,Side.Right,0).Position;
        var route=new PlannedPathData {HasError=false,PlannedPath=[new PlannedItem {item=new ParsedRoadList([road]),startNode=start,endNode=end,direction=Direction.Forward,laneIndex=1}]};
        var telemetry=new GameTelemetryData {sdkActive=true,time=1};
        telemetry.truckFloat.speed=10;telemetry.truckPlacement.coordinate=new(point.X,point.Y,point.Z);
        telemetry.truckPlacement.rotation=new(.5,0,0);
        var clock=(System.Diagnostics.Stopwatch)typeof(TollPlugin).GetField("clock",Hidden)!.GetValue(plugin)!;
        void Prepare() {
            plugin.SetActive(false);state.EnableAssists=false;
            Call(plugin,"OnTelemetry",telemetry);
            Set(plugin,"active",true);state.EnableAssists=true;
            Call(plugin,"OnPath",route);
            assert(plugin.Active&&!plugin.FaultBraking,"Straight forward route must be accepted");
        }
        telemetry.truckFloat.speed=0;
        Call(plugin,"OnTelemetry",telemetry);
        Set(plugin,"active",true);state.EnableAssists=true;
        Call(plugin,"OnPath",new PlannedPathData {HasError=true,ErrorMessage="Waiting for map data...",PlannedPath=[]});
        assert((float)Call(plugin,"Evaluate")! == -.35f && sent.Count==0,"Loading map at standstill only requests an ACC hold");
        Call(plugin,"OnPath",new PlannedPathData {HasError=true,ErrorMessage="No suitable starting road found for path planning.",PlannedPath=[]});
        assert(!plugin.Active&&!state.EnableAssists&&!plugin.FaultBraking&&sent.Count==0,"Unsuitable start road rejects F8 without latching pedals");

        telemetry.truckFloat.speed=10;Prepare();
        double readyAt=clock.Elapsed.TotalSeconds;
        foreach (var field in new[]{"gameTimeChangedAt","gatesAt","evaluatedAt"}) Set(plugin,field,readyAt);
        Set(plugin,"decisionArmed",true);
        assert(float.IsNaN((float)Call(plugin,"Evaluate")!),"Normal route delegates to native ACC when there is no toll constraint");
        Set(plugin,"evaluatedAt",readyAt-.35);
        assert((float)Call(plugin,"Evaluate")! == -.5f && plugin.Active && sent.Count==0,
            "Brief decision loss on a verified approach must request no gas without emergency output");
        Call(plugin,"OnPath",new PlannedPathData {HasError=true,ErrorMessage="No suitable starting road found for path planning.",PlannedPath=[]});
        assert(plugin.FaultBraking&&sent[^1].Variables.abackward==-1,"Explicit route loss during recovery still brakes immediately");

        Prepare();
        Call(plugin,"OnPath",new PlannedPathData {HasError=true,ErrorMessage="No suitable starting road found for path planning.",PlannedPath=[]});
        assert(plugin.FaultBraking&&!plugin.Active&&!state.EnableAssists,"Path error must brake and stop steering without waiting for ACC");
        assert(sent[^1].Variables.abackward==-1 && sent[^1].Variables.aforward==null && sent[^1].Variables.steering==0,"Request signed brake and explicitly release the steering override");
        // An outstanding native ACC frame at full throttle must still mix to braking.
        var output=sent[^1];float mixed=(output.Properties.Weight*output.Variables.abackward!.Value+1)/(output.Properties.Weight+1);
        assert(mixed<-.97f && output.ChannelDefinition.Timeout<=.2f,"Fault brake dominates the known ACC weight and has a bounded channel timeout");
        state.EnableAssists=true;
        assert((float)Call(plugin,"Evaluate")!<0 && !state.EnableAssists,"F8 cannot release fault braking or resume driving");
        Call(plugin,"OnPath",route);
        assert(plugin.FaultBraking,"Recovered route must not automatically release brake");
        telemetry.truckFloat.speed=0;Call(plugin,"OnTelemetry",telemetry);
        assert(plugin.FaultBraking&&sent[^1].Variables.abackward==-1,"Hold at standstill until explicit takeover");
        Call(plugin,"OnCancel",null,new ControlChangeEventArgs(false,null!));
        assert(!plugin.FaultBraking&&sent[^1].Variables.abackward==0&&sent[^1].Variables.steering==0,"Actual F9 release event must neutralize pedals and steering");
        assert(!plugin.Active,"F9 does not silently prepare another journey");

        Prepare();state.EnableAssists=false;
        Call(plugin,"OnCancel",null,new ControlChangeEventArgs(false,null!));
        Call(plugin,"OnPath",new PlannedPathData {HasError=true,ErrorMessage="Assists are disabled.",PlannedPath=[]});
        assert(!plugin.FaultBraking&&plugin.Active,"Ordinary F9 must not be mistaken for tracking failure");
        Prepare();Set(plugin,"headingTurns",0d);
        var reversed=new PlannedPathData {HasError=false,PlannedPath=[new PlannedItem {item=route.PlannedPath[0].item,startNode=end,endNode=start,direction=Direction.Backward,laneIndex=1}]};
        Call(plugin,"OnPath",reversed);
        assert(plugin.FaultBraking&&plugin.Status.Contains("kehrt"),"Native replan in the opposite direction must latch a stop even if truck heading matches it");

        telemetry.truckFloat.speed=10;Prepare();
        Set(plugin,"telemetryAt",1d);
        Set(plugin,"pathAt",-100d);
        Call(plugin,"SafetyWatchdog",1d);
        assert(plugin.FaultBraking,"Stale route triggers braking without an ACC callback");
        Prepare();telemetry.truckPlacement.rotation=new(0,0,0);
        Call(plugin,"OnTelemetry",telemetry);
        assert(plugin.FaultBraking,"Opposite heading triggers braking on the telemetry callback");
        telemetry.truckPlacement.rotation=new(.5,0,0);Prepare();
        Set(plugin,"telemetryAt",-100d);Call(plugin,"SafetyWatchdog",1d);
        assert(plugin.FaultBraking,"Telemetry loss keeps fault braking independent of telemetry updates");

        // Exercise the installed host's channel ingestion without its constructor,
        // which would start shared-memory output threads. No actual vehicle output.
        var host=(GameOutput)RuntimeHelpers.GetUninitializedObject(typeof(GameOutput));
        host.Channels=new();host.OnControlEvent(sent[^1]);
        assert(host.Channels[FaultBrake.ChannelId].Variables.abackward==-1,"Installed output API accepts brake event");
        plugin.OnDisable();host.OnControlEvent(sent[^1]);
        assert(!plugin.FaultBraking&&host.Channels[FaultBrake.ChannelId].Variables.abackward==0,"Plugin unload must neutralize its own pedal output");
    }
}
