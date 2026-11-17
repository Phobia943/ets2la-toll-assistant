using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using ETS2LA.Game.Output;
using MautAssistent;

public static class BrakeGeometryIntegration
{
    public static void BrakeWarning(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture();f.F8();f.Probe();
        var physics=typeof(TollPlugin).GetNestedType("PhysicsObservation",BindingFlags.NonPublic)!;
        var telemetry=typeof(TollPlugin).GetNestedType("TelemetryFrame",BindingFlags.NonPublic)!;
        object Frame(int i)=>Activator.CreateInstance(telemetry,i*.05,(ulong)(1000000+i*50000),true,false,20d,f.Point,.5d,
            Activator.CreateInstance(physics,(ulong)(1000000+i*50000),(ulong)(1000000+i*50000),0f,0f,0f,0f,0f,0f,0f))!;
        var routeLock=typeof(TollPlugin).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Plugin)!;
        int before=f.Outputs.Count;
        Task? delivered=null;
        try {
            Monitor.Enter(routeLock);
            delivered=Task.Run(()=>{
                for(int i=0;i<=21;i++) {
                    f.Call("OnControlOutput",new ControlEvent {
                        ChannelDefinition=new(){Id=TollPlugin.LongitudinalChannelId},Variables=new(){aforward=0f,abackward=-.5f}
                    });
                    f.Call("ObserveBrakeResponse",Frame(i));
                }
            });
            assert(delivered.Wait(TimeSpan.FromSeconds(1)),"A brake-response warning cannot wait for route work");
            assert(f.Get<object?>("brakeWarning")!=null,"Own output fields must feed the response guard");
            assert(f.State.EnableAssists&&f.Plugin.Active&&!f.Plugin.FaultBraking&&f.Outputs.Count==before,
                "Diagnostic failure must not drop working steering or inject another ineffective brake command");
        } finally {Monitor.Exit(routeLock);delivered?.Wait(TimeSpan.FromSeconds(2));}
        assert(SpinWait.SpinUntil(()=>f.Notices.Any(n=>n.Contains("Bremsanforderung ohne Spielreaktion")),TimeSpan.FromSeconds(2)),
            "The warning must reach the notification callback");
        f.F9();assert(f.Get<object?>("brakeWarning")==null&&!f.State.EnableAssists,"Manual takeover clears the warning and its session latch");
    }
    public static void Reader(Action<bool,string> assert)
    {
        if(!OperatingSystem.IsWindows())return;
        string name="Local\\MautGeometry-Test-"+Guid.NewGuid();
        using(var missing=new VehicleGeometryReader(_=>true,name)) {
            assert(missing.Read()==null,"An absent observer is optional");
            bool created=false;
            try {using var unexpected=MemoryMappedFile.OpenExisting(name);created=true;}catch(FileNotFoundException){}
            assert(!created,"The reader must never create the observer mapping");
        }
        using var map=MemoryMappedFile.CreateNew(name,256);
        using var writer=map.CreateViewAccessor();
        var bytes=GeometryBrakeTests.GeometryFrame(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        writer.WriteArray(0,bytes,0,bytes.Length);
        int checks=0;
        using var reader=new VehicleGeometryReader(pid=>{checks++;return pid==123;},name);
        assert(reader.Read()!=null&&reader.Read()!=null&&checks==1,"A coherent generation is validated once");
        writer.Write(12,3);
        assert(reader.Read()==null&&reader.Status.Contains("gerade geschrieben"),"In-progress writes cannot look like valid geometry");
        writer.Write(12,4);writer.Write(32,457UL);
        assert(reader.Read()!=null&&checks==2,"A new observer session must recheck its producer");
        using var wrongProcess=new VehicleGeometryReader(_=>false,name);
        assert(wrongProcess.Read()==null,"Unverified processes cannot supply vehicle measurements");
        using var exitedProcess=new VehicleGeometryReader(_=>throw new Win32Exception(),name);
        assert(exitedProcess.Read()==null,"An inaccessible process is an observation failure, never a driving fault");
        writer.Write(28,0U);assert(reader.Read()==null,"Native shutdown invalidates the last geometry");
        reader.Dispose();writer.WriteArray(0,bytes,0,bytes.Length);
        assert(reader.Read()==null,"A late status worker cannot reopen the reader after disable");
        reader.Resume();assert(reader.Read()!=null,"Only explicit plugin enable permits reconnection");
    }
}
