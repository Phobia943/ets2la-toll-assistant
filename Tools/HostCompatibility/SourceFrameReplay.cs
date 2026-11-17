using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Numerics;
using Mono.Cecil;
using Mono.Cecil.Cil;
using B=System.Reflection.BindingFlags;
namespace MautHostCompatibility;

internal static class SourceFrameReplay
{
    const B All=B.Instance|B.Static|B.Public|B.NonPublic;
    static void Set(object o,string n,object? v) {
        if(o.GetType().GetProperty(n,All) is {} p)p.SetValue(o,v);else o.GetType().GetField(n,All)!.SetValue(o,v);
    }
    static object? Get(object o,string n)=>o.GetType().GetProperty(n,All) is {} p?p.GetValue(o):o.GetType().GetField(n,All)!.GetValue(o);
    static Action<T> Sink<T>(Action<object> f)=>value=>f(value!);
    internal static void PrepareSandbox(TypeDefinition provider)
    {
        // Clone the ACTUAL decoder/update. Only memory-opening/ReadArray are
        // replaced: it decodes a supplied private byte array, then executes the
        // real capture-before-Publish and real event callback ordering.
        var source=Rewrite.Method(provider,"Update");
        var copy=new MethodDefinition("ReplayOwnUpdate",Mono.Cecil.MethodAttributes.Public,provider.Module.TypeSystem.Void);
        provider.Methods.Add(copy);Rewrite.CopyBody(source,copy,provider,provider);
        var il=copy.Body.GetILProcessor();int opens=0,reads=0;
        var hook=new FieldDefinition("ReplayBeforeOwnCapture",Mono.Cecil.FieldAttributes.Public|Mono.Cecil.FieldAttributes.Static,provider.Module.ImportReference(typeof(Action)));
        provider.Fields.Add(hook);
        var capture=copy.Body.Instructions.Single(i=>i.Operand is MethodReference m&&m.Name.StartsWith("MautCaptureOwn",StringComparison.Ordinal));
        // The actual capture has four arguments loaded immediately before it.
        var hookAt=capture.Previous.Previous.Previous.Previous;
        il.InsertBefore(hookAt,Instruction.Create(OpCodes.Ldsfld,hook));
        il.InsertBefore(hookAt,Instruction.Create(OpCodes.Callvirt,provider.Module.ImportReference(typeof(Action).GetMethod("Invoke")!)));
        foreach(var instruction in copy.Body.Instructions.ToArray()) {
            if(instruction.Operand is not MethodReference call)continue;
            if(call.DeclaringType.FullName==provider.FullName&&call.Name=="TryOpenMemory") {
                instruction.OpCode=OpCodes.Pop;instruction.Operand=null;il.InsertAfter(instruction,Instruction.Create(OpCodes.Ldc_I4_1));opens++;
            } else if(call.DeclaringType.FullName is "System.IO.MemoryMappedFiles.MemoryMappedViewAccessor" or "System.IO.UnmanagedMemoryAccessor"&&call.Name=="ReadArray") {
                for(int i=0;i<call.Parameters.Count+(call.HasThis?1:0);i++)il.InsertBefore(instruction,Instruction.Create(OpCodes.Pop));
                instruction.OpCode=OpCodes.Ldc_I4_0;instruction.Operand=null;reads++;
            }
        }
        if(opens!=1||reads!=1||copy.Body.Instructions.Any(i=>i.Operand is MethodReference m&&
            (m.Name=="OpenExisting"||m.Name=="ReadArray"&&m.DeclaringType.FullName is "System.IO.MemoryMappedFiles.MemoryMappedViewAccessor" or "System.IO.UnmanagedMemoryAccessor")))
            throw new InvalidDataException("Unverified private provider decoder fixture: "+provider.FullName+" opens="+opens+" reads="+reads);
    }
    internal static object EmptyTraffic(Assembly game,int count=39)
    {
        var type=game.GetType("ETS2LA.Game.SDK.TrafficData",true)!;var data=Activator.CreateInstance(type)!;
        var vehicleType=type.GetField("vehicles")!.FieldType.GetElementType()!;var list=Array.CreateInstance(vehicleType,count);
        for(int i=0;i<count;i++) {
            var v=Activator.CreateInstance(vehicleType)!;var trailerType=vehicleType.GetField("trailers")!.FieldType.GetElementType()!;
            Set(v,"Position",Vector3.Zero);Set(v,"Rotation",default(Quaternion));Set(v,"Size",Vector3.Zero);
            Set(v,"speed",0f);Set(v,"acceleration",0f);Set(v,"trailer_count",(short)0);Set(v,"id",(short)0);Set(v,"isTMP",false);Set(v,"isTrailer",false);
            var trailers=Array.CreateInstance(trailerType,3);
            for(int t=0;t<3;t++){var trailer=Activator.CreateInstance(trailerType)!;Set(trailer,"parent",v);Set(trailer,"Position",Vector3.Zero);
                Set(trailer,"Rotation",default(Quaternion));Set(trailer,"Size",Vector3.Zero);trailers.SetValue(trailer,t);}
            Set(v,"trailers",trailers);list.SetValue(v,i);
        }
        Set(data,"vehicles",list);return data;
    }
    internal static object EmptyParked(Assembly game,int count=40)
    {
        var type=game.GetType("ETS2LA.Game.SDK.ParkedVehicleData",true)!;var data=Activator.CreateInstance(type)!;
        var collection=type.GetField("vehicles")!.FieldType;var list=(System.Collections.IList)Activator.CreateInstance(collection)!;
        for(int i=0;i<count;i++){var v=Activator.CreateInstance(collection.GenericTypeArguments[0])!;Set(v,"Position",Vector3.Zero);
            Set(v,"Rotation",default(Quaternion));Set(v,"Size",Vector3.Zero);Set(v,"id",0);Set(v,"isTrailer",false);list.Add(v);}
        Set(data,"vehicles",list);return data;
    }
    internal static object EmptySignals(Assembly game)
    {
        var type=game.GetType("ETS2LA.Game.SDK.SemaphoreData",true)!;var data=Activator.CreateInstance(type)!;
        Set(data,"semaphores",Array.CreateInstance(type.GetField("semaphores")!.FieldType.GetElementType()!,0));return data;
    }
    internal static void Capture(Type output,string kind,object data,long? origin=null,long? epoch=null)=>output.GetMethod("MautCaptureOwn"+kind)!.Invoke(null,[data,origin??Stopwatch.GetTimestamp(),epoch??(long)output.GetMethod("MautReadOwnSourceEpoch")!.Invoke(null,null)!]);
    static object Frame(Type output,string kind)=>output.GetField("MautOwn"+kind+"Frame")!.GetValue(null)!;
    static object Data(object frame)=>frame.GetType().GetProperty("Item2")!.GetValue(frame)!;
    static long Origin(object frame)=>(long)frame.GetType().GetProperty("Item1")!.GetValue(frame)!;

    internal static void Run(AssemblyLoadContext context,Assembly game,Type output,Action<bool,string> check)
    {
        var traffic=EmptyTraffic(game,1);var actor=((Array)Get(traffic,"vehicles")!).GetValue(0)!;
        Set(actor,"Position",new Vector3(1,2,3));Set(actor,"Rotation",new Quaternion(1,0,0,0));Set(actor,"Size",new Vector3(2,4,6));
        Set(actor,"speed",7f);Set(actor,"acceleration",1f);Set(actor,"trailer_count",(short)2);Set(actor,"id",(short)123);Set(actor,"isTMP",true);
        var sourceTrailer=((Array)Get(actor,"trailers")!).GetValue(0)!;Set(sourceTrailer,"Position",new Vector3(4,5,6));
        Capture(output,"Traffic",EmptyTraffic(game,0)); // JIT before assigning short producer origins.
        long origin=Stopwatch.GetTimestamp();Capture(output,"Traffic",traffic,origin);var frame=Frame(output,"Traffic");
        var copiedActor=((Array)Get(Data(frame),"vehicles")!).GetValue(0)!;
        Set(actor,"Position",new Vector3(99));Set(actor,"speed",99f);Set(actor,"id",(short)999);Set(actor,"isTMP",false);
        Set(sourceTrailer,"Position",new Vector3(99));Set(sourceTrailer,"parent",null);
        check(Origin(frame)==origin&&(Vector3)Get(copiedActor,"Position")! ==new Vector3(1,2,3)&&
            (float)Get(copiedActor,"speed")! ==7f&&(short)Get(copiedActor,"id")! ==123&&(bool)Get(copiedActor,"isTMP")!,
            "Own traffic frame binds original origin and copied actor identity/flags/physics instead of mutable provider data");
        var copiedTrailer=((Array)Get(copiedActor,"trailers")!).GetValue(0)!;
        check((Vector3)Get(copiedTrailer,"Position")! ==new Vector3(4,5,6)&&ReferenceEquals(Get(copiedTrailer,"parent"),copiedActor)&&
            !ReferenceEquals(copiedActor,actor),"Every copied trailer links to its copied parent, never the original mutable actor");
        Set(traffic,"vehicles",Array.CreateInstance(actor.GetType(),0));
        check(((Array)Get(Data(frame),"vehicles")!).Length==1,"Replacing original traffic array cannot change an accepted owned frame");
        var parked=EmptyParked(game,1);var parkedActor=((System.Collections.IList)Get(parked,"vehicles")!)[0]!;
        Set(parkedActor,"id",(short)8);Set(parkedActor,"isTrailer",true);Set(parkedActor,"Position",new Vector3(2,3,4));
        Capture(output,"Parked",parked);var parkedCopy=((System.Collections.IList)Get(Data(Frame(output,"Parked")),"vehicles")!)[0]!;
        Set(parkedActor,"id",(short)0);Set(parkedActor,"Position",Vector3.Zero);
        check(Convert.ToInt32(Get(parkedCopy,"id"))==8&&(bool)Get(parkedCopy,"isTrailer")!&&(Vector3)Get(parkedCopy,"Position")! ==new Vector3(2,3,4),
            "Owned parked frame preserves actor and trailer flags after provider mutation");
        var signalData=EmptySignals(game);var signalType=signalData.GetType().GetField("semaphores")!.FieldType.GetElementType()!;
        var signal=Activator.CreateInstance(signalType)!;Set(signal,"id",7);Set(signal,"state",1);Set(signal,"position",new Vector3(4,5,6));
        var signalArray=Array.CreateInstance(signalType,1);signalArray.SetValue(signal,0);Set(signalData,"semaphores",signalArray);
        Capture(output,"Semaphore",signalData);var signalCopy=((Array)Get(Data(Frame(output,"Semaphore")),"semaphores")!).GetValue(0)!;
        Set(signal,"state",3);Set(signal,"position",new Vector3(99));
        check((int)Get(signalCopy,"state")! ==1&&(Vector3)Get(signalCopy,"position")! ==new Vector3(4,5,6),"Owned signal state/anchor cannot change after capture");
        Capture(output,"Traffic",EmptyTraffic(game),Stopwatch.GetTimestamp()-Stopwatch.Frequency*23/100);
        check(output.GetField("MautOwnTrafficFrame")!.GetValue(null)==null,"An already230ms-old source cannot acquire a fresh owned frame");
        Capture(output,"Traffic",EmptyTraffic(game,40));
        check(output.GetField("MautOwnTrafficFrame")!.GetValue(null)==null,"Overflowing provider slots are rejected instead of silently truncated");

        // Execute a copied actual provider decoder over private zero bytes, with
        // a real synchronous Publish callback deliberately held for230ms.
        var providerType=game.GetType("ETS2LA.Game.SDK.TrafficProvider",true)!;
        providerType.GetField("ReplayBeforeOwnCapture")!.SetValue(null,(Action)(()=>{}));
        var provider=RuntimeHelpers.GetUninitializedObject(providerType);var buffer=new byte[6800];
        Set(provider,"_buffer",buffer);Set(provider,"_reader",Activator.CreateInstance(providerType.GetField("_reader",All)!.FieldType,[buffer]));
        Set(provider,"_currentData",EmptyTraffic(game,0));Set(provider,"_lastVehicles",Array.CreateInstance(actor.GetType(),0));
        Set(provider,"_lastVehicleById",Activator.CreateInstance(providerType.GetField("_lastVehicleById",All)!.FieldType));
        Set(provider,"_sinceReconnect",Stopwatch.StartNew());string eventName="private.own-source."+Guid.NewGuid();Set(provider,"EventString",eventName);
        var backend=context.LoadFromAssemblyName(new AssemblyName("ETS2LA.Backend"));var events=backend.GetType("ETS2LA.Backend.Events.Events",true)!;
        var bus=events.GetProperty("Current")!.GetValue(null)!;bool hadFrameBeforeCallback=false;long callbackOrigin=0;bool holdPublish=true;
        var handler=typeof(SourceFrameReplay).GetMethod(nameof(Sink),All)!.MakeGenericMethod(traffic.GetType()).Invoke(null,[(Action<object>)(_=> {
            var observed=Frame(output,"Traffic");hadFrameBeforeCallback=observed!=null;callbackOrigin=observed==null?0:Origin(observed);
            if(holdPublish)Thread.Sleep(230);
        })]);
        events.GetMethods().Single(m=>m.Name=="Subscribe"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2)
            .MakeGenericMethod(traffic.GetType()).Invoke(bus,[eventName,handler]);
        providerType.GetMethod("ReplayOwnUpdate")!.Invoke(provider,null);
        var after=Frame(output,"Traffic");
        check(hadFrameBeforeCallback&&after!=null&&Origin(after)==callbackOrigin&&
            !(bool)output.GetMethod("MautOwnOriginFresh")!.Invoke(null,[Origin(after)])!,
            "Actual private provider Update captures BEFORE blocked Publish; its230ms-old origin is never freshly stamped afterward");
        holdPublish=false;
        var tuple=Frame(output,"Traffic");long epoch=(long)output.GetField("MautOwnSourceEpoch")!.GetValue(null)!;
        providerType.GetMethod("MautReconnectMemory",All)!.Invoke(provider,null);
        check(ReferenceEquals(tuple,Frame(output,"Traffic"))&&(long)output.GetField("MautOwnSourceEpoch")!.GetValue(null)! ==epoch,
            "Successful reconnect preserves owned tuple, original origin and source epoch");
        providerType.GetMethod("CloseMemory",All)!.Invoke(provider,null);
        check(output.GetField("MautOwnTrafficFrame")!.GetValue(null)==null&&(long)output.GetField("MautOwnSourceEpoch")!.GetValue(null)! >epoch,
            "Actual provider close revokes owned source authority and advances the monotonic source epoch");
        Capture(output,"Traffic",EmptyTraffic(game));
        var renewed=Frame(output,"Traffic");
        // A decoder already in flight still carries its entry epoch. It may
        // neither attest an old read nor erase a newer valid post-close frame.
        Capture(output,"Traffic",EmptyTraffic(game),Stopwatch.GetTimestamp(),epoch);
        check(ReferenceEquals(renewed,Frame(output,"Traffic")),"An old-epoch decoder completing after Close cannot attest or erase a newer source frame");
        Capture(output,"Traffic",EmptyTraffic(game,40),Stopwatch.GetTimestamp(),epoch);
        check(ReferenceEquals(renewed,Frame(output,"Traffic")),"Malformed old-epoch decoder cannot erase a valid post-close frame");
        check(Frame(output,"Traffic")!=null&&(long)output.GetField("MautOwnSourceEpoch")!.GetValue(null)! !=epoch,
            "A fresh post-close snapshot cannot hide the earlier source-epoch revocation");
        using var decoderEntered=new ManualResetEventSlim();using var decoderRelease=new ManualResetEventSlim();
        providerType.GetField("ReplayBeforeOwnCapture")!.SetValue(null,(Action)(()=>{
            decoderEntered.Set();if(!decoderRelease.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Private decoder not released");}));
        var decoding=Task.Run(()=>providerType.GetMethod("ReplayOwnUpdate")!.Invoke(provider,null));
        if(!decoderEntered.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Private decoder did not reach capture barrier");
        try {
            providerType.GetMethod("CloseMemory",All)!.Invoke(provider,null);
            Capture(output,"Traffic",EmptyTraffic(game));renewed=Frame(output,"Traffic");
        } finally {decoderRelease.Set();if(!decoding.Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Private decoder did not finish");}
        check(ReferenceEquals(renewed,Frame(output,"Traffic")),"Real Update entry-epoch survives a decode barrier: Close plus fresh frame cannot reattest its pre-close read");
        providerType.GetField("ReplayBeforeOwnCapture")!.SetValue(null,(Action)(()=>{}));
    }
}
