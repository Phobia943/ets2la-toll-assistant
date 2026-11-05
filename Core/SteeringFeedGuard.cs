namespace MautAssistent;

public enum SteeringFeedState { Off, Starting, Healthy, Lost }

/// <summary>Requires live steering commands, independently of route validity.</summary>
public sealed class SteeringFeedGuard
{
    public const double MaxAge=.15;
    public const double StartGrace=.35;
    double? started;
    bool received;
    public void Reset(){started=null;received=false;}
    public SteeringFeedState Check(double now,double lastCommandAt,bool required)
    {
        if(!required){Reset();return SteeringFeedState.Off;}
        if(!double.IsFinite(now))return SteeringFeedState.Lost;
        started??=now;
        double age=now-lastCommandAt;
        if(double.IsFinite(lastCommandAt)&&age>=0&&age<=MaxAge)
        {received=true;return SteeringFeedState.Healthy;}
        if(received || now-started.Value>=StartGrace)return SteeringFeedState.Lost;
        return SteeringFeedState.Starting;
    }
}
