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
    bool block,blockEvaluation;
    readonly ManualResetEventSlim entered=new(),release=new();
    const B All=B.Public|B.NonPublic|B.Instance|B.Static;
    const string AccId="local.mautassistent.longitudinal",NativeAccId="AdaptiveCruiseControl.Acceleration",LaneId="LaneAssist.SteeringCorrection";
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
            ReplaceWrite(output,"MautRawWriteBool",typeof(Action<MemoryMappedViewAccessor,int,bool>));
            ReplaceWrite(output,"WriteDouble",typeof(Action<MemoryMappedViewAccessor,int,double>));
            var frame=Rewrite.Method(output,"MautFrame");
            foreach(var instruction in frame.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==typeof(MemoryMappedViewAccessor).FullName&&m.Name=="Flush")) {
                instruction.OpCode=OpCodes.Pop;instruction.Operand=null;
            }
            var camera=game.GetType("ETS2LA.Game.SDK.CameraProvider");BodyReturnStatic(camera,"get_Current","ReplayCurrent");ForbidConstructors(camera);
            BodyReturnStatic(camera,"GetCurrentData","ReplayData");
            var telemetry=game.GetType("ETS2LA.Game.Telemetry.GameTelemetry");
            BodyReturnStatic(telemetry,"get_Current","ReplayCurrent");BodyReturnStatic(telemetry,"GetCurrentData","ReplayData");
            ForbidConstructors(telemetry);
            foreach(string name in new[]{"TrafficProvider","ParkedVehiclesProvider","SemaphoreProvider"}) {
                var provider=game.GetType("ETS2LA.Game.SDK."+name);ForbidConstructors(provider);
                SourceFrameReplay.PrepareSandbox(provider);
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
            var afterEvaluation=new FieldDefinition("ReplayAfterEvaluation",Mono.Cecil.FieldAttributes.Public|Mono.Cecil.FieldAttributes.Static,accModule.ImportReference(typeof(Action)));type.Fields.Add(afterEvaluation);
            var evaluate=Rewrite.Method(type,"EvaluateConstraints");evaluate.Body=new(evaluate);var il=evaluate.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,target);
            il.Emit(OpCodes.Ldsfld,afterEvaluation);il.Emit(OpCodes.Callvirt,accModule.ImportReference(typeof(Action).GetMethod("Invoke")!));il.Emit(OpCodes.Ret);
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
        // PathLib's real lane-change builder reads this passive speed-limit
        // DTO. Both getters above are isolated field reads, never singletons.
        var telemetryType=gameAsm.GetType("ETS2LA.Game.Telemetry.GameTelemetry",true)!;
        telemetryType.GetField("ReplayCurrent")!.SetValue(null,RuntimeHelpers.GetUninitializedObject(telemetryType));
        var telemetryData=RuntimeHelpers.GetUninitializedObject(telemetryType.GetField("ReplayData")!.FieldType);
        var truckFloatField=telemetryData.GetType().GetField("truckFloat")!;
        var truckFloat=truckFloatField.FieldType.IsValueType?Activator.CreateInstance(truckFloatField.FieldType)!:RuntimeHelpers.GetUninitializedObject(truckFloatField.FieldType);
        Set(truckFloat,"speedLimit",100f/3.6f);truckFloatField.SetValue(telemetryData,truckFloat);
        telemetryType.GetField("ReplayData")!.SetValue(null,telemetryData);
        foreach(var field in outputType.GetFields(All).Where(f=>f.Name.StartsWith("Maut",StringComparison.Ordinal))) {
            _=field.GetCustomAttributes(false);_=new NullabilityInfoContext().Create(field);
        }
        Set(host,"EventString","test.maut.compat."+Guid.NewGuid().ToString("N"));
        Set(host,"Channels",Activator.CreateInstance(outputType.GetField("Channels")!.FieldType));
        Set(host,"curFrameFloats",new Dictionary<string,List<Tuple<float,float>>>());
        var offsets=new Dictionary<string,int>();int cursor=0;
        foreach(var field in variablesType.GetFields()){offsets[field.Name]=cursor;cursor+=field.FieldType==typeof(float?)?4:1;}
        Set(host,"legacyShmOffsets",offsets);Set(host,"legacyAccessor",legacyMarker);Set(host,"modernAccessor",modernMarker);
        var activationWrites=new List<bool>();
        void Write(MemoryMappedViewAccessor target,int offset,double value) {
            var sink=ReferenceEquals(target,legacyMarker)?legacy:ReferenceEquals(target,modernMarker)?modern:throw new Exception("Unknown output sink");sink[offset]=value;
            if(ReferenceEquals(target,legacyMarker)&&offset==offsets["activate"])activationWrites.Add(value!=0);
        }
        outputType.GetField("ReplayWriteFloat")!.SetValue(null,(Action<MemoryMappedViewAccessor,int,float>)((map,offset,value)=>Write(map,offset,value)));
        outputType.GetField("ReplayMautRawWriteBool")!.SetValue(null,(Action<MemoryMappedViewAccessor,int,bool>)((map,offset,value)=>Write(map,offset,value?1:0)));
        outputType.GetField("ReplayWriteDouble")!.SetValue(null,(Action<MemoryMappedViewAccessor,int,double>)Write);
        // Compile the transplanted output terminals before assigning short-lived
        // native timestamps. First JIT latency must not become a test grace.
        foreach(string name in new[]{"OnControlEvent","MautFrame","MautAccept","MautSnapshotDefinition","MautSnapshotVariables","MautPedalsValid"})
            RuntimeHelpers.PrepareMethod(outputType.GetMethod(name,All)!.MethodHandle);
        void Assert(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        PrefabRoutingReplay.Run(gameAsm,Assert);
        void Frame(bool modernOutput=false)=>outputType.GetMethod("MautFrame")!.Invoke(host,[modernOutput]);
        float Gas()=>(float)legacy.GetValueOrDefault(offsets["aforward"]);
        float Brake()=>(float)legacy.GetValueOrDefault(offsets["abackward"]);
        void Enable(bool value)=>stateType.GetProperty("EnableAssists")!.SetValue(state,value);
        long Epoch()=>(long)outputType.GetMethod("MautCaptureEpoch")!.Invoke(null,null)!;
        object? lastSentVariables=null;
        object Send(string id,long epoch,float pedal=0,float? steering=null,float timeout=1,long? issuedAt=null) {
            long origin=issuedAt??Stopwatch.GetTimestamp();
            var definition=Activator.CreateInstance(definitionType)!;Set(definition,"Id",id);Set(definition,"Timeout",timeout);Set(definition,"MautEpoch",epoch);Set(definition,"MautIssuedAt",origin);Set(definition,"MautSourceEpoch",outputType.GetField("MautOwnSourceEpoch")!.GetValue(null));Set(definition,"MautSourceAt",origin);
            var properties=Activator.CreateInstance(propertiesType)!;Set(properties,"Weight",1f);
            var variables=Activator.CreateInstance(variablesType)!;
            if(steering.HasValue)Set(variables,"steering",steering.Value);else Set(variables,pedal<0?"abackward":"aforward",pedal);
            var message=Activator.CreateInstance(eventType)!;Set(message,"ChannelDefinition",definition);Set(message,"Properties",properties);Set(message,"Variables",variables);
            lastSentVariables=variables;
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
        Assert(Gas()==0&&Brake()==0,"Expiry cannot transfer longitudinal authority to an unrecognized weighted pedal owner");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Remove("test.other-owner");
        foreach(float hostile in new[]{1f,-1f}) {
            Send(NativeAccId,Epoch(),hostile);Send("hostile.foreign-pedals",0,hostile);Frame();
            Assert(Gas()==0&&Brake()==0,"Native ACC and unknown channels are permanently excluded from normal pedal authority: "+hostile);
        }
        ((System.Collections.IDictionary)Get(host,"Channels")!).Remove("hostile.foreign-pedals");
        Send(AccId,Epoch(),.6f);Frame(true);
        Assert(Gas()==0&&Brake()==0&&modern[13]>.59&&modern[17]==1,"Modern mode clears the abandoned legacy pedal path");
        Frame(false);Assert(modern[13]==0&&modern[17]==0&&Gas()>.59,"Legacy mode clears the abandoned modern override");
        Send(NativeAccId,Epoch(),-1);Send("hostile.weighted-throttle",0,1);Frame();
        Assert(Math.Abs(Gas()-.6f)<.001&&Brake()==0,"Foreign brake and throttle cannot dilute or veto the one authorized longitudinal owner");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Remove("hostile.weighted-throttle");
        Send("local.mautassistent.fault-brake",0,-1);Frame();
        Assert(Brake()==1&&Gas()==0,"Fault brake dominates even a fresh strong authorized throttle without weighting dilution");
        Set(lastSentVariables!,"abackward",float.NaN);Frame();
        Assert(Brake()==1&&Gas()==0,"Mutating the publisher's old variables cannot erase the snapshotted fault brake");
        Send("local.mautassistent.fault-brake",0,1);Send("local.mautassistent.fault-brake",0,float.NaN);Frame();
        Assert(Brake()==1&&Gas()==0,"Malformed fault-channel replacements cannot revoke an existing valid full brake");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Remove("local.mautassistent.fault-brake");
        Send("local.mautassistent.fault-brake",0,1);Frame();
        Assert(Math.Abs(Gas()-.6f)<.001&&Brake()==0,"Forged positive fault-channel throttle cannot create or dilute normal pedal authority");
        Send("local.mautassistent.fault-brake",0,float.NaN);Frame();
        Assert(Math.Abs(Gas()-.6f)<.001&&Brake()==0,"A nonfinite fault-channel value cannot poison the physical pedal axis");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Remove("local.mautassistent.fault-brake");
        Send(AccId,Epoch(),.6f);Set(lastSentVariables!,"aforward",1f);Frame();
        Assert(Math.Abs(Gas()-.6f)<.001,"Publisher mutation cannot rewrite an accepted original longitudinal result");
        var normal=((System.Collections.IDictionary)Get(host,"Channels")!)[AccId]!;
        Set(Get(normal,"Properties")!,"Weight",float.NaN);Frame();
        Assert(Math.Abs(Gas()-.6f)<.001&&Brake()==0,"The sole longitudinal source does not depend on a foreign weighting property");
        Set(Get(normal,"Variables")!,"abackward",-.4f);Frame();
        Assert(Gas()==0&&Brake()==0,"Mutated simultaneous throttle and brake are rejected as one invalid command at frame time");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,2f,-2f}) {
            Send(AccId,Epoch(),invalid);Frame();Assert(Gas()==0&&Brake()==0,"Invalid signed pedal command is wholly rejected: "+invalid);
        }
        Send(LaneId,Epoch(),steering:.2f);Send(AccId,Epoch(),steering:.9f);Frame();
        Assert(Math.Abs(modern[0]-.2f)<.001&&Gas()==0&&Brake()==0,"Longitudinal channel cannot smuggle steering into the original lane owner");
        Send(AccId,Epoch(),.6f);
        Enable(false);Enable(true);long next=Epoch();Send(AccId,next,.2f);Send(AccId,firstEpoch,1);Frame();
        Assert(Math.Abs(Gas()-.2)<.001,"Late old session cannot replace or remove a fresh new-session result");
        var reused=Send(AccId,Epoch(),.4f);Enable(false);Enable(true);Set(reused,"MautEpoch",Epoch());Frame();
        Assert(Gas()==0,"Mutating a reused result definition cannot relabel the queued old epoch");
        Send(AccId,Epoch(),.2f);Send(AccId,Epoch(),1,issuedAt:Stopwatch.GetTimestamp()-Stopwatch.Frequency);Frame();
        Assert(Math.Abs(Gas()-.2)<.001,"A same-session stale issue cannot replace a fresh channel at ingestion");
        foreach(long invalid in new[]{0L,long.MinValue,long.MaxValue,Stopwatch.GetTimestamp()+Stopwatch.Frequency}) {
            Send(AccId,Epoch(),1,issuedAt:invalid);Frame();
            Assert(Math.Abs(Gas()-.2)<.001,"Missing, negative or future native issue timestamp is rejected without removing a fresh result: "+invalid);
        }
        var issuedReuse=Send(AccId,Epoch(),.6f);Set(issuedReuse,"MautIssuedAt",Stopwatch.GetTimestamp()+Stopwatch.Frequency);
        Thread.Sleep(230);Set(issuedReuse,"MautIssuedAt",Stopwatch.GetTimestamp());Frame();
        Assert(Gas()==0,"Reusing and refreshing a publisher definition cannot renew the stored origin timestamp; frame independently enforces 200 ms age");
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
        TickAcc();Frame(); // Warm the real Tick/PID/event path without relaxing production age checks.
        int before=published.Count;TickAcc();Frame();
        Assert((float)Get(acc,"currentSpeed")! == .3f&&published.Count==before+1&&Brake()==0&&Gas()==0,
            "Actual native low-speed ACC Tick may publish but cannot acquire pedal authority");
        // Freeze a real native result before host ingestion, then cancel AND
        // start a new session before allowing the old result to arrive.
        Set(acc,"ReplayTarget",1f);block=true;entered.Reset();release.Reset();
        Task oldTick=Task.Run(TickAcc);if(!entered.Wait(TimeSpan.FromSeconds(2)))throw new Exception("Native publish was not intercepted");
        try {Enable(false);Enable(true);Send(AccId,Epoch(),.25f);}finally{release.Set();}
        if(!oldTick.Wait(TimeSpan.FromSeconds(3)))throw new Exception("Native publish did not finish");block=false;Frame();
        Assert(Math.Abs(Gas()-.25)<.001,"Actual delayed native tick cannot rearm after disable and re-enable");
        // Delay INSIDE the real native Tick, after its constraint result is
        // known but before it constructs/publishes the output definition. A
        // stamp taken at publication would incorrectly pass this same-session case.
        ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
        long sameSession=Epoch();Set(acc,"ReplayTarget",1f);blockEvaluation=true;entered.Reset();release.Reset();
        Task oldCalculation=Task.Run(TickAcc);
        if(!entered.Wait(TimeSpan.FromSeconds(2)))throw new Exception("Native evaluation boundary was not intercepted");
        Thread.Sleep(230);release.Set();
        if(!oldCalculation.Wait(TimeSpan.FromSeconds(3)))throw new Exception("Native delayed calculation did not finish");
        blockEvaluation=false;
        var delayedDefinition=Get(published[^1],"ChannelDefinition")!;
        long originalIssue=(long)Get(delayedDefinition,"MautIssuedAt")!;
        Assert(Epoch()==sameSession&&Stopwatch.GetTimestamp()-originalIssue>Stopwatch.Frequency/5&&
            !((System.Collections.IDictionary)Get(host,"Channels")!).Contains(AccId),
            "Actual native Tick delayed after Evaluate and before publication is rejected at ingestion without a session change");
        Frame();Assert(Gas()==0&&Brake()==0,"Stale native calculation cannot become newly stamped gas in the physical frame");
        TickAcc();Frame();Assert(Gas()==0&&Brake()==0,"Even a genuinely fresh native ACC Tick remains excluded after the delayed result");
        Send(AccId,Epoch(),.2f);Frame();Assert(Gas()>.19&&Brake()==0,"The real local longitudinal channel independently acquires current authority");
        Enable(false);Frame();Assert(Gas()==0&&Brake()==0,"F9 still clears fresh native output immediately after age rejection");
        Enable(true);
        var holdMethod=outputType.GetMethod("MautSetPlannedHold")!;
        void Hold(long epoch,string? key,float floor,long? issuedAt=null)=>holdMethod.Invoke(null,[epoch,key,floor,issuedAt??Stopwatch.GetTimestamp()]);
        Hold(Epoch(),"delayed-calculation-hold",1,originalIssue);Frame();
        Assert(Brake()==0,"The real delayed Tick origin cannot acquire a freshly stamped planned hold in the same session");
        Hold(Epoch(),"origin-deadline",.35f,Stopwatch.GetTimestamp()-Stopwatch.Frequency*15/100);
        Frame();Assert(Brake()>=.35f,"An accepted still-current hold can use its remaining origin lease");
        Thread.Sleep(120);Frame();Assert(Brake()==0,"Hold expiry is bounded by its original issue time, not the later publish time");
        Enable(false);Enable(true);
        long orderedNow=Stopwatch.GetTimestamp(),olderOrigin=orderedNow-Stopwatch.Frequency*8/100,newerOrigin=orderedNow-Stopwatch.Frequency*2/100;
        Send(AccId,Epoch(),-.7f,issuedAt:newerOrigin);Send(AccId,Epoch(),1,issuedAt:olderOrigin);Frame();
        Assert(Brake()>.69&&Gas()==0,"Same-session older throttle cannot overwrite a newer native brake within their valid TTL");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();Send(AccId,Epoch(),1,issuedAt:olderOrigin);Frame();
        Assert(Brake()==0&&Gas()==0,"Native origin watermark survives removal of the channel and prevents resurrection");
        Hold(Epoch(),"newer-hold",.65f,newerOrigin);Hold(Epoch(),null,0,olderOrigin);Frame();
        Assert(Brake()>=.65f&&Gas()==0,"Same-session older clear cannot erase a newer planned hold");
        Enable(false);Enable(true);Send(AccId,Epoch(),.2f,issuedAt:olderOrigin);Frame();
        Assert(Gas()>.19&&Brake()==0,"Changing assist epoch resets native and hold origin watermarks");
        Set(Get(Get(acc,"telemetryData")!,"truckFloat")!,"speed",.384f);Set(acc,"ReplayTarget",-.9f);
        TickAcc();Frame();
        Assert(Gas()>.19&&Brake()==0,"Actual native creeping-speed brake cannot veto a fresh authorized local command");
        Hold(Epoch(),"plaza-A",.35f);Frame();
        Assert(Math.Abs(Brake()-.35f)<.001&&Gas()==0,"Planned hold applies a physical pedal floor after real native aggregation");
        Send("test.strong-throttle",0,1);Frame();
        Assert(Brake()>=.35f&&Gas()==0,"Another weighted throttle owner cannot dilute an authorized planned brake hold");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Remove("test.strong-throttle");
        Send(AccId,Epoch(),-.9f);Frame();Assert(Brake()>.89f,"Hold never weakens a stronger native brake");
        Hold(Epoch(),"plaza-A",1);Frame();Assert(Brake()==1&&Gas()==0,"Creep escalation reaches full physical brake");
        Hold(Epoch(),"plaza-B",.65f);Send(AccId,Epoch(),.8f,timeout:.001f);Thread.Sleep(10);Frame();
        Assert(Brake()>=.65f&&Gas()==0,"Expiring the last native axis cannot reset the authorized hold back to zero");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();Frame();
        Assert(Brake()>=.65f&&Gas()==0,"An empty channel collection still respects the separate authorized planned hold");
        Frame(true);Assert(Math.Abs(modern[13]+.65)<.001&&modern[17]==1&&Gas()==0&&Brake()==0,"Modern output receives the same planned brake floor and clears legacy pedals");
        long oldHoldEpoch=Epoch();Enable(false);Frame(true);
        Assert(modern[13]==0&&modern[17]==0&&Gas()==0&&Brake()==0,"Manual cancellation immediately releases planned hold in both output paths");
        Enable(true);Send(AccId,Epoch(),.2f);Hold(oldHoldEpoch,"late-old-plaza",1);Frame();
        Assert(Gas()>.19&&Brake()==0,"A delayed hold result from the old session cannot stop a new session");
        Hold(Epoch(),"new-plaza",.35f);Hold(oldHoldEpoch,null,0);Frame();
        Assert(Brake()>=.35f&&Gas()==0,"A delayed old clear cannot release a newer planned hold");
        Hold(Epoch(),null,0);Frame();Assert(Gas()>.19&&Brake()==0,"Resolution of the current stop releases only the current planned floor");
        Hold(Epoch(),"expires",.35f);Thread.Sleep(280);Frame();
        Assert(Brake()==0,"A hold without renewed data authority cannot silently extend its lease");
        Hold(Epoch(),"mode-change",.35f);mode.SetValue(state,Enum.ToObject(mode.PropertyType,2));Frame();
        Assert(Brake()==0&&Gas()==0,"Lane-only mode revokes all planned pedal rights");
        mode.SetValue(state,Enum.ToObject(mode.PropertyType,1));
        var accType=acc.GetType();var settingsType=accType.Assembly.GetType("AdaptiveCruiseControl.ACCSettings",true)!;
        object trustedPath=new();
        outputType.GetField("MautNativePathPermission")!.SetValue(null,(Func<object,bool>)(p=>ReferenceEquals(p,trustedPath)));
        Assert((bool)outputType.GetMethod("MautNativePathKnown")!.Invoke(null,[trustedPath])!&&
            !(bool)outputType.GetMethod("MautNativePathKnown")!.Invoke(null,[new object()])!,
            "A foreign or previous native route cannot borrow the current plugin route's eligibility");
        var isolatedSettings=RuntimeHelpers.GetUninitializedObject(settingsType);Set(isolatedSettings,"IgnoredConstraints",new List<string>());
        settingsType.GetField("ReplayCurrent")!.SetValue(null,isolatedSettings);
        // Native ACC's real Tick is intentionally still executable in this
        // fixture, proving that late/hostile native publications cannot mix
        // into the new owner's physical axis even if the plugin is re-enabled.
        ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
        foreach(float hostile in new[]{1f,-.7f}) {
            Set(acc,"ReplayTarget",hostile);TickAcc();Frame();
            Assert(Gas()==0&&Brake()==0,"Real native ACC calculation has no normal output authority: "+hostile);
        }
        Send(AccId,Epoch(),.3f);Set(acc,"ReplayTarget",-1f);TickAcc();Frame();
        Assert(Math.Abs(Gas()-.3f)<.001&&Brake()==0,"A real native brake cannot compete with the one new local controller");
        ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
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
        void Pause(long origin,long? issuedAt=null){Set(lane,"wasActive",true);Set(lane,"tickCounter",6);laneType.GetMethod("CheckPlaySound",All)!.Invoke(lane,[false,false,origin,issuedAt??Stopwatch.GetTimestamp()]);}
        long pauseEpoch=Epoch();Pause(pauseEpoch);Frame();
        Assert(legacy.GetValueOrDefault(offsets["pause"])==1,"Actual native lane-error helper emits an authorized pause pulse");
        Enable(false);Frame();Thread.Sleep(70);Frame();
        Assert(legacy.GetValueOrDefault(offsets["pause"])==0,"Pause pulse is cleared on takeover without a deferred toggle task");
        Enable(true);Pause(pauseEpoch);Frame();
        Assert(legacy.GetValueOrDefault(offsets["pause"])==0,"Actual old-origin lane-error helper cannot pause a new session");
        Pause(Epoch(),Stopwatch.GetTimestamp()-Stopwatch.Frequency);Frame();
        Assert(legacy.GetValueOrDefault(offsets["pause"])==0,"Actual same-session lane pause helper preserves its old tick origin and cannot freshly stamp a stale calculation");
        SourceFrameReplay.Run(context,gameAsm,outputType,Assert);
        foreach(bool modernMode in new[]{false,true}) {
            Enable(false);Enable(true);((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
            var owned=Send(AccId,Epoch(),.4f);Frame(modernMode);
            Assert(modernMode?modern.GetValueOrDefault(13)>0:Gas()>0,"Fresh own source epoch admits normal output before source Close");
            long oldSource=(long)Get(owned,"MautSourceEpoch")!;
            outputType.GetMethod("MautInvalidateOwnSources")!.Invoke(null,null);
            Set(owned,"MautSourceEpoch",outputType.GetField("MautOwnSourceEpoch")!.GetValue(null));Frame(modernMode);
            Assert(Gas()==0&&Brake()==0&&modern.GetValueOrDefault(13)==0,"Source Close revokes already stored throttle in either output mode; publisher mutation cannot relabel its snapshot");
            var staleDefinition=Send(AccId,Epoch(),.6f);Set(staleDefinition,"MautSourceEpoch",oldSource);
            // Re-submit the explicit stale epoch with a fresh issue clock.
            var staleMessage=Activator.CreateInstance(eventType)!;var vars=Activator.CreateInstance(variablesType)!;Set(vars,"aforward",.8f);
            Set(staleDefinition,"MautIssuedAt",Stopwatch.GetTimestamp());Set(staleMessage,"ChannelDefinition",staleDefinition);
            Set(staleMessage,"Properties",Activator.CreateInstance(propertiesType));Set(staleMessage,"Variables",vars);
            ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();outputType.GetMethod("OnControlEvent")!.Invoke(host,[staleMessage]);Frame(modernMode);
            Assert(Gas()==0&&Brake()==0&&modern.GetValueOrDefault(13)==0,"Stale source epoch is rejected at ingress even with a fresh command issue time");
            ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
            var aging=Send(AccId,Epoch(),.4f);Set(aging,"MautSourceAt",Stopwatch.GetTimestamp()-Stopwatch.Frequency*19/100);
            Set(aging,"MautIssuedAt",Stopwatch.GetTimestamp());
            var ageMessage=Activator.CreateInstance(eventType)!;Set(ageMessage,"ChannelDefinition",aging);Set(ageMessage,"Properties",Activator.CreateInstance(propertiesType));Set(ageMessage,"Variables",vars);
            outputType.GetMethod("OnControlEvent")!.Invoke(host,[ageMessage]);Frame(modernMode);
            Assert(modernMode?modern.GetValueOrDefault(13)>0:Gas()>0,"A source still within200ms may publish a bounded positive command");
            Thread.Sleep(20);SourceFrameReplay.Capture(outputType,"Traffic",SourceFrameReplay.EmptyTraffic(gameAsm));Frame(modernMode);
            Assert(Gas()==0&&Brake()==0&&modern.GetValueOrDefault(13)==0,"Original source expires at physical Frame despite a newer provider publication and still-fresh command");
            foreach(long invalidOrigin in new[]{0L,-1L,Stopwatch.GetTimestamp()+Stopwatch.Frequency}) {
                ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
                Set(aging,"MautIssuedAt",Stopwatch.GetTimestamp());Set(aging,"MautSourceAt",invalidOrigin);
                outputType.GetMethod("OnControlEvent")!.Invoke(host,[ageMessage]);Frame(modernMode);
                Assert(Gas()==0&&Brake()==0&&modern.GetValueOrDefault(13)==0,"Nonpositive/future source origin cannot authorize throttle");
            }
            ((System.Collections.IDictionary)Get(host,"Channels")!).Clear();
            var noGas=Send(AccId,Epoch(),-.35f);Set(noGas,"MautSourceAt",0L);Set(noGas,"MautIssuedAt",Stopwatch.GetTimestamp());
            var noGasVars=Activator.CreateInstance(variablesType)!;Set(noGasVars,"abackward",-.35f);Set(ageMessage,"ChannelDefinition",noGas);Set(ageMessage,"Variables",noGasVars);
            outputType.GetMethod("OnControlEvent")!.Invoke(host,[ageMessage]);Frame(modernMode);
            Assert(modernMode?modern.GetValueOrDefault(13)<0:Brake()>0,"Unknown-source ordinary no-gas braking remains available with source origin0");

        }

        PaymentReplay.Run(outputType,host,Frame,Enable,Epoch,()=>legacy.GetValueOrDefault(offsets["activate"]),Assert,
            activationWrites,()=>outputType.GetMethod("OnControlEvent")!.Invoke(host,[NewNeutralEvent()]));
        if(Environment.GetEnvironmentVariable("MAUT_PLUGIN_CANDIDATE") is {Length:>0} pluginCandidate)
            PluginHoldReplay.Run(pluginCandidate,root,prepared,sandbox,context,gameAsm,stateAsm,host,state,Frame,Gas,Brake,Enable,Epoch,Assert,()=> (float)modern.GetValueOrDefault(13),()=> (float)modern.GetValueOrDefault(0));
        InputBridgeHostReplay.RunIfRequested(outputType,host,state,legacyMarker,modernMarker,offsets,Assert);
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
    void AfterEvaluation()
    {
        if(blockEvaluation){entered.Set();if(!release.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException("Native evaluation release timed out");}
    }
    void PrepareAcc(AssemblyLoadContext context,Assembly game,Assembly stateAssembly,string sandbox)
    {
        var camera=game.GetType("ETS2LA.Game.SDK.CameraProvider",true)!;
        camera.GetField("ReplayCurrent")!.SetValue(null,RuntimeHelpers.GetUninitializedObject(camera));
        var cameraData=camera.GetField("ReplayData")!.FieldType;var cameraValue=RuntimeHelpers.GetUninitializedObject(cameraData);
        Set(cameraValue,"truckRotation",System.Numerics.Quaternion.Identity);camera.GetField("ReplayData")!.SetValue(null,cameraValue);
        var accAssembly=context.LoadFromAssemblyPath(Path.Combine(sandbox,"AdaptiveCruiseControl.dll"));
        var type=accAssembly.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin",true)!;acc=RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("ReplayAfterEvaluation")!.SetValue(null,(Action)AfterEvaluation);
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
