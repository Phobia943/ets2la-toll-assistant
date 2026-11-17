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
        =>CheckAge(now,double.IsFinite(lastCommandAt)?now-lastCommandAt:null,required);

    // Native commands supply their age directly from their original monotonic
    // tick domain. 'now' is only the unchanged startup-grace clock; never align
    // a producer stamp by subtracting an age from a separately sampled clock.
    public SteeringFeedState CheckAge(double now,double? age,bool required)
    {
        if(!required){Reset();return SteeringFeedState.Off;}
        if(!double.IsFinite(now))return SteeringFeedState.Lost;
        started??=now;
        if(age is double value&&double.IsFinite(value)&&value>=0&&value<=MaxAge)
        {received=true;return SteeringFeedState.Healthy;}
        if(received || now-started.Value>=StartGrace)return SteeringFeedState.Lost;
        return SteeringFeedState.Starting;
    }
}
