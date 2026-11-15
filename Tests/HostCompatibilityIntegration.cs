using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ETS2LA.Game.Output;
using ETS2LA.Shared;
using MautAssistent;
public static class HostCompatibilityIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        assert(!PinnedHostBuild.Matches(typeof(GameOutput),"ETS2LA.Game.dll"),"The original output host cannot pass the new exact package identity check");
        string? prepared=Environment.GetEnvironmentVariable("MAUT_HOST_CANDIDATE");
        if(string.IsNullOrEmpty(prepared))return;
        var context=new AssemblyLoadContext("maut-metadata-only-"+Guid.NewGuid(),isCollectible:true);
        context.Resolving+=(_,name)=>AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==name.Name);
        try {
            var assembly=context.LoadFromAssemblyPath(Path.Combine(prepared,"Plugins/tumppi066.laneassist/LaneAssist.dll"));
            var type=assembly.GetType("LaneAssist.LaneAssistPlugin",true)!;
            assert(PinnedHostBuild.Matches(type,"Plugins/tumppi066.laneassist/LaneAssist.dll"),"Exact prepared Lane hash/MVID/revision passes embedded manifest validation");
            // No constructor, tick, SDK read, settings initialization or output.
            var lane=(IPlugin)RuntimeHelpers.GetUninitializedObject(type);
            var observation=NativeSteeringDiagnostics.Read(lane);
            assert(observation.Supported&&observation.ObservationIssue==null,
                "The exact patched Lane retains native error diagnostics after receiving a new module identity");
        } finally {context.Unload();}
    }
}
