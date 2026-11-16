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
        foreach(string pure in new[]{"FaultBrake","SceneStopHold","SceneHoldBrake","PlannedHoldBrake"})
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
        if((string?)output.GetField("MautCompatibility")!.GetRawConstantValue()!="0.3.18-host-3")throw new InvalidDataException("Plugin hold replay requires Host3");

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
        void NativeBrake(float value)
        {
            var definition=Activator.CreateInstance(definitionType)!;Set(definition,"Id","AdaptiveCruiseControl.Acceleration");
            Set(definition,"Timeout",1f);Set(definition,"MautEpoch",epoch());Set(definition,"MautIssuedAt",Stopwatch.GetTimestamp());
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
        Reset();Floors(.65f,0);Commit();NativeBrake(1);frame(false);
        Check(brake()==1&&gas()==0,"a genuine native full brake is not diluted by the combined plugin floor");

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
        Reset();enable(false);frame(false);
        File.WriteAllText(Path.Combine(prepared,"plugin-hold-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{
            checks,passed=true,pluginSha256=sealedHash,
            pluginMvid=assembly.ManifestModule.ModuleVersionId,hostMvid=gameAssembly.ManifestModule.ModuleVersionId,
            liveMapsOpened=false,pluginInstanceConstructorsCalled=false,faultConstructorsCalled=false,
            coreHoldConstructorsCalled=false,auditedStaticInitializersOnly=true,outputThreadsStarted=false,
            scope="Real CommitPlannedHolds/ClearSceneHold with controlled floor inputs and isolated real output sinks"}));
    }
}
