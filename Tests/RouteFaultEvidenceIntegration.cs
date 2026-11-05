using System.Reflection;
using System.Text.Json;
using MautAssistent;

public static class RouteFaultEvidenceIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture();var planned=f.Plan();
        var native=planned.PlannedPath[0];
        var state=typeof(PathLib.PlannedItem).GetField("laneChangeStartFactor",BindingFlags.Instance|BindingFlags.NonPublic)!;
        state.SetValue(native,.25f);f.Plan=()=>planned;f.F8();f.Probe();
        assert(f.State.EnableAssists&&!f.Plugin.FaultBraking,"A valid plan must be accepted before the route error");
        // A later native mutation must not rewrite the accepted evidence.
        native.laneIndex=2;state.SetValue(native,.9f);
        f.Call("OnPath",new PathLib.PlannedPathData {HasError=true,ErrorMessage="No suitable starting road found for path planning.",PlannedPath=[]});
        assert(f.Plugin.FaultBraking,"Preserving evidence cannot turn a route error into permission to keep driving");
        var fault=f.Get<object>("lastFault");
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(fault,new JsonSerializerOptions {
            NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));
        var root=json.RootElement;
        assert(root.GetProperty("routePlanSource").GetString()=="LastAccepted"&&root.GetProperty("routePlan").GetArrayLength()==1,
            "An empty native error event must retain the preceding accepted route for reconstruction");
        var first=root.GetProperty("routePlan")[0];
        assert(first.GetProperty("lane").GetInt32()==1&&first.GetProperty("laneChangeStartFactor").GetSingle()==.25f,
            "The retained evidence must preserve the accepted lane and interpolation state, not the later host mutation");
        assert(root.GetProperty("lastAcceptedPlanAge").GetDouble()<1,"Evidence must state its age instead of appearing current");
        f.F9();
        assert(f.Get<object?>("lastAcceptedPlan")==null,"A new manual session cannot reuse a previous accepted route as control state");
    }
}
