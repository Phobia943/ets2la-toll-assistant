using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using B=System.Reflection.BindingFlags;
namespace MautHostCompatibility;

// Authored scheduling counterexample, not a historical driving replay. Uses
// actual owned-frame capture, signal binding, plugin Tick and protected host
// sinks. No Init/SDK/provider/output loop or live model constructor executes.
internal static class FreshHazardReplay
{
    const B All=B.Instance|B.Static|B.Public|B.NonPublic;
    static FieldInfo Field(Type t,string n) {
        for(Type? p=t;p!=null;p=p.BaseType)if(p.GetField(n,All|B.DeclaredOnly) is {} f)return f;
        throw new MissingFieldException(t.FullName,n);
    }
    static void Set(object o,string n,object? v)=>Field(o.GetType(),n).SetValue(o,v);
    static object? Get(object o,string n)=>Field(o.GetType(),n).GetValue(o);
    static void Member(object o,string n,object? v) {
        if(o.GetType().GetProperty(n,All) is {} p)p.SetValue(o,v);else Set(o,n,v);
    }
    static Func<T> Read<T>(Func<object?> f)=>()=>(T)f()!;
    static Func<int,T> ProcessReader<T>(object identity)=>_=>(T)identity;
    static Action<T> Sink<T>(Action<object> f)=>v=>f(v!);
    static Func<A,C,D,object> Geometry<A,C,D>()=>(_,_,_)=>new object();
    static Delegate Reader(Type t,Func<object?> f)=>(Delegate)typeof(FreshHazardReplay).GetMethod(nameof(Read),All)!.MakeGenericMethod(t).Invoke(null,[f])!;

    internal static int Run(string sealedCandidate,string root,Assembly pluginAssembly,Assembly gameAssembly,Assembly stateAssembly,
        AssemblyLoadContext context,object host,object state,Action<bool> frame,Func<float> gas,Func<float> brake,
        Func<float> modernPedal,Func<float> steering,Action<bool> enable,Func<long> epoch,Action<bool,string> assert)
    {
        // Reuse the existing transitive constructor audit, not a weakened
        // independent list of allowed initialization side effects.
        typeof(LongitudinalPluginReplay).GetMethod("AuditConstruction",All)!.Invoke(null,[sealedCandidate,root]);
        if(context==AssemblyLoadContext.Default||AssemblyLoadContext.GetLoadContext(pluginAssembly)!=context||
            AssemblyLoadContext.GetLoadContext(gameAssembly)!=context||AssemblyLoadContext.GetLoadContext(stateAssembly)!=context)
            throw new InvalidDataException("Fresh hazard replay requires the sealed isolated context");
        using(var module=Mono.Cecil.ModuleDefinition.ReadModule(sealedCandidate))
            if(module.Mvid!=pluginAssembly.ManifestModule.ModuleVersionId)throw new InvalidDataException("Audited candidate MVID mismatch");
        int count=0;var observations=new List<(bool Okay,string Why)>();
        void Check(bool okay,string why){observations.Add((okay,why));Console.WriteLine((okay?"PASS ":"FAIL ")+why);count++;}
        var type=pluginAssembly.GetType("MautAssistent.TollPlugin",true)!;
        object? targetPlugin=null;bool rawAcceptance=false;int outputDelayMs=0;
        var output=gameAssembly.GetType("ETS2LA.Game.Output.GameOutput",true)!;
        var eventType=gameAssembly.GetType("ETS2LA.Game.Output.ControlEvent",true)!;
        object Pure(string name,params object?[] v)=>Activator.CreateInstance(pluginAssembly.GetType("MautAssistent."+name,true)!,v)!;
        var ctor=type.GetConstructors(All).Single(c=>c.GetParameters().Any(p=>p.Name=="readState"));
        var args=ctor.GetParameters().Select(p=>p.HasDefaultValue?p.DefaultValue:null).ToArray();
        foreach(var p in ctor.GetParameters()) {
            if(p.Name=="readState")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>state);
            if(p.Name=="notify")args[p.Position]=(Action<string,string>)((_,_)=>{});
            if(p.Name=="output")args[p.Position]=typeof(FreshHazardReplay).GetMethod(nameof(Sink),All)!.MakeGenericMethod(eventType)
                .Invoke(null,[(Action<object>)(m=>{
                    if(outputDelayMs>0)Thread.Sleep(outputDelayMs);
                    output.GetMethod("OnControlEvent")!.Invoke(host,[m]);
                    // Match the production raw callback admission only for the
                    // return transaction tests below. The ordinary DI Tick's
                    // constructor/file/module gates are still bypassed.
                    if(rawAcceptance)Set(targetPlugin!,"requireHostCompatibility",true);
                })]);
            if(p.Name=="readAcc")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>throw new InvalidOperationException("Native ACC queried"));
            if(p.Name is "readPlanner" or "readLaneAssist")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>null);
            if(p.Name=="readCruiseLimits")args[p.Position]=Reader(p.ParameterType.GenericTypeArguments[0],()=>Pure("CruiseLimits",0d,false));
            if(p.Name=="writeDiagnostic")args[p.Position]=(Action<string,string>)((_,_)=>throw new InvalidOperationException("Unexpected diagnostic I/O"));
            if(p.Name=="buildStatusGeometry")args[p.Position]=typeof(FreshHazardReplay).GetMethod(nameof(Geometry),All)!
                .MakeGenericMethod(p.ParameterType.GenericTypeArguments[..3]).Invoke(null,null);
        }
        var plugin=ctor.Invoke(args);targetPlugin=plugin;type.GetMethod("StopDiagnostics",All)!.Invoke(plugin,null);Set(plugin,"vehicleGeometry",null);
        Set(plugin,"readLongitudinalTraffic",null);
        var clock=(Stopwatch)Get(plugin,"clock")!;
        var options=Pure("TollOptions");Member(options,"CurvePreviewEnabled",false);Member(options,"BriskOpenRoads",false);
        Set(plugin,"options",options);Set(plugin,"controller",Pure("TollController",options));
        Set(plugin,"lastWrite",double.MaxValue);Set(plugin,"lastMotionAt",double.MaxValue);Set(plugin,"lastMapWrite",0d);
        var parsedType=gameAssembly.GetType("ETS2LA.Game.Data.ParsedPrefab",true)!;
        var parsed=RuntimeHelpers.GetUninitializedObject(parsedType);
        var descriptorType=parsedType.GetProperty("Descriptor")?.PropertyType??Field(parsedType,"Descriptor").FieldType;
        var descriptor=Activator.CreateInstance(descriptorType)!;
        var curvesType=descriptorType.GetProperty("NavCurves")?.PropertyType??Field(descriptorType,"NavCurves").FieldType;
        var curveType=curvesType.GenericTypeArguments[0];var curves=(IList)Activator.CreateInstance(curvesType)!;
        foreach(var (a,b,id) in new[]{(0f,-300f,-1),(-300f,-320f,3),(-320f,-500f,-1)}) {
            var c=Activator.CreateInstance(curveType)!;
            foreach(var (n,v) in new (string,object)[]{("StartPosition",new Vector3(0,0,a)),("EndPosition",new Vector3(0,0,b)),
                ("StartRotation",Quaternion.Identity),("EndRotation",Quaternion.Identity),("Length",a-b),("SemaphoreId",id)})Member(c,n,v);
            var allowed=curveType.GetProperty("AllowedVehicles")?.PropertyType??Field(curveType,"AllowedVehicles").FieldType;
            Member(c,"AllowedVehicles",Enum.Parse(allowed,"AllVehicles"));curves.Add(c);
        }
        for(int i=0;i<curves.Count;i++) {
            void Add(string name,int index) {
                var list=curves[i]!.GetType().GetProperty(name)?.GetValue(curves[i])??Get(curves[i]!,name)!;
                list.GetType().GetMethod("Add",[typeof(int)])!.Invoke(list,[index]);
            }
            if(i>0)Add("PreviousLines",i-1);
            if(i+1<curves.Count)Add("NextLines",i+1);
        }
        Member(descriptor,"NavCurves",curves);Member(parsed,"Descriptor",descriptor);
        var semaphoreListType=descriptorType.GetProperty("Semaphores")?.PropertyType??Field(descriptorType,"Semaphores").FieldType;
        var semaphores=(IList)Activator.CreateInstance(semaphoreListType)!;
        var semaphore=Activator.CreateInstance(semaphoreListType.GenericTypeArguments[0])!;
        var anchor=new Vector3(3,0,-300);
        Member(semaphore,"SemaphoreId",3u);Member(semaphore,"Position",anchor);
        var profileType=semaphore.GetType().GetProperty("Profile")?.PropertyType??Field(semaphore.GetType(),"Profile").FieldType;
        Member(semaphore,"Profile",profileType==typeof(string)?"cr_1x1":Activator.CreateInstance(profileType,["cr_1x1"]));
        var semType=semaphore.GetType().GetProperty("Type")?.PropertyType??Field(semaphore.GetType(),"Type").FieldType;
        Member(semaphore,"Type",Enum.Parse(semType,"UseProfile"));semaphores.Add(semaphore);Member(descriptor,"Semaphores",semaphores);
        var prefabType=parsedType.GetProperty("Prefab")?.PropertyType??Field(parsedType,"Prefab").FieldType;
        var prefab=Activator.CreateInstance(prefabType)!;Member(prefab,"Uid",123UL);Member(parsed,"Prefab",prefab);
        var pathType=gameAssembly.GetType("ETS2LA.Game.Data.PrefabPath",true)!;
        var pathCtor=pathType.GetConstructors().Single(c=>c.GetParameters().Length==7);
        var start=Activator.CreateInstance(pathCtor.GetParameters()[1].ParameterType)!;
        var end=Activator.CreateInstance(pathCtor.GetParameters()[2].ParameterType)!;
        Member(start,"Uid",1UL);Member(end,"Uid",2UL);
        var direction=Enum.Parse(pathCtor.GetParameters()[4].ParameterType,"Forward");
        var prefabPath=pathCtor.Invoke([parsed,start,end,curves,direction,Matrix4x4.Identity,Vector3.Zero]);
        var pathLib=context.LoadFromAssemblyName(new AssemblyName("PathLib"));
        var itemType=pathLib.GetType("PathLib.PlannedItem",true)!;var item=Activator.CreateInstance(itemType)!;
        foreach(var (n,v) in new (string,object)[]{("item",parsed),("startNode",start),("endNode",end),("prefabPath",prefabPath),("direction",direction)})Set(item,n,v);
        var dataType=pathLib.GetType("PathLib.PlannedPathData",true)!;var data=Activator.CreateInstance(dataType)!;
        var items=(IList)Activator.CreateInstance(Field(dataType,"PlannedPath").FieldType)!;items.Add(item);Set(data,"PlannedPath",items);Set(data,"HasError",false);
        var trace=pluginAssembly.GetType("MautAssistent.RouteMatcher",true)!.GetMethod("Sample",[dataType])!.Invoke(null,[data])!;
        object Signals(bool ownRed=false,bool neighborRed=false) {
            var live=SourceFrameReplay.EmptySignals(gameAssembly);var t=Field(live.GetType(),"semaphores").FieldType.GetElementType()!;
            var a=Array.CreateInstance(t,neighborRed?2:1);
            for(int i=0;i<a.Length;i++) {
                var s=Activator.CreateInstance(t)!;Member(s,"id",3);Member(s,"state",i==0?(ownRed?2:8):2);
                Member(s,"position",anchor+new Vector3(i==0?0:8,0,0));
                var st=Field(t,"type").FieldType;Member(s,"type",Enum.Parse(st,"TRAFFICLIGHT"));a.SetValue(s,i);
            }
            Member(live,"semaphores",a);return live;
        }
        object Traffic(bool close=false,bool distant=false,float? at=null) {
            var live=SourceFrameReplay.EmptyTraffic(gameAssembly);
            if(close||distant) {
                var body=((Array)Get(live,"vehicles")!).GetValue(0)!;
                Member(body,"id",(short)1);Member(body,"Position",new Vector3(0,0,at??(close?-70:-150)));
                Member(body,"Rotation",new Quaternion(1,0,0,0));Member(body,"Size",new Vector3(2,4,6));
                Member(body,"speed",distant?5f:0f);
            }
            return live;
        }
        object? standaloneSignals=null;
        void Sources(string scenario) {
            SourceFrameReplay.Capture(output,"Traffic",Traffic(scenario=="same-negative-bound",scenario is "same-body-bound" or "lower-positive-bound" or "positive-bound-still-safe" or "new-negative-bound"));
            SourceFrameReplay.Capture(output,"Parked",SourceFrameReplay.EmptyParked(gameAssembly));
            var s=standaloneSignals??Signals(scenario=="known-far-red");SourceFrameReplay.Capture(output,"Semaphore",s);
            Set(plugin,"requireHostCompatibility",true);
            try{type.GetMethod("OnGates",All)!.Invoke(plugin,[s]);}finally{Set(plugin,"requireHostCompatibility",false);}
        }
        void Restore(string scenario="none") {
            enable(false);enable(true);((IDictionary)Get(host,"Channels")!).Clear();
            Set(plugin,"subscribed",true);Set(plugin,"active",true);Set(plugin,"preview",false);Set(plugin,"paused",false);
            Set(plugin,"<_IsRunning>k__BackingField",true);Set(plugin,"path",data);Set(plugin,"safetyTrace",trace);Set(plugin,"lastAcceptedPlan",data);
            Set(plugin,"position",new Vector3(0,0,-50));Set(plugin,"headingTurns",0d);Set(plugin,"speed",5d);
            Set(plugin,"supervisor",Pure("DriveSupervisor",Pure("DriveCheckpoint",(double?)0,null,null,true)));
            Set(plugin,"longitudinal",Pure("LongitudinalController"));Set(plugin,"accelerationKnown",true);
            if(scenario is "lower-positive-bound" or "new-negative-bound") {
                // Explicit authored prior controller state: the pending old
                // acceleration is actually above the replacement positive
                // bound. The sibling still-safe case starts normally and is
                // already below that same new bound.
                var control=Get(plugin,"longitudinal")!;Set(control,"at",clock.Elapsed.TotalSeconds-.05);
                Set(control,"epoch",epoch());Set(control,"accelerationCommand",scenario=="new-negative-bound"?-.02d:1.2d);Set(control,"throttle",.3f);
            }
            Set(plugin,"holdPedalFloor",0f);Set(plugin,"scenePedalFloor",0f);Set(plugin,"takeoverUntil",-100d);
            Set(plugin,"decision",pluginAssembly.GetType("MautAssistent.TollDecision",true)!.GetMethod("Idle")!.Invoke(null,["Authored clear route"]));
            state.GetType().GetProperty("DesiredSpeed")!.SetValue(state,25f);
            if(scenario=="new-negative-bound")state.GetType().GetProperty("DesiredSpeed")!.SetValue(state,(float)(5-.02/.55));
            Sources(scenario);double now=clock.Elapsed.TotalSeconds;
            foreach(string n in new[]{"telemetryAt","gatesAt","pathAt","gameTimeChangedAt","evaluatedAt","lastAcceptedPlanAt"})Set(plugin,n,now);
            Set(plugin,"sdk",true);Set(plugin,"decisionArmed",true);
        }
        // Warm exact Tick/JIT paths outside the measured commit race, then
        // recapture all original source clocks for every independent case.
        Restore();type.GetMethod("Tick")!.Invoke(plugin,null);frame(false);
        foreach(bool modern in new[]{false,true})foreach(string change in new[]{"none","same-green","neighbor-red","own-red","known-far-red","same-negative-bound","same-body-bound","lower-positive-bound","positive-bound-still-safe","new-negative-bound","new-body"}) {
            Restore(change);long before=(long)output.GetMethod("MautReadOwnSourceEpoch")!.Invoke(null,null)!;
            bool injected=false;
            Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>{
                injected=true;
                if(change=="new-negative-bound")
                    SourceFrameReplay.Capture(output,"Traffic",Traffic(distant:true,at:-(float)(50+7+16/Math.Sqrt(1+.01/1.2))));
                else if(change is "same-negative-bound" or "same-body-bound" or "lower-positive-bound" or "positive-bound-still-safe" or "new-body")
                    SourceFrameReplay.Capture(output,"Traffic",Traffic(change is "same-negative-bound" or "new-body",change is "same-body-bound" or "lower-positive-bound" or "positive-bound-still-safe",change is "lower-positive-bound" or "positive-bound-still-safe"?-100:null));
                else if(change!="none")SourceFrameReplay.Capture(output,"Semaphore",Signals(change is "own-red" or "known-far-red",change=="neighbor-red"));
            }));
            type.GetMethod("Tick")!.Invoke(plugin,null);frame(modern);
            long after=(long)output.GetMethod("MautReadOwnSourceEpoch")!.Invoke(null,null)!;
            var snapshot=Get(plugin,"longitudinalSnapshot")!;bool published=(bool)snapshot.GetType().GetProperty("Published")!.GetValue(snapshot)!;
            float physicalGas=modern?Math.Max(0,modernPedal()):gas();
            float physicalBrake=modern?Math.Max(0,-modernPedal()):brake();
            Console.WriteLine($"FRESH_HAZARD mode={(modern?"modern":"legacy")} change={change} published={published} gas={physicalGas:R} brake={physicalBrake:R} sourceEpoch={before}/{after} evidence={Get(plugin,"longitudinalTrafficEvidence")}");
            // Independently prove that the exact current model/route policy
            // binds this replacement as own red, not merely a nearby actor.
            var currentPolicy=type.GetMethod("ReadLongitudinalSurroundings",All)!.Invoke(plugin,null)!;
            bool currentClear=(bool)currentPolicy.GetType().GetProperty("Clear")!.GetValue(currentPolicy)!;
            var pendingOutput=snapshot.GetType().GetProperty("Output")!.GetValue(snapshot)!;
            double planned=(double)pendingOutput.GetType().GetProperty("TargetAcceleration")!.GetValue(pendingOutput)!;
            float currentBound=(float)currentPolicy.GetType().GetProperty("Acceleration")!.GetValue(currentPolicy)!;
            Console.WriteLine("FRESH_HAZARD plannedAcceleration="+planned+" currentPolicy="+currentPolicy);
            Check(currentClear==(change is "none" or "same-green" or "neighbor-red"),"Actual current route policy distinguishes own danger from unchanged/neighbor signals: "+change);
            Check(injected&&before==after,"Fresh valid replacement keeps the source epoch: "+change);
            bool expected=change is "own-red" or "new-body" or "lower-positive-bound" or "new-negative-bound"?physicalGas==0:
                change=="same-negative-bound"?physicalGas==0&&physicalBrake>0:physicalGas>0;
            if(change=="lower-positive-bound")expected&=currentBound>0&&currentBound<planned;
            if(change=="positive-bound-still-safe")expected&=currentBound>planned&&planned>0;
            if(change=="new-negative-bound")expected&=planned<currentBound&&currentBound<0;
            Check(expected,
                "Final actual host gas respects only newly bound own danger, without starving harmless16ms frames: "+change+"/"+(modern?"modern":"legacy"));
        }
        Set(plugin,"BeforeLongitudinalCommit",null);
        // Full pending-return transaction through the actual protected host.
        // Prior focus observations are authored; current Tick, raw host stamps,
        // source admission, both physical sinks and post-callback checks run.
        var contextType=pluginAssembly.GetType("MautAssistent.GameInputContext",true)!;
        var identityType=pluginAssembly.GetType("MautAssistent.GameProcessIdentity",true)!;
        string image=Path.Combine(Path.GetTempPath(),"maut-isolated-focus","eurotrucks2.exe");int foreground=42;Action? duringFocus=null;
        var identity=Activator.CreateInstance(identityType,[42,"eurotrucks2",image,(Func<bool>)(()=>true),null])!;
        var processReader=typeof(FreshHazardReplay).GetMethod(nameof(ProcessReader),All)!.MakeGenericMethod(identityType).Invoke(null,[identity]);
        var inputContext=contextType.GetConstructors(All).Single().Invoke([new[]{image},processReader,
            (Func<nint>)(()=>1),(Func<nint,int>)(_=>{duringFocus?.Invoke();return foreground;}),(Func<long>)Stopwatch.GetTimestamp]);
        Check((bool)contextType.GetMethod("RefreshProcessIdentity",All)!.Invoke(inputContext,[42])!,
            "Focus host replay verifies the exact authored process outside Tick");
        Set(plugin,"inputContext",inputContext);Set(plugin,"inputGamePid",42);
        var physicsType=type.GetNestedType("PhysicsObservation",B.NonPublic)!;
        var telemetryType=type.GetNestedType("TelemetryFrame",B.NonPublic)!;
        ulong simulated=1_000_000;
        void Physics() {
            simulated+=50_000;
            var physics=Activator.CreateInstance(physicsType,[simulated,simulated,0f,0f,0f,0f,0f,0f,0f])!;
            var telemetry=Activator.CreateInstance(telemetryType,[clock.Elapsed.TotalSeconds,simulated,true,false,5d,new Vector3(0,0,-50),0d,physics])!;
            telemetryType.GetProperty("Origin")!.SetValue(telemetry,Stopwatch.GetTimestamp());
            Set(plugin,"incomingTelemetry",telemetry);Set(plugin,"latestDeliveredTelemetry",telemetry);
            type.GetMethod("DrainInputFrames",All)!.Invoke(plugin,null);
        }
        void CurrentTick(bool mode) {
            Sources("none");Physics();double now=clock.Elapsed.TotalSeconds;
            foreach(string n in new[]{"telemetryAt","gatesAt","pathAt","gameTimeChangedAt","evaluatedAt","lastAcceptedPlanAt"})Set(plugin,n,now);
            try{type.GetMethod("Tick")!.Invoke(plugin,null);}finally{Set(plugin,"requireHostCompatibility",false);}
            frame(mode);
        }
        object Core()=>Get(plugin,"longitudinal")!;
        bool Flag(string name)=>(bool)Core().GetType().GetProperty(name)!.GetValue(Core())!;
        void SeedReturn() {
            var recovery=Pure("PedalFocusRecovery");long now=Stopwatch.GetTimestamp();
            long source=(long)output.GetMethod("MautReadOwnSourceEpoch")!.Invoke(null,null)!;
            object Sample(double ago,bool front,ulong index) {
                long stamp=now-(long)(ago*Stopwatch.Frequency);
                return Pure("PedalFocusSample",stamp,Stopwatch.Frequency,stamp,stamp,source,42,identity,front,index,index,true);
            }
            for(int i=0;i<8;i++)recovery.GetType().GetMethod("Observe")!.Invoke(recovery,[true,true,false,.05,Sample(.7-i*.05,false,(ulong)i+1)]);
            for(int i=0;i<5;i++)recovery.GetType().GetMethod("BeginStep")!.Invoke(recovery,[Sample(.25-i*.05,true,(ulong)i+9)]);
            Check((bool)recovery.GetType().GetProperty("ReturnPending")!.GetValue(recovery)!,"Authored prior focus history reaches a pending retry, not an input receipt");
            Set(Core(),"focusRecovery",recovery);
        }
        rawAcceptance=true;
        foreach(bool mode in new[]{false,true}) {
            Restore();Physics();Thread.Sleep(5);Physics();bool zero=true,noPhantom=true;int neutralCommits=0;
            Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>foreground=99));
            for(int i=0;i<28;i++) {
                foreground=42;Thread.Sleep(15);CurrentTick(mode);
                var sample=Get(plugin,"longitudinalSnapshot")!;var command=sample.GetType().GetProperty("Output")!.GetValue(sample)!;
                bool published=(bool)sample.GetType().GetProperty("Published")!.GetValue(sample)!;
                bool missing=(bool)command.GetType().GetProperty("DeliveryMissing")!.GetValue(command)!;
                bool hasFocus=sample.GetType().GetProperty("Input")!.GetValue(sample)!.GetType().GetProperty("Focus")!
                    .GetValue(sample.GetType().GetProperty("Input")!.GetValue(sample))!=null;
                if(published)neutralCommits++;
                zero &=hasFocus&&Get(plugin,"longitudinalSourceLease")!=null&&(mode?modernPedal():gas())==0;
                noPhantom &=!missing;
            }
            Check(zero&&neutralCommits>0,"Repeated actual raw-source/context/host focus vetoes deliver only neutral gas in "+(mode?"modern":"legacy"));
            Check(noPhantom,"Repeated actually neutralized host commands create no phantom missing receipt in "+(mode?"modern":"legacy"));
            foreground=42;Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>Thread.Sleep(225)));CurrentTick(mode);
            var expired=Get(plugin,"longitudinalSnapshot")!;
            Check(!(bool)expired.GetType().GetProperty("Published")!.GetValue(expired)!,
                "An actually expired original source/command cannot publish at the protected host in "+(mode?"modern":"legacy"));
            Check((float)Get(Core(),"throttle")! ==0,"A fully discarded actual host Tick leaves no phantom expected positive gas in "+(mode?"modern":"legacy"));
            Set(plugin,"BeforeLongitudinalCommit",null);
            Restore();Physics();Thread.Sleep(5);Physics();SeedReturn();foreground=42;
            Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>foreground=99));CurrentTick(mode);
            Check(Flag("FocusSuspended")&&!Flag("FocusReturnPending")&&(mode?modernPedal():gas())==0,
                "Actual host final focus veto revokes pending return immediately in "+(mode?"modern":"legacy"));
            foreground=42;Set(plugin,"BeforeLongitudinalCommit",null);Thread.Sleep(20);CurrentTick(mode);
            Check(Flag("FocusSuspended")&&(mode?modernPedal():gas())==0,
                "Actual following foreground Tick cannot borrow pre-veto return stability in "+(mode?"modern":"legacy"));
            for(int i=0;i<12;i++){Thread.Sleep(25);CurrentTick(mode);}
            Check(!Flag("FocusSuspended")&&!Flag("FocusReturnPending")&&(mode?modernPedal():gas())>0,
                "Fresh stable return finishes only after an actual current raw-stamped host publication in "+(mode?"modern":"legacy"));
            SeedReturn();outputDelayMs=170;CurrentTick(mode);outputDelayMs=0;
            Check(Flag("FocusSuspended")&&!(mode?modernPedal():gas()).Equals(float.NaN)&&(mode?modernPedal():gas())==0,
                "A170ms actual output callback wait cannot acknowledge expired return authority in "+(mode?"modern":"legacy"));
        }
        if(Environment.GetEnvironmentVariable("MAUT_MAP_GATE_FIXTURE") is {Length:>0} gateFixture) {
            object worker=MapGateEndCommitReplay.Create(gateFixture,Path.GetDirectoryName(sealedCandidate)!,context,pluginAssembly,new Vector3(0,0,-50));
            var workerType=worker.GetType();
            Set(plugin,"readRigObservation",Reader(Field(type,"readRigObservation").FieldType.GenericTypeArguments[0],()=>null));
            foreach(bool mode in new[]{false,true})foreach(bool reset in new[]{false,true}) {
                worker=workerType.GetConstructors().Single().Invoke([new Vector3(0,0,-50)]);
                Set(plugin,"staticRoadWorker",worker);foreground=42;duringFocus=null;Set(plugin,"BeforeLongitudinalCommit",null);
                var s=Signals();var st=Field(s.GetType(),"semaphores").FieldType.GetElementType()!;
                var signal=Activator.CreateInstance(st)!;Member(signal,"id",0);Member(signal,"state",3);
                Member(signal,"position",new Vector3(3,0,-70));Member(signal,"rotation",new Quaternion(1,0,0,0));
                Member(signal,"type",Enum.Parse(Field(st,"type").FieldType,"GATE"));
                var both=Array.CreateInstance(st,2);both.SetValue(((Array)Get(s,"semaphores")!).GetValue(0),0);both.SetValue(signal,1);
                Member(s,"semaphores",both);standaloneSignals=s;
                Restore();Physics();Thread.Sleep(5);Physics();
                Set(plugin,"BeforeLongitudinalCommit",(Action)(()=>Set(worker,"Armed",reset)));
                duringFocus=()=>{
                    if((bool)Get(worker,"Armed")!&&(bool)Get(worker,"FinalProviderSeen")!&&!(bool)Get(worker,"ResetObserved")!)
                        workerType.GetMethod("Reset")!.Invoke(worker,null);
                };
                CurrentTick(mode);
                var sourceLease=Get(plugin,"longitudinalSourceLease");
                var gateLease=sourceLease?.GetType().GetProperty("MapGate",All)?.GetValue(sourceLease);
                var snapshot=Get(plugin,"longitudinalSnapshot")!;
                bool published=(bool)snapshot.GetType().GetProperty("Published")!.GetValue(snapshot)!;
                float actual=mode?Math.Max(0,modernPedal()):gas();
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{mapGateEndCommit=true,mode=mode?"modern":"legacy",reset,
                    published,physicalGas=actual,sourceLease=sourceLease!=null,mapGateLease=gateLease!=null,
                    providerChecks=Get(worker,"ProviderChecks"),finalProviderSeen=Get(worker,"FinalProviderSeen"),resetObserved=Get(worker,"ResetObserved")}));
                Check(sourceLease!=null&&gateLease!=null,"Actual owned-frame read and Tick retain a standalone MapGate source lease in "+(mode?"modern":"legacy"));
                Check(reset?(bool)Get(worker,"FinalProviderSeen")!&&(bool)Get(worker,"ResetObserved")!:!(bool)Get(worker,"ResetObserved")!,
                    "Gate worker reset is staged only inside final actual focus capture after provider validation: "+reset);
                Check(reset?!published&&actual==0:published&&actual>0,
                    "Actual standalone open gate permits gas, while final local source reset revokes both physical sinks: "+reset+"/"+(mode?"modern":"legacy"));
                duringFocus=null;standaloneSignals=null;Set(plugin,"staticRoadWorker",null);
            }
        }
        rawAcceptance=false;((IDisposable)inputContext).Dispose();Set(plugin,"BeforeLongitudinalCommit",null);enable(false);
        foreach(var observation in observations)assert(observation.Okay,observation.Why);
        return count;
    }
}
