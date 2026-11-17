using MautAssistent;

public static class NativeCommandTimingTests
{
    public static void ClockDomains(Action<bool,string> assert)
    {
        const long frequency=1_000_000,issued=1_000_000;
        // Reproducible observer inter-read delay: local clock sampled at1.005s,
        // raw-age clock at1.015s; producer actually issued at1.000s.
        double localBefore=5000.005;
        double observedAge=NativeCommandTime.AgeSeconds(issued,1_015_000,frequency)!.Value;
        double biasedOrigin=localBefore-observedAge;
        var old=new SteeringFeedGuard();old.Check(5000.050,biasedOrigin,true);
        assert(old.Check(5000.145,biasedOrigin,true)==SteeringFeedState.Lost,
            "The concrete old cross-clock conversion falsely ages a145ms command to155ms");
        var exact=new SteeringFeedGuard();exact.CheckAge(5000.050,NativeCommandTime.UnboundedAgeSeconds(issued,1_050_000,frequency),true);
        assert(exact.CheckAge(5000.145,NativeCommandTime.UnboundedAgeSeconds(issued,1_145_000,frequency),true)==SteeringFeedState.Healthy,
            "Raw origin age remains145ms despite arbitrary observer clock offset and inter-read delay");
        assert(exact.CheckAge(5000.151,NativeCommandTime.UnboundedAgeSeconds(issued,1_151_000,frequency),true)==SteeringFeedState.Lost,
            "A genuine151ms output outage still trips the unchanged150ms deadline");
        // Moving the local read AFTER the age calculation creates the opposite
        // error: a50ms delay relabels an actually180ms-old command as130ms old.
        double ageBeforeDelay=NativeCommandTime.AgeSeconds(issued,1_130_000,frequency)!.Value;
        double falselyFreshOrigin=5000.180-ageBeforeDelay;
        var unsafeAlignment=new SteeringFeedGuard();
        assert(unsafeAlignment.Check(5000.180,falselyFreshOrigin,true)==SteeringFeedState.Healthy,
            "A later local clock read would incorrectly rejuvenate the old origin");
        assert(exact.CheckAge(5000.180,NativeCommandTime.UnboundedAgeSeconds(issued,1_180_000,frequency),true)==SteeringFeedState.Lost,
            "Raw tick age never rejuvenates an180ms-old command after observer delay");
        foreach(double unrelatedClock in new[]{.1,5000.1,1_000_000_000.1}) {
            var g=new SteeringFeedGuard();
            assert(g.CheckAge(unrelatedClock,.145,true)==SteeringFeedState.Healthy,
                "Changing local clock origin cannot change a valid raw producer age");
        }
    }
    public static void Bounds(Action<bool,string> assert)
    {
        assert(NativeCommandTime.AgeSeconds(1_000,1_200,1_000)==.2,
            "The existing200ms output-admission boundary is unchanged");
        assert(NativeCommandTime.AgeSeconds(1_000,1_201,1_000)==null&&NativeCommandTime.UnboundedAgeSeconds(1_000,1_201,1_000)==.201,
            "Diagnostic age remains available after admission expiry without accepting that stale output");
        assert(NativeCommandTime.AgeSeconds(1_000,1_000,1_000)==0,
            "A current positive producer stamp has zero age");
        foreach(var item in new[]{(0L,10L,1000L),(-1L,10L,1000L),(11L,10L,1000L),(1L,10L,0L),(1L,10L,-1L)})
            assert(NativeCommandTime.UnboundedAgeSeconds(item.Item1,item.Item2,item.Item3)==null&&
                NativeCommandTime.AgeSeconds(item.Item1,item.Item2,item.Item3)==null,
                "Zero/negative/future origins and invalid frequencies never yield fresh-command age");
        assert(NativeCommandTime.UnboundedAgeSeconds(long.MaxValue-1000,long.MaxValue-850,1000)==.15,
            "Integer subtraction preserves a150tick delta even at very large monotonic stamps");
    }
    public static void GuardContract(Action<bool,string> assert)
    {
        var g=new SteeringFeedGuard();
        assert(g.CheckAge(10,null,true)==SteeringFeedState.Starting&&g.CheckAge(10.349,null,true)==SteeringFeedState.Starting,
            "Missing first output retains exactly the existing startup grace");
        assert(g.CheckAge(10.351,null,true)==SteeringFeedState.Lost,"Startup grace still expires after350ms");
        g.Reset();assert(g.CheckAge(20,.15,true)==SteeringFeedState.Healthy,"The150ms steering boundary is still accepted");
        assert(g.CheckAge(20.001,.150001,true)==SteeringFeedState.Lost,"A healthy feed exceeding150ms still fails immediately");
        foreach(double? bad in new double?[]{null,double.NaN,double.PositiveInfinity,-.001}) {
            var invalid=new SteeringFeedGuard();invalid.CheckAge(30,0,true);
            assert(invalid.CheckAge(30.01,bad,true)==SteeringFeedState.Lost,"Unknown/nonfinite/negative age cannot keep a once-healthy feed alive");
        }
        assert(g.CheckAge(double.NaN,0,true)==SteeringFeedState.Lost,"Invalid watchdog clock remains fail-closed");
        assert(g.CheckAge(40,10,false)==SteeringFeedState.Off&&g.CheckAge(41,null,true)==SteeringFeedState.Starting,
            "Disabling resets the old session and requires fresh startup rather than reusing received state");
        var a=new SteeringFeedGuard();var b=new SteeringFeedGuard();
        foreach(var item in new[]{(1d,double.NegativeInfinity),(1.1d,1.09d),(1.3d,1.09d)})
            assert(a.Check(item.Item1,item.Item2,true)==b.CheckAge(item.Item1,double.IsFinite(item.Item2)?item.Item1-item.Item2:null,true),
                "Legacy fixture Check delegates to the same unchanged age-state contract");
    }
}
