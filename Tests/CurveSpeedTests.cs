using System.Numerics;
using MautAssistent;

public static class CurveSpeedTests
{
    public static Vector3 Point(double distance,double radius=40,double approach=300)
    {
        if(distance<=approach)return new(0,0,(float)distance);
        double arc=radius*Math.PI/2,angle=Math.Min(Math.PI/2,(distance-approach)/radius);
        if(distance<=approach+arc)return new((float)(radius*(1-Math.Cos(angle))),0,(float)(approach+radius*Math.Sin(angle)));
        return new((float)(radius+distance-approach-arc),0,(float)(approach+radius));
    }
    public static RouteTrace Approach(Vector3 origin=default,double radius=40,double approach=300,double spacing=1)
        =>new(Enumerable.Range(0,(int)Math.Ceiling((approach+radius*Math.PI/2+160)/spacing)+1).Select(i=>origin+Point(i*spacing,radius,approach)));
    public static void Geometry(Action<bool,string> assert)
    {
        foreach(var offset in new[]{Vector3.Zero,new Vector3(60000,20,-80000)}) {
            var circle=new RouteTrace(Enumerable.Range(0,180).Select(i=>offset+new Vector3((float)(100*Math.Cos(i*.01)),0,(float)(100*Math.Sin(i*.01)))));
            assert(circle.TryCurvature(70,6,out var k)&&Math.Abs(Math.Abs(k)-.01)<.0003,"Curvature must remain stable at large world coordinates");
        }
        var hill=new RouteTrace(Enumerable.Range(0,200).Select(i=>new Vector3(0,(float)(10*Math.Sin(i*.01)),i)));
        assert(hill.TryCurvature(50,6,out var straight)&&straight==0,"Vertical grades are not horizontal bends");
        assert(!hill.TryCurvature(2,6,out _)&&!hill.TryCurvature(hill.Length,6,out _),"No extrapolated road beyond the verified trace");
        var seam=new RouteTrace([new(0,0,0),new(0,0,100),new(.8f,0,100),new(.8f,0,200)],[2]);
        assert(!seam.TryCurvature(100,6,out _)&&CurveSpeedProfile.Build(seam,Vector3.Zero).Count==0,
            "Short item seams must not become artificial sharp bends");
        var spike=new RouteTrace(Enumerable.Range(0,220).Select(i=>new Vector3(i==100?.2f:0,0,i)));
        var noise=CurveSpeedProfile.Build(spike,Vector3.Zero).Evaluate(Vector3.Zero,25,1.2);
        assert(noise==null||!float.IsFinite(noise.MaxAcceleration),"An isolated interpolation spike cannot order braking on a straight road");
        foreach(float slope in new[]{.1f,.37f,.77f,1.3f}) {
            var origin=new Vector3(70000,10,-80000);
            var diagonal=new RouteTrace(Enumerable.Range(0,600).Select(i=>origin+new Vector3(i*slope,0,i)));
            var preview=CurveSpeedProfile.Build(diagonal,origin);
            for(int i=0;i<200;i+=10) {
                var result=preview.Evaluate(origin+new Vector3(i*slope,0,i),25,1.2);
                assert(result==null||!float.IsFinite(result.MaxAcceleration)||result.MaxAcceleration>=0,
                    $"World-coordinate rounding on a straight road cannot command braking: slope {slope}, {result}");
            }
        }
    }
    public static void Limits(Action<bool,string> assert)
    {
        var route=Approach();var profile=CurveSpeedProfile.Build(route,Vector3.Zero);
        var early=profile.Evaluate(Point(90),25,1.2)!;
        assert(early is {Radius:>35 and <65,CurveSpeed:>5 and <9}&&early.MaxAcceleration<0&&early.MaxAcceleration>=-1.20001,
            $"A 40 m bend requires bounded slowing while still over 200 m ahead: {early}");
        var inCurve=profile.Evaluate(Point(325),20,1.2)!;
        assert(inCurve is {Late:true,AllowedSpeed:>0,MaxAcceleration:>=-1.2f},"A late bend must be visible without converting the comfort bound into a hard stop");
        assert(!float.IsFinite(profile.Evaluate(Point(325),2,1.2)!.MaxAcceleration),"A slow truck must retain the native acceleration decision");
        assert(profile.Evaluate(Point(500),20,1.2)==null,"An old bend cannot keep limiting long after the exit");
        assert(profile.Evaluate(new(1000,0,0),20,1.2)==null&&profile.Evaluate(Vector3.Zero,double.NaN,1.2)==null,
            "Out-of-corridor or invalid data cannot authorize a geometric speed bound");
        var dense=CurveSpeedProfile.Build(Approach(spacing:.25),Vector3.Zero).Evaluate(Point(90),25,1.2)!;
        var sparse=CurveSpeedProfile.Build(Approach(spacing:2),Vector3.Zero).Evaluate(Point(90),25,1.2)!;
        assert(Math.Abs(dense.AllowedSpeed-sparse.AllowedSpeed)<.4,"Point count must not be mistaken for metres of braking distance");
        var mirrored=new RouteTrace(Enumerable.Range(0,530).Select(i=>Point(i)*new Vector3(-1,1,1)));
        var opposite=CurveSpeedProfile.Build(mirrored,Vector3.Zero).Evaluate(Point(90),25,1.2)!;
        assert(Math.Abs(opposite.AllowedSpeed-early.AllowedSpeed)<.2,"Left and right bends require the same lateral bound");
        var esses=new RouteTrace(Enumerable.Range(0,240).Select(i=>new Vector3((float)(3*Math.Sin(i/10d)),0,i)));
        var essesDecision=CurveSpeedProfile.Build(esses,Vector3.Zero).Evaluate(Vector3.Zero,25,1.2);
        assert(essesDecision!=null&&float.IsFinite(essesDecision.MaxAcceleration)&&essesDecision.MaxAcceleration<0,
            "Alternating bends must not cancel into a supposedly straight road");
    }
    public static void ApproachSimulation(Action<bool,string> assert)
    {
        var profile=CurveSpeedProfile.Build(Approach(),Vector3.Zero);
        var delay=new Queue<double>(Enumerable.Repeat(0d,20));double speed=25,distance=0,maxEntrySpeed=0,minAcceleration=0;
        for(int i=0;i<1800&&distance<380;i++) {
            var limit=profile.Evaluate(Point(distance),speed,1.2);
            double command=limit!=null&&float.IsFinite(limit.MaxAcceleration)?Math.Min(.4,limit.MaxAcceleration):.4;
            delay.Enqueue(command);double acceleration=delay.Dequeue();minAcceleration=Math.Min(minAcceleration,acceleration);
            double next=Math.Max(0,speed+acceleration*.05);distance+=(speed+next)*.025;speed=next;
            if(distance is >=300 and <=355)maxEntrySpeed=Math.Max(maxEntrySpeed,speed);
        }
        assert(distance>=380&&speed>0&&maxEntrySpeed<=7.1&&minAcceleration>=-1.20001,
            $"With 1 s response delay, the simulation must slow before the bend without stopping: curve {maxEntrySpeed*3.6:F1} km/h, acceleration {minAcceleration:F2}");
        Console.WriteLine($"  curve simulation: 90 km/h approach, R=40 m, 1 s command delay, bend speed <= {maxEntrySpeed*3.6:F2} km/h, acceleration >= {minAcceleration:F2} m/s²");
    }
    public static void Worker(Action<bool,string> assert)
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        int calls=0;var first=Approach();var latest=Approach(radius:80);
        var cache=new CurvePreview((route,origin)=>{
            if(Interlocked.Increment(ref calls) is 1 or 3){entered.Set();if(!release.Wait(TimeSpan.FromSeconds(4)))throw new TimeoutException();}
            return CurveSpeedProfile.Build(route,origin);
        });
        try {
            cache.RequestProfile(first,Vector3.Zero);assert(entered.Wait(TimeSpan.FromSeconds(2)),"Hold the actual profile worker");
            for(int i=0;i<100;i++)cache.RequestProfile(Approach(radius:50+i),Vector3.Zero);
            cache.RequestProfile(latest,Vector3.Zero);
            assert(cache.Read(first)==null&&cache.Read(latest)==null,"Incomplete geometry must remain optional");
            release.Set();assert(SpinWait.SpinUntil(()=>cache.IsIdle,TimeSpan.FromSeconds(3)),"The worker must drain");
            assert(calls==2&&cache.Read(latest)!=null&&cache.Read(first)==null,"Keep one active build and only the latest pending route");
            entered.Reset();release.Reset();cache.RequestProfile(first,Vector3.Zero);
            assert(entered.Wait(TimeSpan.FromSeconds(2))&&cache.Read(latest)==null,"A new request invalidates even a retained older result");
            cache.Clear();release.Set();
            assert(SpinWait.SpinUntil(()=>cache.IsIdle,TimeSpan.FromSeconds(3))&&cache.Read(first)==null,
                "A result completed after manual takeover must not reappear");
        } finally {release.Set();}
        var failed=new CurvePreview((_,_)=>throw new IOException());failed.RequestProfile(first,Vector3.Zero);
        assert(SpinWait.SpinUntil(()=>failed.IsIdle,TimeSpan.FromSeconds(2))&&failed.Read(first)==null&&failed.ErrorFor(first)==nameof(IOException),
            "Optional preview failure must be reported without invented geometry");
    }
}
