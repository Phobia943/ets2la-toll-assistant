using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Collections;
using MautAssistent;

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

[MethodImpl(MethodImplOptions.NoInlining)]
void Run()
{
    var results=new List<string>();
    void Check(string name, Action test) { try {test(); results.Add("PASS "+name);} catch(Exception ex) {results.Add("FAIL "+name+": "+ex); } }
    void Assert(bool v,string message="Assertion failed") {if(!v)throw new Exception(message);}
    #if INTEGRATION
    Check("Route measurements isolate PathLib lane-change mutations from native planning",()=>SnapshotIntegration.Run(Assert));
    Check("Synthetic prefab integration: route, right-to-left lanes, tracking, persistence, unknown plaza guard",()=>RouteIntegration.Run(Assert));
    #endif
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
        Assert(c.Step(new(2.3,0,true,true,null)).Phase.Contains("manuell"));
    });
    Check("A different gate cannot release the tracked gate",()=>{
        var c=new TollController(new()); c.Step(Input(0));
        Assert(c.Step(Input(.1,state:BarrierState.Open,key:"B")).Phase.Contains("manuell"));
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
    Check("F9, plugin stop, observation and lane-only mode override stale output",()=>{
        Assert(ControlGuard.Check(true,true,false,false,false,10,true)==ControlPermission.Neutral);
        Assert(ControlGuard.Check(true,false,true,false,true,0,false)==ControlPermission.Neutral);
        Assert(ControlGuard.Check(false,true,true,false,true,0,false)==ControlPermission.Neutral);
        Assert(ControlGuard.Check(true,true,true,true,true,0,false)==ControlPermission.Neutral);
    });
    Check("Watchdog requests disarm on stale telemetry, stale decisions and missing gate",()=>{
        Assert(ControlGuard.Check(true,true,true,false,true,.1,false)==ControlPermission.Limit);
        Assert(ControlGuard.Check(true,true,true,false,false,.1,false)==ControlPermission.Disarm);
        Assert(ControlGuard.Check(true,true,true,false,true,.31,false)==ControlPermission.Disarm);
        Assert(ControlGuard.Check(true,true,true,false,true,.1,true)==ControlPermission.Disarm);
    });
    Check("Startup holds until the first route and times out without accelerating",()=>{
        var s=new StartupGuard();
        Assert(s.Check(false,false,0)==StartupPermission.Neutral);
        Assert(s.Check(true,false,1)==StartupPermission.Hold);
        Assert(s.Check(true,false,2.9)==StartupPermission.Hold);
        Assert(s.Check(true,false,3)==StartupPermission.Disarm);
        s.Reset();Assert(s.Check(true,false,4)==StartupPermission.Hold);
        Assert(s.Check(true,true,4.1)==StartupPermission.Ready);
        Assert(s.Check(true,false,4.2)==StartupPermission.Disarm);
        Assert(s.Check(false,false,4.3)==StartupPermission.Neutral);
        Assert(s.Check(true,false,4.4)==StartupPermission.Hold);
    });
    #if INTEGRATION
    Check("Official ACC 2.1.0 type accepts bridge; attach/detach preserves native constraints",()=>{
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
