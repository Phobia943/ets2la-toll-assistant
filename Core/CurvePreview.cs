using System.Numerics;

namespace MautAssistent;

/// <summary>Bounded builds; compatible completed profiles survive route refreshes.</summary>
public sealed class CurvePreview(Func<RouteTrace,Vector3,CurveSpeedProfile>? build=null)
{
    sealed record Request(long Epoch,RouteTrace Route,Vector3 Origin,double RequiredAhead,double Horizon);
    sealed record Result(long Epoch,RouteTrace Route,Vector3 Origin,CurveSpeedProfile? Profile,string? Error);
    readonly object sync=new();
    readonly Func<RouteTrace,Vector3,double,CurveSpeedProfile> build=(route,origin,horizon)=>
        build==null?CurveSpeedProfile.Build(route,origin,horizon):build(route,origin);
    Request? wanted,pending,building;
    Result? result;
    long epoch;
    bool worker;
    public bool IsIdle {get{lock(sync)return !worker;}}
    public void RequestProfile(RouteTrace route,Vector3 origin)=>RequestProfile(route,origin,CurveSpeedProfile.Horizon);
    public void RequestProfile(RouteTrace route,Vector3 origin,double requiredAhead)
    {
        if(!double.IsFinite(requiredAhead)||requiredAhead<0)throw new ArgumentOutOfRangeException(nameof(requiredAhead));
        lock(sync) {
            wanted=new(epoch,route,origin,requiredAhead,Math.Clamp(requiredAhead+160,CurveSpeedProfile.Horizon,CurveSpeedProfile.MaximumHorizon));
            if(result?.Profile is { } ready&&ready.Covers(route,origin,requiredAhead+50))return;
            if(building is { } current&&Compatible(current,wanted))return;
            if(pending is { } queued&&Compatible(queued,wanted))return;
            pending=wanted;
            if(!worker){worker=true;ThreadPool.QueueUserWorkItem(_=>Run());}
        }
    }
    static bool Compatible(Request built,Request current)
    {
        if(built.Epoch!=current.Epoch||!built.Route.MatchesAhead(current.Route,current.Origin,current.RequiredAhead+24,out var along,out var other))return false;
        double start=Math.Max(0,built.Route.Project(built.Origin).Along-8);
        return along>=start&&Math.Min(built.Route.Length,along+Math.Min(current.RequiredAhead+24,current.Route.Length-other))<=start+built.Horizon;
    }
    public void Clear(){lock(sync){epoch++;wanted=pending=null;Volatile.Write(ref result,null);}}
    public CurveSpeedProfile? Read(RouteTrace route) {
        var current=Volatile.Read(ref result);
        return current?.Profile is { } profile&&profile.Covers(route,current.Origin,CurveSpeedProfile.Horizon)?profile:null;
    }
    public CurveSpeedProfile? Read(RouteTrace route,Vector3 position,double requiredAhead)
    {
        var current=Volatile.Read(ref result);
        return current?.Profile is { } profile&&profile.Covers(route,position,requiredAhead)?profile:null;
    }
    public string? ErrorFor(RouteTrace route) {
        var current=Volatile.Read(ref result);
        return ReferenceEquals(current?.Route,route)?current.Error:null;
    }
    void Run()
    {
        while(true) {
            Request request;
            lock(sync){if(pending==null){building=null;worker=false;return;}building=request=pending;pending=null;}
            Result completed;
            try {completed=new(request.Epoch,request.Route,request.Origin,build(request.Route,request.Origin,request.Horizon),null);}
            catch(Exception ex){completed=new(request.Epoch,request.Route,request.Origin,null,ex.GetType().Name);}
            lock(sync) {
                if(epoch==request.Epoch&&wanted is { } latest&&
                    (ReferenceEquals(latest.Route,request.Route)||completed.Profile?.Covers(latest.Route,latest.Origin,latest.RequiredAhead)==true))
                    Volatile.Write(ref result,completed);
                building=null;
            }
        }
    }
}
