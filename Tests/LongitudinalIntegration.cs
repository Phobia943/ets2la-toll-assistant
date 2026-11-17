using MautAssistent;
using ETS2LA.State;
using ETS2LA.Controls;
using ETS2LA.Game.Output;
using System.Diagnostics;

public static class LongitudinalIntegration
{
    public static void SimulationClock(Action<bool,string> check)
    {
        var physics=typeof(TollPlugin).GetNestedType("PhysicsObservation",System.Reflection.BindingFlags.NonPublic)!;
        var telemetry=typeof(TollPlugin).GetNestedType("TelemetryFrame",System.Reflection.BindingFlags.NonPublic)!;
        void Deliver(RollingStartIntegration.Fixture f,ulong simulated,double speed) {
            var reading=Activator.CreateInstance(physics,simulated,simulated,0f,0f,0f,0f,0f,0f,0f)!;
            var frame=Activator.CreateInstance(telemetry,f.Now,simulated,true,false,speed,f.Point,.5d,reading)!;
            telemetry.GetProperty("Origin")!.SetValue(frame,Stopwatch.GetTimestamp());
            f.Set("incomingTelemetry",frame);
            f.Call("DrainInputFrames");
        }
        var fast=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        var delayed=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        Deliver(fast,1000000,20);Deliver(delayed,1000000,20);
        Deliver(fast,1050000,20.05);Thread.Sleep(35);Deliver(delayed,1050000,20.05);
        check(fast.Get<bool>("accelerationKnown")&&delayed.Get<bool>("accelerationKnown")&&
            Math.Abs(fast.Get<double>("observedAcceleration")-1)<.001&&
            Math.Abs(delayed.Get<double>("observedAcceleration")-1)<.001,
            "Real frame draining measures identical dynamics across different callback arrival delays");
        Deliver(delayed,1150000,20.15);
        check(Math.Abs(delayed.Get<double>("observedAcceleration")-1)<.001,"Coalesced source frames use their actual simulation interval");
        Deliver(delayed,1150000,20.2);
        check(!delayed.Get<bool>("accelerationKnown")&&delayed.Get<double>("observedAcceleration")==0,
            "A repeated simulation timestamp cannot acquire dynamics from advancing arrival time");
        fast.Call("StopDiagnostics");delayed.Call("StopDiagnostics");
    }
    public static void RepeatedPhysics(Action<bool,string> check)
    {
        var f=Started();
        var physics=typeof(TollPlugin).GetNestedType("PhysicsObservation",System.Reflection.BindingFlags.NonPublic)!;
        var telemetry=typeof(TollPlugin).GetNestedType("TelemetryFrame",System.Reflection.BindingFlags.NonPublic)!;
        void Deliver(ulong simulated,double age=0) {
            var p=Activator.CreateInstance(physics,simulated,simulated,0f,0f,0f,0f,0f,0f,0f)!;
            var frame=Activator.CreateInstance(telemetry,f.Now-age,simulated,true,false,5d,f.Point,.5d,p)!;
            telemetry.GetProperty("Origin")!.SetValue(frame,Stopwatch.GetTimestamp()-(long)(age*Stopwatch.Frequency));
            f.Set("incomingTelemetry",frame);f.Call("DrainInputFrames");
        }
        Deliver(1_000_000);Deliver(1_050_000);f.Fresh(5);
        f.Call("RunLongitudinal",1L,Stopwatch.GetTimestamp());
        var initial=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(initial.Published&&initial.Output.Throttle>0&&initial.Output.Brake==0&&initial.DynamicsOrigin>0,
            "Two actual drained simulation frames establish an original propulsion evidence lease");
        Thread.Sleep(20);Deliver(1_050_000);f.Fresh(5);f.Call("RunLongitudinal",1L,Stopwatch.GetTimestamp());
        var repeated=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(repeated.Published&&repeated.Output.Throttle>0&&repeated.Output.Brake==0&&
            repeated.DynamicsOrigin==initial.DynamicsOrigin&&!repeated.Bounds.Any(b=>b.Source=="DynamicsPending"),
            "Identical physics repetition cannot introduce a brake bound or renew the derivative origin");
        Thread.Sleep(210);Deliver(1_050_000);f.Fresh(5);f.Call("RunLongitudinal",1L,Stopwatch.GetTimestamp());
        var expired=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(expired.Output.Throttle==0&&expired.Bounds.Any(b=>b.Source=="DynamicsPending"),
            "Fresh repeat callbacks cannot keep expired dynamics alive");
        f.Call("StopDiagnostics");

        var late=Started();
        var dynamics=late.Get<SimulationDynamics>("simulationDynamics");
        Thread.Sleep(250);
        long qpc=Stopwatch.GetTimestamp();double now=late.Now;
        dynamics.Observe(now-.24,qpc-(long)(Stopwatch.Frequency*.24),1_000_000,1_000_000,5,late.Point,.5,true,false);
        dynamics.Observe(now-.19,qpc-(long)(Stopwatch.Frequency*.19),1_050_000,1_050_000,5,late.Point,.5,true,false);
        // The constructor is an offline DI fixture; make the real telemetry
        // requirement explicit without enabling any live host or SDK singleton.
        var p2=Activator.CreateInstance(physics,1_050_000UL,1_050_000UL,0f,0f,0f,0f,0f,0f,0f)!;
        late.Set("appliedTelemetry",Activator.CreateInstance(telemetry,now,1_050_000UL,true,false,5d,late.Point,.5d,p2)!);
        late.Plugin.BeforeLongitudinalCommit=()=>Thread.Sleep(40);
        late.Fresh(5);late.Call("RunLongitudinal",1L,Stopwatch.GetTimestamp());
        var stale=late.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(stale.Output.Throttle>0&&!stale.Published,
            "An originally valid derivative that expires during final output work cannot publish gas");
        late.Call("StopDiagnostics");
    }
    static RollingStartIntegration.Fixture Started()
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.DuringReadAcc=()=>throw new Exception("The own controller must never query native ACC");
        f.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();f.State.DesiredSpeed=25;f.Fresh();return f;
    }
    public static void TickAndTakeover(Action<bool,string> check)
    {
        var f=Started();f.Plugin.Tick();
        check(f.State.EnableAssists&&f.Plugin.Active&&!f.Plugin.FaultBraking,"FSD starts with no ACC instance");
        var command=f.Outputs.LastOrDefault(o=>o.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
        check(command?.Variables.aforward>0&&command.Variables.abackward==0,"Own actual Tick publishes acceleration without ACC callbacks");
        var snapshot=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(snapshot.Published&&snapshot.Input.TargetSpeed==25&&snapshot.Bounds.Any(b=>b.Source=="Traffic"),"Atomic control snapshot explains the actual output");
        f.Fresh();f.Call("OnControlOutput",new ControlEvent{ChannelDefinition=new(){Id="AdaptiveCruiseControl.Acceleration"},Variables=new(){aforward=1}});
        check(f.Get<object>("lastAccCommand").GetType().GetProperty("Throttle")!.GetValue(f.Get<object>("lastAccCommand")) is float pedal&&pedal<1,
            "Stray native ACC is not misreported as own pedal authority");
        f.F9();int count=f.Outputs.Count;f.Fresh();f.Plugin.Tick();
        check(!f.State.EnableAssists&&!f.Plugin.FaultBraking&&f.Outputs.Skip(count).All(o=>o.Variables.aforward is null or 0),"F9 prevents new throttle and keeps manual authority");
        f.Call("StopDiagnostics");

        var lane=Started();lane.State.DrivingMode=DrivingMode.LaneAssistOnly;lane.Fresh();lane.Plugin.Tick();
        check(lane.Outputs.All(o=>o.ChannelDefinition.Id!=TollPlugin.LongitudinalChannelId),"Lane-only mode produces no automatic pedals");lane.Call("StopDiagnostics");

        var braking=Started();braking.Traffic=()=>new(-2,false,"Same-lane standing vehicle",20,"traffic-vehicle");braking.Fresh();braking.Plugin.Tick();
        command=braking.Outputs.Last(o=>o.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
        check(command.Variables.aforward==0&&command.Variables.abackward<0,"Fresh traffic stop overrides cruise in the actual Tick");
        braking.Call("OnBrakeInput",null,new ControlChangeEventArgs(.8f,null!));
        check(!braking.State.EnableAssists&&!braking.Plugin.FaultBraking,"Physical brake cancels own regulation and its fault output");braking.Call("StopDiagnostics");
    }
    public static void LateWork(Action<bool,string> check)
    {
        var f=Started();
        f.DuringReadTraffic=f.F9;
        f.Plugin.Tick();
        check(!f.State.EnableAssists&&f.Outputs.All(o=>o.Variables.aforward is null or 0),"Cancellation during observation cannot publish a late throttle command");
        f.Call("StopDiagnostics");
        var late=Started();late.DuringReadTraffic=()=>Thread.Sleep(230);late.Plugin.Tick();
        check(late.Outputs.All(o=>o.ChannelDefinition.Id!=TollPlugin.LongitudinalChannelId),"Long tick cannot restamp its expired output origin");late.Call("StopDiagnostics");
    }
    public static void PedalDeadman(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(true,withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Set("steeringMonitoringLive",true);f.Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();
        void Lane()=>f.Call("OnControlOutput",new ControlEvent {ChannelDefinition=new(){Id="LaneAssist.SteeringCorrection"},Variables=new(){steering=0}});
        Lane();f.Call("CheckSteeringOutput");f.Fresh();f.Plugin.Tick();f.Call("CheckLongitudinalOutput");
        check(f.State.EnableAssists&&!f.Plugin.FaultBraking,"Fresh real pedal command establishes the independent lease");
        var routeLock=f.Get<object>("sync");
        Task? blockedTick=null;
        Monitor.Enter(routeLock);
        try {
            blockedTick=Task.Run(f.Plugin.Tick);
            for(int i=0;i<6;i++){Thread.Sleep(32);Lane();f.Call("CheckSteeringOutput");}
            check(!blockedTick.IsCompleted&&!f.Plugin.FaultBraking,"The real producer Tick is blocked while live lane commands remain healthy");
            var task=Task.Run(()=>f.Call("CheckLongitudinalOutput"));
            check(task.Wait(TimeSpan.FromMilliseconds(300)),"Pedal deadman does not wait for a blocked route/control tick");
            check(!f.State.EnableAssists&&f.Plugin.FaultBraking&&f.Outputs.Any(o=>o.ChannelDefinition.Id==FaultBrake.ChannelId&&o.Variables.abackward==-1),
                "Lost own pedals explicitly brake despite continuing healthy lane commands");
        } finally {Monitor.Exit(routeLock);check(blockedTick?.Wait(TimeSpan.FromSeconds(2))==true,"Blocked old Tick settles after the independent trip");}
        f.F9();f.Call("CheckLongitudinalOutput");
        check(!f.Plugin.FaultBraking&&!f.State.EnableAssists,"Manual F9 clears the deadman without restarting it");f.Call("StopDiagnostics");
    }
    public static void RoadLimitAndGameCruise(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,readCruiseLimits:()=>new(0,true),writeDiagnostic:(_,_)=>{});
        f.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});f.F8();f.Probe();f.State.DesiredSpeed=25;
        f.Fresh(0);f.Set("startCheck",new TollPlugin.StartCheck(f.Point,.5,ConnectorStart.NavigationKey(f.Planner),null));
        f.Set("decision",new TollDecision("Langsame Durchfahrt",10/3.6,.35f,"canonical-open-booth"));
        f.Call("RunLongitudinal",1L,Stopwatch.GetTimestamp());
        var result=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(Math.Abs(result.Input.TargetSpeed-10/3.6)<.001&&result.Output.Throttle>0&&result.Output.Brake==0,
            "Missing road limit zero cannot convert a confirmed local open passage into a zero-speed hold");
        var physics=typeof(TollPlugin).GetNestedType("PhysicsObservation",System.Reflection.BindingFlags.NonPublic)!;
        var reading=Activator.CreateInstance(physics,1UL,1UL,0f,0f,0f,0f,0f,0f,0f)!;
        physics.GetProperty("GameCruise")!.SetValue(reading,true);f.Set("physicsObservation",reading);
        f.Fresh();f.Plugin.Tick();
        check(!f.State.EnableAssists&&!f.Plugin.Active&&!f.Plugin.FaultBraking&&f.Notices.Any(n=>n.Contains("Spieltempomaten")),
            "Active game cruise yields to manual authority without blindly toggling it or mixing controllers");f.Call("StopDiagnostics");
    }
}
