using System.Numerics;
namespace MautAssistent;
internal interface IStaticRoadChecks
{
    string Status{get;}
    StaticRoadWorkerClient.Result? Read(RouteTrace trace,Vector3 position,double horizon,long manualEpoch);
    bool IsCurrent(StaticRoadWorkerClient.Result result,RouteTrace trace,long manualEpoch);
    void Reset();
}
