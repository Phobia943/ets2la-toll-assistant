using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using B=System.Reflection.BindingFlags;

namespace MautHostCompatibility;

// Optional integration at the genuine plugin COMMIT boundary. The input floor
// fields are controlled fixtures; deriving scene/branch observations is tested
// elsewhere. No driving algorithm, product instance constructor, timer or SDK
// loop is started. The audited static initializer may create its harmless SDK DTO.
internal static class PluginHoldReplay
{
    const B All=B.Instance|B.Static|B.Public|B.NonPublic;
    static FieldInfo Field(Type type,string name)
    {
        for(Type? current=type;current!=null;current=current.BaseType)
            if(current.GetField(name,All|B.DeclaredOnly) is { } field)return field;
        throw new MissingFieldException(type.FullName,name);
    }
    static void Set(object value,string name,object? field)=>Field(value.GetType(),name).SetValue(value,field);
    static object? Get(object value,string name)=>Field(value.GetType(),name).GetValue(value);
    static Func<T> Reader<T>(object state)=>()=>(T)state;
    static Action<T> Sink<T>(Action<object> output)=>value=>output(value!);

    static void Audit(string candidate,string root,string prepared)
    {
        using var module=ModuleDefinition.ReadModule(candidate);
        if(module.GetType("<Module>").Methods.Any(m=>m.IsConstructor))throw new InvalidDataException("Unaudited plugin module initializer");
        var plugin=module.GetType("MautAssistent.TollPlugin")??throw new InvalidDataException("Plugin type missing");
        foreach(string pure in new[]{"FaultBrake","SceneStopHold","SceneHoldBrake","PlannedHoldBrake","SteeringFeedGuard"})
            if(module.GetType("MautAssistent."+pure).Methods.Any(m=>m.IsConstructor&&m.IsStatic))
                throw new InvalidDataException("Unaudited hold/fault static initializer: "+pure);
        foreach(string name in new[]{"CommitPlannedHolds","ClearSceneHold","PublishPlannedHold","SensorsFresh"})
            _=Rewrite.Method(plugin,name);
        var manifest=module.Resources.OfType<EmbeddedResource>().Single(r=>r.Name=="MautAssistent.HostCompatibility.json");
        if(System.Text.Encoding.UTF8.GetString(manifest.GetResourceData())!=File.ReadAllText(Path.Combine(prepared,"compatibility.json")))
            throw new InvalidDataException("Plugin is not sealed to this prepared host manifest");
        string[] allowed={
            "System.Void ETS2LA.Controls.ControlDefinition::.ctor()",
            "System.Void ETS2LA.Controls.ControlDefinition::set_Type(ETS2LA.Controls.ControlType)",
            "System.Void System.Text.Json.JsonSerializerOptions::.ctor()",
            "System.Void System.Text.Json.JsonSerializerOptions::set_WriteIndented(System.Boolean)",
            "System.Void System.Text.Json.JsonSerializerOptions::set_NumberHandling(System.Text.Json.Serialization.JsonNumberHandling)",
            "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)",
            "System.Reflection.FieldInfo System.Type::GetField(System.String)",
            "System.Reflection.MethodInfo System.Type::GetMethod(System.String)"};
        var cctor=Rewrite.Method(plugin,".cctor");
        foreach(var instruction in cctor.Body.Instructions) {
            if(instruction.Operand is MethodReference call&&!allowed.Contains(call.FullName,StringComparer.Ordinal))
                throw new InvalidDataException("Unaudited plugin static initialization call: "+call.FullName);
            if(instruction.Operand is FieldReference field&&field.DeclaringType.FullName!=plugin.FullName&&field.DeclaringType.FullName!="ETS2LA.Controls.ControlDefinition")
                throw new InvalidDataException("Unaudited static initializer field: "+field.FullName);
            if(instruction.OpCode.Code is Code.Calli or Code.Jmp or Code.Ldftn or Code.Ldvirtftn)
                throw new InvalidDataException("Indirect static initialization is forbidden");
        }
        foreach(string assembly in new[]{"ETS2LA.Controls","ETS2LA.Shared"}) {
            using var dependency=ModuleDefinition.ReadModule(Path.Combine(root,assembly+".dll"));
            if(dependency.GetType("<Module>").Methods.Any(m=>m.IsConstructor))throw new InvalidDataException("Unaudited SDK module initializer");
            string type=assembly=="ETS2LA.Controls"?"ETS2LA.Controls.ControlDefinition":"ETS2LA.Shared.Plugin";
            var definition=dependency.GetType(type);
            if(definition.Methods.Any(m=>m.IsConstructor&&m.IsStatic))throw new InvalidDataException("Unaudited SDK base/DTO static initializer");
            if(assembly=="ETS2LA.Controls")foreach(var method in definition.Methods.Where(m=>m.IsConstructor||m.Name=="set_Type"))
                if(method.Body.Instructions.Any(i=>i.Operand is MethodReference called&&called.FullName!="System.Void System.Object::.ctor()"))
                    throw new InvalidDataException("The audited ControlDefinition DTO now calls external code");
        }
    }

    internal static void Run(string candidate,string root,string prepared,string sandbox,AssemblyLoadContext context,
        Assembly gameAssembly,Assembly stateAssembly,object host,object state,Action<bool> frame,Func<float> gas,Func<float> brake,
        Action<bool> enable,Func<long> epoch,Action<bool,string> assert,Func<float> modernPedal)
    {
        candidate=Path.GetFullPath(candidate);
        var copy=Path.Combine(sandbox,"MautAssistent.dll");File.Copy(candidate,copy,false);
        // Candidate builds may continue in parallel. Audit and execute exactly
        // one copied snapshot, held read-only against replacement throughout.
        using var sealedFile=new FileStream(copy,FileMode.Open,FileAccess.Read,FileShare.Read);
        string sealedHash=Convert.ToHexString(SHA256.HashData(sealedFile));
        Audit(copy,root,prepared);
        if(AssemblyLoadContext.GetLoadContext(gameAssembly)!=context||AssemblyLoadContext.GetLoadContext(stateAssembly)!=context||
            context==AssemblyLoadContext.Default)throw new InvalidDataException("Plugin replay requires the existing isolated host context");
        foreach(string dependency in new[]{"ETS2LA.Controls","ETS2LA.Shared"}) {
            var loaded=context.LoadFromAssemblyName(new AssemblyName(dependency));
            using var audited=ModuleDefinition.ReadModule(Path.Combine(root,dependency+".dll"));
            if(AssemblyLoadContext.GetLoadContext(loaded)!=context||loaded.ManifestModule.ModuleVersionId!=audited.Mvid)
                throw new InvalidDataException("Plugin resolved an unaudited or non-isolated SDK dependency: "+dependency);
        }
        var assembly=context.LoadFromAssemblyPath(copy);
        if(AssemblyLoadContext.GetLoadContext(assembly)!=context)throw new InvalidDataException("Candidate escaped isolated context");
        var type=assembly.GetType("MautAssistent.TollPlugin",true)!;
        var output=gameAssembly.GetType("ETS2LA.Game.Output.GameOutput",true)!;
        var definitionType=gameAssembly.GetType("ETS2LA.Game.Output.ControlChannelDefinition",true)!;
        var variablesType=gameAssembly.GetType("ETS2LA.Game.Output.ControlVariables",true)!;
        var propertiesType=gameAssembly.GetType("ETS2LA.Game.Output.ControlProperties",true)!;
        var eventType=gameAssembly.GetType("ETS2LA.Game.Output.ControlEvent",true)!;
        using(var pinned=ModuleDefinition.ReadModule(Path.Combine(prepared,"ETS2LA.Game.dll")))
            if(pinned.Mvid!=gameAssembly.ManifestModule.ModuleVersionId)throw new InvalidDataException("Wrong isolated host module identity");
        using(var pinned=ModuleDefinition.ReadModule(copy))
            if(pinned.Mvid!=assembly.ManifestModule.ModuleVersionId)throw new InvalidDataException("Wrong candidate module identity");
        if((string?)output.GetField("MautCompatibility")!.GetRawConstantValue()!="0.4.0-host-1")throw new InvalidDataException("Plugin replay requires the exclusive longitudinal host revision");

        // Do not invoke any product constructor. Build precisely the fields
        // used by the real Commit/Clear and FaultBrake.RunCurrent methods.
        object NewCore(string name)
        {
            var t=assembly.GetType("MautAssistent."+name,true)!;var value=RuntimeHelpers.GetUninitializedObject(t);
            foreach(var field in t.GetFields(All).Where(f=>!f.IsStatic&&f.FieldType.Assembly==assembly&&
                f.FieldType.Name is "PlannedHoldBrake" or "SceneHoldBrake"))field.SetValue(value,NewCore(field.FieldType.Name));
            t.GetMethod("Reset")!.Invoke(value,null);return value;
        }
        var plugin=RuntimeHelpers.GetUninitializedObject(type);var clock=Stopwatch.StartNew();Set(plugin,"clock",clock);
        var reader=typeof(PluginHoldReplay).GetMethod(nameof(Reader),All)!.MakeGenericMethod(state.GetType()).Invoke(null,[state]);
        Set(plugin,"readState",reader);
        Set(plugin,"<_IsRunning>k__BackingField",true);
        Set(plugin,"holdBrake",NewCore("PlannedHoldBrake"));Set(plugin,"sceneHold",NewCore("SceneStopHold"));
        var faultType=assembly.GetType("MautAssistent.FaultBrake",true)!;var fault=RuntimeHelpers.GetUninitializedObject(faultType);
        Set(fault,"sync",new object());Set(fault,"operation",new AsyncLocal<long?>());
        Action<object> accept=message=>output.GetMethod("OnControlEvent")!.Invoke(host,[message]);
        var sink=typeof(PluginHoldReplay).GetMethod(nameof(Sink),All)!.MakeGenericMethod(eventType).Invoke(null,[accept]);
        var publish=faultType.GetFields(All).Single(f=>f.FieldType.IsGenericType&&f.FieldType.GetGenericTypeDefinition()==typeof(Action<>));
        publish.SetValue(fault,sink);Set(plugin,"faultBrake",fault);
        var bound=(MethodInfo?)type.GetField("setPlannedHold",All)!.GetValue(null);
        if(bound?.DeclaringType!=output||bound.GetParameters().Length!=4||AssemblyLoadContext.GetLoadContext(bound.Module.Assembly)!=context)
            throw new InvalidDataException("Plugin reflected a non-isolated or wrong native hold method");
        var capture=(MethodInfo?)type.GetField("captureOutputEpoch",All)!.GetValue(null);
        if(capture?.DeclaringType!=output)throw new InvalidDataException("Plugin captured a foreign native epoch");
        foreach(string name in new[]{"CommitPlannedHolds","PublishPlannedHold","ClearSceneHold","SensorsFresh","get_State"})
            RuntimeHelpers.PrepareMethod(type.GetMethod(name,All)!.MethodHandle);

        void Call(string name,params object?[] args)=>type.GetMethod(name,All)!.Invoke(plugin,args);
        void Fresh()
        {
            double now=clock.Elapsed.TotalSeconds;
            foreach(string name in new[]{"telemetryAt","gatesAt","gameTimeChangedAt","pathAt"})Set(plugin,name,now);
            foreach(string name in new[]{"active","sdk"})Set(plugin,name,true);
            foreach(string name in new[]{"preview","paused","brakeHeld"})Set(plugin,name,false);
            Set(plugin,"<_IsRunning>k__BackingField",true);Set(plugin,"takeoverUntil",-100d);
            Set(plugin,"driveAction",Enum.Parse(Field(type,"driveAction").FieldType,"Drive"));
        }
        void Commit(long? origin=null,long? issued=null)=>Call("CommitPlannedHolds",origin??epoch(),issued??Stopwatch.GetTimestamp());
        void Floors(float branch,float scene){Set(plugin,"holdPedalFloor",branch);Set(plugin,"scenePedalFloor",scene);}
        void ClearChannels()=>((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
        void Reset()
        {
            enable(false);enable(true);Fresh();ClearChannels();Floors(0,0);Commit();frame(false);
        }
        void OwnBrake(float value)
        {
            var definition=Activator.CreateInstance(definitionType)!;Set(definition,"Id","local.mautassistent.longitudinal");
            Set(definition,"Timeout",1f);Set(definition,"MautEpoch",epoch());Set(definition,"MautIssuedAt",Stopwatch.GetTimestamp());Set(definition,"MautSourceEpoch",output.GetMethod("MautReadOwnSourceEpoch")!.Invoke(null,null));
            var variables=Activator.CreateInstance(variablesType)!;Set(variables,"abackward",-value);
            var properties=Activator.CreateInstance(propertiesType)!;Set(properties,"Weight",1f);
            var message=Activator.CreateInstance(eventType)!;Set(message,"ChannelDefinition",definition);Set(message,"Variables",variables);Set(message,"Properties",properties);
            accept(message);
        }

        int checks=0;
        void Check(bool okay,string message){assert(okay,"Real plugin commit: "+message);checks++;}
        Reset();Floors(.65f,.35f);Commit();frame(false);
        Check(Math.Abs(brake()-.65)<.001&&gas()==0,"branch and scene combine to the stronger .65 physical brake floor");
        Call("ClearSceneHold");Fresh();Commit();frame(false);
        Check(Math.Abs(brake()-.65)<.001&&gas()==0,"clearing the scene cannot release a still-active .65 branch hold");
        frame(true);Check(Math.Abs(modernPedal()+.65)<.001&&brake()==0&&gas()==0,"the same product commit reaches the modern sink and neutralizes legacy pedals");
        Reset();Floors(0,.35f);Commit();frame(false);
        Check(Math.Abs(brake()-.35)<.001&&gas()==0,"resolving a branch preserves a .35 scene/mapped/proof floor supplied at the commit boundary");
        Call("ClearSceneHold");Fresh();Commit();frame(false);
        Check(brake()==0&&gas()==0,"clearing the final scene withdraws the last physical floor");

        Reset();Floors(.65f,.35f);Commit();faultType.GetMethod("Trip")!.Invoke(fault,null);frame(false);
        Check(brake()==1&&gas()==0,"an actual FaultBrake publication remains stronger than the planned floor");
        faultType.GetMethod("Release")!.Invoke(fault,null);
        Reset();Floors(.65f,0);Commit();OwnBrake(1);frame(false);
        Check(brake()==1&&gas()==0,"a genuine own-channel full brake is not diluted by the combined plugin floor");

        Reset();Floors(.65f,.35f);Commit();frame(false);
        Check(brake()>.64,"F9 regression begins with an established product floor");
        long oldEpoch=epoch(),oldIssue=Stopwatch.GetTimestamp();
        enable(false);Commit(oldEpoch,oldIssue);frame(false);
        Check(brake()==0&&gas()==0,"F9 before a late root commit denies its old-epoch floor");
        Reset();long sameEpoch=epoch();long clearOrigin=(long)output.GetField("MautHoldIssuedAt",All)!.GetValue(null)!;
        oldIssue=Stopwatch.GetTimestamp();Thread.Sleep(230);Fresh();Floors(.65f,0);Commit(sameEpoch,oldIssue);frame(false);
        Check(epoch()==sameEpoch&&brake()==0&&gas()==0&&(long)output.GetField("MautHoldIssuedAt",All)!.GetValue(null)! == clearOrigin,
            "a same-session calculation blocked for 230 ms cannot establish or refresh a floor despite freshly observed current sensors");

        foreach(string invalid in new[]{"paused","telemetryAt","gatesAt","gameTimeChangedAt","pathAt","assists-off","not-running","preview","manual-brake"}) {
            Reset();Floors(.65f,.35f);Commit();frame(false);
            Check(brake()>.64,"invalid-state regression begins from a real established physical hold: "+invalid);
            Fresh();
            switch(invalid) {
                case "paused":Set(plugin,"paused",true);break;
                case "telemetryAt":Set(plugin,"telemetryAt",clock.Elapsed.TotalSeconds-2);break;
                case "gatesAt":Set(plugin,"gatesAt",clock.Elapsed.TotalSeconds-2);break;
                case "gameTimeChangedAt":Set(plugin,"gameTimeChangedAt",clock.Elapsed.TotalSeconds-2);break;
                case "pathAt":Set(plugin,"pathAt",clock.Elapsed.TotalSeconds-2);break;
                case "assists-off":Set(state,"<EnableAssists>k__BackingField",false);break;
                case "not-running":Set(plugin,"<_IsRunning>k__BackingField",false);break;
                case "preview":Set(plugin,"preview",true);break;
                case "manual-brake":Set(plugin,"brakeHeld",true);break;
            }
            // The host still permits this epoch, so these fail if the PRODUCT
            // condition is missing; MautAssists=false cannot mask the result.
            Check((bool)output.GetField("MautAssists")!.GetValue(null)!,"host authority remains enabled while only the product state is invalid: "+invalid);
            Commit();frame(false);
            Check(brake()==0&&gas()==0,"product state withdraws its physical floor: "+invalid);
            if(invalid=="assists-off")Set(state,"<EnableAssists>k__BackingField",true);
        }
        // Exercise the real raw-origin observer/store against the REAL isolated
        // Host3 epoch source. No timer, native lane Tick or live output is run.
        Reset();Set(plugin,"requireHostCompatibility",true);Set(plugin,"monitorSteering",true);
        Set(plugin,"steeringSync",new object());Set(plugin,"steeringGuard",NewCore("SteeringFeedGuard"));
        Set(plugin,"steeringMonitoringLive",true);Set(plugin,"lastSteeringCommand",null);
        foreach(string name in new[]{"OnControlOutput","StoreSteering","CheckSteeringOutput","SteeringCommandAge"})
            RuntimeHelpers.PrepareMethod(type.GetMethod(name,All)!.MethodHandle);
        object NativeSteering(long origin,long issued,float value=.12f)
        {
            var definition=Activator.CreateInstance(definitionType)!;Set(definition,"Id","LaneAssist.SteeringCorrection");
            Set(definition,"Timeout",1f);Set(definition,"MautEpoch",origin);Set(definition,"MautIssuedAt",issued);
            var variables=Activator.CreateInstance(variablesType)!;Set(variables,"steering",value);
            var message=Activator.CreateInstance(eventType)!;Set(message,"ChannelDefinition",definition);
            Set(message,"Variables",variables);Set(message,"Properties",Activator.CreateInstance(propertiesType));return message;
        }
        void ObserveSteering(long origin,long issued)=>Call("OnControlOutput",NativeSteering(origin,issued));
        string Feed(){Call("CheckSteeringOutput");return Get(plugin,"steeringFeedState")!.ToString()!;}
        var clone=typeof(object).GetMethod("MemberwiseClone",All)!;
        object CloneCommand(object source,long origin,long issued,double localAt)
        {
            var copyCommand=clone.Invoke(source,null)!;var commandType=copyCommand.GetType();
            commandType.GetProperty("Epoch")!.SetValue(copyCommand,origin);
            commandType.GetProperty("IssuedTicks")!.SetValue(copyCommand,issued);
            commandType.GetProperty("At")!.SetValue(copyCommand,localAt);return copyCommand;
        }
        int steeringOriginChecks=0;
        void SteeringCheck(bool okay,string why){Check(okay,"steering origin: "+why);steeringOriginChecks++;}
        long firstSteeringEpoch=epoch(),firstSteeringOrigin=Stopwatch.GetTimestamp();
        ObserveSteering(firstSteeringEpoch,firstSteeringOrigin);
        var firstCommand=Get(plugin,"lastSteeringCommand");
        SteeringCheck(firstCommand!=null&&
            (long?)firstCommand.GetType().GetProperty("IssuedTicks")!.GetValue(firstCommand)==firstSteeringOrigin&&
            (long?)firstCommand.GetType().GetProperty("Epoch")!.GetValue(firstCommand)==firstSteeringEpoch,
            "the current verified Host3 epoch and raw producer ticks are retained by the genuine observer");
        SteeringCheck(Feed()=="Healthy","a genuine current-epoch fresh command makes the watchdog healthy");
        enable(false);SteeringCheck(Feed()=="Off","native disable ends the old watchdog session");
        enable(true);long secondSteeringEpoch=epoch();
        SteeringCheck(secondSteeringEpoch!=firstSteeringEpoch,"the real F9/F8 state transition changes the isolated Host3 epoch");
        // Intentionally retain the old record: this tests the guard's epoch
        // defense independently of Cancel/Stop clearing the normal lifecycle.
        SteeringCheck(ReferenceEquals(firstCommand,Get(plugin,"lastSteeringCommand"))&&Feed()=="Starting",
            "an already stored fresh E1 command cannot make E2 healthy before its first own output");
        var delayedOld=CloneCommand(firstCommand!,firstSteeringEpoch,Stopwatch.GetTimestamp(),clock.Elapsed.TotalSeconds+1000);
        Call("StoreSteering",delayedOld);
        SteeringCheck(ReferenceEquals(firstCommand,Get(plugin,"lastSteeringCommand"))&&Feed()=="Starting",
            "a callback validated before F9 but committed after F8 cannot store its now-foreign epoch");
        ObserveSteering(firstSteeringEpoch,Stopwatch.GetTimestamp());
        SteeringCheck(ReferenceEquals(firstCommand,Get(plugin,"lastSteeringCommand")),
            "outer observer admission also rejects a late old-epoch callback");
        long secondSteeringOrigin=Stopwatch.GetTimestamp();ObserveSteering(secondSteeringEpoch,secondSteeringOrigin);
        var secondCommand=Get(plugin,"lastSteeringCommand");
        SteeringCheck(secondCommand!=null&&!ReferenceEquals(secondCommand,firstCommand)&&Feed()=="Healthy",
            "a new E2 command is accepted and restores healthy monitoring without borrowing E1");
        foreach(long invalidOrigin in new[]{0L,-1L,Stopwatch.GetTimestamp()-Stopwatch.Frequency,
            Stopwatch.GetTimestamp()+Stopwatch.Frequency,long.MaxValue}) {
            ObserveSteering(secondSteeringEpoch,invalidOrigin);
            SteeringCheck(ReferenceEquals(secondCommand,Get(plugin,"lastSteeringCommand")),
                "outer admission rejects zero/negative/expired/future producer stamps without renewing the current command: "+invalidOrigin);
            var delayedInvalid=CloneCommand(secondCommand!,secondSteeringEpoch,invalidOrigin,clock.Elapsed.TotalSeconds+1000);
            Call("StoreSteering",delayedInvalid);
            SteeringCheck(ReferenceEquals(secondCommand,Get(plugin,"lastSteeringCommand")),
                "the atomic store rechecks invalid producer stamps even after earlier callback validation: "+invalidOrigin);
        }
        var oldRawNewLocal=CloneCommand(secondCommand!,secondSteeringEpoch,secondSteeringOrigin-1,clock.Elapsed.TotalSeconds+1000);
        Call("StoreSteering",oldRawNewLocal);
        SteeringCheck(ReferenceEquals(secondCommand,Get(plugin,"lastSteeringCommand")),
            "a larger local receipt timestamp cannot outrank a smaller raw producer origin");
        // Deterministically block the ACTUAL epoch getter, not a fixture
        // delegate. A140ms origin crosses the150ms watchdog deadline while the
        // same epoch's host gate is held for30ms. Never renew its origin.
        long waitEpoch=epoch(),waitOrigin=Stopwatch.GetTimestamp()-Stopwatch.Frequency*14/100;
        var waitCommand=CloneCommand(secondCommand!,waitEpoch,waitOrigin,clock.Elapsed.TotalSeconds-.14);
        Set(plugin,"lastSteeringCommand",null);Call("StoreSteering",waitCommand);
        SteeringCheck(ReferenceEquals(waitCommand,Get(plugin,"lastSteeringCommand")),
            "the wait regression begins with a genuinely admitted current-epoch raw command");
        var hostGate=output.GetField("MautGate",All)!.GetValue(null)!;
        var ageMethod=type.GetMethod("SteeringCommandAge",All)!;
        long releaseTicks=0,probeTicks=0;double probeNow=0;double? probeAge=null;Exception? probeFailure=null;
        var waitThread=new Thread(()=> {
            try {
                // Retain the prior API branch solely to demonstrate the
                // regression against a sealed older candidate, without edits.
                if(ageMethod.GetParameters().Length==1) {
                    var result=ageMethod.Invoke(plugin,[waitCommand])!;var resultType=result.GetType();
                    probeNow=(double)resultType.GetField("Item1")!.GetValue(result)!;
                    probeTicks=(long)resultType.GetField("Item2")!.GetValue(result)!;
                    probeAge=(double?)resultType.GetField("Item3")!.GetValue(result);
                } else {
                    probeNow=clock.Elapsed.TotalSeconds;probeTicks=Stopwatch.GetTimestamp();
                    probeAge=(double?)ageMethod.Invoke(plugin,[waitCommand,probeTicks,probeNow]);
                }
            }catch(Exception ex){probeFailure=ex;}
        }) {IsBackground=true,Name="isolated-host-epoch-wait-review"};
        bool monitorBlocked=false;
        Monitor.Enter(hostGate);
        try {
            waitThread.Start();
            monitorBlocked=SpinWait.SpinUntil(()=>
                (waitThread.ThreadState&System.Threading.ThreadState.WaitSleepJoin)!=0,TimeSpan.FromSeconds(2));
            if(monitorBlocked)Thread.Sleep(30);
            releaseTicks=Stopwatch.GetTimestamp();
        } finally {Monitor.Exit(hostGate);}
        if(!waitThread.Join(TimeSpan.FromSeconds(2)))throw new TimeoutException("Isolated epoch probe failed to exit the released host gate");
        var epochWaitProbe=new {monitorBlocked,probeFailure=probeFailure?.ToString(),waitEpoch,waitOrigin,releaseTicks,probeTicks,probeAge,
            clockFrequency=Stopwatch.Frequency,probeTicksAfterRelease=probeTicks>=releaseTicks,
            scope="Controlled test thread blocked in the real isolated host epoch getter; no native Tick or timer executed"};
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{epochWaitProbe}));
        SteeringCheck(monitorBlocked&&probeFailure==null,"the real epoch getter demonstrably waits on its held host monitor and then completes");
        SteeringCheck(probeTicks>=releaseTicks,
            "the watchdog source-age clock is sampled after the epoch getter's actual gate wait");
        double expectedProbeAge=(double)(probeTicks-waitOrigin)/Stopwatch.Frequency;
        SteeringCheck(probeAge is double actualAge&&Math.Abs(actualAge-expectedProbeAge)<1e-9&&actualAge>.15&&epoch()==waitEpoch,
            "an unchanged epoch cannot conceal producer aging past150ms while its getter is blocked");
        var waitGuard=NewCore("SteeringFeedGuard");var checkAge=waitGuard.GetType().GetMethod("CheckAge")!;
        checkAge.Invoke(waitGuard,[probeNow-.01,(double?)0,true]);
        SteeringCheck(checkAge.Invoke(waitGuard,[probeNow,probeAge,true])!.ToString()=="Lost",
            "the post-wait raw age still trips the unchanged150ms watchdog deadline");
        // Block only the FINAL age probe. Telemetry delivered during that
        // epoch-getter wait must be sampled afterwards, before committing AEB.
        Fresh();Set(plugin,"latestDeliveredTelemetry",null);Set(plugin,"lastSteeringCommand",
            CloneCommand(secondCommand!,epoch(),Stopwatch.GetTimestamp()-Stopwatch.Frequency,clock.Elapsed.TotalSeconds-1));
        var pauseGuard=NewCore("SteeringFeedGuard");Set(plugin,"steeringGuard",pauseGuard);
        pauseGuard.GetType().GetMethod("CheckAge")!.Invoke(pauseGuard,[clock.Elapsed.TotalSeconds,(double?)0,true]);
        var faultGate=Get(fault,"sync")!;Exception? pauseFailure=null;
        bool initialPauseFault=(bool)faultType.GetProperty("Latched")!.GetValue(fault)!;
        var pauseThread=new Thread(()=>{try{Call("CheckSteeringOutput");}catch(Exception ex){pauseFailure=ex;}})
            {IsBackground=true,Name="isolated-final-epoch-pause-review"};
        bool firstFaultWait=false,finalEpochWait=false,faultHeld=false,hostHeld=false;
        try {
            Monitor.Enter(faultGate);faultHeld=true;pauseThread.Start();
            firstFaultWait=SpinWait.SpinUntil(()=>
                (pauseThread.ThreadState&System.Threading.ThreadState.WaitSleepJoin)!=0,TimeSpan.FromSeconds(2));
            if(firstFaultWait) {
                Monitor.Enter(hostGate);hostHeld=true;
                Monitor.Exit(faultGate);faultHeld=false;
                finalEpochWait=SpinWait.SpinUntil(()=> {
                    if(Monitor.TryEnter(faultGate)){Monitor.Exit(faultGate);return false;}
                    return (pauseThread.ThreadState&System.Threading.ThreadState.WaitSleepJoin)!=0;
                },TimeSpan.FromSeconds(2));
                if(finalEpochWait) {
                    var frameType=type.GetNestedType("TelemetryFrame",B.NonPublic)!;
                    var pauseFrame=RuntimeHelpers.GetUninitializedObject(frameType);
                    frameType.GetProperty("Paused")!.SetValue(pauseFrame,true);
                    frameType.GetProperty("Active")!.SetValue(pauseFrame,true);
                    frameType.GetProperty("Arrived")!.SetValue(pauseFrame,clock.Elapsed.TotalSeconds);
                    Set(plugin,"latestDeliveredTelemetry",pauseFrame);
                }
            }
        } finally {
            if(hostHeld)Monitor.Exit(hostGate);
            if(faultHeld)Monitor.Exit(faultGate);
        }
        if(!pauseThread.Join(TimeSpan.FromSeconds(2)))throw new TimeoutException("Isolated paused final probe did not exit released gates");
        var finalPauseProbe=new{initialFaultLatched=initialPauseFault,firstFaultWait,finalEpochWait,probeFailure=pauseFailure?.ToString(),
            feed=Get(plugin,"steeringFeedState")!.ToString(),latched=(bool)faultType.GetProperty("Latched")!.GetValue(fault)!,
            assists=(bool)state.GetType().GetProperty("EnableAssists")!.GetValue(state)!,
            scope="Private paused TelemetryFrame delivered while the genuine FINAL epoch getter waits; no OnTelemetry or SDK invocation"};
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{finalPauseProbe}));
        SteeringCheck(!initialPauseFault&&firstFaultWait&&finalEpochWait&&pauseFailure==null,
            "the watchdog demonstrably reaches its first fault gate and then waits in the final epoch getter");
        SteeringCheck(finalPauseProbe.feed=="Off"&&!finalPauseProbe.latched&&finalPauseProbe.assists,
            "SDK pause delivered during the FINAL epoch wait cancels the stale decision without faulting or disabling assists");
        Set(plugin,"latestDeliveredTelemetry",null);
        Reset();enable(false);frame(false);
        int longitudinalTickChecks=LongitudinalPluginReplay.Run(copy,root,assembly,gameAssembly,stateAssembly,context,host,state,frame,gas,brake,modernPedal,enable,epoch,assert);
        File.WriteAllText(Path.Combine(prepared,"plugin-hold-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{
            checks,steeringOriginChecks,longitudinalTickChecks,epochWaitProbe,finalPauseProbe,passed=true,pluginSha256=sealedHash,
            pluginMvid=assembly.ManifestModule.ModuleVersionId,hostMvid=gameAssembly.ManifestModule.ModuleVersionId,
            liveMapsOpened=false,productionConstructorsCalled=false,auditedInjectedConstructorCalled=true,
            auditedStaticInitializersOnly=true,outputThreadsStarted=false,
            scope="Real hold Commit/Clear, own plugin Tick/controller, source decoding and independent deadman; private audited DI fixture, protected host epochs and dictionary sinks, no SDK maps or production timers"}));
    }
}
