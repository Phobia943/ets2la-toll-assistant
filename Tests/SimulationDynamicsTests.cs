using System.Numerics;
using MautAssistent;

public static class SimulationDynamicsTests
{
    public static void Run(Action<bool,string> check) {LeaseAndDuplicates(check);Invalidation(check);}
    static SimulationDynamics Ready()
    {
        var value=new SimulationDynamics(10_000);
        value.Observe(1,10_000,1_000_000,1_000_000,10,Vector3.Zero,.5,true,false);
        value.Observe(1.05,10_500,1_050_000,1_050_000,10.1,new(0,0,.5f),.5,true,false);
        return value;
    }
    public static void LeaseAndDuplicates(Action<bool,string> check)
    {
        var zero=new SimulationDynamics(10_000);
        zero.Observe(0,500,1_000_000,1_000_000,5,Vector3.Zero,0,true,false);
        zero.Observe(.05,1_000,1_050_000,1_050_000,5,Vector3.Zero,0,true,false);
        check(zero.Read(.05,1_000) is {Acceleration:0,Origin:1_000},
            "Arrival zero is valid and pose sanity is an upper bound, not a minimum travelled distance at measured speed");
        var value=new SimulationDynamics(10_000);
        value.Observe(1,10_000,1_000_000,1_000_000,10,Vector3.Zero,.5,true,false);
        check(value.Read(1,10_000)==null,"A single physical sample is not a measured acceleration certificate");
        value.Observe(1.05,10_500,1_050_000,1_050_000,10.1,new(0,0,.5f),.5,true,false);
        var initial=value.Read(1.06,10_600);
        check(initial is {Origin:10_500,SimStamp:1_050_000}&&Math.Abs(initial.Value.Acceleration-2)<1e-10,
            "Two advancing simulation frames measure acceleration in simulated time with the original producer origin");
        value.Observe(1.10,11_000,1_100_000,1_050_000,10.1,new(0,0,.5f),.5,true,false);
        value.Observe(1.20,12_000,1_200_000,1_050_000,10.1,new(0,0,.5f),.5,true,false);
        check(value.Read(1.24,12_400)==initial,"Identical physics with advancing delivery/SDK clocks retains evidence without renewing either origin");
        check(value.Read(1.2501,12_501)==null,"A stream of duplicate callbacks cannot extend a measured certificate past200ms");
        value.Observe(1.30,13_000,1_300_000,1_300_000,10.6,new(0,0,3.1f),.5,true,false);
        var fresh=value.Read(1.31,13_100);
        check(fresh is {Origin:13_000}&&Math.Abs(fresh.Value.Acceleration-2)<1e-10,
            "Next actual physics advance measures from the previous distinct simulation sample, not a duplicate arrival clock");
        var qpc=Ready();check(qpc.Read(1.06,12_501)==null,"A fresh arrival clock cannot hide an expired original QPC origin");
        var local=Ready();check(local.Read(1.251,10_600)==null,"A fresh QPC cannot hide an expired original arrival origin");
        foreach(bool futureQpc in new[]{false,true}) {
            var future=Ready();
            check(future.Read(futureQpc?1.06:1.04,futureQpc?10_499:10_600)==null&&future.Read(1.06,10_600)==null,
                "A future original clock is revoked instead of becoming valid when observation catches up: "+futureQpc);
            future.Observe(1.1,11_000,1_100_000,1_100_000,10.2,new(0,0,1),.5,true,false);
            check(future.Read(1.1,11_000)==null,"A future-clock revocation requires a new first physical baseline");
            future.Observe(1.15,11_500,1_150_000,1_150_000,10.3,new(0,0,1.5f),.5,true,false);
            check(future.Read(1.15,11_500)!=null,"Two new coherent physical samples recover after future-clock revocation");
        }
        var rewind=Ready();check(rewind.Read(1.20,12_000)!=null&&rewind.Read(1.19,11_900)==null&&rewind.Read(1.20,12_000)==null,
            "Read-clock rewind revokes the certificate instead of resurrecting an earlier lease");
        value.Reset();check(value.Read(1.32,13_200)==null,"Caller takeover/phase reset immediately revokes the prior physical basis");
        value.Observe(1.35,13_500,1_350_000,1_350_000,10,new(0,0,3.5f),.5,true,false);
        check(value.Read(1.35,13_500)==null,"First frame after explicit phase reset cannot reuse the old derivative");
        value.Observe(1.4,14_000,1_400_000,1_400_000,10,new(0,0,4),.5,true,false);
        check(value.Read(1.4,14_000) is {Acceleration:0},"Two fresh advancing post-reset frames establish a new physical derivative");
    }
    public static void Invalidation(Action<bool,string> check)
    {
        foreach(string mutation in new[]{"speed","position","heading"}) {
            var value=Ready();
            value.Observe(1.1,11_000,1_100_000,1_050_000,mutation=="speed"?10.2:10.1,
                mutation=="position"?new(0,0,.501f):new(0,0,.5f),mutation=="heading"?.500001:.5,true,false);
            check(value.Read(1.1,11_000)==null,"Changed physics at the same simulation stamp revokes evidence: "+mutation);
            value.Observe(1.11,11_100,1_110_000,1_050_000,10.1,new(0,0,.5f),.5,true,false);
            value.Observe(1.15,11_500,1_150_000,1_150_000,10.2,new(0,0,1.5f),.5,true,false);
            check(value.Read(1.15,11_500)==null,"A quarantined mutable frame cannot serve as the next derivative baseline: "+mutation);
            value.Observe(1.2,12_000,1_200_000,1_200_000,10.3,new(0,0,2),.5,true,false);
            check(value.Read(1.2,12_000)!=null,"Two coherent new physics frames recover after the invalid stamp: "+mutation);
        }
        foreach(string issue in new[]{"paused","inactive","arrival","qpc","sdk","sim","speed","nan","pose","heading","jump","derivative"}) {
            var value=Ready();
            value.Observe(issue=="arrival"?1.04:1.1,issue=="qpc"?10_400:11_000,
                issue=="sdk"?1_040_000u:1_100_000u,issue=="sim"?1_040_000u:1_100_000u,
                issue=="nan"?double.NaN:issue=="speed"?101:issue=="derivative"?30:10.2,
                issue=="pose"?new(float.NaN,0,0):issue=="jump"?new(100,0,0):new(0,0,1),issue=="heading"?double.NaN:.5,
                issue!="inactive",issue=="paused");
            check(value.Read(1.1,11_000)==null,"Invalid source/phase data cannot retain a measured dynamics lease: "+issue);
        }
        var gap=Ready();gap.Observe(2,20_000,2_000_000,2_000_000,10,new(0,0,10),.5,true,false);
        check(gap.Read(2,20_000)==null,"An excessive simulation gap only starts a new baseline, never an invented derivative");
        gap.Observe(2.05,20_500,2_050_000,2_050_000,10,new(0,0,10.5f),.5,true,false);
        check(gap.Read(2.05,20_500) is {Acceleration:0},"A second valid frame recovers after a known source gap");
    }
}
