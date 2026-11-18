using System.Numerics;
namespace MautAssistent;
internal interface IStaticRoadChecks
{
    string Status{get;}
    StaticRoadWorkerClient.Result? Read(RouteTrace trace,Vector3 position,double horizon,long manualEpoch);
    StaticRoadWorkerClient.Result? Read(RouteTrace trace,Vector3 position,double horizon,long manualEpoch,RigRoadObservation? rig)
        =>Read(trace,position,horizon,manualEpoch);
    bool IsCurrent(StaticRoadWorkerClient.Result result,RouteTrace trace,long manualEpoch);
    bool IsLocallyCurrent(StaticRoadWorkerClient.Result result)=>false;
    bool IsRigCurrent(StaticRoadWorkerClient.Result result,RigRoadObservation? rig)=>!result.Hits.Any(h=>h.Rig);
    void Reset();
}
