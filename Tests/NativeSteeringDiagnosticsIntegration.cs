using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ETS2LA.Shared;
using MautAssistent;
using PathLib;

public static class NativeSteeringDiagnosticsIntegration
{
    public sealed class SettingsFixture
    {
        public int SteeringPointCount=5;
        public float RawControlSpeedThreshold=27;
        public bool WidenTurnsWithTrailer=true;
        public float WidenStartSpeed=10;
        public float MaxWidenAmount=1.5f;
        public float MaxWidenAtAngle=30;
    }
    public static void SettingsWithoutInitialization(Action<bool,string> assert)
    {
        int created=0;
        var lazy=new Lazy<SettingsFixture>(()=>{created++;return new();});
        assert(NativeSteeringDiagnostics.ReadInitializedSettings(lazy)==null&&created==0&&!lazy.IsValueCreated,
            "The observer cannot initialize native settings merely to report them");
        _=lazy.Value;
        var settings=NativeSteeringDiagnostics.ReadInitializedSettings(lazy);
        assert(settings?.SteeringPointCount==5&&settings.MaxWidenAmount==1.5f&&settings.WidenTurnsWithTrailer==true&&created==1,
            "Once already initialized, report the actual in-memory settings without constructing another instance");
        assert(NativeSteeringDiagnostics.ReadInitializedSettings(new object())==null,
            "Only a framework Lazy instance can provide the initialization guard");
    }
    public static void Run(Action<bool,string> assert)
    {
        var root=Environment.GetEnvironmentVariable("EtsRoot")!;
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Plugins","tumppi066.laneassist","LaneAssist.dll"));
        var type=assembly.GetType("LaneAssist.LaneAssistPlugin",true)!;
        // No constructor, native Tick, providers or shared output memory.
        var plugin=(IPlugin)RuntimeHelpers.GetUninitializedObject(type);plugin._IsRunning=true;
        void Set(string name,object value)=>type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,value);
        Set("lastFrameError","Closest point on lane is too far from the truck");
        Set("lastFrameAngleOutput",.12f);Set("lastFrameDistanceOutput",.25f);Set("lastFrameTurnOffset",.4f);Set("lastFrameSteeringOutput",float.NaN);
        Set("lastPlannedPathData",new PlannedPathData {HasError=true,ErrorMessage="Test path error",PlannedPath=[]});
        var s=NativeSteeringDiagnostics.Read(plugin);
        assert(s.Supported&&s.Running&&s.Error!.Contains("too far")&&s.AngleOutput==.12f&&s.TurnOffset==.4f,
            "The exact installed build must expose the native reason and last control terms without executing it");
        assert(s.PathHasError==true&&s.PathError=="Test path error"&&s.SteeringOutput==null,
            "Read the native path flag while keeping nonfinite diagnostics out of the snapshot");
        assert(!s.AtomicSnapshot,"Separate native fields cannot be claimed as an atomic control frame");
        var json=System.Text.Json.JsonSerializer.Serialize(s);
        assert(json.Contains("Closest point")&&!json.Contains("NaN"),"The diagnostic snapshot must serialize without special-number settings");
        Set("lastFrameSteeringOutput",-.37f);
        assert(NativeSteeringDiagnostics.Read(plugin).SteeringOutput==.37f,"Native display steering must be normalized to the actual published control-channel sign");
        assert(!NativeSteeringDiagnostics.Read(new RollingStartIntegration.Planner()).Supported,
            "Unknown native builds cannot be interpreted using guessed private fields");
        assert(NativeSteeringDiagnostics.Capture(()=>throw new InvalidOperationException()).ObservationIssue==nameof(InvalidOperationException),
            "Provider lookup errors cannot escape into the independent brake timer");
        plugin._IsRunning=false;
        assert(!NativeSteeringDiagnostics.Read(plugin).Running,"Disabled native lane assist remains distinguishable from an active provider");
    }
}
