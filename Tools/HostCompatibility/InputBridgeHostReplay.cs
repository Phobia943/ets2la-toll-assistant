using System.Collections;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MautHostCompatibility;

// The actual pinned output methods are already loaded in Replay's sealed ALC.
// Only their storage terminals are redirected to this test DLL's GUID map.
// This does not execute native producer ticks, a game, or any live SDK mapping.
internal static class InputBridgeHostReplay
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    const string Pedals="local.mautassistent.longitudinal",Lane="LaneAssist.SteeringCorrection";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint Marker();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Initialize(uint version,ref InitParameters parameters);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Shutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Register(nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Log(int type,nint text);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Active(byte active,nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Next(out InputEvent input,uint flags,nint context);
    [StructLayout(LayoutKind.Sequential)] struct InitParameters
    {public nint GameName,GameId;public uint GameVersion,Padding;public nint Log,Register;}
    [StructLayout(LayoutKind.Sequential)] struct Device
    {public nint Name,DisplayName;public uint Type,Count;public nint Inputs,Context,Active,Next;}
    [StructLayout(LayoutKind.Explicit,Size=28)] struct InputEvent
    {[FieldOffset(0)]public uint Index;[FieldOffset(4)]public float Float;[FieldOffset(4)]public byte Bool;}
    static void Set(object value,string field,object? data)=>value.GetType().GetField(field,All)!.SetValue(value,data);
    static object? Get(object value,string field)=>value.GetType().GetField(field,All)!.GetValue(value);
    static T Export<T>(nint library,string name) where T:Delegate=>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));

    internal static void RunIfRequested(Type output,object host,object state,object legacyMarker,object modernMarker,
        IReadOnlyDictionary<string,int> offsets,Action<bool,string> assert)
    {
        var requested=Environment.GetEnvironmentVariable("MAUT_INPUT_BRIDGE_TEST");
        if(string.IsNullOrWhiteSpace(requested))return;
        if(!OperatingSystem.IsWindows()||IntPtr.Size!=8)throw new NotSupportedException("Input bridge replay requires Win64");
        if(AssemblyLoadContext.GetLoadContext(output.Assembly) is not {IsCollectible:true}||
            output.GetField("ReplayWriteFloat",All)==null||output.GetField("ReplayCurrent",All)?.GetValue(null)!=host)
            throw new InvalidOperationException("Only Replay's sealed isolated host may enter the bridge fixture");
        string file=Path.GetFullPath(requested);
        // Inspect before loading or calling init. Reject the product candidate,
        // negative control, missing marker and every non-GUID command namespace.
        if(Path.GetFileName(file)!="InputBridge.Test.dll")throw new InvalidDataException("Only the protected test DLL is accepted");
        byte[] bytes=File.ReadAllBytes(file);string content=Encoding.Latin1.GetString(bytes);
        var commandNames=Regex.Matches(content,@"Local\\MautInputBridgeTest_(run_[0-9a-f]{32})_Commands\x00");
        var statusNames=Regex.Matches(content,@"Local\\MautInputBridgeTest_(run_[0-9a-f]{32})_Status\x00");
        if(commandNames.Count!=1||statusNames.Count!=1||commandNames[0].Groups[1].Value!=statusNames[0].Groups[1].Value||
            content.Contains("Local\\SCSControls",StringComparison.Ordinal)||content.Contains("MautInputBridgeStatus_v1",StringComparison.Ordinal))
            throw new InvalidDataException("Test DLL map namespaces were not uniquely isolated");
        string commands=commandNames[0].Value.TrimEnd('\0'),status=statusNames[0].Value.TrimEnd('\0');
        nint library=0,gameName=0,gameId=0;bool initialized=false;
        Shutdown? shutdown=null;Device device=default;Exception? registrationError=null;
        var inputs=new List<(string Name,int Type,int Offset)>();
        int checks=0;void Check(bool condition,string reason){assert(condition,reason);checks++;}
        var floatField=output.GetField("ReplayWriteFloat",All)!;
        var boolField=output.GetField("ReplayMautRawWriteBool",All)!;
        var doubleField=output.GetField("ReplayWriteDouble",All)!;
        var oldFloat=floatField.GetValue(null);var oldBool=boolField.GetValue(null);var oldDouble=doubleField.GetValue(null);
        Register register=pointer=> {
            try {
                device=Marshal.PtrToStructure<Device>(pointer);
                if(device.Count!=276||device.Context==0||device.Next==0||device.Active==0||Marshal.PtrToStringUTF8(device.Name)!="laneassist")
                    throw new InvalidDataException("Unexpected native device identity");
                int offset=0;
                for(int i=0;i<device.Count;i++) {
                    nint item=device.Inputs+i*24;string name=Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(item))!;
                    int type=Marshal.ReadInt32(item+16);
                    if(type is not (1 or 5)||!offsets.TryGetValue(name,out int hostOffset)||hostOffset!=offset)
                        throw new InvalidDataException("Native ABI differs from actual host layout: "+name);
                    inputs.Add((name,type,offset));offset+=type==5?4:1;
                }
                if(offset!=342||offsets.Count!=inputs.Count)throw new InvalidDataException("Host/native input extent mismatch");
                return 0;
            } catch(Exception ex){registrationError=ex;return -7;}
        };
        Log log=(_,_)=>{};
        try {
            library=NativeLibrary.Load(file);
            if(Export<Marker>(library,"MautInputBridgeTestMarker")()!=0x54455354)
                throw new InvalidDataException("Unprotected or negative native adapter rejected before init");
            shutdown=Export<Shutdown>(library,"scs_input_shutdown");
            gameName=Marshal.StringToCoTaskMemUTF8("Isolated output replay");gameId=Marshal.StringToCoTaskMemUTF8("input_bridge_test");
            var init=new InitParameters{GameName=gameName,GameId=gameId,GameVersion=1,
                Log=Marshal.GetFunctionPointerForDelegate(log),Register=Marshal.GetFunctionPointerForDelegate(register)};
            if(Marshal.SizeOf<InitParameters>()!=40||Marshal.SizeOf<Device>()!=56||Marshal.SizeOf<InputEvent>()!=28)
                throw new InvalidDataException("SCS interop ABI has unexpected sizes");
            int result=Export<Initialize>(library,"scs_input_init")(0x10000,ref init);
            if(result!=0)throw new InvalidOperationException("Native test initialization failed",registrationError);
            initialized=true;
            Check(inputs.Count==276,"Actual pinned host layout matches all native InputBridge inputs before any host write");
            using var map=MemoryMappedFile.OpenExisting(commands,MemoryMappedFileRights.ReadWrite);
            using var view=map.CreateViewAccessor(0,342,MemoryMappedFileAccess.ReadWrite);
            using var statusMap=MemoryMappedFile.OpenExisting(status,MemoryMappedFileRights.Read);
            using var statusView=statusMap.CreateViewAccessor(0,216,MemoryMappedFileAccess.Read);
            var modern=new Dictionary<int,double>();
            void Write(MemoryMappedViewAccessor target,int offset,double value,int size) {
                if(ReferenceEquals(target,modernMarker)){modern[offset]=value;return;}
                if(!ReferenceEquals(target,legacyMarker)||offset<0||offset+size>342)throw new InvalidOperationException("Unknown isolated sink");
                if(size==1)view.Write(offset,value!=0);else if(size==4)view.Write(offset,(float)value);
                else throw new InvalidOperationException("Legacy does not have double inputs");
            }
            floatField.SetValue(null,(Action<MemoryMappedViewAccessor,int,float>)((target,offset,value)=>Write(target,offset,value,4)));
            boolField.SetValue(null,(Action<MemoryMappedViewAccessor,int,bool>)((target,offset,value)=>Write(target,offset,value?1:0,1)));
            doubleField.SetValue(null,(Action<MemoryMappedViewAccessor,int,double>)((target,offset,value)=>Write(target,offset,value,8)));
            var active=ExportPointer<Active>(device.Active);var next=ExportPointer<Next>(device.Next);
            void Frame()=>output.GetMethod("MautFrame",All)!.Invoke(host,[false]);
            void Enable(bool value)=>state.GetType().GetProperty("EnableAssists",All)!.SetValue(state,value);
            long Epoch()=>(long)output.GetMethod("MautCaptureEpoch",All)!.Invoke(null,null)!;
            void Clear(){Enable(false);((IDictionary)Get(host,"Channels")!).Clear();Frame();}
            object Send(string id,float pedal=0,float? steering=null,long? epoch=null,long? origin=null) {
                var assembly=output.Assembly;
                object New(string type)=>Activator.CreateInstance(assembly.GetType("ETS2LA.Game.Output."+type,true)!)!;
                var definition=New("ControlChannelDefinition");long stamp=origin??Stopwatch.GetTimestamp();
                Set(definition,"Id",id);Set(definition,"Timeout",1f);Set(definition,"MautEpoch",epoch??Epoch());
                Set(definition,"MautIssuedAt",stamp);Set(definition,"MautSourceAt",stamp);
                Set(definition,"MautSourceEpoch",output.GetField("MautOwnSourceEpoch",All)!.GetValue(null));
                var variables=New("ControlVariables");
                if(steering.HasValue)Set(variables,"steering",steering.Value);else Set(variables,pedal<0?"abackward":"aforward",pedal);
                var properties=New("ControlProperties");Set(properties,"Weight",1f);
                var message=New("ControlEvent");Set(message,"ChannelDefinition",definition);Set(message,"Properties",properties);Set(message,"Variables",variables);
                output.GetMethod("OnControlEvent",All)!.Invoke(host,[message]);return message;
            }
            Dictionary<string,float> ReadFrame(bool reactivated=false,Action<uint>? afterEvent=null) {
                var values=new Dictionary<string,float>();
                for(uint i=0;i<inputs.Count;i++) {
                    int code=next(out var input,i==0?(reactivated?3u:1u):0,device.Context);
                    if(code!=0||input.Index!=i)throw new InvalidOperationException("Native SDK event enumeration changed");
                    var descriptor=inputs[(int)i];values[descriptor.Name]=descriptor.Type==5?input.Float:input.Bool;
                    afterEvent?.Invoke(i);
                }
                if(next(out _,0,device.Context)!=-4)throw new InvalidOperationException("Native SDK frame did not terminate");
                return values;
            }
            static bool Neutral(Dictionary<string,float> values)=>values["aforward"]==0&&values["abackward"]==0&&values["steering"]==0;
            Clear();state.GetType().GetProperty("DrivingMode",All)!.SetValue(state,Enum.ToObject(state.GetType().GetProperty("DrivingMode",All)!.PropertyType,1));
            Enable(true);active(1,device.Context);
            Send(Pedals,.6f);Send(Lane,steering:.25f);Frame();var observed=ReadFrame(true);
            Check(Math.Abs(observed["aforward"]-.6f)<.001&&observed["abackward"]==0,
                "Real host throttle frame reaches the GUID map and actual native SCS callback");
            Check(observed["steering"]==0&&Math.Abs(modern.GetValueOrDefault(0)-.25)<.001&&modern.GetValueOrDefault(4)==1,
                "Real host steering stays on its modern output; no invented legacy steering path");
            Check(statusView.ReadUInt64(144)>0&&Math.Abs(statusView.ReadSingle(160)-.6f)<.001,
                "Native callback diagnosis independently reports the returned throttle event");
            Send(Pedals,-.7f);Frame();observed=ReadFrame();
            Check(observed["aforward"]==0&&Math.Abs(observed["abackward"]-.7f)<.001,
                "Real host braking clears throttle and reaches the native callback as a positive brake input");
            long revoked=Epoch();Enable(false);Frame();observed=ReadFrame();
            Check(Neutral(observed)&&modern.GetValueOrDefault(0)==0&&modern.GetValueOrDefault(4)==0,
                "Manual-disable host transition clears pedals and releases modern steering before callback polling");
            Enable(true);Send(Pedals,.9f,epoch:revoked);Frame();observed=ReadFrame();
            Check(Neutral(observed),"An old assist epoch cannot produce native input after re-enabling");

            long original=Stopwatch.GetTimestamp();var originalMessage=Send(Pedals,.45f,origin:original);Send(Lane,steering:.2f);Frame();ReadFrame();
            active(0,device.Context);ulong beforeInactive=statusView.ReadUInt64(104);
            while(Stopwatch.GetTimestamp()-original<=Stopwatch.Frequency*.23)Thread.Sleep(1);
            Frame(); // The real host expires the original command while the SDK does not poll.
            Check(statusView.ReadUInt64(104)==beforeInactive,
                "Inactive SDK polling is not fabricated as native callback acknowledgement");
            active(1,device.Context);observed=ReadFrame(true);
            Check(Neutral(observed)&&modern.GetValueOrDefault(0)==0,
                "Original host deadline expires during inactivity and reactivation cannot replay cached pedals");
            output.GetMethod("OnControlEvent",All)!.Invoke(host,[originalMessage]);Frame();observed=ReadFrame();
            Check(Neutral(observed)&&(long)Get(Get(originalMessage,"ChannelDefinition")!,"MautIssuedAt")! == original,
                "Replaying a stale command after activation neither restores input nor changes its original timestamp");
            Send(Pedals,.35f);Frame();observed=ReadFrame();
            Check(Math.Abs(observed["aforward"]-.35f)<.001&&observed["abackward"]==0,
                "A genuinely new command after activation traverses the same real host/native pipeline");
            Send(Pedals,.55f);Frame();observed=ReadFrame(afterEvent:index=>{if(index==70){Enable(false);Frame();}});
            Check(Neutral(observed),"Manual disable during a partially polled SDK frame clears the still-unrequested pedal events");
            Clear();ReadFrame();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
                kind="pinned-host-to-native-inputbridge",checks,passed=true,liveMapsOpened=false,
                bridgeSha256=Convert.ToHexString(SHA256.HashData(bytes)),commandMap=commands,
                scope="Authored commands -> actual sealed host -> GUID storage terminal -> native SCS callbacks. No game input/physics proof; steering uses modern output."
            }));
        } finally {
            floatField.SetValue(null,oldFloat);boolField.SetValue(null,oldBool);doubleField.SetValue(null,oldDouble);
            if(initialized)shutdown!();if(library!=0)NativeLibrary.Free(library);
            if(gameName!=0)Marshal.FreeCoTaskMem(gameName);if(gameId!=0)Marshal.FreeCoTaskMem(gameId);
            GC.KeepAlive(register);GC.KeepAlive(log);
        }
    }
    static T ExportPointer<T>(nint pointer) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(pointer);
}
