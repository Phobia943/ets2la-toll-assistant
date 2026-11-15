using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using B=System.Reflection.BindingFlags;

namespace MautHostCompatibility;

// Runs only copied test assemblies. Constructors/singletons that could open
// live maps are made throwing/injected before any native code is executed.
internal sealed class Replay
{
    readonly Dictionary<int,double> legacy=[],modern=[];
    readonly object legacyMarker=RuntimeHelpers.GetUninitializedObject(typeof(MemoryMappedViewAccessor));
    readonly object modernMarker=RuntimeHelpers.GetUninitializedObject(typeof(MemoryMappedViewAccessor));
    readonly List<object> published=[];
    object host=null!,state=null!,acc=null!;
    Type outputType=null!,stateType=null!,eventType=null!,definitionType=null!,variablesType=null!,propertiesType=null!;
    object bus=null!;
    bool block;
    readonly ManualResetEventSlim entered=new(),release=new();
    const B All=B.Public|B.NonPublic|B.Instance|B.Static;
    const string AccId="AdaptiveCruiseControl.Acceleration",LaneId="LaneAssist.SteeringCorrection";
    static void Set(object owner,string name,object? value)=>owner.GetType().GetField(name,All)!.SetValue(owner,value);
    static object? Get(object owner,string name)=>owner.GetType().GetField(name,All)!.GetValue(owner);
    static void BodyReturnStatic(TypeDefinition type,string getter,string fieldName)
    {
        var method=Rewrite.Method(type,getter);
        var field=new FieldDefinition(fieldName,Mono.Cecil.FieldAttributes.Public|Mono.Cecil.FieldAttributes.Static,method.ReturnType);type.Fields.Add(field);
        method.Body=new(method);var il=method.Body.GetILProcessor();il.Emit(OpCodes.Ldsfld,field);il.Emit(OpCodes.Ret);
    }
    static void ForbidConstructors(TypeDefinition type)
    {
        foreach(var method in type.Methods.Where(m=>m.IsConstructor&&!m.IsStatic)) {
            method.Body=new(method);var il=method.Body.GetILProcessor();il.Emit(OpCodes.Ldstr,"Live constructor forbidden in isolated replay");
            il.Emit(OpCodes.Newobj,type.Module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!));il.Emit(OpCodes.Throw);
        }
    }
    static void ForbidMethod(MethodDefinition method)
    {
        method.Body=new(method);var il=method.Body.GetILProcessor();il.Emit(OpCodes.Ldstr,"Live loop/map entry forbidden in isolated replay");
        il.Emit(OpCodes.Newobj,method.Module.ImportReference(typeof(NotSupportedException).GetConstructor([typeof(string)])!));il.Emit(OpCodes.Throw);
    }
    static void ReplaceWrite(TypeDefinition output,string name,Type action)
    {
        var method=Rewrite.Method(output,name);var module=output.Module;
        var field=new FieldDefinition("Replay"+name,Mono.Cecil.FieldAttributes.Public|Mono.Cecil.FieldAttributes.Static,module.ImportReference(action));output.Fields.Add(field);
        method.Body=new(method);var il=method.Body.GetILProcessor();il.Emit(OpCodes.Ldsfld,field);
        il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt,module.ImportReference(action.GetMethod("Invoke")!));il.Emit(OpCodes.Ret);
    }
    internal static void Run(string root,string prepared)
    {
        var replay=new Replay();replay.Execute(root,prepared);
    }
    void Execute(string root,string prepared)
    {
        string sandbox=Path.Combine(prepared,"isolated-replay-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sandbox);
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(root);resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        foreach(var dir in Directory.GetDirectories(Path.Combine(root,"Libraries")))resolver.AddSearchDirectory(dir);
        var parameters=new ReaderParameters{AssemblyResolver=resolver};
        using(var game=ModuleDefinition.ReadModule(Path.Combine(prepared,"ETS2LA.Game.dll"),parameters)) {
            var output=game.GetType("ETS2LA.Game.Output.GameOutput");BodyReturnStatic(output,"get_Current","ReplayCurrent");ForbidConstructors(output);
            ForbidMethod(Rewrite.Method(output,"Tick"));ForbidMethod(Rewrite.Method(output,"TryOpenMemory"));
            ReplaceWrite(output,"WriteFloat",typeof(Action<MemoryMappedViewAccessor,int,float>));
            ReplaceWrite(output,"WriteBool",typeof(Action<MemoryMappedViewAccessor,int,bool>));
            ReplaceWrite(output,"WriteDouble",typeof(Action<MemoryMappedViewAccessor,int,double>));
            var frame=Rewrite.Method(output,"MautFrame");
            foreach(var instruction in frame.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==typeof(MemoryMappedViewAccessor).FullName&&m.Name=="Flush")) {
                instruction.OpCode=OpCodes.Pop;instruction.Operand=null;
            }
            var camera=game.GetType("ETS2LA.Game.SDK.CameraProvider");BodyReturnStatic(camera,"get_Current","ReplayCurrent");ForbidConstructors(camera);
            BodyReturnStatic(camera,"GetCurrentData","ReplayData");
            ForbidConstructors(game.GetType("ETS2LA.Game.Telemetry.GameTelemetry"));
            foreach(string name in new[]{"TrafficProvider","ParkedVehiclesProvider","SemaphoreProvider"}) {
                var provider=game.GetType("ETS2LA.Game.SDK."+name);ForbidConstructors(provider);
                // Failed-open terminal only: never execute an actual memory
                // opening path, even when testing the provider's Update guard.
                var open=Rewrite.Method(provider,"TryOpenMemory");open.Body=new(open);var il=open.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,Rewrite.Method(provider,"CloseMemory"));il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
            }
            game.Write(Path.Combine(sandbox,"ETS2LA.Game.dll"));
        }
        using(var stateModule=ModuleDefinition.ReadModule(Path.Combine(prepared,"ETS2LA.State.dll"),parameters)) {
            var type=stateModule.GetType("ETS2LA.State.ApplicationState");BodyReturnStatic(type,"get_Current","ReplayCurrent");ForbidConstructors(type);
            stateModule.Write(Path.Combine(sandbox,"ETS2LA.State.dll"));
        }
        using(var accModule=ModuleDefinition.ReadModule(Path.Combine(prepared,"Plugins/tumppi066.adaptivecruisecontrol/AdaptiveCruiseControl.dll"),parameters)) {
            var type=accModule.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin");
            var settings=accModule.GetType("AdaptiveCruiseControl.ACCSettings");BodyReturnStatic(settings,"get_Current","ReplayCurrent");ForbidConstructors(settings);
            var target=new FieldDefinition("ReplayTarget",Mono.Cecil.FieldAttributes.Public,accModule.TypeSystem.Single);type.Fields.Add(target);
            var evaluate=Rewrite.Method(type,"EvaluateConstraints");evaluate.Body=new(evaluate);var il=evaluate.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,target);il.Emit(OpCodes.Ret);
            accModule.Write(Path.Combine(sandbox,"AdaptiveCruiseControl.dll"));
        }
        using(var laneModule=ModuleDefinition.ReadModule(Path.Combine(prepared,"Plugins/tumppi066.laneassist/LaneAssist.dll"),parameters)) {
            var type=laneModule.GetType("LaneAssist.LaneAssistPlugin");ForbidConstructors(type);
            // Keep the actual native error/pause predicates and publisher. Only
            // audio/configuration terminals and unrelated PID reset are injected.
            var reset=Rewrite.Method(type,"ResetPIDs");reset.Body=new(reset);reset.Body.GetILProcessor().Emit(OpCodes.Ret);
            var helper=Rewrite.Method(type,"CheckPlaySound");var il=helper.Body.GetILProcessor();
            foreach(var instruction in helper.Body.Instructions.ToArray()) {
                if(instruction.Operand is MethodReference call&&call.Name=="get_Current"&&
                    call.DeclaringType.FullName is "ETS2LA.Audio.AudioHandler" or "ETS2LA.Settings.Global.AssistanceSettings") {
                    instruction.OpCode=OpCodes.Ldnull;instruction.Operand=null;
                } else if(instruction.Operand is MethodReference audio&&audio.DeclaringType.FullName=="ETS2LA.Audio.AudioHandler"&&audio.Name=="Play") {
                    for(int n=0;n<audio.Parameters.Count;n++)il.InsertBefore(instruction,Instruction.Create(OpCodes.Pop));
                    instruction.OpCode=OpCodes.Pop;instruction.Operand=null;
                } else if(instruction.Operand is FieldReference field&&field.Name=="PauseWhenUnstable"||
                    instruction.Operand is MethodReference setting&&setting.Name=="get_PauseWhenUnstable") {
                    instruction.OpCode=OpCodes.Pop;instruction.Operand=null;il.InsertAfter(instruction,Instruction.Create(OpCodes.Ldc_I4_1));
                }
            }
            laneModule.Write(Path.Combine(sandbox,"LaneAssist.dll"));
        }
        var paths=Directory.EnumerateFiles(root,"*.dll",SearchOption.AllDirectories).GroupBy(Path.GetFileNameWithoutExtension)
            .ToDictionary(g=>g.Key!,g=>g.First(),StringComparer.OrdinalIgnoreCase);
        foreach(var file in Directory.GetFiles(sandbox,"*.dll"))paths[Path.GetFileNameWithoutExtension(file)]=file;
        var context=new ReplayContext(paths);
        var gameAsm=context.LoadFromAssemblyPath(Path.Combine(sandbox,"ETS2LA.Game.dll"));
        var stateAsm=context.LoadFromAssemblyPath(Path.Combine(sandbox,"ETS2LA.State.dll"));
        outputType=gameAsm.GetType("ETS2LA.Game.Output.GameOutput",true)!;stateType=stateAsm.GetType("ETS2LA.State.ApplicationState",true)!;
        eventType=gameAsm.GetType("ETS2LA.Game.Output.ControlEvent",true)!;
        definitionType=gameAsm.GetType("ETS2LA.Game.Output.ControlChannelDefinition",true)!;
        variablesType=gameAsm.GetType("ETS2LA.Game.Output.ControlVariables",true)!;
        propertiesType=gameAsm.GetType("ETS2LA.Game.Output.ControlProperties",true)!;
        host=RuntimeHelpers.GetUninitializedObject(outputType);state=RuntimeHelpers.GetUninitializedObject(stateType);
        outputType.GetField("ReplayCurrent")!.SetValue(null,host);stateType.GetField("ReplayCurrent")!.SetValue(null,state);
        foreach(var field in outputType.GetFields(All).Where(f=>f.Name.StartsWith("Maut",StringComparison.Ordinal))) {
            _=field.GetCustomAttributes(false);_=new NullabilityInfoContext().Create(field);
        }
        Set(host,"EventString","test.maut.compat."+Guid.NewGuid().ToString("N"));
        Set(host,"Channels",Activator.CreateInstance(outputType.GetField("Channels")!.FieldType));
        Set(host,"curFrameFloats",new Dictionary<string,List<Tuple<float,float>>>());
        var offsets=new Dictionary<string,int>();int cursor=0;
        foreach(var field in variablesType.GetFields()){offsets[field.Name]=cursor;cursor+=field.FieldType==typeof(float?)?4:1;}
        Set(host,"legacyShmOffsets",offsets);Set(host,"legacyAccessor",legacyMarker);Set(host,"modernAccessor",modernMarker);
        void Write(MemoryMappedViewAccessor target,int offset,double value) {
            var sink=ReferenceEquals(target,legacyMarker)?legacy:ReferenceEquals(target,modernMarker)?modern:throw new Exception("Unknown output sink");sink[offset]=value;
        }
        outputType.GetField("ReplayWriteFloat")!.SetValue(null,(Action<MemoryMappedViewAccessor,int,float>)((map,offset,value)=>Write(map,offset,value)));
        outputType.GetField("ReplayWriteBool")!.SetValue(null,(Action<MemoryMappedViewAccessor,int,bool>)((map,offset,value)=>Write(map,offset,value?1:0)));
        outputType.GetField("ReplayWriteDouble")!.SetValue(null,(Action<MemoryMappedViewAccessor,int,double>)Write);
        void Assert(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        void Frame(bool modernOutput=false)=>outputType.GetMethod("MautFrame")!.Invoke(host,[modernOutput]);
        float Gas()=>(float)legacy.GetValueOrDefault(offsets["aforward"]);
        float Brake()=>(float)legacy.GetValueOrDefault(offsets["abackward"]);
        void Enable(bool value)=>stateType.GetProperty("EnableAssists")!.SetValue(state,value);
        long Epoch()=>(long)outputType.GetMethod("MautCaptureEpoch")!.Invoke(null,null)!;
        object Send(string id,long epoch,float pedal=0,float? steering=null,float timeout=1) {
            var definition=Activator.CreateInstance(definitionType)!;Set(definition,"Id",id);Set(definition,"Timeout",timeout);Set(definition,"MautEpoch",epoch);
            var properties=Activator.CreateInstance(propertiesType)!;Set(properties,"Weight",1f);
            var variables=Activator.CreateInstance(variablesType)!;
            if(steering.HasValue)Set(variables,"steering",steering.Value);else Set(variables,pedal<0?"abackward":"aforward",pedal);
            var message=Activator.CreateInstance(eventType)!;Set(message,"ChannelDefinition",definition);Set(message,"Properties",properties);Set(message,"Variables",variables);
            outputType.GetMethod("OnControlEvent")!.Invoke(host,[message]);
            return definition;
        }
        var mode=stateType.GetProperty("DrivingMode")!;
        // Match the real constructor's backing-field initialization without
        // invoking a setter: first Enable must still synchronize FullFSD.
        Set(state,"<DrivingMode>k__BackingField",Enum.ToObject(mode.PropertyType,1));
        Enable(true);long firstEpoch=Epoch();Enable(true);
        Assert(Epoch()==firstEpoch,"Repeated enable preserves the active session epoch");
        foreach(float pedal in new[]{.5f,-.7f}) {
            Send(AccId,Epoch(),pedal);Frame();
            Assert(pedal>0?Gas()>.49:Brake()>.69,"Native positive and negative commands reach the isolated legacy sink");
            Send(AccId,Epoch(),pedal,timeout:.001f);Send(LaneId,Epoch(),steering:.4f);Thread.Sleep(10);Frame();
            Assert(Gas()==0&&Brake()==0&&modern[0]>.39,"Expired pedal owner is neutral while steering remains active");
        }
        Send(AccId,Epoch(),.5f,timeout:.001f);Send("test.other-owner",0,.3f);Thread.Sleep(10);Frame();
        Assert(Math.Abs(Gas()-.3)<.001,"Expiry does not neutralize a different valid pedal owner");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Remove("test.other-owner");
        Send(AccId,Epoch(),.6f);Frame(true);
        Assert(Gas()==0&&Brake()==0&&modern[13]>.59&&modern[17]==1,"Modern mode clears the abandoned legacy pedal path");
        Frame(false);Assert(modern[13]==0&&modern[17]==0&&Gas()>.59,"Legacy mode clears the abandoned modern override");
        Enable(false);Enable(true);long next=Epoch();Send(AccId,next,.2f);Send(AccId,firstEpoch,1);Frame();
        Assert(Math.Abs(Gas()-.2)<.001,"Late old session cannot replace or remove a fresh new-session result");
        var reused=Send(AccId,Epoch(),.4f);Enable(false);Enable(true);Set(reused,"MautEpoch",Epoch());Frame();
        Assert(Gas()==0,"Mutating a reused result definition cannot relabel the queued old epoch");
        Enable(false);Send(AccId,next,1);Send(LaneId,next,steering:1);Frame();
        Assert(Gas()==0&&Brake()==0&&modern[0]==0&&modern[4]==0,"Disabled assists reject late native gas and steering");
        Send("local.mautassistent.fault-brake",0,-1);Frame();
        Assert(Brake()==1&&Gas()==0,"Independent fault brake remains available after native disable");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();Enable(true);
        mode.SetValue(state,Enum.ToObject(mode.PropertyType,2));
        Send(AccId,Epoch(),1);Send(LaneId,Epoch(),steering:.4f);Frame();
        Assert(Gas()==0&&modern[0]>.39,"Lane-only mode cannot acquire pedal authority");
        mode.SetValue(state,Enum.ToObject(mode.PropertyType,0));Send(AccId,Epoch(),.3f);Send(LaneId,Epoch(),steering:.9f);Frame();
        Assert(Gas()>.29&&modern[0]==0,"ACC-only mode cannot acquire steering authority");
        mode.SetValue(state,Enum.ToObject(mode.PropertyType,7));Send(AccId,Epoch(),1);Send(LaneId,Epoch(),steering:1);Frame();
        Assert(Gas()==0&&modern[0]==0,"Unknown driving modes receive no autonomous axis authority");
        mode.SetValue(state,Enum.ToObject(mode.PropertyType,1));
        PrepareAcc(context,gameAsm,stateAsm,sandbox);
        Set(acc,"ReplayTarget",-.35f);outputType.GetMethod("OnControlEvent")!.Invoke(host,[NewNeutralEvent()]);
        int before=published.Count;TickAcc();Frame();
        Assert((float)Get(acc,"currentSpeed")! == .3f&&published.Count==before+1&&Brake()>0&&Gas()==0,
            "Actual native 0.3 m/s tick publishes a brake for the former no-publish band");
        // Freeze a real native result before host ingestion, then cancel AND
        // start a new session before allowing the old result to arrive.
        Set(acc,"ReplayTarget",1f);block=true;entered.Reset();release.Reset();
        Task oldTick=Task.Run(TickAcc);if(!entered.Wait(TimeSpan.FromSeconds(2)))throw new Exception("Native publish was not intercepted");
        try {Enable(false);Enable(true);Send(AccId,Epoch(),.25f);}finally{release.Set();}
        if(!oldTick.Wait(TimeSpan.FromSeconds(3)))throw new Exception("Native publish did not finish");block=false;Frame();
        Assert(Math.Abs(Gas()-.25)<.001,"Actual delayed native tick cannot rearm after disable and re-enable");
        var accType=acc.GetType();var settingsType=accType.Assembly.GetType("AdaptiveCruiseControl.ACCSettings",true)!;
        object trustedPath=new();
        outputType.GetField("MautNativePathPermission")!.SetValue(null,(Func<object,bool>)(p=>ReferenceEquals(p,trustedPath)));
        Assert((bool)outputType.GetMethod("MautNativePathKnown")!.Invoke(null,[trustedPath])!&&
            !(bool)outputType.GetMethod("MautNativePathKnown")!.Invoke(null,[new object()])!,
            "A foreign or previous native route cannot borrow the current plugin route's eligibility");
        var isolatedSettings=RuntimeHelpers.GetUninitializedObject(settingsType);Set(isolatedSettings,"IgnoredConstraints",new List<string>());
        settingsType.GetField("ReplayCurrent")!.SetValue(null,isolatedSettings);
        // Exercise real Tick/PID/publication/aggregation, with only its constraint
        // boundary injected. These are policy tests, not a driven traffic proof.
        var controls=new Dictionary<string,float>{{"Path Curvature",-.7f},{"Leading Vehicle",float.NaN},{"Path Semaphore",float.NaN},{"Mautassistent (experimentell)",float.NaN}};
        Set(acc,"constraintValues",controls);Set(Get(Get(acc,"telemetryData")!,"truckFloat")!,"speed",85/3.6f);
        stateType.GetProperty("DesiredSpeed")!.SetValue(state,80/3.6f);Set(acc,"ReplayTarget",-.7f);
        outputType.GetField("MautCoastPermission")!.SetValue(null,(Func<bool>)(()=>true));
        object trafficEvidence=new(),parkedEvidence=new(),semaphoreEvidence=new();
        outputType.GetField("MautTrafficData")!.SetValue(null,trafficEvidence);outputType.GetField("MautParkedData")!.SetValue(null,parkedEvidence);outputType.GetField("MautSemaphoreData")!.SetValue(null,semaphoreEvidence);
        void Curve(float raw=float.PositiveInfinity,bool failed=false) {
            outputType.GetMethod("MautMarkNoLimit")!.Invoke(null,[0,"\nNo significant vehicle. ",true,trafficEvidence,parkedEvidence]);
            outputType.GetMethod("MautMarkNoLimit")!.Invoke(null,[1,"No upcoming semaphores.",true,semaphoreEvidence,null]);
            outputType.GetMethod("MautBeginCurve")!.Invoke(null,null);outputType.GetMethod("MautCurveSample")!.Invoke(null,[raw]);
            if(failed)outputType.GetMethod("MautInvalidateCurve")!.Invoke(null,null);
            outputType.GetMethod("MautFinishCurve")!.Invoke(null,null);
        }
        void FreshTraffic() {
            long stamp=Stopwatch.GetTimestamp();outputType.GetField("MautTrafficAt")!.SetValue(null,stamp);outputType.GetField("MautParkedAt")!.SetValue(null,stamp);
            outputType.GetField("MautSemaphoreAt")!.SetValue(null,stamp);
            // Our injected constraint boundary supplies a complete same-cycle
            // snapshot; the real evaluator stamps these before enumeration.
            outputType.GetField("MautCycleTrafficAt",All)!.SetValue(null,stamp);outputType.GetField("MautCycleParkedAt",All)!.SetValue(null,stamp);
        }
        Curve();FreshTraffic();TickAcc();Frame();
        Assert(Gas()==0&&Brake()==0&&(float)Get(acc,"pedalOutput")! ==0,"85 to 80 correction publishes genuinely neutral gas and brake through real native Tick");
        foreach(int kind in new[]{0,1}) {
            outputType.GetMethod("MautMarkNoLimit")!.Invoke(null,[kind,"No planned path or telemetry data.",false,null,null]);
            FreshTraffic();TickAcc();Frame();Assert(Brake()>0,"Fresh SDK reads cannot disguise unavailable native constraint inputs: "+kind);Curve();
        }
        outputType.GetMethod("MautMarkNoLimit")!.Invoke(null,[0,"\nNo significant vehicle. ",true,new object(),parkedEvidence]);
        FreshTraffic();TickAcc();Frame();Assert(Brake()>0,"Fresh provider reads cannot certify a foreign/default native traffic container");Curve();
        foreach(string hazard in new[]{"Leading Vehicle","Path Semaphore","Mautassistent (experimentell)"}) {
            controls[hazard]=-.2f;FreshTraffic();TickAcc();Frame();
            Assert(Brake()>0,"The same small speed difference keeps native braking for "+hazard);controls[hazard]=float.NaN;
        }
        Curve(80/3.6f);FreshTraffic();TickAcc();Frame();Assert(Brake()>0,"A future raw curve target below current speed prohibits coasting before the bound turns negative");
        Curve(failed:true);FreshTraffic();TickAcc();Frame();Assert(Brake()>0,"Caught native curve-sampling failure cannot become straight-road permission");
        Curve();outputType.GetField("MautTrafficAt")!.SetValue(null,0L);TickAcc();Frame();Assert(Brake()>0,"Unknown/default empty traffic is not confirmed live-empty traffic");
        FreshTraffic();outputType.GetField("MautParkedAt")!.SetValue(null,Stopwatch.GetTimestamp()-Stopwatch.Frequency);TickAcc();Frame();Assert(Brake()>0,"Stale parked-vehicle evidence prohibits coasting");
        FreshTraffic();outputType.GetField("MautCoastPermission")!.SetValue(null,(Func<bool>)(()=>false));TickAcc();Frame();Assert(Brake()>0,"Unknown route or recovery prohibits neutralization");
        outputType.GetField("MautCoastPermission")!.SetValue(null,(Func<bool>)(()=>true));
        Set(Get(Get(acc,"telemetryData")!,"truckVector")!,"acceleration",new System.Numerics.Vector3(0,0,-1));
        // Direct check uses the same native measured-acceleration contract; its
        // downhill latch must survive the subsequent controller braking cycle.
        bool Check(float acceleration)=>(bool)outputType.GetMethod("MautCanCoast")!.Invoke(null,[Epoch(),85/3.6f,80/3.6f,acceleration,controls])!;
        FreshTraffic();Assert(!Check(.2f)&&!Check(-.1f),"Downhill acceleration exits coasting and stays locked out until target speed or a new target/session");
        bool crossedThread=Task.Run(()=>{Curve();FreshTraffic();return Check(-.1f);}).GetAwaiter().GetResult();
        Assert(!crossedThread,"A following ACC tick on another worker cannot lose the downhill session latch");
        Enable(false);Frame();Assert(Gas()==0&&Brake()==0,"Manual cancellation also clears a coasting session");
        Enable(true);Curve();FreshTraffic();
        Assert(Check(-.1f),"A new session can coast again with complete fresh same-cycle evidence");
        outputType.GetField("MautTrafficAt")!.SetValue(null,Stopwatch.GetTimestamp()-Stopwatch.Frequency);
        var trafficProvider=gameAsm.GetType("ETS2LA.Game.SDK.TrafficProvider",true)!;
        trafficProvider.GetMethod("MautReconnectMemory",All)!.Invoke(RuntimeHelpers.GetUninitializedObject(trafficProvider),null);
        Assert(!Check(-.1f),"A normal reconnect cannot renew old evidence beyond the 0.2 second freshness bound");
        outputType.GetField("MautCoastPermission")!.SetValue(null,null);
        foreach(string name in new[]{"PathCurvatureConstraint","LeadingVehicleConstraint","PathSemaphoreConstraint"}) {
            var type=accType.Assembly.GetType("AdaptiveCruiseControl.Constraints."+name,true)!;
            RuntimeHelpers.PrepareMethod(type.GetMethod("GetMaxAcceleration")!.MethodHandle);
            var native=RuntimeHelpers.GetUninitializedObject(type);
            if(name=="PathCurvatureConstraint") {
                Set(native,"telemetryData",Get(acc,"telemetryData"));
                type.GetMethod("GetMaxAcceleration")!.Invoke(native,null);
                Assert(!(bool)outputType.GetField("MautCurveComplete")!.GetValue(null)!,"Actual curvature early return without a native route cannot certify complete coverage");
            } else {
                type.GetMethod("NoLimit",All)!.Invoke(native,[name=="LeadingVehicleConstraint"?"\nNo significant vehicle. ":"No upcoming semaphores."]);
                Assert(!(bool)outputType.GetField(name=="LeadingVehicleConstraint"?"MautLeadingKnown":"MautSemaphoreKnown",All)!.GetValue(null)!,"Actual native no-limit helper refuses default/absent inputs: "+name);
            }
        }
        foreach(var provider in new[]{("TrafficProvider","MautTrafficAt"),("ParkedVehiclesProvider","MautParkedAt"),("SemaphoreProvider","MautSemaphoreAt")}) {
            var type=gameAsm.GetType("ETS2LA.Game.SDK."+provider.Item1,true)!;
            foreach(string method in new[]{"Update","UpdateThread","CloseMemory"})RuntimeHelpers.PrepareMethod(type.GetMethod(method,All)!.MethodHandle);
            var empty=RuntimeHelpers.GetUninitializedObject(type);
            long beforeReconnect=Stopwatch.GetTimestamp();object knownContainer=new();
            var containerField=outputType.GetField(provider.Item2[..^2]+"Data")!;
            outputType.GetField(provider.Item2)!.SetValue(null,beforeReconnect);containerField.SetValue(null,knownContainer);
            type.GetMethod("MautReconnectMemory",All)!.Invoke(empty,null);
            Assert((long)outputType.GetField(provider.Item2)!.GetValue(null)! ==beforeReconnect&&ReferenceEquals(containerField.GetValue(null),knownContainer),
                "Normal successful reconnect preserves the exact old snapshot without renewing it: "+provider.Item1);
            type.GetMethod("Update",All)!.Invoke(empty,null);
            Assert((long)outputType.GetField(provider.Item2)!.GetValue(null)! ==0&&containerField.GetValue(null)==null,
                "Actual Update with injected failed reopen cannot certify an empty default container: "+provider.Item1);
            outputType.GetField(provider.Item2)!.SetValue(null,beforeReconnect);containerField.SetValue(null,knownContainer);
            // Only close is executed, with null accessors and handles. Never
            // execute a provider update or constructor against a real map.
            type.GetMethod("CloseMemory",All)!.Invoke(empty,null);
            Assert((long)outputType.GetField(provider.Item2)!.GetValue(null)! ==0&&containerField.GetValue(null)==null,
                "Actual provider CloseMemory invalidates its successful-read evidence: "+provider.Item1);
        }
        var laneAssembly=context.LoadFromAssemblyPath(Path.Combine(sandbox,"LaneAssist.dll"));
        var laneType=laneAssembly.GetType("LaneAssist.LaneAssistPlugin",true)!;var lane=RuntimeHelpers.GetUninitializedObject(laneType);
        RuntimeHelpers.PrepareMethod(laneType.GetMethod("Tick")!.MethodHandle);
        Assert(true,"Rewritten native Lane.Tick is JIT-valid without executing its live algorithm");
        Set(lane,"telemetryData",Get(acc,"telemetryData"));
        void Pause(long origin){Set(lane,"wasActive",true);Set(lane,"tickCounter",6);laneType.GetMethod("CheckPlaySound",All)!.Invoke(lane,[false,false,origin]);}
        long pauseEpoch=Epoch();Pause(pauseEpoch);Frame();
        Assert(legacy.GetValueOrDefault(offsets["pause"])==1,"Actual native lane-error helper emits an authorized pause pulse");
        Enable(false);Frame();Thread.Sleep(70);Frame();
        Assert(legacy.GetValueOrDefault(offsets["pause"])==0,"Pause pulse is cleared on takeover without a deferred toggle task");
        Enable(true);Pause(pauseEpoch);Frame();
        Assert(legacy.GetValueOrDefault(offsets["pause"])==0,"Actual old-origin lane-error helper cannot pause a new session");
        File.WriteAllText(Path.Combine(prepared,"native-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{checks,passed=true,liveMapsOpened=false,outputThreadsStarted=false,fixtureDirectory=sandbox}));
        Console.WriteLine($"{checks} isolated native replay checks passed; no live maps or output threads.");
    }
    int checks;
    object NewNeutralEvent()
    {
        var message=Activator.CreateInstance(eventType)!;
        var definition=Activator.CreateInstance(definitionType)!;Set(definition,"Id","test.placeholder");Set(definition,"Timeout",1f);
        Set(message,"ChannelDefinition",definition);Set(message,"Properties",Activator.CreateInstance(propertiesType));Set(message,"Variables",Activator.CreateInstance(variablesType));return message;
    }
    void HandleNative(object message)
    {
        published.Add(message);
        if(block){entered.Set();if(!release.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException();}
        outputType.GetMethod("OnControlEvent")!.Invoke(host,[message]);
    }
    void TickAcc()=>acc.GetType().GetMethod("Tick")!.Invoke(acc,null);
    void PrepareAcc(AssemblyLoadContext context,Assembly game,Assembly stateAssembly,string sandbox)
    {
        var camera=game.GetType("ETS2LA.Game.SDK.CameraProvider",true)!;
        camera.GetField("ReplayCurrent")!.SetValue(null,RuntimeHelpers.GetUninitializedObject(camera));
        var cameraData=camera.GetField("ReplayData")!.FieldType;var cameraValue=RuntimeHelpers.GetUninitializedObject(cameraData);
        Set(cameraValue,"truckRotation",System.Numerics.Quaternion.Identity);camera.GetField("ReplayData")!.SetValue(null,cameraValue);
        var accAssembly=context.LoadFromAssemblyPath(Path.Combine(sandbox,"AdaptiveCruiseControl.dll"));
        var type=accAssembly.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin",true)!;acc=RuntimeHelpers.GetUninitializedObject(type);
        var settingsType=accAssembly.GetType("AdaptiveCruiseControl.ACCSettings",true)!;
        var settings=RuntimeHelpers.GetUninitializedObject(settingsType);Set(settings,"IgnoredConstraints",new List<string>());settingsType.GetField("ReplayCurrent")!.SetValue(null,settings);
        var telemetryType=type.GetField("telemetryData",All)!.FieldType;var telemetry=Activator.CreateInstance(telemetryType)!;
        var floats=Get(telemetry,"truckFloat")!;Set(floats,"speed",.3f);Set(acc,"telemetryData",telemetry);
        if((float)Get(Get(telemetry,"truckFloat")!,"speed")! != .3f)throw new Exception("Telemetry fixture speed was not retained");
        var filter=type.GetField("inputAccelerationKalmanFilter",All)!.FieldType;
        Set(acc,"inputAccelerationKalmanFilter",Activator.CreateInstance(filter,[.01f,.67f,1f]));
        Set(acc,"DryMass",9500f);Set(acc,"StaticFriction",500f);Set(acc,"MaxEngineForce",45000f);Set(acc,"MaxBrakingForce",90000f);
        stateType.GetProperty("DesiredSpeed")!.SetValue(state,10f);
        var backend=context.LoadFromAssemblyName(new AssemblyName("ETS2LA.Backend"));
        var events=backend.GetType("ETS2LA.Backend.Events.Events",true)!;
        bus=events.GetProperty("Current")!.GetValue(null)!;
        var callback=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(eventType),this,GetType().GetMethod(nameof(HandleNative),All)!);
        events.GetMethods().Single(m=>m.Name=="Subscribe"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2)
            .MakeGenericMethod(eventType).Invoke(bus,[Get(host,"EventString"),callback]);
    }
    sealed class ReplayContext(Dictionary<string,string> paths):AssemblyLoadContext("maut-host-replay-"+Guid.NewGuid(),isCollectible:true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            // Share framework types with the harness, not private app copies.
            if(name.Name!.StartsWith("System.",StringComparison.Ordinal)||name.Name is "System" or "netstandard" or "mscorlib" or "Microsoft.CSharp")return null;
            return paths.TryGetValue(name.Name,out var path)?LoadFromAssemblyPath(path):null;
        }
    }
}
