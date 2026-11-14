using System.Numerics;

namespace MautAssistent;

/// <summary>One active build plus one latest pending request. Never waits on route geometry.</summary>
public sealed class CurvePreview(Func<RouteTrace,Vector3,CurveSpeedProfile>? build=null)
{
    sealed record Request(long Generation,RouteTrace Route,Vector3 Origin);
    sealed record Result(RouteTrace Route,CurveSpeedProfile? Profile,string? Error);
    readonly object sync=new();
    readonly Func<RouteTrace,Vector3,CurveSpeedProfile> build=build??CurveSpeedProfile.Build;
    Request? wanted,pending;
    Result? result;
    long generation;
    bool worker;
    public bool IsIdle {get{lock(sync)return !worker;}}
    public void RequestProfile(RouteTrace route,Vector3 origin)
    {
        lock(sync) {
            if(ReferenceEquals(wanted?.Route,route))return;
            pending=wanted=new(++generation,route,origin);
            Volatile.Write(ref result,null);
            if(!worker){worker=true;ThreadPool.QueueUserWorkItem(_=>Run());}
        }
    }
    public void Clear(){lock(sync){generation++;wanted=pending=null;Volatile.Write(ref result,null);}}
    public CurveSpeedProfile? Read(RouteTrace route) {
        var current=Volatile.Read(ref result);
        return ReferenceEquals(current?.Route,route)?current.Profile:null;
    }
    public string? ErrorFor(RouteTrace route) {
        var current=Volatile.Read(ref result);
        return ReferenceEquals(current?.Route,route)?current.Error:null;
    }
    void Run()
    {
        while(true) {
            Request request;
            lock(sync){if(pending==null){worker=false;return;}request=pending;pending=null;}
            Result completed;
            try {completed=new(request.Route,build(request.Route,request.Origin),null);}
            catch(Exception ex){completed=new(request.Route,null,ex.GetType().Name);}
            lock(sync)if(wanted?.Generation==request.Generation)Volatile.Write(ref result,completed);
        }
    }
}
