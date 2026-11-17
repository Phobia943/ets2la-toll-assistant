using System.Diagnostics;
using System.Reflection;

namespace MautAssistent;

internal sealed record NativeAccSnapshot(long ReadStartedTicks,long ReadFinishedTicks,long ClockFrequency,
    bool Supported,string? ObservationIssue,float? Speed,float? Acceleration,float? TargetAcceleration,
    float? PedalOutput,IReadOnlyDictionary<string,float>? Constraints)
{
    public bool AtomicSnapshot => false;
    public Guid? Module {get;init;}
}

// Observations only: never call a native constraint, PID, property getter or
// tick. The concurrently updated fields are explicitly NOT one atomic frame.
internal static class NativeAccDiagnostics
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    const int MaxConstraints=64;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<Type,Dictionary<string,FieldInfo?>> layouts=new();
    internal static NativeAccSnapshot Capture(object? plugin)
    {
        long began=Stopwatch.GetTimestamp();
        try {
            var type=plugin?.GetType();
            if(type?.FullName!="AdaptiveCruiseControl.AdaptiveCruiseControlPlugin"||
                !PinnedHostBuild.Matches(type,"Plugins/tumppi066.adaptivecruisecontrol/AdaptiveCruiseControl.dll"))
                return Missing(began,"UnverifiedBuild");
            return ReadFields(plugin!,began) with {Module=type.Module.ModuleVersionId};
        } catch(Exception ex){return Missing(began,ex.GetType().Name);}
    }
    static NativeAccSnapshot Missing(long began,string issue)=>new(began,Stopwatch.GetTimestamp(),Stopwatch.Frequency,
        false,issue,null,null,null,null,null);
    internal static NativeAccSnapshot ReadFields(object plugin,long began)
    {
        try {
            var layout=layouts.GetOrAdd(plugin.GetType(),type=>new[]{"constraintValues","currentSpeed","currentAcceleration","targetAcceleration","pedalOutput"}
                .ToDictionary(name=>name,name=>type.GetField(name,Hidden)));
            object? Field(string name)=>layout[name]?.GetValue(plugin);
            float? Value(string name)=>Field(name) is float raw?raw:null;
            if(Field("constraintValues") is not Dictionary<string,float> source||source.Count>MaxConstraints)
                return Missing(began,"ConstraintFieldsUnavailable");
            var values=new Dictionary<string,float>();
            foreach(var item in source) {
                if(values.Count>=MaxConstraints||item.Key.Length>256)return Missing(began,"ConstraintCopyLimit");
                values.Add(item.Key,item.Value);
            }
            float? speed=Value("currentSpeed"),acceleration=Value("currentAcceleration"),
                target=Value("targetAcceleration"),pedal=Value("pedalOutput");
            if(speed==null||acceleration==null||target==null||pedal==null)return Missing(began,"ScalarFieldsUnavailable");
            return new(began,Stopwatch.GetTimestamp(),Stopwatch.Frequency,true,null,speed,acceleration,target,pedal,values);
        } catch(Exception ex){return Missing(began,ex.GetType().Name);}
    }
}
