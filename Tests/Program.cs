using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using MautAssistent;

if (args is ["--replay", var file])
{
    using var report = JsonDocument.Parse(File.ReadAllText(file));
    var recording = report.RootElement.GetProperty("driveRecording").Deserialize<DriveRecording>(
        new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!;
    var replay = new DriveSupervisor(recording.InitialState);
    int mismatches = 0;
    foreach (var sample in recording.Samples)
    {
        var actual = replay.Step(sample.Frame);
        if (actual == sample.Action) continue;
        Console.WriteLine($"{sample.Frame.Time:F3}s: recorded {sample.Action}, replayed {actual}");
        mismatches++;
    }
    Console.WriteLine($"{recording.Samples.Length} decisions replayed, {mismatches} differences. " +
        $"Incident: {report.RootElement.GetProperty("action").GetString()} ({report.RootElement.GetProperty("reason").GetString()})");
    return recording.Samples.Length > 0 && mismatches == 0 ? 0 : 1;
}

#if INTEGRATION
var etsRoot=Environment.GetEnvironmentVariable("EtsRoot") ?? throw new InvalidOperationException("Set EtsRoot to the ETS2LA installation directory.");
var references=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../References"));
AssemblyLoadContext.Default.Resolving += (_,name) => {
    foreach(var root in new[]{references,etsRoot}) {
        var file=Path.Combine(root,name.Name+".dll");
        if(File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
    }
    return null;
};
#endif
Run();
return Environment.ExitCode;

[MethodImpl(MethodImplOptions.NoInlining)]
void Run()
{
    var results=new List<string>();
    void Check(string name, Action test) { try {test(); results.Add("PASS "+name);} catch(Exception ex) {results.Add("FAIL "+name+": "+ex); } }
    void Assert(bool v,string message="Assertion failed") {if(!v)throw new Exception(message);}
    Check("Connector evidence distinguishes approaching lanes and expires after a stop", () =>
    {
        var memory = new ConnectorHistory();
        for (int i = 1; i <= 12; i++) memory.Observe(i, new System.Numerics.Vector3(0, 0, i), 1);
        var trace = memory.Snapshot(20, new System.Numerics.Vector3(0, 0, 12));
        Assert(trace.Length == 12);
        var sameLane = new RouteTrace([new(0,0,0), new(0,0,30)]);
        var otherLane = new RouteTrace([new(3,0,0), new(0,0,14), new(0,0,30)]);
        Assert(ConnectorHistory.Fit(sameLane, trace) == 0);
        Assert(ConnectorHistory.Fit(otherLane, trace) > 1);
        Assert(double.IsPositiveInfinity(ConnectorHistory.Fit(sameLane, trace.Reverse().ToArray())));
        Assert(memory.Snapshot(110, new System.Numerics.Vector3(0, 0, 12)).Length == 0);
        memory.Observe(25, new System.Numerics.Vector3(0,0,11), -1);
        Assert(memory.Snapshot(25, new System.Numerics.Vector3(0,0,11)).Length == 0);
    });
    Check("Unknown toll approach decelerates by available distance without emergency output",()=>{
        var d=ApproachStop.Evaluate(90/3.6,300);
        Assert(d.MaxAcceleration<-.9f && d.MaxAcceleration>-1.3f);
        Assert(ApproachStop.Evaluate(90/3.6,200).MaxAcceleration<d.MaxAcceleration);
        Assert(ApproachStop.Evaluate(0,100).MaxAcceleration<0,"Unknown lane must never launch from standstill");
        Assert(!d.RequiresTakeover,"Planned halt must not enter fatal tracking path");
    });
    Check("Planned approach stops before the branch and stays stopped",()=>{
        double speed=25,distance=300;
        for(int i=0;i<1200;i++) {
            var d=ApproachStop.Evaluate(speed,distance);
            double next=Math.Max(0,speed+d.MaxAcceleration*.05);
            distance-=(speed+next)*.025;speed=next;
            Assert(distance>0,"Crossed stop line");
        }
        Assert(speed<.1 && distance<30,"Must reach a controlled standstill before the branch");
        Assert(ApproachStop.Evaluate(speed,distance).MaxAcceleration<0,"Hold must persist");
    });
    Check("Too little approach distance or invalid geometry cannot relax the emergency stop",()=>{
        foreach(var scenario in new[]{(25d,60d),(2d,-1.1d),(double.NaN,200d),(0d,double.NaN)}) {
            bool rejected=false;
            try {ApproachStop.Evaluate(scenario.Item1,scenario.Item2);}catch(InvalidOperationException){rejected=true;}
            Assert(rejected);
        }
    });
    #if INTEGRATION
    Check("Short item seams do not create false opposite road headings",()=>DirectionSeamIntegration.Run(Assert));
    Check("Automatic preparation attaches only at standstill and preserves fault takeover",()=>AutomaticPreparationIntegration.Run(Assert));
    Check("Distant planning gaps do not trip current tracking; nearby gaps still brake",()=>FutureGapIntegration.Run(Assert));
    Check("Tracking faults brake independently of ACC; F9 releases and F8 cannot restart",()=>FaultBrakeIntegration.Run(Assert));
    Check("Toll exit hands over to the connected road lane, not merely a target lane",()=>ExitHandoffIntegration.Run(Assert));
    Check("Curved toll exit remains tracked by the native path projection",()=>ExitProjectionIntegration.Run(Assert));
    Check("Unprepared or faulted F8 cannot silently resume through tick, route, telemetry or ACC",()=>PreparationIntegration.Run(Assert));
    Check("Route measurements isolate PathLib lane-change mutations from native planning",()=>SnapshotIntegration.Run(Assert));
    Check("Synthetic prefab integration: route, right-to-left lanes, tracking, persistence, unknown plaza guard",()=>RouteIntegration.Run(Assert));
    #endif
    Check("Automatic preparation waits for a full second of uninterrupted readiness",()=>{
        var auto=new AutomaticPreparation();
        Assert(!auto.ShouldAttempt(0,true));Assert(!auto.ShouldAttempt(.9,true));
        Assert(!auto.ShouldAttempt(1,false));Assert(!auto.ShouldAttempt(2,true));
        Assert(!auto.ShouldAttempt(2.9,true));Assert(auto.ShouldAttempt(3,true));
    });
    Check("Automatic preparation retries at most once per second until ready",()=>{
        var auto=new AutomaticPreparation();auto.ShouldAttempt(0,true);
        Assert(auto.ShouldAttempt(1,true));Assert(!auto.ShouldAttempt(1.5,true));
        Assert(auto.ShouldAttempt(2,true));Assert(!auto.ShouldAttempt(double.NaN,true));
    });
    Check("Automatic preparation remains cancelled until explicitly requested",()=>{
        var auto=new AutomaticPreparation();auto.ShouldAttempt(0,true);auto.Cancel();
        Assert(!auto.Requested&&!auto.ShouldAttempt(100,true));auto.Request();
        Assert(!auto.ShouldAttempt(101,true));Assert(auto.ShouldAttempt(102,true));
    });
    Check("Only marked short item connectors are excluded from road direction",()=>{
        var points=new System.Numerics.Vector3[]{new(0,0,10),new(0,0,0),new(.2f,0,1),new(.2f,0,-10)};
        var truck=new System.Numerics.Vector3(.1f,0,.5f);
        var actual=new RouteTrace(points);var seam=new RouteTrace(points,new[]{2});
        Assert(new TrackingGuard().Check(actual,truck,0,15)!=null,"Real reversed segment must still stop");
        var guard=new TrackingGuard();Assert(guard.Check(seam,truck,0,15)==null);
        Assert(guard.Check(seam,truck,.5,15)!=null,"Counter-direction remains protected");
        Assert(guard.LastDirectionFailure!=null);
    });
    Check("Connector metadata cannot hide large gaps or erase all driving directions",()=>{
        bool rejected=false;
        try {new RouteTrace(new System.Numerics.Vector3[]{new(0,0,0),new(0,0,3)},new[]{1});}catch(ArgumentException){rejected=true;}
        Assert(rejected);
        var connectorsOnly=new RouteTrace(new System.Numerics.Vector3[]{new(0,0,0),new(0,0,1)},new[]{1});
        Assert(new TrackingGuard().Check(connectorsOnly,System.Numerics.Vector3.Zero,.5,0)!=null);
    });
    Check("Planning horizon grows with speed and retains a minimum reserve",()=>{
        Assert(TrackingGuard.RequiredLookahead(0)==25);
        Assert(TrackingGuard.RequiredLookahead(53/3.6)>80);
        Assert(TrackingGuard.RequiredLookahead(90/3.6)>190);
        Assert(double.IsPositiveInfinity(TrackingGuard.RequiredLookahead(double.NaN)));
    });
    Check("Tracking guard rejects opposite headings and reversed replacement routes",()=>{
        var north=new RouteTrace(new System.Numerics.Vector3[]{new(0,0,20),new(0,0,-100)});
        var south=new RouteTrace(new System.Numerics.Vector3[]{new(0,0,-100),new(0,0,20)});
        var guard=new TrackingGuard();
        Assert(guard.Check(north,System.Numerics.Vector3.Zero,0,10)==null);
        Assert(guard.Check(north,System.Numerics.Vector3.Zero,.5,10)!=null,"Opposite heading must not be folded onto same road axis");
        Assert(guard.Check(south,System.Numerics.Vector3.Zero,.5,10)!=null,"A replan must not silently reverse accepted travel direction");
        guard.Reset();Assert(guard.Check(south,System.Numerics.Vector3.Zero,.5,10)==null,"Manual takeover may start a new journey");
        Assert(guard.Check(south,new(4,0,0),.5,10)!=null,"Lost lateral tracking must stop even with no gate nearby");
        Assert(guard.Check(south,System.Numerics.Vector3.Zero,.5,-2)!=null,"Reverse gear must not drive under autopilot");
        Assert(guard.Check(south,System.Numerics.Vector3.Zero,double.NaN,10)!=null);
    });
    Check("Tracking direction follows bends instead of comparing only with initial compass heading",()=>{
        var route=new RouteTrace(new System.Numerics.Vector3[]{new(0,0,20),new(0,0,0),new(20,0,0),new(20,0,20)});
        var guard=new TrackingGuard();
        Assert(guard.Check(route,new(0,0,10),0,5)==null);
        Assert(guard.Check(route,new(10,0,0),.75,5)==null);
        Assert(guard.Check(route,new(20,0,10),.5,5)==null);
    });
    Check("Automatic payment preference excludes manual, unknown and unreachable lanes",()=>{
        var payment=new Dictionary<string,TollPayment>{[PaymentPolicy.Key("R",4)]=TollPayment.Manual,
            [PaymentPolicy.Key("R",12)]=TollPayment.Automatic};
        Assert(PaymentPolicy.ChooseAutomatic("R",new[]{4,12},4,payment)==12);
        Assert(PaymentPolicy.ChooseAutomatic("R",new[]{4},4,payment)==null);
        Assert(PaymentPolicy.ChooseAutomatic("other",new[]{4,12},12,payment)==null);
        Assert(PaymentPolicy.ChooseAutomatic("R",new[]{15},15,payment)==null);
    });
    Check("Legacy lane settings never become automatic payment without confirmation",()=>{
        var file=Path.Combine(AppContext.BaseDirectory,"legacy-payment-test.json");
        File.WriteAllText(file,"{\"Options\":{},\"Gates\":{\"R\":12}}");
        try {var prefs=new PreferenceStore(file);prefs.Load();
            Assert(prefs.Gates["R"]==12 && prefs.Payments.Count==0);
            Assert(PaymentPolicy.ChooseAutomatic("R",new[]{12},12,prefs.Payments)==null);
        } finally {File.Delete(file);}
    });
    Check("Persistent settings migration preserves confirmed lanes and never overwrites an existing store",()=>{
        var legacyFile=Path.Combine(AppContext.BaseDirectory,"migration-old-settings.json");
        var newFile=Path.Combine(AppContext.BaseDirectory,"migration-new-settings.json");
        try {
            var legacy=new PreferenceStore(legacyFile);
            legacy.Save(new TollOptions{ApproachSpeedKph=18},new(){["synthetic"]=12},new(){[PaymentPolicy.Key("synthetic",12)]=TollPayment.Automatic});
            var persistent=new PreferenceStore(newFile);persistent.MigrateFrom(legacyFile);persistent.Load();
            Assert(persistent.Options.ApproachSpeedKph==18 && persistent.Gates["synthetic"]==12);
            Assert(persistent.Payments[PaymentPolicy.Key("synthetic",12)]==TollPayment.Automatic && File.Exists(legacyFile));
            File.WriteAllText(legacyFile,"invalid");persistent.MigrateFrom(legacyFile);persistent.Load();
            Assert(persistent.Gates["synthetic"]==12,"Existing preferences take precedence over a legacy copy");
            File.Delete(newFile);bool rejected=false;
            try {persistent.MigrateFrom(legacyFile);}catch(System.Text.Json.JsonException){rejected=true;}
            Assert(rejected&&!File.Exists(newFile),"Never persist an invalid legacy preference file");
        } finally {File.Delete(legacyFile);File.Delete(newFile);}
    });
    TollInput Input(double t, double speed=0, double distance=8, BarrierState state=BarrierState.Closed, string key="A", bool enabled=true, bool fresh=true) => new(t,speed,enabled,fresh,new(key,distance,state));
    Check("Passed gate can close behind the truck without ordering another stop",()=>{
        var c=new TollController(new());
        for(int n=0;n<=20;n++)c.Step(Input(n*.1,distance:8,state:BarrierState.Open));
        var crossing=c.Step(Input(2.1,distance:-2,state:BarrierState.Open));Assert(crossing.Phase=="Langsame Durchfahrt");
        var closing=c.Step(Input(2.2,distance:-10,state:BarrierState.Closing));Assert(closing.TargetSpeed==10/3.6);
        Assert(c.Step(Input(2.3,distance:-31,state:BarrierState.Closed)).GateKey==null);
        Assert(c.Step(Input(2.4,distance:8,state:BarrierState.Closed,key:"B")).TargetSpeed==0);
    });
    Check("Passage memory survives route pruning only behind a released, identical gate",()=>{
        var memory=new PassageMemory();
        var route=new RouteTrace(new[]{new System.Numerics.Vector3(0,0,-100),new System.Numerics.Vector3(0,0,100)});
        memory.Capture("A",System.Numerics.Vector3.Zero,route);
        var truck=new System.Numerics.Vector3(0,0,12);
        var exit=memory.AfterCrossing("A",truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,true);
        Assert(exit!=null && Math.Abs(exit.Distance+12)<.001);
        Assert(memory.AfterCrossing("A",-truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,true)==null);
        Assert(memory.AfterCrossing("A",truck,System.Numerics.Vector3.Zero,BarrierState.Open,false,true)==null);
        Assert(memory.AfterCrossing("A",truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,false)==null);
        Assert(memory.AfterCrossing("B",truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,true)==null);
        Assert(memory.AfterCrossing("A",truck+new System.Numerics.Vector3(5,0,0),System.Numerics.Vector3.Zero,BarrierState.Open,true,true)==null);
        memory.Reset();Assert(memory.AfterCrossing("A",truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,true)==null);
    });
    Check("Manual cancellation clears a lost old booth before observing the next plaza",()=>{
        var c=new TollController(new());c.Step(Input(0));
        Assert(c.Step(new(.1,0,true,true,null)).GateKey=="A");
        c.Step(new(.2,0,false,false,null));Assert(!c.Tracking);
        Assert(c.Step(Input(.3,distance:100,key:"B")).GateKey=="B");
    });
    Check("Synthetic plaza: both entries reach the requested gate without crossing carriageways",()=>{
        CurveLink[] curves=[
            new(0,[2,3],20,true,-1), new(1,[2,3],20,true,-1),
            new(2,[4],10,true,12), new(3,[4],10,true,4),
            new(4,[],20,true,-1), new(5,[6],20,true,-1), new(6,[],20,true,-1)
        ];
        foreach(int start in new[]{0,1}) {
            var route=CurveGraph.ViaGate(curves,start,4,12);Assert(route!=null,"No truck path from entry "+start);
            Assert(route!.Where(i=>curves[i].GateId>=0).Select(i=>curves[i].GateId).SequenceEqual(new[]{12}));
        }
        Assert(CurveGraph.ViaGate(curves,5,6,12)==null,"Opposite carriageway must not be crossed");
        var blocked=curves.Select(c=>c.GateId==12?c with{TruckAllowed=false}:c).ToArray();
        Assert(CurveGraph.ViaGate(blocked,0,4,12)==null,"No fallback to a car-only or different gate");
    });
    Check("Curve projection follows the lane instead of the straight chord",()=>{
        var points=Enumerable.Range(0,201).Select(i=> {
            double a=i*Math.PI/400;return new System.Numerics.Vector3((float)(30*Math.Sin(a)),0,(float)(30*Math.Cos(a)));
        }).ToArray();
        var route=new RouteTrace(points);var truck=route.Project(points[100]);var barrier=route.Project(points[180]);
        Assert(truck.Offset<.001);Assert(Math.Abs((barrier.Along-truck.Along)-30*Math.PI/5)<.01);
        Assert(route.Project(points[190]).Along>barrier.Along,"Distance must become negative after passing");
    });
    Check("Closed and opening barriers hold",()=>{
        var c=new TollController(new());
        foreach(var state in new[]{BarrierState.Closed,BarrierState.Closing,BarrierState.Opening,BarrierState.Unknown}) {
            var d=c.Step(Input(0,state:state)); Assert(d.TargetSpeed==0 && d.MaxAcceleration<0);
        }
    });
    Check("Open delay requires continuous fresh frames",()=>{
        var c=new TollController(new());
        for(int i=0;i<20;i++) Assert(c.Step(Input(i*.1,state:BarrierState.Open)).TargetSpeed==0);
        Assert(c.Step(Input(2,state:BarrierState.Open)).Phase=="Langsame Durchfahrt");
        Assert(c.Step(Input(2.1,state:BarrierState.Opening)).TargetSpeed==0);
        Assert(c.Step(Input(2.2,state:BarrierState.Open)).TargetSpeed==0);
    });
    Check("Time jump cannot skip opening delay",()=>{
        var c=new TollController(new()); c.Step(Input(0,state:BarrierState.Open));
        Assert(c.Step(Input(3,state:BarrierState.Open)).TargetSpeed==0);
        Assert(c.Step(Input(1,state:BarrierState.Open)).TargetSpeed==0);
    });
    Check("F9/reset yields no constraint",()=>{
        var c=new TollController(new()); c.Step(Input(0));
        Assert(float.IsNaN(c.Step(Input(.1,enabled:false)).MaxAcceleration)); Assert(!c.Tracking);
        Assert(c.Step(Input(.2,state:BarrierState.Open)).TargetSpeed==0);
    });
    Check("Data loss never grants clearance",()=>{
        var c=new TollController(new());
        for(int i=0;i<=20;i++)c.Step(Input(i*.1,state:BarrierState.Open));
        var stale=c.Step(Input(2.1,state:BarrierState.Open,fresh:false)); Assert(stale.MaxAcceleration<0);
        Assert(c.Step(Input(2.2,state:BarrierState.Open)).TargetSpeed==0);
        Assert(c.Step(new(2.3,0,true,true,null)).RequiresTakeover);
    });
    Check("A different gate cannot release the tracked gate",()=>{
        var c=new TollController(new()); c.Step(Input(0));
        Assert(c.Step(Input(.1,state:BarrierState.Open,key:"B")).RequiresTakeover);
    });
    Check("Exit and reverse release tracking",()=>{
        var c=new TollController(new());c.Step(Input(0));
        Assert(float.IsNaN(c.Step(Input(.1,distance:-31)).MaxAcceleration));Assert(!c.Tracking);
        c.Step(Input(.2)); Assert(float.IsNaN(c.Step(Input(.3,speed:-1)).MaxAcceleration));
    });
    Check("Reject non-finite and out-of-range settings",()=>{
        foreach(var o in new[]{new TollOptions{OpenDelaySeconds=double.NaN},new TollOptions{PassageSpeedKph=50},new TollOptions{StopOffsetMetres=0}}) {
            bool thrown=false;try{o.Validate();}catch(ArgumentException){thrown=true;}Assert(thrown);
        }
    });
    Check("Simulated approaches stop ahead of barrier from 30/60/90 km/h",()=>{
        foreach(double initial in new[]{30d,60d,90d}) {
            var c=new TollController(new());double v=initial/3.6, d=400;var delayed=new Queue<float>(Enumerable.Repeat(0f,20));
            for(int i=0;i<6000;i++) {
                var dec=c.Step(Input(i*.05,speed:v,distance:d));
                float bound=dec.MaxAcceleration;
                delayed.Enqueue(float.IsNaN(bound)?0:bound);
                var accel=delayed.Dequeue();v=Math.Max(0,v+accel*.05);d-=v*.05;
                Assert(d>=7.5,$"{initial}: crossed stop point at {d:F2}m");
                if(v<.01 && d<15)break;
            }
            Assert(v<.01 && d<15,$"{initial}: failed to stop, v={v}, d={d}");
            results.Add($"  simulation {initial} km/h: stop at {d:F2} m from telemetry origin (1 s actuation delay)");
        }
    });
    Check("Gate selection excludes adjacent lanes and refuses ambiguous booths",()=>{
        var lane=new GateProjection(new("A",50,BarrierState.Closed),1);
        var adjacent=new GateProjection(new("B",49,BarrierState.Open),3);
        Assert(GateSelection.Select([lane,adjacent],null)?.Key=="A");
        Assert(GateSelection.Select([lane,adjacent with{LateralOffset=1.8}],null)==null);
        Assert(GateSelection.Select([lane,adjacent with{LateralOffset=1.8}],"A")?.Key=="A");
        Assert(GateSelection.Select([adjacent],"A")==null);
    });
    Check("Recorded 163 m approach is limited from standstill, without an acceleration gap",()=>{
        var c=new TollController(new());
        var d=c.Step(Input(0,distance:163.227));
        Assert(c.Tracking && d.TargetSpeed<=20/3.6 && d.MaxAcceleration<=.35f);
    });
    Check("Recorded toll plaza selects mapped gate 15, excluding neighbouring gate 14",()=>{
        var mapped = new GateProjection(new("15",163.227,BarrierState.Closed),2.663,true);
        var neighbour = new GateProjection(new("14",163.229,BarrierState.Open),3.108);
        Assert(GateSelection.Select([neighbour,mapped],null)?.Key=="15");
        Assert(GateSelection.Select([neighbour,mapped with{RouteMatched=false}],null)==null);
        Assert(GateSelection.Select([mapped with{LateralOffset=6}],null)==null);
        Assert(GateSelection.Select([mapped,neighbour with{RouteMatched=true}],null)==null);
    });
    Check("Behind-gate selection only keeps a previously tracked barrier",()=>{
        var behind=new GateProjection(new("A",-12,BarrierState.Open),1);
        Assert(GateSelection.Select([behind],null)==null);
        Assert(GateSelection.Select([behind],"A")!=null);
    });
    Check("Standstill start waits for loading and rejects an unavailable starting road without an emergency stop",()=>{
        var frame = new DriveFrame(0, 0, true, false, true, RouteFeed.Loading, .01, false, false);
        var control = new DriveSupervisor();
        Assert(control.Step(frame) == DriveAction.AwaitRoute);
        Assert(control.Step(frame with { Time = 6 }) == DriveAction.AwaitRoute);
        Assert(control.Step(frame with { Time = 8 }) == DriveAction.RejectStart);
        control.Reset();
        Assert(control.Step(frame with { Route = RouteFeed.Rejected }) == DriveAction.RejectStart);
        Assert(control.Step(frame with { Speed = 10, Route = RouteFeed.Rejected }) == DriveAction.EmergencyStop);
    });
    Check("Brief input loss requires continuous recovery and resets after manual cancellation",()=>{
        var ready = new DriveFrame(0, 16, true, false, true, RouteFeed.Valid, .01, true, true);
        var control = new DriveSupervisor();
        Assert(control.Step(ready) == DriveAction.Drive);
        Assert(control.Step(ready with { Time = .05, DecisionReady = false }) == DriveAction.Recover);
        var resumed = new DriveSupervisor(control.Checkpoint);
        foreach (var (time, fresh, expected) in new[]{
            (.10, true, DriveAction.Recover), (.20, false, DriveAction.Recover),
            (.22, true, DriveAction.Recover), (.38, true, DriveAction.Drive)
        })
        {
            var input = ready with { Time = time, DecisionReady = fresh };
            Assert(control.Step(input) == expected && resumed.Step(input) == expected);
        }
        Assert(control.Step(ready with { Time = .40, Enabled = false, TelemetryFresh = false }) == DriveAction.Idle);
        Assert(control.Step(ready with { Time = .41, Speed = 0, Route = RouteFeed.Waiting }) == DriveAction.AwaitRoute);
    });
    Check("Loss of the verified corridor, telemetry, or a toll safety input cannot use the recovery window",()=>{
        var ready = new DriveFrame(0, 25, true, false, true, RouteFeed.Valid, .01, true, true);
        foreach (var fault in new[]{
            ready with { Route = RouteFeed.Rejected },
            ready with { TelemetryFresh = false },
            ready with { DecisionReady = false, CanRecover = false },
            ready with { RouteAge = 1 }
        })
        {
            var control = new DriveSupervisor();
            Assert(control.Step(ready) == DriveAction.Drive);
            Assert(control.Step(fault with { Time = .1 }) == DriveAction.EmergencyStop);
        }
        var timeout = new DriveSupervisor();
        timeout.Step(ready);
        Assert(timeout.Step(ready with { Time = .1, DecisionReady = false }) == DriveAction.Recover);
        Assert(timeout.Step(ready with { Time = .61, DecisionReady = false }) == DriveAction.EmergencyStop);
        var toll = new DriveSupervisor();
        Assert(toll.Step(ready with { CanRecover = false }) == DriveAction.Drive);
        Assert(toll.Step(ready with { Time = .6, RouteAge = .6, CanRecover = false }) == DriveAction.Drive);
        Assert(toll.Step(ready with { Time = 1, RouteAge = 1, CanRecover = false }) == DriveAction.EmergencyStop);
    });
    #if INTEGRATION
    Check("Verified ACC type accepts bridge; attach/detach preserves native constraints",()=>{
        Assert(AccBridge.SupportsVersion("2.1.0")&&AccBridge.SupportsVersion("2.1.1")&&!AccBridge.SupportsVersion("2.2.0"),"Reject unverified ACC versions");
        var a=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(references,"AdaptiveCruiseControl.dll"));
        var baseType=a.GetType("AdaptiveCruiseControl.Constraints.Constraint",true)!;
        var accType=a.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin",true)!;
        var gate=AccBridge.CreateConstraint(baseType,()=>-.75f,()=>"test");
        Assert((float)baseType.GetMethod("GetMaxAcceleration")!.Invoke(gate,null)! == -.75f);
        var log=baseType.GetMethod("GetLogData")!.Invoke(gate,null)!;
        Assert((string)log.GetType().GetProperty("ConstraintName")!.GetValue(log)! == AccBridge.ConstraintName);
        // No ACC constructor, providers, worker threads or game writes are invoked.
        var acc=RuntimeHelpers.GetUninitializedObject(accType);
        var field=accType.GetField("constraints")!;
        var list=(IList)Activator.CreateInstance(field.FieldType)!;
        list.Add(gate);list.Add(gate);list.Add(gate);field.SetValue(acc,list);
        var bridge=new AccBridge();bridge.Attach(acc,()=>-.5f,()=>"active");
        var attached=bridge.Constraint!;
        Assert(((IList)field.GetValue(acc)!).Count==4 && list.Count==3);
        bridge.Attach(acc,()=>1,()=>"duplicate");Assert(((IList)field.GetValue(acc)!).Count==4);
        bridge.Detach();Assert(((IList)field.GetValue(acc)!).Count==3);
        Assert(float.IsNaN((float)baseType.GetMethod("GetMaxAcceleration")!.Invoke(attached,null)!));
    });
    #endif
    foreach(var line in results)Console.WriteLine(line);
    Environment.ExitCode=results.Any(s=>s.StartsWith("FAIL"))?1:0;
}
