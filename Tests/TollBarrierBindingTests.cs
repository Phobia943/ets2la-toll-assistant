using MautAssistent;

public static class TollBarrierBindingTests
{
    public static void Policy(Action<bool,string> assert)
    {
        var options=new TollOptions();double v=100/3.6;
        var far=TollBarrierBinding.Evaluate(v,TollBarrierBinding.Horizon(v,options)+1,false,options);
        assert(!far.WithinHorizon&&float.IsNaN(far.Decision.MaxAcceleration),"Confirmed lane planning does not impose a distant premature stop");
        var approach=TollBarrierBinding.Evaluate(v,TollBarrierBinding.Horizon(v,options)-1,false,options);
        assert(approach.WithinHorizon&&double.IsFinite(approach.Decision.TargetSpeed!.Value)&&float.IsFinite(approach.Decision.MaxAcceleration)&&
            approach.Decision.MaxAcceleration>=-options.ComfortableDeceleration&&!approach.Decision.RequiresTakeover&&approach.Decision.GateKey==null,
            "A missing short-range SDK gate creates a finite ordinary approach limit without fake observed gate identity or AEB");
        var hold=TollBarrierBinding.Evaluate(0,8,false,options);
        assert(hold.Holding&&hold.Decision.TargetSpeed==0&&hold.Decision.MaxAcceleration<0&&!hold.Decision.RequiresTakeover,
            "An unbound selected map lane holds before its physical anchor");
        foreach(double? unknown in new double?[]{null,double.NaN,double.PositiveInfinity}) {
            var result=TollBarrierBinding.Evaluate(12,unknown,false,options);
            assert(result.Holding&&float.IsFinite(result.Decision.MaxAcceleration)&&result.Decision.TargetSpeed==0&&!result.Decision.RequiresTakeover,
                "Unavailable current anchor distance does not silently produce idle/NaN throttle permission");
        }
        var newlyBound=TollBarrierBinding.Evaluate(0,8,true,options);
        assert(!newlyBound.Holding&&newlyBound.Decision.TargetSpeed==null,"A real SDK binding withdraws only the auxiliary unbound-map hold without a new F8");
        var controller=new TollController(options);
        var closed=controller.Step(new(0,0,true,true,new("real",8,BarrierState.Unknown)));
        var opened=controller.Step(new(.1,0,true,true,new("real",8,BarrierState.Open)));
        var delayed=controller.Step(new(2.2,0,true,true,new("real",8,BarrierState.Open)));
        assert(closed.TargetSpeed==0&&opened.TargetSpeed==0&&delayed.TargetSpeed==0,
            "A gap in source time does not skip the existing opening delay");
        for(double time=2.3;time<=4.5;time+=.1)delayed=controller.Step(new(time,0,true,true,new("real",8,BarrierState.Open)));
        assert(delayed.TargetSpeed>0,"The existing gate controller releases an actually open observed gate without changing the assist session");
    }
}
