using System.Reflection;
using ETS2LA.Shared;

namespace MautAssistent;

internal sealed record NativeSteeringSnapshot(bool Supported,bool Running,Guid? Module,string? Error,
    float? AngleOutput,float? DistanceOutput,float? TurnOffset,float? SteeringOutput,
    bool? PathHasError,string? PathError,string? ObservationIssue,bool AtomicSnapshot=false,NativeLaneSettings? Settings=null);
internal sealed record NativeLaneSettings(int? SteeringPointCount,float? RawControlSpeedThreshold,
    bool? WidenTurnsWithTrailer,float? WidenStartSpeed,float? MaxWidenAmount,float? MaxWidenAtAngle);

/// <summary>Read-only, non-atomic snapshot of an exactly verified native build.</summary>
internal static class NativeSteeringDiagnostics
{
    static readonly Guid KnownModule=new("6635f522-9dbf-41a6-8cf9-0944bd797012");
    static readonly Guid KnownPathLib=new("8d0821da-1971-4caa-a8be-78ea1093e51d");
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static NativeLaneSettings? ReadInitializedSettings(object? lazy)
    {
        // Never initialize a native settings instance or load a settings file.
        if(lazy==null || !lazy.GetType().IsGenericType || lazy.GetType().GetGenericTypeDefinition()!=typeof(Lazy<>) ||
            lazy.GetType().GetProperty("IsValueCreated")!.GetValue(lazy) is not true)return null;
        var settings=lazy.GetType().GetProperty("Value")!.GetValue(lazy);
        if(settings==null)return null;
        object? Field(string name)=>settings.GetType().GetField(name)?.GetValue(settings);
        float? Number(string name)=>Field(name) is float value&&float.IsFinite(value)?value:null;
        return new(Field("SteeringPointCount") as int?,Number("RawControlSpeedThreshold"),
            Field("WidenTurnsWithTrailer") as bool?,Number("WidenStartSpeed"),Number("MaxWidenAmount"),Number("MaxWidenAtAngle"));
    }
    internal static NativeSteeringSnapshot Capture(Func<IPlugin?> provider)
    {
        try {return Read(provider());}
        catch(Exception ex){return new(false,false,null,null,null,null,null,null,null,null,ex.GetType().Name);}
    }
    internal static NativeSteeringSnapshot Read(IPlugin? plugin)
    {
        if(plugin==null)return new(false,false,null,null,null,null,null,null,null,null,"ProviderUnavailable");
        var type=plugin.GetType();var module=type.Module.ModuleVersionId;
        if(module!=KnownModule || type.FullName!="LaneAssist.LaneAssistPlugin")
            return new(false,plugin._IsRunning,module,null,null,null,null,null,null,null,"UnverifiedBuild");
        try {
            object? Field(string name)=>type.GetField(name,Hidden)?.GetValue(plugin);
            float? Number(string name)=>Field(name) is float value&&float.IsFinite(value)?value:null;
            var path=Field("lastPlannedPathData");
            // Native stores the sign-inverted value for its display slider.
            // Report the control-channel convention used by our motion log.
            var displaySteering=Number("lastFrameSteeringOutput");
            object? PathMember(string name)=>path!=null&&path.GetType().Module.ModuleVersionId==KnownPathLib?
                path.GetType().GetField(name)?.GetValue(path)??path.GetType().GetProperty(name)?.GetValue(path):null;
            return new(true,plugin._IsRunning,module,Field("lastFrameError") as string,
                Number("lastFrameAngleOutput"),Number("lastFrameDistanceOutput"),Number("lastFrameTurnOffset"),displaySteering.HasValue?-displaySteering.Value:null,
                PathMember("HasError") as bool?,PathMember("ErrorMessage") as string,null,
                Settings:ReadInitializedSettings(type.Assembly.GetType("LaneAssist.LaneAssistSettings")?
                    .GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null)));
        } catch(Exception ex) {
            // Diagnostic failure must not alter braking or invoke native ticks.
            return new(false,plugin._IsRunning,module,null,null,null,null,null,null,null,ex.GetType().Name);
        }
    }
}
