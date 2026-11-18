using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using B=System.Reflection.BindingFlags;
namespace MautHostCompatibility;

// Real plugin Tick -> own controller -> protected host -> two dictionary sinks.
// Only the audited DI constructor runs; never Init/OnEnable, native ACC, SDK,
// model workers or output loops. TrafficClear is an explicitly injected input,
// so this integration does not prove perception or source acquisition.
internal static class LongitudinalPluginReplay
{
    const B All=B.Instance|B.Static|B.Public|B.NonPublic;
    static FieldInfo Field(Type type,string name) {
        for(Type? t=type;t!=null;t=t.BaseType)if(t.GetField(name,All|B.DeclaredOnly) is {} f)return f;
        throw new MissingFieldException(type.FullName,name);
    }
    static void Set(object owner,string name,object? value)=>Field(owner.GetType(),name).SetValue(owner,value);
    static object? Get(object owner,string name)=>Field(owner.GetType(),name).GetValue(owner);
    static void Member(object owner,string name,object? value) {
        if(owner.GetType().GetProperty(name,All) is {} p)p.SetValue(owner,value);else Set(owner,name,value);
    }
    static Func<T> Read<T>(Func<object?> read)=>()=>(T)read()!;
    static Action<T> Sink<T>(Action<object> write)=>value=>write(value!);
    static Func<A,C,D,object> Geometry<A,C,D>()=>(_,_,_)=>new object();
    static Delegate Reader(Type type,Func<object?> read)=>(Delegate)typeof(LongitudinalPluginReplay).GetMethod(nameof(Read),All)!.MakeGenericMethod(type).Invoke(null,[read])!;

    static void AuditConstruction(string candidate,string root)
    {
        using var module=ModuleDefinition.ReadModule(candidate);
        using var shared=ModuleDefinition.ReadModule(Path.Combine(root,"ETS2LA.Shared.dll"));
        if(module.GetType("<Module>").Methods.Any(m=>m.IsConstructor)||shared.GetType("<Module>").Methods.Any(m=>m.IsConstructor))
            throw new InvalidDataException("Module initialization is not permitted in the Tick fixture");
        var plugin=module.GetType("MautAssistent.TollPlugin");
        var ctor=plugin.Methods.Single(m=>m.IsConstructor&&!m.IsStatic&&m.Parameters.Any(p=>p.Name=="readState"));
        var seen=new HashSet<string>();
        void Visit(MethodDefinition method) {
            if(!seen.Add(method.FullName)||!method.HasBody)return;
            if(method.DeclaringType!=plugin&&method.IsConstructor&&method.DeclaringType.Methods.FirstOrDefault(m=>m.IsConstructor&&m.IsStatic) is {} init)Visit(init);
            foreach(var instruction in method.Body.Instructions) {
                if(instruction.OpCode.Code is Mono.Cecil.Cil.Code.Calli or Mono.Cecil.Cil.Code.Jmp)
                    throw new InvalidDataException("Indirect construction in Tick fixture");
                if(instruction.Operand is FieldReference field&&instruction.OpCode.Code is Mono.Cecil.Cil.Code.Ldsfld or Mono.Cecil.Cil.Code.Stsfld&&
                    field.DeclaringType.FullName.StartsWith("MautAssistent.",StringComparison.Ordinal)&&field.DeclaringType.FullName!=plugin.FullName&&
                    field.Resolve().DeclaringType.Methods.FirstOrDefault(m=>m.IsConstructor&&m.IsStatic) is {} fieldInit)Visit(fieldInit);
                // ldftn constructs dormant injected/default callbacks. Calling
                // one during construction would separately hit Invoke below
                // and be rejected; the callbacks are not executed here.
                if(instruction.Operand is not MethodReference call||instruction.OpCode.Code is not
                    (Mono.Cecil.Cil.Code.Call or Mono.Cecil.Cil.Code.Callvirt or Mono.Cecil.Cil.Code.Newobj))continue;
                string owner=call.DeclaringType.GetElementType().FullName;
                if(call.DeclaringType.Scope==module||owner.StartsWith("MautAssistent.",StringComparison.Ordinal)) {Visit(call.Resolve());continue;}
                if(owner=="ETS2LA.Shared.Plugin"&&call.Name==".ctor") {Visit(shared.GetType(owner).Methods.Single(m=>m.IsConstructor&&!m.IsStatic));continue;}
                bool allowed=call.Name==".ctor"&&(owner=="System.Object"||owner.StartsWith("System.Collections.Generic.",StringComparison.Ordinal)||
                    owner.StartsWith("System.Func`",StringComparison.Ordinal)||owner.StartsWith("System.Action",StringComparison.Ordinal)||owner.StartsWith("System.Threading.AsyncLocal`",StringComparison.Ordinal))||
                    owner=="System.Diagnostics.Stopwatch"&&call.Name=="StartNew"||owner=="System.Environment"&&call.Name=="GetFolderPath"||
                    owner=="System.IO.Path"&&call.Name=="Combine"||owner=="System.Array"&&call.Name=="Empty"||
                    owner=="System.StringComparer"&&call.Name=="get_Ordinal"||
                    owner=="System.Type"&&call.Name is "GetTypeFromHandle" or "GetField" or "GetMethod" or "GetProperty"||
                    call.FullName=="System.Void System.ArgumentOutOfRangeException::.ctor(System.String)";
                if(!allowed)throw new InvalidDataException("Unaudited Tick fixture constructor call: "+call.FullName);
            }
        }
        Visit(ctor); // Includes field initializers, readers, preference/router/fault and diagnostics construction.
    }

    internal static int Run(string sealedCandidate,string root,Assembly pluginAssembly,Assembly gameAssembly,Assembly stateAssembly,
        AssemblyLoadContext context,object host,object state,Action<bool> frame,Func<float> gas,Func<float> brake,
        Func<float> modernPedal,Func<float> steering,Action<bool> enable,Func<long> epoch,Action<bool,string> assert)
    {
        int count=0;void Check(bool okay,string reason){assert(okay,reason);count++;}
        AuditConstruction(sealedCandidate,root);
        if(AssemblyLoadContext.GetLoadContext(pluginAssembly)!=context||AssemblyLoadContext.GetLoadContext(gameAssembly)!=context||
            AssemblyLoadContext.GetLoadContext(stateAssembly)!=context||context==AssemblyLoadContext.Default)
            throw new InvalidDataException("Tick fixture must use the already sealed isolated host/plugin context");
        using(var audited=ModuleDefinition.ReadModule(sealedCandidate))if(audited.Mvid!=pluginAssembly.ManifestModule.ModuleVersionId)
            throw new InvalidDataException("Tick fixture candidate differs from the audited copied module");
        if(Environment.GetEnvironmentVariable("MAUT_SLOW_EXIT_ONLY")=="1")
            return SlowExitHostReplay.Run(Environment.GetEnvironmentVariable("MAUT_SLOW_EXIT_FIXTURE")??throw new InvalidDataException("Slow-exit fixture required"),
                Path.GetDirectoryName(sealedCandidate)!,context,pluginAssembly,gameAssembly,state,assert);
        if(Environment.GetEnvironmentVariable("MAUT_FRESH_HAZARD_ONLY")=="1")
            return FreshHazardReplay.Run(sealedCandidate,root,pluginAssembly,gameAssembly,stateAssembly,context,host,state,frame,gas,brake,modernPedal,steering,enable,epoch,assert);
        // Explicit counterfactual runner: reuse all outer sealing/ALC guards,
        // but isolate the OnPath regression from unrelated new-observer tests.
        if(Environment.GetEnvironmentVariable("MAUT_TRANSITION_ONLY")=="1")
            return CanonicalGateHostReplay.Run(Environment.GetEnvironmentVariable("MAUT_CANONICAL_FIXTURE")??throw new InvalidDataException("Fixture required"),
                Path.GetDirectoryName(sealedCandidate)!,context,pluginAssembly,gameAssembly,host,frame,gas,brake,modernPedal,assert);
        var type=pluginAssembly.GetType("MautAssistent.TollPlugin",true)!;
        var output=gameAssembly.GetType("ETS2LA.Game.Output.GameOutput",true)!;
        var eventType=gameAssembly.GetType("ETS2LA.Game.Output.ControlEvent",true)!;
        var definitionType=gameAssembly.GetType("ETS2LA.Game.Output.ControlChannelDefinition",true)!;
        var variablesType=gameAssembly.GetType("ETS2LA.Game.Output.ControlVariables",true)!;
        var propertiesType=gameAssembly.GetType("ETS2LA.Game.Output.ControlProperties",true)!;
        var ctor=type.GetConstructors(All).Single(c=>c.GetParameters().Any(p=>p.Name=="readState"));
        object Pure(string name,params object?[] values)=>Activator.CreateInstance(pluginAssembly.GetType("MautAssistent."+name,true)!,values)!;
        var traffic=Pure("TrafficSpeedDecision",float.NaN,true,"Injected fresh empty traffic",null,null);
        var cruise=Pure("CruiseLimits",0d,false);
        var emitted=new List<object>();Action? duringTraffic=null;int nativeReads=0;
        Action<object> accept=message=>output.GetMethod("OnControlEvent")!.Invoke(host,[message]);
        var args=ctor.GetParameters().Select(p=>p.HasDefaultValue?p.DefaultValue:null).ToArray();
        foreach(var p in ctor.GetParameters()) {
            if(p.Name=="readState")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>state);
            if(p.Name=="notify")args[p.Position]=(Action<string,string>)((_,_)=>{});
            if(p.Name=="output")args[p.Position]=typeof(LongitudinalPluginReplay).GetMethod(nameof(Sink),All)!.MakeGenericMethod(eventType).Invoke(null,[(Action<object>)(m=>{emitted.Add(m);accept(m);})]);
            if(p.Name=="readAcc")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>{nativeReads++;throw new InvalidOperationException("Own Tick queried native ACC");});
            if(p.Name is "readPlanner" or "readLaneAssist")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>null);
            if(p.Name=="readCruiseLimits")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>cruise);
            if(p.Name=="readLongitudinalTraffic")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>{duringTraffic?.Invoke();return traffic;});
            if(p.Name=="writeDiagnostic")args[p.Position]=(Action<string,string>)((_,_)=>throw new InvalidOperationException("Tick replay attempted diagnostic I/O"));
            if(p.Name=="buildStatusGeometry")args[p.Position]=typeof(LongitudinalPluginReplay).GetMethod(nameof(Geometry),All)!.MakeGenericMethod(p.ParameterType.GenericTypeArguments[..3]).Invoke(null,null);
        }
        var plugin=ctor.Invoke(args);
        type.GetMethod("StopDiagnostics",All)!.Invoke(plugin,null); // No Enqueue can start a worker, even on fixture failure.
        Set(plugin,"vehicleGeometry",null); // Unexpected fault diagnostics must not open a live geometry map.
        var injectedTraffic=Get(plugin,"readLongitudinalTraffic");Set(plugin,"readLongitudinalTraffic",null);
        Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>duringTraffic?.Invoke()));
        bool autoSources=true;object? testSignals=null;
        var clock=(Stopwatch)Get(plugin,"clock")!;
        var options=Pure("TollOptions");Member(options,"CurvePreviewEnabled",false);Member(options,"BriskOpenRoads",false);
        Set(plugin,"options",options);Set(plugin,"controller",Pure("TollController",options));
        Set(plugin,"lastWrite",double.MaxValue);Set(plugin,"lastMotionAt",double.MaxValue);Set(plugin,"lastMapWrite",0d);

        // A genuine bounded straight path, using only local parsed DTOs. No
        // installation, asset files, provider or native map is consulted.
        var parsedType=gameAssembly.GetType("ETS2LA.Game.Data.ParsedPrefab",true)!;
        var parsed=RuntimeHelpers.GetUninitializedObject(parsedType);
        var descriptorType=parsedType.GetProperty("Descriptor")?.PropertyType??Field(parsedType,"Descriptor").FieldType;
        var descriptor=RuntimeHelpers.GetUninitializedObject(descriptorType);
        var curvesMember=descriptorType.GetProperty("NavCurves");var curvesType=curvesMember?.PropertyType??Field(descriptorType,"NavCurves").FieldType;
        var curveType=curvesType.GenericTypeArguments[0];var curve=RuntimeHelpers.GetUninitializedObject(curveType);
        foreach(var (name,value) in new (string,object)[]{("StartPosition",Vector3.Zero),("EndPosition",new Vector3(0,0,-500)),
            ("StartRotation",Quaternion.Identity),("EndRotation",Quaternion.Identity),("Length",500f),("SemaphoreId",-1)})Member(curve,name,value);
        var curves=(System.Collections.IList)Activator.CreateInstance(curvesType)!;curves.Add(curve);Member(descriptor,"NavCurves",curves);Member(parsed,"Descriptor",descriptor);
        var prefabType=parsedType.GetProperty("Prefab")?.PropertyType??Field(parsedType,"Prefab").FieldType;
        var prefab=Activator.CreateInstance(prefabType)!;Member(prefab,"Uid",123UL);Member(parsed,"Prefab",prefab);
        var pathType=gameAssembly.GetType("ETS2LA.Game.Data.PrefabPath",true)!;var pathCtor=pathType.GetConstructors().Single(c=>c.GetParameters().Length==7);
        var nodeType=pathCtor.GetParameters()[1].ParameterType;
        var start=Activator.CreateInstance(nodeType)!;var end=Activator.CreateInstance(nodeType)!;
        Member(start,"Uid",1UL);Member(end,"Uid",2UL);
        var direction=Enum.Parse(pathCtor.GetParameters()[4].ParameterType,"Forward");
        var prefabPath=pathCtor.Invoke([parsed,start,end,curves,direction,Matrix4x4.Identity,Vector3.Zero]);
        var pathLib=context.LoadFromAssemblyName(new AssemblyName("PathLib"));
        var itemType=pathLib.GetType("PathLib.PlannedItem",true)!;var item=Activator.CreateInstance(itemType)!;
        foreach(var (name,value) in new (string,object)[]{("item",parsed),("startNode",start),("endNode",end),("prefabPath",prefabPath),("direction",direction)})Set(item,name,value);
        var dataType=pathLib.GetType("PathLib.PlannedPathData",true)!;var data=Activator.CreateInstance(dataType)!;
        var items=(System.Collections.IList)Activator.CreateInstance(Field(dataType,"PlannedPath").FieldType)!;items.Add(item);
        Set(data,"PlannedPath",items);Set(data,"HasError",false);
        var matcher=pluginAssembly.GetType("MautAssistent.RouteMatcher",true)!;
        var trace=matcher.GetMethod("Sample",[dataType])!.Invoke(null,[data])!;
        var point=new Vector3(0,0,-50);
        var supervisor=Pure("DriveSupervisor",Pure("DriveCheckpoint",(double?)0,null,null,true));
        Set(plugin,"supervisor",supervisor);Set(plugin,"subscribed",true);Set(plugin,"active",true);
        Set(plugin,"<_IsRunning>k__BackingField",true);Set(plugin,"path",data);Set(plugin,"safetyTrace",trace);
        Set(plugin,"position",point);Set(plugin,"headingTurns",0d);Set(plugin,"speed",5d);Set(plugin,"takeoverUntil",-100d);
        Set(plugin,"decision",pluginAssembly.GetType("MautAssistent.TollDecision",true)!.GetMethod("Idle")!.Invoke(null,["Injected clear route"]));
        void Fresh() {
            double now=clock.Elapsed.TotalSeconds;
            foreach(string name in new[]{"telemetryAt","gatesAt","pathAt","gameTimeChangedAt","evaluatedAt","lastAcceptedPlanAt"})Set(plugin,name,now);
            Set(plugin,"sdk",true);Set(plugin,"paused",false);Set(plugin,"decisionArmed",true);
        }
        void Tick(){if(autoSources)Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));Fresh();type.GetMethod("Tick")!.Invoke(plugin,null);}
        void Clear()=>((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
        void Send(string id,float signed,float? steering=null) {
            var definition=Activator.CreateInstance(definitionType)!;Set(definition,"Id",id);Set(definition,"Timeout",.2f);
            Set(definition,"MautEpoch",epoch());Set(definition,"MautIssuedAt",Stopwatch.GetTimestamp());
            var variables=Activator.CreateInstance(variablesType)!;if(steering.HasValue)Set(variables,"steering",steering.Value);else Set(variables,signed<0?"abackward":"aforward",signed);
            var properties=Activator.CreateInstance(propertiesType)!;Set(properties,"Weight",1f);
            var message=Activator.CreateInstance(eventType)!;Set(message,"ChannelDefinition",definition);Set(message,"Properties",properties);Set(message,"Variables",variables);accept(message);
            if(id=="LaneAssist.SteeringCorrection")type.GetMethod("OnControlOutput",All)!.Invoke(plugin,[message]);
        }
        state.GetType().GetProperty("DesiredSpeed")!.SetValue(state,25f);enable(false);enable(true);Clear();
        Tick();frame(false);
        var snapshot=Get(plugin,"longitudinalSnapshot")??throw new InvalidDataException("Real Tick did not reach longitudinal regulation");
        var result=snapshot.GetType().GetProperty("Output")!.GetValue(snapshot)!;
        var actualInput=snapshot.GetType().GetProperty("Input")!.GetValue(snapshot)!;
        Check((bool)snapshot.GetType().GetProperty("Published")!.GetValue(snapshot)!&&gas()>0&&brake()==0&&
            (bool)actualInput.GetType().GetProperty("RouteKnown")!.GetValue(actualInput)!&&
            (double)actualInput.GetType().GetProperty("TargetSpeed")!.GetValue(actualInput)! ==25&&
            Math.Abs(gas()-(float)result.GetType().GetProperty("Throttle")!.GetValue(result)!)<1e-6,
            "Real plugin Tick without ACC publishes its own exact throttle through the protected legacy sink");
        frame(true);Check(modernPedal()>0&&gas()==0&&brake()==0,"The same real plugin Tick reaches modern output and clears both legacy pedals");
        object OwnMessage()=>emitted.Last(m=>(string)Get(Get(m,"ChannelDefinition")!,"Id")! =="local.mautassistent.longitudinal");
        void RawStore(object message) {
            Set(plugin,"requireHostCompatibility",true);
            try {type.GetMethod("OnControlOutput",All)!.Invoke(plugin,[message]);}finally{Set(plugin,"requireHostCompatibility",false);}
        }
        var originalOwn=OwnMessage();RawStore(originalOwn);var rawCommand=Get(plugin,"lastAccCommand")!;
        var originalDefinition=Get(originalOwn,"ChannelDefinition")!;
        Check((long?)rawCommand.GetType().GetProperty("IssuedTicks")!.GetValue(rawCommand)==(long)Get(originalDefinition,"MautIssuedAt")!&&
            (long?)rawCommand.GetType().GetProperty("Epoch")!.GetValue(rawCommand)==(long)Get(originalDefinition,"MautEpoch")!,
            "The genuine produced Tick event passes protected OnControlOutput/StorePedals with exact original raw issued ticks and epoch");

        Send("AdaptiveCruiseControl.Acceleration",-1);Send("hostile.concurrent-pedal",1);frame(false);
        Check(gas()>0&&brake()==0,"Concurrent old ACC brake and foreign throttle cannot change the real plugin Tick command");
        Check(nativeReads==0,"Real own Tick never looked up a native ACC instance");
        Thread.Sleep(230);Send("LaneAssist.SteeringCorrection",0,.2f);frame(true);
        Check(modernPedal()==0&&gas()==0&&brake()==0,"Expired real Tick is neutral even with fresh lane and foreign channel activity");
        RawStore(originalOwn);
        Check(ReferenceEquals(rawCommand,Get(plugin,"lastAccCommand")),"Protected RawStore rejects the original expired Tick event without renewing watchdog observation time");

        Clear();Set(plugin,"longitudinal",Pure("LongitudinalController"));duringTraffic=()=>enable(false);Tick();frame(false);duringTraffic=null;
        Check(gas()==0&&brake()==0&&!(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!&&!(bool)snapshot.GetType().GetProperty("Published")!.GetValue(Get(plugin,"longitudinalSnapshot")!)!,
            "F9 epoch revocation during the actual Tick prevents late physical throttle");
        enable(true);Set(plugin,"active",true);Set(plugin,"supervisor",Pure("DriveSupervisor",Pure("DriveCheckpoint",(double?)0,null,null,true)));
        Set(plugin,"safetyTrace",trace);Set(plugin,"path",data);Set(plugin,"longitudinal",Pure("LongitudinalController"));
        Clear();duringTraffic=()=>Thread.Sleep(230);Tick();frame(false);duringTraffic=null;
        Check(gas()==0&&brake()==0&&!(bool)snapshot.GetType().GetProperty("Published")!.GetValue(Get(plugin,"longitudinalSnapshot")!)!,
            "Slow real plugin Tick cannot renew its original timestamp at host publication");

        void Restore() {
            enable(false);enable(true);Clear();Set(plugin,"active",true);Set(plugin,"preview",false);Set(plugin,"paused",false);
            Set(plugin,"supervisor",Pure("DriveSupervisor",Pure("DriveCheckpoint",(double?)0,null,null,true)));
            Set(plugin,"safetyTrace",trace);Set(plugin,"path",data);Set(plugin,"lastAcceptedPlan",data);Set(plugin,"longitudinal",Pure("LongitudinalController"));
            Set(plugin,"accelerationKnown",true);Set(plugin,"holdPedalFloor",0f);Set(plugin,"scenePedalFloor",0f);
            Set(plugin,"decisionArmed",true);Set(plugin,"takeoverUntil",-100d);Fresh();
        }
        autoSources=false;Set(plugin,"readLongitudinalTraffic",null);
        void Sources(object trafficData) {
            SourceFrameReplay.Capture(output,"Traffic",trafficData);SourceFrameReplay.Capture(output,"Parked",SourceFrameReplay.EmptyParked(gameAssembly));
            var signals=testSignals??SourceFrameReplay.EmptySignals(gameAssembly);SourceFrameReplay.Capture(output,"Semaphore",signals);
            // File/ALC pins were audited before all calls. Here the real gate
            // callback consumes the actual isolated owned tuple+origin; the DI
            // Tick still bypasses production warmup/file checks afterward.
            Set(plugin,"requireHostCompatibility",true);
            try{type.GetMethod("OnGates",All)!.Invoke(plugin,[signals]);}finally{Set(plugin,"requireHostCompatibility",false);}
        }
        Restore();Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));Tick();frame(false);
        Check(gas()>0&&brake()==0,"Real plugin Tick decodes39 traffic and40 parked empty slots through owned host frames without a TrafficClear delegate");
        frame(true);Check(modernPedal()>0&&gas()==0&&brake()==0,"Actual empty SDK-frame path also reaches the modern physical sink");
        Restore();var malformed=SourceFrameReplay.EmptyTraffic(gameAssembly);var malformedActor=((Array)Get(malformed,"vehicles")!).GetValue(0)!;
        Set(malformedActor,"id",(short)1);Sources(malformed);Tick();frame(false);
        Check(gas()==0&&brake()>0,"An identified zero-geometry traffic actor is unknown evidence, never treated as an unused SDK slot");
        Restore();var occupied=SourceFrameReplay.EmptyTraffic(gameAssembly);var standing=((Array)Get(occupied,"vehicles")!).GetValue(0)!;
        Set(standing,"id",(short)1);Member(standing,"Position",new Vector3(0,0,-70));Member(standing,"Rotation",new Quaternion(1,0,0,0));Member(standing,"Size",new Vector3(2,4,6));
        Sources(occupied);Tick();frame(false);
        Check(gas()==0&&brake()>0,"A valid stationary same-lane traffic body produces actual brake through plugin Tick and host sink");
        Restore();Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));Thread.Sleep(230);Tick();frame(false);
        Check(gas()==0&&brake()>0,"Original owned traffic-frame age expires during observation and cannot become fresh SDK clearance");

        // Real consumer decision -> real host epoch-lock wait. New source
        // publication must not renew the three original observation clocks.
        void LeaseWait(int sourceAgeMs,bool revoke,string label,bool expectedPublish,int waitMs=40) {
            Restore();Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));Thread.Sleep(sourceAgeMs);
            using var gateEntered=new ManualResetEventSlim();Task? lockOwner=null;
            var gate=output.GetField("MautGate")!.GetValue(null)!;
            Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>{
                lockOwner=Task.Run(()=>{lock(gate) {
                    gateEntered.Set();Thread.Sleep(waitMs);
                    if(revoke)output.GetMethod("MautInvalidateOwnSources")!.Invoke(null,null);
                    Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));
                }});
                if(!gateEntered.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Private host gate did not block final authority read");
            }));
            try {Tick();if(lockOwner!=null&&!lockOwner.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Private source publisher did not finish");}
            finally {Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>duringTraffic?.Invoke()));}
            frame(false);var observed=Get(plugin,"longitudinalSnapshot")!;
            bool published=(bool)observed.GetType().GetProperty("Published")!.GetValue(observed)!;
            Check(Get(plugin,"longitudinalSourceLease")!=null&&published==expectedPublish&&(expectedPublish?gas()>0&&brake()==0:gas()==0&&brake()==0),label+" [published="+published+", gas="+gas()+", brake="+brake()+", sourceLease="+Get(plugin,"longitudinalSourceLease")+", output="+observed.GetType().GetProperty("Output")!.GetValue(observed)+"]");
            frame(true);Check(expectedPublish?modernPedal()>0:modernPedal()==0,label+" also holds at the modern sink");
        }
        LeaseWait(20,false,"Original20ms source plus40ms host wait permits normal next-frame progress",true);
        LeaseWait(150,false,"Original150ms source plus75ms host wait cannot be rejuvenated by fresh replacement frames",false,75);
        LeaseWait(20,true,"Provider Close plus fresh replacement during the host wait revokes the original clear decision",false);

        // Young commands cannot attest source authority that their actual
        // original observations have since lost. No relabeling of the event.
        Set(plugin,"readLongitudinalTraffic",null);Set(plugin,"monitorSteering",false);Set(plugin,"steeringMonitoringLive",false);Restore();autoSources=true;Tick();
        var sourceRevokedEvent=OwnMessage();RawStore(sourceRevokedEvent);var sourceRevokedRecord=Get(plugin,"lastAccCommand")!;
        long timelineOrigin=(long)Get(Get(sourceRevokedEvent,"ChannelDefinition")!,"MautIssuedAt")!;
        void WaitAge(long origin,double seconds){while((Stopwatch.GetTimestamp()-origin)/(double)Stopwatch.Frequency<seconds)Thread.Sleep(1);}
        WaitAge(timelineOrigin,.010);output.GetMethod("MautInvalidateOwnSources")!.Invoke(null,null);
        WaitAge(timelineOrigin,.016);Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));
        RawStore(sourceRevokedEvent);frame(false);
        Set(plugin,"requireHostCompatibility",true);
        object? revokedAge;
        try {revokedAge=type.GetMethod("PedalAge",All)!.Invoke(plugin,[sourceRevokedRecord]);}finally{Set(plugin,"requireHostCompatibility",false);}
        Check(ReferenceEquals(sourceRevokedRecord,Get(plugin,"lastAccCommand"))&&revokedAge==null&&gas()==0&&brake()==0,
            "Young actual Tick output cannot renew RawStore or watchdog health after source Close, even with fresh replacement frames");
        Set(plugin,"monitorSteering",true);Set(plugin,"steeringMonitoringLive",true);WaitAge(timelineOrigin,.025);
        Set(plugin,"requireHostCompatibility",true);
        try{type.GetMethod("CheckLongitudinalOutput",All)!.Invoke(plugin,null);}finally{Set(plugin,"requireHostCompatibility",false);}
        Check(!(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!&&gas()==0&&brake()==0,
            "Accepted Tick0 then Close10ms and new source16ms: timer25ms withdraws gas without misclassifying the still-running producer as failed");
        WaitAge(timelineOrigin,.050);autoSources=false;
        var noGasTraffic=SourceFrameReplay.EmptyTraffic(gameAssembly);Set(((Array)Get(noGasTraffic,"vehicles")!).GetValue(0)!,"id",(short)1);
        Sources(noGasTraffic);Tick();var timelineNoGas=OwnMessage();RawStore(timelineNoGas);frame(false);
        Check(Get(Get(timelineNoGas,"Variables")!,"aforward") is float timelineGas&&timelineGas==0&&brake()>0&&
            !(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!,
            "A genuine new ordinary no-gas Tick at50ms is accepted after source turnover without a reset or false emergency");
        long noGasOrigin=(long)Get(Get(timelineNoGas,"ChannelDefinition")!,"MautIssuedAt")!;
        WaitAge(noGasOrigin,.155);Set(plugin,"requireHostCompatibility",true);
        try{type.GetMethod("CheckLongitudinalOutput",All)!.Invoke(plugin,null);}finally{Set(plugin,"requireHostCompatibility",false);}
        frame(false);Check((bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!&&brake()==1&&gas()==0,
            "Without any later accepted plan the unchanged150ms producer deadline still trips physical fault braking");
        Get(plugin,"faultBrake")!.GetType().GetMethod("Release")!.Invoke(Get(plugin,"faultBrake"),null);
        type.GetMethod("ResetLongitudinalWatchdog",All)!.Invoke(plugin,null);Set(plugin,"monitorSteering",false);Set(plugin,"steeringMonitoringLive",false);

        Restore();autoSources=false;Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));Thread.Sleep(150);Tick();
        var sourceAgedEvent=OwnMessage();
        Check(Get(Get(sourceAgedEvent,"Variables")!,"aforward") is float sourceAgedGas&&sourceAgedGas>0,"Source-age observer regression begins with genuine positive own Tick throttle");
        RawStore(sourceAgedEvent);var sourceAgedRecord=Get(plugin,"lastAccCommand")!;
        Thread.Sleep(65);Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));RawStore(sourceAgedEvent);frame(true);
        Set(plugin,"requireHostCompatibility",true);
        object? sourceAgedAge;
        try {sourceAgedAge=type.GetMethod("PedalAge",All)!.Invoke(plugin,[sourceAgedRecord]);}finally{Set(plugin,"requireHostCompatibility",false);}
        Check(ReferenceEquals(sourceAgedRecord,Get(plugin,"lastAccCommand"))&&sourceAgedAge==null&&modernPedal()==0,
            "A still-young command loses RawStore/watchdog authority when its original150ms source ages beyond200ms; new sources cannot rejuvenate it [same="+ReferenceEquals(sourceAgedRecord,Get(plugin,"lastAccCommand"))+", age="+sourceAgedAge+", modern="+modernPedal()+", record="+sourceAgedRecord+", definitionSourceAt="+Get(Get(sourceAgedEvent,"ChannelDefinition")!,"MautSourceAt")+"]");
        Restore();output.GetMethod("MautInvalidateOwnSources")!.Invoke(null,null);Tick();
        var unknownBrake=OwnMessage();RawStore(unknownBrake);var unknownRecord=Get(plugin,"lastAccCommand")!;
        Set(plugin,"requireHostCompatibility",true);
        object? unknownAge;
        try {unknownAge=type.GetMethod("PedalAge",All)!.Invoke(plugin,[unknownRecord]);}finally{Set(plugin,"requireHostCompatibility",false);}
        frame(false);
        Check(Get(Get(unknownBrake,"Variables")!,"aforward") is float noGas&&noGas==0&&unknownAge is double&&brake()>0,
            "A real unknown-source no-gas ordinary brake remains an observed current command with sourceAt0");
        // Independent watchdog probe while the REAL tick owns sync and waits
        // inside its traffic delegate. No production timer or provider runs.
        Restore();autoSources=true;Set(plugin,"readLongitudinalTraffic",null);Set(plugin,"monitorSteering",true);Set(plugin,"steeringMonitoringLive",true);
        Send("LaneAssist.SteeringCorrection",0,.2f);type.GetMethod("CheckSteeringOutput",All)!.Invoke(plugin,null);
        Tick();RawStore(OwnMessage());frame(false);Set(plugin,"requireHostCompatibility",true);
        try {type.GetMethod("CheckLongitudinalOutput",All)!.Invoke(plugin,null);}finally{Set(plugin,"requireHostCompatibility",false);}
        var baseline=Get(plugin,"longitudinalSnapshot")!;long baselineOrigin=(long)baseline.GetType().GetProperty("IssuedAt")!.GetValue(baseline)!;
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        duringTraffic=()=>{entered.Set();if(!release.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException("Private tick was not released");};
        Set(plugin,"readLongitudinalTraffic",injectedTraffic);
        Task blocked=Task.Run(Tick);if(!entered.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Actual tick did not block at traffic observation");
        bool beforeDeadlineHealthy=false,trippedWhileBlocked=false;double tripAge=0;
        try {
            var timeout=Stopwatch.StartNew();
            while(timeout.Elapsed.TotalMilliseconds<500) {
                Send("LaneAssist.SteeringCorrection",0,.2f);type.GetMethod("CheckSteeringOutput",All)!.Invoke(plugin,null);
                Set(plugin,"requireHostCompatibility",true);
                try{type.GetMethod("CheckLongitudinalOutput",All)!.Invoke(plugin,null);}finally{Set(plugin,"requireHostCompatibility",false);}
                frame(false);
                double age=(Stopwatch.GetTimestamp()-baselineOrigin)/(double)Stopwatch.Frequency;
                bool latched=(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!;
                if(age<.14)beforeDeadlineHealthy|=!latched&&gas()>0;
                if(latched){tripAge=age;trippedWhileBlocked=!blocked.IsCompleted&&brake()==1&&gas()==0;break;}
                Thread.Sleep(5);
            }
            Check(beforeDeadlineHealthy,"Fresh own command remains admitted before the150ms deadman deadline");
            Check(trippedWhileBlocked&&tripAge is >=.15 and <.2,
                "Independent pedal watchdog reaches real full-brake sink before200ms while actual Tick remains blocked and lane stays fresh; age="+tripAge);
        } finally {release.Set();if(!blocked.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException("Actual blocked tick failed to finish");duringTraffic=null;}
        frame(true);Check(modernPedal()==-1&&gas()==0&&brake()==0,"The blocked-tick fault brake also dominates the modern sink");
        var controls=context.LoadFromAssemblyName(new AssemblyName("ETS2LA.Controls"));
        using(var controlsSource=ModuleDefinition.ReadModule(Path.Combine(root,"ETS2LA.Controls.dll"))) {
            var change=controlsSource.GetType("ETS2LA.Controls.ControlChangeEventArgs");
            var seen=new HashSet<string>();
            void AuditDto(MethodDefinition method) {
                if(!seen.Add(method.FullName))return;
                if(method.IsStatic)throw new InvalidDataException("Unaudited F9 DTO static initializer");
                foreach(var r in method.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>()) {
                    if(r.DeclaringType.FullName==change.FullName){AuditDto(r.Resolve());continue;}
                    if(r.FullName is not ("System.Void System.Object::.ctor()" or "System.Void System.EventArgs::.ctor()"))
                        throw new InvalidDataException("Unaudited F9 event DTO call: "+r.FullName);
                }
            }
            foreach(var constructor in change.Methods.Where(m=>m.IsConstructor))AuditDto(constructor);
        }
        var cancellation=Activator.CreateInstance(controls.GetType("ETS2LA.Controls.ControlChangeEventArgs",true)!,[false,null]);
        type.GetMethod("OnCancel",All)!.Invoke(plugin,[null,cancellation]);frame(false);
        Check(gas()==0&&brake()==0&&!(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!,
            "Actual F9 handler releases the blocked-tick fault and both physical pedals without rearming");
        // Actual OnTelemetry delivers coherent parking state; no private
        // PhysicsObservation/clock replacement is used for these cases.
        ulong parkingStamp=5000000,parkingSdkStamp=5000000;Vector3 parkingPoint=point;
        var telemetryDto=gameAssembly.GetType("ETS2LA.Game.Telemetry.GameTelemetryData",true)!;
        void LiveParking(double rawSpeed,bool parking,bool repeatPhysics=false) {
            var dto=Activator.CreateInstance(telemetryDto)!;if(!repeatPhysics)parkingStamp+=50000;parkingSdkStamp+=50000;
            Member(dto,"sdkActive",true);Member(dto,"paused",false);Member(dto,"time",parkingSdkStamp);
            Member(dto,"simulatedTime",parkingStamp);Member(dto,"renderTime",parkingSdkStamp);
            var floats=Get(dto,"truckFloat")!;Member(floats,"speed",(float)rawSpeed);Member(floats,"engineRpm",1200f);
            Member(floats,"userThrottle",0f);Member(floats,"gameThrottle",0f);Member(floats,"userBrake",0f);Member(floats,"gameBrake",0f);
            var ints=Get(dto,"truckInt")!;Member(ints,"gear",1);Member(ints,"gearDashboard",1);
            Member(Get(dto,"truckBool")!,"parkingBrake",parking);
            var placement=Get(dto,"truckPlacement")!;var coordinateType=Field(placement.GetType(),"coordinate").FieldType;
            Member(placement,"coordinate",Activator.CreateInstance(coordinateType,[(double)parkingPoint.X,(double)parkingPoint.Y,(double)parkingPoint.Z]));
            Member(placement,"rotation",Activator.CreateInstance(coordinateType,[0d,0d,0d]));
            type.GetMethod("OnTelemetry",All)!.Invoke(plugin,[dto]);
        }
        void ParkingTick(double rawSpeed,bool parking) {LiveParking(rawSpeed,parking);Send("LaneAssist.SteeringCorrection",0,.2f);Tick();frame(false);}
        // These are real callback/DrainInputFrames/Tick/host paths. Only the
        // simulator DTO is synthetic; duplicate physics must retain the first
        // measured derivative's origin even while SDK and traffic frames move.
        Restore();autoSources=true;Set(plugin,"readLongitudinalTraffic",null);
        Set(plugin,"monitorSteering",false);Set(plugin,"steeringMonitoringLive",false);
        Get(plugin,"simulationDynamics")!.GetType().GetMethod("Reset")!.Invoke(Get(plugin,"simulationDynamics"),null);
        LiveParking(5,false);Thread.Sleep(20);LiveParking(5,false);
        Send("LaneAssist.SteeringCorrection",0,.2f);Tick();frame(false);
        var dynamicsFirst=Get(plugin,"longitudinalSnapshot")!;
        long dynamicsOrigin=(long)dynamicsFirst.GetType().GetProperty("DynamicsOrigin")!.GetValue(dynamicsFirst)!;
        Check(gas()>0&&brake()==0,"Two genuinely advancing OnTelemetry physics frames establish a usable original dynamics certificate through the actual host sink");
        bool repeatGas=true,repeatOrigin=true,repeatNoPending=true;
        for(int repeat=0;repeat<3;repeat++) {
            Thread.Sleep(20);LiveParking(5,false,true);Send("LaneAssist.SteeringCorrection",0,.2f);Tick();frame(false);
            var repeatedSnapshot=Get(plugin,"longitudinalSnapshot")!;
            repeatGas&=gas()>0&&brake()==0;
            repeatOrigin&=(long?)repeatedSnapshot.GetType().GetProperty("DynamicsOrigin")!.GetValue(repeatedSnapshot)==dynamicsOrigin;
            var repeatedBounds=(System.Collections.IEnumerable)repeatedSnapshot.GetType().GetProperty("Bounds")!.GetValue(repeatedSnapshot)!;
            repeatNoPending&=!repeatedBounds.Cast<object>().Any(b=>(string?)b.GetType().GetProperty("Source")!.GetValue(b)=="DynamicsPending");
        }
        Check(repeatGas&&repeatOrigin&&repeatNoPending,"Repeated identical physics with advancing SDK/traffic frames keeps actual gas without a fake DynamicsPending brake or renewed dynamics origin");
        long definitionDynamicsSource=(long)Get(Get(OwnMessage(),"ChannelDefinition")!,"MautSourceAt")!;
        Check(definitionDynamicsSource>0&&definitionDynamicsSource<=dynamicsOrigin,"The unchanged actual emitted event binds its source deadline to the original dynamics origin, not the latest traffic callback");
        frame(true);Check(modernPedal()>0&&gas()==0&&brake()==0,"The benign physics-repeat throttle also reaches the genuine modern output sink");
        WaitAge(dynamicsOrigin,.15);LiveParking(5,false,true);Send("LaneAssist.SteeringCorrection",0,.2f);Tick();frame(false);
        Check(gas()>0&&brake()==0,"A fresh own Tick still uses the bounded original dynamics lease before its200ms deadline");
        WaitAge(dynamicsOrigin,.205);
        Sources(SourceFrameReplay.EmptyTraffic(gameAssembly));Send("LaneAssist.SteeringCorrection",0,.2f);frame(false);
        Check(gas()==0&&brake()==0,"Fresh replacement traffic frames cannot keep an already emitted gas command alive after its original dynamics certificate expires");
        frame(true);Check(modernPedal()==0,"Expired original dynamics withdraws positive gas from the modern sink as well");
        LiveParking(5,false,true);Send("LaneAssist.SteeringCorrection",0,.2f);Tick();frame(false);
        var expiredSnapshot=Get(plugin,"longitudinalSnapshot")!;
        var expiredBounds=(System.Collections.IEnumerable)expiredSnapshot.GetType().GetProperty("Bounds")!.GetValue(expiredSnapshot)!;
        Check(gas()==0&&brake()>0&&expiredBounds.Cast<object>().Any(b=>(string?)b.GetType().GetProperty("Source")!.GetValue(b)=="DynamicsPending"),
            "A later actual Tick with only repeated stale physics retains the ordinary unknown-dynamics deceleration instead of inventing fresh acceleration");
        Restore();autoSources=true;Set(plugin,"readLongitudinalTraffic",null);Set(plugin,"monitorSteering",false);Set(plugin,"steeringMonitoringLive",false);
        LiveParking(0,true);
        output.GetMethod("MautSetPlannedHold")!.Invoke(null,[epoch(),"old-parking-test-hold",.65f,Stopwatch.GetTimestamp()]);frame(false);
        Check(brake()>.64,"Parking regression begins with a real previously admitted physical planned-hold lease");
        var parkedClock=Stopwatch.StartNew();int parkedSamples=0;bool parkedNeutral=true,parkedSteering=true;
        var parkingRows=new List<object>();
        double[] signedStand=[-.011614151299,.03,-.099,.099,0];
        while(parkedClock.Elapsed.TotalSeconds<2.1||parkedSamples<40) {
            ParkingTick(signedStand[parkedSamples%signedStand.Length],true);parkedNeutral&=gas()==0&&brake()==0;
            float legacyGas=gas(),legacyBrake=brake();frame(true);parkedNeutral&=modernPedal()==0;parkedSteering&=steering()>.19;
            parkingRows.Add(new{index=parkedSamples,raw=signedStand[parkedSamples%signedStand.Length],legacyGas,legacyBrake,modern=modernPedal(),steering=steering(),
                hostFloor=output.GetField("MautHoldFloor",All)!.GetValue(null),hold=Get(plugin,"holdPedalFloor"),scene=Get(plugin,"scenePedalFloor"),
                active=Get(plugin,"active"),assists=state.GetType().GetProperty("EnableAssists")!.GetValue(state),snapshot=Get(plugin,"longitudinalSnapshot")});
            parkedSamples++;Thread.Sleep(50);
        }
        Check(parkedSamples>=40&&parkedClock.Elapsed.TotalSeconds>=2.1&&parkedNeutral&&parkedSteering&&!(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!,
            "Fresh actual OnTelemetry parking brake plus signed stationary jitter stays physically0/0 for2s, clears old normal holds and preserves lane output [samples="+parkedSamples+", neutral="+parkedNeutral+", steering="+parkedSteering+"]");
        var parkRelease=Stopwatch.StartNew();while(parkRelease.Elapsed.TotalSeconds<.22){ParkingTick(0,false);Thread.Sleep(50);}
        var parkingReleaseDiagnostic=new{gas=gas(),brake=brake(),action=Get(plugin,"driveAction"),
            fault=Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake")),snapshot=Get(plugin,"longitudinalSnapshot")};
        Check(gas()>0&&gas()<.15&&brake()==0,"Releasing the measured parking brake with a valid plan resumes only gentle actual throttle [gas="+gas()+", brake="+brake()+", action="+Get(plugin,"driveAction")+"]");
        ParkingTick(1,true);frame(false);
        Check(gas()==0&&brake()>0,"A parking brake reported at1m/s cannot neutralize actual required service braking");
        void LateParkingCheck(bool braking) {
            Restore();autoSources=true;
            ParkingTick(braking?5:0,false);Thread.Sleep(20);ParkingTick(braking?5:0,false);Thread.Sleep(20);
            using var hostHeld=new ManualResetEventSlim();Task? holder=null,delivery=null;bool observedBeforeUnlock=false;
            var hostGate=output.GetField("MautGate")!.GetValue(null)!;float proposedGas=0,proposedBrake=0;
            Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>{
                proposedGas=(float)Get(Get(plugin,"longitudinal")!,"throttle")!;proposedBrake=(float)Get(Get(plugin,"longitudinal")!,"brake")!;
                holder=Task.Run(()=>{lock(hostGate) {
                    hostHeld.Set();Thread.Sleep(15);
                    delivery=Task.Run(()=>LiveParking(braking?1:0,true));
                    var limit=Stopwatch.StartNew();
                    while(limit.Elapsed.TotalMilliseconds<60) {
                        var latest=Get(plugin,"latestDeliveredTelemetry");var latestPhysics=latest?.GetType().GetProperty("Physics")!.GetValue(latest);
                        if(latestPhysics?.GetType().GetProperty("ParkingBrake")!.GetValue(latestPhysics) is true){observedBeforeUnlock=true;break;}
                        Thread.Sleep(1);
                    }
                }});
                if(!hostHeld.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Private parking host epoch barrier did not enter");
            }));
            try{Tick();if(holder!=null&&!holder.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Parking epoch owner did not finish");
                if(delivery!=null&&!delivery.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Actual parking callback did not finish");}
            finally{Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>duringTraffic?.Invoke()));}
            frame(false);
            Check(observedBeforeUnlock&&gas()==0&&(braking?proposedBrake>0&&brake()>=proposedBrake-.001:proposedGas>0&&brake()==0),
                braking?"Actual PB telemetry arriving during final epoch wait preserves an already required gate brake":
                    "Actual PB telemetry arriving during final epoch wait vetoes stale throttle before physical publication");
            frame(true);Check((braking?modernPedal()<0:modernPedal()==0)&&steering()>.19,
                "Late parking veto preserves the corresponding modern output and healthy lane sink");
        }
        LateParkingCheck(false);
        // Clear this case's real frames before unrelated synthetic DTO cases.
        Set(plugin,"latestDeliveredTelemetry",null);Set(plugin,"incomingTelemetry",null);Set(plugin,"appliedTelemetry",null);Set(plugin,"physicsObservation",null);
        Set(plugin,"speed",5d);Set(plugin,"observedAcceleration",0d);

        // Input receipt and simulation-effective pedal are independent. This
        // actual Tick series reaches both physical sinks with no native ACC.
        Restore();autoSources=true;Set(plugin,"readLongitudinalTraffic",null);
        Set(plugin,"monitorSteering",false);Set(plugin,"steeringMonitoringLive",false);
        var physicsType=type.GetNestedType("PhysicsObservation",All)!;
        void PedalPhysics(float received,float effective) {
            var physical=Activator.CreateInstance(physicsType,[1UL,1UL,0f,0f,0f,0f,0f,effective,0f])!;
            Member(physical,"ReceivedThrottle",received);Member(physical,"ReceivedBrake",0f);Member(physical,"EffectiveClutch",effective==0?1f:0f);
            Member(physical,"Gear",12);Member(physical,"DashboardGear",11);Member(physical,"EngineRpm",1400f);Set(plugin,"physicsObservation",physical);
        }
        object OutputNow(){var sample=Get(plugin,"longitudinalSnapshot")!;return sample.GetType().GetProperty("Output")!.GetValue(sample)!;}
        void PedalTick(float received,float effective){PedalPhysics(received,effective);Send("LaneAssist.SteeringCorrection",0,.2f);Tick();frame(false);}
        var warming=Stopwatch.StartNew();
        while(warming.Elapsed.TotalSeconds<2.2) {
            PedalTick(.6f,.6f);
            if(gas()>=.6f)break;Thread.Sleep(50);
        }
        float beforeCut=gas();Check(beforeCut>=.6f&&brake()==0,"Real own Tick builds an accepted throttle above0.6 with separate received/effective feedback");
        var cut=Stopwatch.StartNew();
        while(cut.Elapsed.TotalSeconds<.55){PedalTick(.6f,0);Thread.Sleep(50);}
        var limited=OutputNow();
        Check(gas()>0&&gas()<=beforeCut+.001&&brake()==0&&!(bool)limited.GetType().GetProperty("Fault")!.GetValue(limited)!&&
            (bool)limited.GetType().GetProperty("ThrottleEffectLimited")!.GetValue(limited)!&&
            !(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!,
            "Received throttle0.6 with temporarily effective0 freezes gas adaptation without fault braking through real Tick and legacy sink");
        frame(true);Check(modernPedal()>0&&steering()>.19&&brake()==0&&gas()==0,
            "Simulation-side throttle suppression preserves the actual modern pedal and original lane sink");
        var missing=Stopwatch.StartNew();while(missing.Elapsed.TotalSeconds<.55){PedalTick(0,0);Thread.Sleep(50);}
        var suspended=OutputNow();
        Check(gas()==0&&brake()==0&&(bool)suspended.GetType().GetProperty("ThrottleDeliveryMissing")!.GetValue(suspended)!&&
            !(bool)suspended.GetType().GetProperty("Fault")!.GetValue(suspended)!&&
            !(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!,
            "Actually missing received throttle suspends propulsion through real Tick without latching a fault brake");
        frame(true);Check(modernPedal()==0&&steering()>.19&&state.GetType().GetProperty("EnableAssists")!.GetValue(state) is true,
            "Missing received throttle leaves the actual lane sink and assist session available for manual takeover");
        Fresh();Set(plugin,"paused",true);type.GetMethod("Tick")!.Invoke(plugin,null);Thread.Sleep(300);frame(false);
        Check(gas()==0&&brake()==0&&!(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!,
            "Actual paused plugin Tick remains neutral without turning a propulsion-only suspension into fault braking");
        PedalTick(.6f,.6f);var resumed=OutputNow();
        Check(gas()==0&&brake()==0&&(bool)resumed.GetType().GetProperty("ThrottleDeliveryMissing")!.GetValue(resumed)!&&
            !(bool)resumed.GetType().GetProperty("Fault")!.GetValue(resumed)!,
            "Actual plugin pause/resume cannot silently clear the existing propulsion suspension even when received/effective gas recovers");
        // Real Tick/host regression: missing brake receipts must keep producing
        // normal commands. Run the actual 150ms producer watchdog throughout,
        // not just a pure controller assertion that could hide a replacement
        // emergency caused by stopped publication.
        Restore();Set(plugin,"speed",15d);Set(plugin,"observedAcceleration",0d);
        Set(plugin,"readLongitudinalTraffic",injectedTraffic);
        traffic=Pure("TrafficSpeedDecision",-1.1f,false,"Authored current scene brake bound",null,null);
        bool ordinaryContinues=true,steeringContinues=true;
        void ReceiptGapTick() {
            PedalPhysics(.29f,.29f);Send("LaneAssist.SteeringCorrection",0,.2f);Tick();RawStore(OwnMessage());
            Set(plugin,"monitorSteering",true);Set(plugin,"steeringMonitoringLive",true);
            Set(plugin,"requireHostCompatibility",true);
            try {type.GetMethod("CheckLongitudinalOutput",All)!.Invoke(plugin,null);}
            finally {Set(plugin,"requireHostCompatibility",false);}
            frame(false);
            ordinaryContinues&=state.GetType().GetProperty("EnableAssists")!.GetValue(state) is true&&
                !(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!;
            steeringContinues&=steering()>.19;
        }
        for(int i=0;i<35;i++){ReceiptGapTick();Thread.Sleep(50);}
        var brakeMissing=OutputNow();
        Check(ordinaryContinues&&steeringContinues&&gas()==0&&brake()>0&&brake()<1&&
            (bool)brakeMissing.GetType().GetProperty("BrakeDeliveryMissing")!.GetValue(brakeMissing)!,
            "Over1.7s of missing brake receipts preserve real Tick publication, healthy steering and the active150ms watchdog without technical fault braking");
        traffic=Pure("TrafficSpeedDecision",float.NaN,true,"Authored brake reason removed",null,null);
        ReceiptGapTick();
        Check(gas()==0&&brake()==0&&ordinaryContinues&&steeringContinues,
            "Removing the actual scene brake reason during receipt loss clears the physical legacy brake without replaying an old request");
        frame(true);Check(modernPedal()==0&&steering()>.19,
            "Receipt loss plus a now-clear scene is neutral at modern pedals while native lane steering remains available");
        traffic=Pure("TrafficSpeedDecision",-6f,false,"Authored new immediate game hazard",null,null);
        ReceiptGapTick();
        Check(gas()==0&&brake()==1&&ordinaryContinues,
            "A new current game hazard still reaches full physical braking during a delivery warning without a technical fault latch");
        traffic=Pure("TrafficSpeedDecision",float.NaN,true,"Authored immediate hazard removed",null,null);
        ReceiptGapTick();
        Check(gas()==0&&brake()==0&&ordinaryContinues,
            "The current hazard ending removes full brake immediately despite continuing receipt warnings");
        Set(plugin,"monitorSteering",false);Set(plugin,"steeringMonitoringLive",false);
        type.GetMethod("ResetLongitudinalWatchdog",All)!.Invoke(plugin,null);
        Set(plugin,"readLongitudinalTraffic",null);Set(plugin,"speed",5d);
        // The same controller still has its propulsion-suspended state. Add a
        // genuine directed closed-gate path, not a precomputed TollDecision.
        Member(curve,"EndPosition",new Vector3(0,0,-70));Member(curve,"Length",70f);Member(curve,"SemaphoreId",10);
        var tailCurve=RuntimeHelpers.GetUninitializedObject(curveType);
        foreach(var pair in new (string,object)[]{("StartPosition",new Vector3(0,0,-70)),("EndPosition",new Vector3(0,0,-500)),
            ("StartRotation",Quaternion.Identity),("EndRotation",Quaternion.Identity),("Length",430f),("SemaphoreId",-1)})Member(tailCurve,pair.Item1,pair.Item2);
        curves.Add(tailCurve);
        var semaphoresMember=descriptorType.GetProperty("Semaphores")?.PropertyType??Field(descriptorType,"Semaphores").FieldType;
        var definitions=(System.Collections.IList)Activator.CreateInstance(semaphoresMember)!;var gateDefinition=Activator.CreateInstance(semaphoresMember.GenericTypeArguments[0])!;
        Member(gateDefinition,"SemaphoreId",10u);Member(gateDefinition,"Position",new Vector3(2.5f,0,-69.6f));
        var modelType=gateDefinition.GetType().GetProperty("Type")!.PropertyType;Member(gateDefinition,"Type",Enum.Parse(modelType,"BarrierAutomatic"));definitions.Add(gateDefinition);Member(descriptor,"Semaphores",definitions);
        var live=SourceFrameReplay.EmptySignals(gameAssembly);var signalType=live.GetType().GetField("semaphores")!.FieldType.GetElementType()!;
        var gateSignal=Activator.CreateInstance(signalType)!;Member(gateSignal,"id",10);Member(gateSignal,"position",new Vector3(2.5f,0,-69.6f));Member(gateSignal,"state",1);
        var sdkType=signalType.GetField("type")!.FieldType;Member(gateSignal,"type",Enum.ToObject(sdkType,2));
        var signalList=Array.CreateInstance(signalType,1);signalList.SetValue(gateSignal,0);Set(live,"semaphores",signalList);testSignals=live;
        // Read-only path DTO references keep the actual route position and
        // direction; only the real gate metadata and segmentation changed.
        Set(plugin,"safetyTrace",matcher.GetMethod("Sample",[dataType])!.Invoke(null,[data]));
        PedalTick(0,0);var gateSnapshot=Get(plugin,"longitudinalSnapshot")!;
        var bounds=(System.Collections.IEnumerable)gateSnapshot.GetType().GetProperty("Bounds")!.GetValue(gateSnapshot)!;
        bool gateLimit=false;foreach(var bound in bounds)if((string)bound!.GetType().GetProperty("Source")!.GetValue(bound)! =="Toll"&&
            bound.GetType().GetProperty("Acceleration")!.GetValue(bound) is double boundValue&&boundValue<0)gateLimit=true;
        Check(gateLimit&&gas()==0&&brake()>0&&!(bool)Get(plugin,"faultBrake")!.GetType().GetProperty("Latched")!.GetValue(Get(plugin,"faultBrake"))!,
            "A later actual closed-gate negative bound still applies service braking after missing gas suspended propulsion");
        frame(true);Check(modernPedal()<0&&steering()>.19&&gas()==0&&brake()==0,
            "The post-suspension real gate brake reaches modern output without losing the lane sink");
        // At the actual stop line a held parking brake suppresses ordinary
        // service holds; releasing it must restore the still-closed gate brake.
        parkingPoint=new(0,0,-67.5f);LiveParking(0,true);Send("LaneAssist.SteeringCorrection",0,.2f);Tick();frame(false);
        Check(gas()==0&&brake()==0,"Actual stationary parking frame at the real closed gate is neutral rather than cycling service brake");
        ParkingTick(0,false);
        Check(gas()==0&&brake()>0,"Releasing parking at the still-closed canonical gate restores its normal service hold");
        frame(true);Check(modernPedal()<0&&steering()>.19,"The released-parking closed-gate hold also reaches modern output with lane preserved");
        LateParkingCheck(true);
        Set(plugin,"latestDeliveredTelemetry",null);Set(plugin,"incomingTelemetry",null);Set(plugin,"appliedTelemetry",null);Set(plugin,"physicsObservation",null);
        testSignals=null;Set(plugin,"physicsObservation",null);
        enable(false);type.GetMethod("StopDiagnostics",All)!.Invoke(plugin,null);
        if(Environment.GetEnvironmentVariable("MAUT_CANONICAL_FIXTURE") is {Length:>0} canonical)
            count+=CanonicalGateHostReplay.Run(canonical,Path.GetDirectoryName(sealedCandidate)!,context,pluginAssembly,gameAssembly,host,frame,gas,brake,modernPedal,assert);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(sealedCandidate)!,"parking-runtime-rows.json"),System.Text.Json.JsonSerializer.Serialize(parkingRows));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(sealedCandidate)!,"parking-release.json"),System.Text.Json.JsonSerializer.Serialize(parkingReleaseDiagnostic));
        count+=FreshHazardReplay.Run(sealedCandidate,root,pluginAssembly,gameAssembly,stateAssembly,context,host,state,frame,gas,brake,modernPedal,steering,enable,epoch,assert);
        return count;
    }
}
