using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using B=System.Reflection.BindingFlags;
namespace MautHostCompatibility;

// A synthetic map fixture, copied and audited before executing its actual
// router/DI constructor/Tick. It shares exactly the isolated product and host.
internal static class CanonicalGateHostReplay
{
    const B All=B.Public|B.NonPublic|B.Instance|B.Static;
    internal static int Run(string fixtureFile,string sandbox,AssemblyLoadContext context,Assembly plugin,Assembly game,
        object host,Action<bool> frame,Func<float> gas,Func<float> brake,Func<float> modern,Action<bool,string> check)
    {
        string copy=Path.Combine(sandbox,"Tests.dll");File.Copy(Path.GetFullPath(fixtureFile),copy,false);
        using var seal=new FileStream(copy,FileMode.Open,FileAccess.Read,FileShare.Read);
        string sha=Convert.ToHexString(SHA256.HashData(seal));
        using var module=ModuleDefinition.ReadModule(copy);
        if(module.GetType("<Module>").Methods.Any(m=>m.IsConstructor))throw new InvalidDataException("Fixture module initialization forbidden");
        var root=module.GetType("CanonicalGateTickReview")??throw new InvalidDataException("Canonical fixture is missing");
        var seen=new HashSet<string>();
        void Audit(MethodDefinition method) {
            if(!seen.Add(method.FullName)||!method.HasBody)return;
            if(method.DeclaringType.Methods.FirstOrDefault(m=>m.IsConstructor&&m.IsStatic) is {} init)Audit(init);
            foreach(var i in method.Body.Instructions) {
                if(i.OpCode.Code is Code.Calli or Code.Jmp)throw new InvalidDataException("Fixture indirect call forbidden");
                if(i.Operand is FieldReference f&&(f.DeclaringType.FullName.StartsWith("CanonicalGateTickReview",StringComparison.Ordinal)||f.DeclaringType.FullName.StartsWith("GateSelectionTransitionIntegration",StringComparison.Ordinal)||f.DeclaringType.FullName.StartsWith("RoutingContinuationIntegration",StringComparison.Ordinal))&&
                    f.Resolve().DeclaringType.Methods.FirstOrDefault(m=>m.IsConstructor&&m.IsStatic) is {} fieldInit)Audit(fieldInit);
                if(i.Operand is not MethodReference r||i.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj))continue;
                string owner=r.DeclaringType.GetElementType().FullName;
                if(owner.StartsWith("CanonicalGateTickReview",StringComparison.Ordinal)||owner.StartsWith("GateSelectionTransitionIntegration",StringComparison.Ordinal)||owner.StartsWith("RoutingContinuationIntegration",StringComparison.Ordinal)) {Audit(r.Resolve());continue;}
                bool allowed=owner.StartsWith("System.",StringComparison.Ordinal)&&!owner.StartsWith("System.IO.",StringComparison.Ordinal)&&
                    !owner.StartsWith("System.Threading.Tasks.",StringComparison.Ordinal)||owner=="System.IO.Path"&&r.Name is "Combine" or "GetTempPath"||
                    owner.StartsWith("MautAssistent.",StringComparison.Ordinal)||owner.StartsWith("TruckLib.",StringComparison.Ordinal)||
                    owner.StartsWith("PathLib.",StringComparison.Ordinal)||owner is "ETS2LA.Shared.Plugin" or "ETS2LA.Shared.PluginInformation" or
                    "ETS2LA.Game.Data.ParsedRoad" or "ETS2LA.Game.Data.PrefabPath" or "ETS2LA.Game.Data.ParsedRoadList" or "ETS2LA.Game.SDK.Semaphore" or "ETS2LA.Game.SDK.NavigationData" or
                    "ETS2LA.Game.SDK.NavigationEntry" or "ETS2LA.State.ApplicationState";
                if(!allowed||r.Name=="get_Current"||owner=="MautAssistent.TollPlugin"&&r.Name==".ctor"&&!(r.Parameters.Count>0&&r.Parameters[0].ParameterType.FullName=="System.Func`1<ETS2LA.State.ApplicationState>"))
                    throw new InvalidDataException("Unaudited canonical fixture target: "+r.FullName);
            }
        }
        Audit(root.Methods.Single(m=>m.Name=="Build"));
        var fixtureType=root.NestedTypes.Single(t=>t.Name=="Fixture");
        foreach(var m in fixtureType.Methods)Audit(m);
        var planner=root.NestedTypes.Single(t=>t.Name=="Planner");foreach(var m in planner.Methods)Audit(m);
        var transitionRoot=module.GetType("GateSelectionTransitionIntegration")??throw new InvalidDataException("Transition fixture is missing");
        foreach(var nested in transitionRoot.NestedTypes.Where(t=>t.Name is "Fixture" or "Planner"))foreach(var m in nested.Methods)Audit(m);
        var loaded=context.LoadFromAssemblyPath(copy);
        if(loaded.ManifestModule.ModuleVersionId!=module.Mvid||AssemblyLoadContext.GetLoadContext(loaded)!=context||
            context.LoadFromAssemblyName(new AssemblyName("MautAssistent"))!=plugin||context.LoadFromAssemblyName(new AssemblyName("ETS2LA.Game"))!=game)
            throw new InvalidDataException("Canonical fixture escaped pinned product/host context");
        var fixture=loaded.GetType("CanonicalGateTickReview+Fixture",true)!;var output=game.GetType("ETS2LA.Game.Output.GameOutput",true)!;
        var eventType=game.GetType("ETS2LA.Game.Output.ControlEvent",true)!;
        var sink=typeof(CanonicalGateHostReplay).GetMethod(nameof(Sink),All)!.MakeGenericMethod(eventType).Invoke(null,
            [(Action<object>)(message=>output.GetMethod("OnControlEvent")!.Invoke(host,[message]))]);
        int checks=0;void Check(bool value,string reason){check(value,reason);checks++;}
        object? Field(object obj,string name)=>obj.GetType().GetField(name,All)!.GetValue(obj);
        void SetPlugin(object obj,string name,object? value)=>plugin.GetType("MautAssistent.TollPlugin",true)!.GetField(name,All)!.SetValue(obj,value);
        object InvokeFixture(object obj,string method,params object?[] args)=>fixture.GetMethod(method)!.Invoke(obj,args)!;
        void Sources(object f) {
            var traffic=SourceFrameReplay.EmptyTraffic(game);var actor=((Array)traffic.GetType().GetField("vehicles")!.GetValue(traffic)!).GetValue(0)!;
            actor.GetType().GetField("id")!.SetValue(actor,(short)1);
            actor.GetType().GetProperty("Position")!.SetValue(actor,new System.Numerics.Vector3(5,0,30));
            actor.GetType().GetProperty("Rotation")!.SetValue(actor,new System.Numerics.Quaternion(1,0,0,0));
            actor.GetType().GetProperty("Size")!.SetValue(actor,new System.Numerics.Vector3(2,4,6));
            SourceFrameReplay.Capture(output,"Traffic",traffic);
            SourceFrameReplay.Capture(output,"Parked",SourceFrameReplay.EmptyParked(game));
            object map=Field(f,"Map")!;var signals=SourceFrameReplay.EmptySignals(game);
            var st=signals.GetType().GetField("semaphores")!.FieldType.GetElementType()!;var array=Array.CreateInstance(st,2);
            array.SetValue(map.GetType().GetProperty("Own")!.GetValue(map),0);array.SetValue(map.GetType().GetProperty("Opposite")!.GetValue(map),1);
            signals.GetType().GetField("semaphores")!.SetValue(signals,array);SourceFrameReplay.Capture(output,"Semaphore",signals);
            var p=Field(f,"Plugin")!;SetPlugin(p,"requireHostCompatibility",true);
            try{p.GetType().GetMethod("OnGates",All)!.Invoke(p,[signals]);}finally{SetPlugin(p,"requireHostCompatibility",false);}
        }
        object Create() {
            var f=Activator.CreateInstance(fixture,[sink,true])!;var p=Field(f,"Plugin")!;SetPlugin(p,"vehicleGeometry",null);SetPlugin(p,"readLongitudinalTraffic",null);
            ((System.Collections.IDictionary)host.GetType().GetField("Channels",All)!.GetValue(host)!).Clear();return f;
        }
        var open=Create();var until=Stopwatch.StartNew();var timings=new List<object>();long previous=0;double largestGap=0;int freshOutputs=0;
        try {
            while(until.Elapsed.TotalSeconds<1.3) {
                long begin=Stopwatch.GetTimestamp();Sources(open);InvokeFixture(open,"Tick",0d);frame(false);
                var product=Field(open,"Plugin")!;var sample=product.GetType().GetField("longitudinalSnapshot",All)!.GetValue(product)!;
                long issued=(long)sample.GetType().GetProperty("IssuedAt")!.GetValue(sample)!;
                bool published=(bool)sample.GetType().GetProperty("Published")!.GetValue(sample)!;
                double elapsed=(Stopwatch.GetTimestamp()-begin)*1000d/Stopwatch.Frequency;
                double priorAge=previous==0?0:(Stopwatch.GetTimestamp()-previous)*1000d/Stopwatch.Frequency;
                if(previous!=0)largestGap=Math.Max(largestGap,priorAge);
                if(published)freshOutputs++;
                timings.Add(new{elapsedMs=elapsed,previousOriginAgeMsAtPublish=priorAge,published});previous=issued;
                Thread.Sleep(50);
            }
            if(Environment.GetEnvironmentVariable("MAUT_TRANSITION_ONLY")!="1")Check(freshOutputs>=2&&largestGap<150,"Consecutive actual toll Ticks at20Hz renew the own command before150ms with a valid neighboring traffic body; max previous-origin age="+largestGap+"ms");
            var p=Field(open,"Plugin")!;var decision=p.GetType().GetField("decision",All)!.GetValue(p)!;
            frame(false);Check((string)decision.GetType().GetProperty("Phase")!.GetValue(decision)! =="Langsame Durchfahrt"&&gas()>0&&brake()==0,
                "Actual canonical gate10 Open/opposite15 Closed Tick passes the real opening delay and reaches legacy throttle");
            frame(true);Check(modern()>0&&gas()==0&&brake()==0,"The canonical own-open/opposite-closed Tick reaches modern throttle");
        } finally {InvokeFixture(open,"Stop");}
        var closed=Create();
        try {
            var map=Field(closed,"Map")!;map.GetType().GetProperty("Own")!.GetValue(map)!.GetType().GetField("state")!.SetValue(map.GetType().GetProperty("Own")!.GetValue(map),1);
            map.GetType().GetProperty("Opposite")!.GetValue(map)!.GetType().GetField("state")!.SetValue(map.GetType().GetProperty("Opposite")!.GetValue(map),3);
            Sources(closed);InvokeFixture(closed,"Tick",0d);Thread.Sleep(20);Sources(closed);InvokeFixture(closed,"Tick",10/3.6);
            frame(false);Check(gas()==0&&brake()>0,"Actual own gate10 Closed/opposite15 Open Tick cannot borrow the foreign release at the legacy sink");
            frame(true);Check(modern()<0&&gas()==0&&brake()==0,"The own-closed/opposite-open canonical Tick also brakes at the modern sink");
        } finally {InvokeFixture(closed,"Stop");}
        // A genuine OnPath Manual -> late Automatic transition. PathLib's
        // otherwise singleton-backed telemetry is already a private DTO.
        var transitionType=loaded.GetType("GateSelectionTransitionIntegration+Fixture",true)!;
        var transition=Activator.CreateInstance(transitionType,[sink,true])!;
        var transitionPlugin=transitionType.GetField("Plugin")!.GetValue(transition)!;SetPlugin(transitionPlugin,"vehicleGeometry",null);
        var telemetryType=game.GetType("ETS2LA.Game.Telemetry.GameTelemetry",true)!;
        var telemetryData=telemetryType.GetField("ReplayData")!.GetValue(null)!;
        object ObjectField(object owner,string name) {
            var f=owner.GetType().GetField(name)!;
            return f.GetValue(owner)??(f.FieldType.IsValueType?Activator.CreateInstance(f.FieldType)!:System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(f.FieldType));
        }
        void Scalar(object owner,string name,object value) {
            var field=owner.GetType().GetField(name,B.Public|B.NonPublic|B.Instance|B.IgnoreCase);
            if(field!=null)field.SetValue(owner,Convert.ChangeType(value,field.FieldType));
            else {var prop=owner.GetType().GetProperty(name,B.Public|B.NonPublic|B.Instance|B.IgnoreCase)!;prop.SetValue(owner,Convert.ChangeType(value,prop.PropertyType));}
        }
        Action<System.Numerics.Vector3,double> beforePath=(position,speed)=>{
            var placement=ObjectField(telemetryData,"truckPlacement");var coordinate=ObjectField(placement,"coordinate");
            Scalar(coordinate,"x",position.X);Scalar(coordinate,"y",position.Y);Scalar(coordinate,"z",position.Z);
            placement.GetType().GetField("coordinate")!.SetValue(placement,coordinate);telemetryData.GetType().GetField("truckPlacement")!.SetValue(telemetryData,placement);
            var tf=ObjectField(telemetryData,"truckFloat");Scalar(tf,"speed",speed);Scalar(tf,"speedLimit",100f/3.6f);telemetryData.GetType().GetField("truckFloat")!.SetValue(telemetryData,tf);
            SourceFrameReplay.Capture(output,"Traffic",SourceFrameReplay.EmptyTraffic(game));SourceFrameReplay.Capture(output,"Parked",SourceFrameReplay.EmptyParked(game));
            var signals=SourceFrameReplay.EmptySignals(game);signals.GetType().GetField("semaphores")!.SetValue(signals,transitionType.GetField("Signals")!.GetValue(transition));
            SourceFrameReplay.Capture(output,"Semaphore",signals);SetPlugin(transitionPlugin,"requireHostCompatibility",true);
            try{transitionPlugin.GetType().GetMethod("OnGates",All)!.Invoke(transitionPlugin,[signals]);}finally{SetPlugin(transitionPlugin,"requireHostCompatibility",false);}
        };
        transitionType.GetField("BeforePath")!.SetValue(transition,beforePath);
        object? Transition(string method,params object?[] args)=>transitionType.GetMethod(method)!.Invoke(transition,args);
        object? TProp(string name)=>transitionType.GetProperty(name)!.GetValue(transition);
        try {
            ((System.Collections.IDictionary)host.GetType().GetField("Channels",All)!.GetValue(host)!).Clear();
            Check(Transition("Prime")!.ToString()=="Drive","Actual late-selection regression first establishes an ordinary standstill route through the real supervisor");
            Transition("ChooseManual");
            Check((int)TProp("Selected")! ==4,"Actual OnPath at130m and77kmh first selects the reachable manual gate4");
            Transition("Upgrade");
            var selected=TProp("Selected");var shortChange=TProp("ShortChange");var armed=TProp("Armed");
            var action=Transition("ProbeWatchdog")!.ToString();
            bool fault=(bool)transitionPlugin.GetType().GetProperty("FaultBraking")!.GetValue(transitionPlugin)!;
            Check((int)selected! ==12&&(bool)shortChange!&&(bool)armed!,
                "Actual OnPath Manual-to-Automatic upgrade keeps a newly derived armed decision before any next Tick; selected="+selected+", short="+shortChange+", armed="+armed+", firstWatchdog="+action+", fault="+fault);
            Check(action=="Drive"&&!fault,"The first real watchdog after a committed late automatic upgrade stays Drive rather than false EmergencyStop; action="+action);
            Transition("Move",65f,20/3.6);Transition("Tick");frame(false);
            var sample=transitionPlugin.GetType().GetField("longitudinalSnapshot",All)!.GetValue(transitionPlugin)!;
            var expected=sample.GetType().GetProperty("Output")!.GetValue(sample)!;
            bool published=(bool)sample.GetType().GetProperty("Published")!.GetValue(sample)!;
            float expectedGas=(float)expected.GetType().GetProperty("Throttle")!.GetValue(expected)!;
            float expectedBrake=(float)expected.GetType().GetProperty("Brake")!.GetValue(expected)!;
            Check(published&&Math.Abs(gas()-expectedGas)<1e-6&&Math.Abs(brake()-expectedBrake)<1e-6&&!fault,
                "The first post-upgrade actual Tick reaches the legacy sink without a stale-decision emergency floor");
            frame(true);Check(Math.Abs(modern()-(expectedGas-expectedBrake))<1e-6&&gas()==0&&brake()==0,
                "The same actual post-upgrade Tick reaches the modern sink and clears legacy pedals");
        } finally {Transition("Stop");}
        File.WriteAllText(Path.Combine(sandbox,"canonical-gate-fixture.json"),System.Text.Json.JsonSerializer.Serialize(new{sha,mvid=module.Mvid,syntheticMap=true,actualProductTick=true,live=false,checks,timings}));
        return checks;
    }
    static Action<T> Sink<T>(Action<object> output)=>value=>output(value!);
}
