using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ETS2LA.Backend.Events;
using ETS2LA.Shared;
using MautAssistent;

public static class NativeEventBusIntegration
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void PublishMarker(Events bus,string topic,object value)=>bus.Publish(topic,value);

    public static void Run(Action<bool,string> assert)
    {
        var bus=Events.Current;var topic="test.mautassistent.native-bus."+Guid.NewGuid().ToString("N");
        var value=new object();var order=new List<int>();bool callerStack=false,samePayload=false,unsubscribed=false;
        int caller=Environment.CurrentManagedThreadId;
        Action<object> second=_=>order.Add(2);
        Action<object> first=data=> {
            order.Add(1);samePayload=ReferenceEquals(data,value);
            callerStack=Environment.CurrentManagedThreadId==caller&&new StackTrace().GetFrames()
                .Any(f=>f.GetMethod()?.Name==nameof(PublishMarker));
            // Subscriber mutation must not wait for the active handler. The
            // already captured second callback must still run in this publish.
            unsubscribed=Task.Run(()=>bus.Unsubscribe(topic,second)).Wait(TimeSpan.FromSeconds(1));
        };
        bus.Subscribe(topic,first);bus.Subscribe(topic,second);
        try {
            PublishMarker(bus,topic,value);
            assert(callerStack&&samePayload&&order.SequenceEqual(new[]{1,2}),
                "The actual host must synchronously deliver the same object in subscription order on the publishing call stack");
            assert(unsubscribed,"The host cannot hold its subscription lock while invoking a callback");
        } finally {bus.Unsubscribe(topic,first);bus.Unsubscribe(topic,second);}

        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        Action<object> blocking=_=> {entered.Set();release.Wait(TimeSpan.FromSeconds(3));};
        bus.Subscribe(topic,blocking);Task? publish=null;bool held=false;
        try {
            publish=Task.Run(()=>PublishMarker(bus,topic,value));
            held=entered.Wait(TimeSpan.FromSeconds(1))&&!publish.IsCompleted;
        } finally {release.Set();publish?.Wait(TimeSpan.FromSeconds(2));bus.Unsubscribe(topic,blocking);}
        assert(held,"Publish cannot return before the subscribed route callback completes");
        assert(ConnectorStart.SupportsEventBusBuild(typeof(Events).Module.ModuleVersionId),
            "The actual inspected host build must be accepted for synchronous route probes");
        assert(ConnectorStart.SupportsEventBusBuild(new("e4372644-7355-4bb8-a350-61dcbecccbcb"))&&
            !ConnectorStart.SupportsEventBusBuild(Guid.Empty),"Keep the prior verified host supported and reject unknown builds");

        var root=Environment.GetEnvironmentVariable("EtsRoot")!;
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root,"Plugins","tumppi066.pathfinding","Pathfinding.dll"));
        var type=assembly.GetType("Pathfinding.PathfindingPlugin",true)!;
        var planner=(IPlugin)RuntimeHelpers.GetUninitializedObject(type);planner._IsRunning=true;
        string? issue=null;
        try {ConnectorStart.Probe(planner,Vector3.Zero,0,[]);}catch(InvalidOperationException ex){issue=ex.Message;}
        assert(issue=="Pathfinding hat die lokale Karte noch nicht geladen.",
            "The actual planner/host/path-library combination must pass compatibility before rejecting the deliberately absent map");
        planner._IsRunning=false;
    }
}
