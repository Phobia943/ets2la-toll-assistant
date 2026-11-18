using System.Diagnostics;
using System.Numerics;
using MautAssistent;

// Instantiated only by the sealed HostCompatibility replay. This fake has no
// process, provider, SDK, I/O, timer or live-map access. It models one immutable
// original worker response and exposes a precisely staged local revocation.
public static class MapGateEndCommitHostFixture
{
    public static object Create(Vector3 truck)=>new Worker(truck);
    sealed class Worker:IStaticRoadChecks
    {
        readonly Vector3 truck;
        StaticRoadWorkerClient.Result? current;
        public bool Present=true;
        public bool Armed=false;
        public bool FinalProviderSeen;
        public bool ResetObserved;
        public int ProviderChecks;
        public string Status=>"Authored immutable standalone gate response";
        public MapGateCandidate Gate {get;private set;}
        public Worker(Vector3 truck)
        {this.truck=truck;Gate=new(91,truck+new Vector3(3,0,-20),Quaternion.Identity,20,15);}
        public StaticRoadWorkerClient.Result? Read(RouteTrace route,Vector3 position,double horizon,long epoch)
        {
            if(!Present)return null;
            if(current!=null)return current;
            double at=route.ProjectForTracking(position).Along;
            Gate=new(91,truck+new Vector3(3,0,-20),Quaternion.Identity,at+20,at+15);
            return current=new(1,1,epoch,Stopwatch.GetTimestamp(),route,"europe",[],false,null){Gates=new[]{Gate}};
        }
        public bool IsCurrent(StaticRoadWorkerClient.Result result,RouteTrace route,long epoch)
        {ProviderChecks++;if(Armed)FinalProviderSeen=true;return Present&&ReferenceEquals(result,current)&&ReferenceEquals(result.Route,route)&&result.Epoch==epoch;}
        public bool IsLocallyCurrent(StaticRoadWorkerClient.Result result)=>Present&&ReferenceEquals(result,current)&&
            StaticRoadWorkerProtocol.Fresh(result.OriginalTimestamp,Stopwatch.GetTimestamp());
        public void Reset(){Present=false;current=null;ResetObserved=true;}
    }
}
