using System.Numerics;
using MautAssistent;
public static class BarrierLifecycleIntegration
{
    public static void Transitions(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture();var controller=f.Get<TollController>("controller");
        var router=f.Get<TollRouter>("router");var plan=new TollBarrierPlan("plaza",8,3,new(0,0,100),Vector3.UnitZ);
        typeof(TollRouter).GetProperty(nameof(TollRouter.SelectedBarrier))!.SetValue(router,plan);
        const string key="3:0.0:100.0";
        for(int i=0;i<=21;i++)controller.Step(new(i*.1,0,true,true,new(key,20,BarrierState.Open)));
        controller.Step(new(2.2,0,true,true,new(key,-2,BarrierState.Open)));f.Set("tracked",key);
        assert(controller.Tracking&&controller.Released,"The actual controller has a confirmed opened and crossed gate");
        var passed=new BarrierBindingResult(true,true,false,null,null,-35,plan.HoldKey,TollDecision.Idle("passed"),false,true);
        assert(f.Call("ApplyBarrierBinding",null,passed)==null&&!controller.Tracking&&f.Get<string?>("tracked")==null,
            "A positively passed identical booth clears real controller ownership even when SDK feed is now empty");
        assert(!controller.Step(new(2.3,0,true,true,null)).RequiresTakeover,"Passed-to-empty must not enter Lost and emergency braking");
        var unbound=new BarrierBindingResult(true,true,false,null,null,100,plan.HoldKey,new("unbound",0,-1,null),false);
        var wrong=new Gate("99:0.0:100.0",5,BarrierState.Open);
        assert(f.Call("ApplyBarrierBinding",wrong,unbound)==null&&!controller.Tracking,
            "An unrelated nearby SDK gate cannot seed ownership for an unbound selected booth");
        var right=new Gate(key,100,BarrierState.Closed);
        var bound=new BarrierBindingResult(true,true,true,right,null,100,plan.HoldKey,TollDecision.Idle("bound"),false);
        var selected=(Gate?)f.Call("ApplyBarrierBinding",null,bound);
        var decision=controller.Step(new(2.4,0,true,true,selected));
        assert(selected==right&&decision.GateKey==key&&!decision.RequiresTakeover&&float.IsFinite(decision.MaxAcceleration),
            "Later real binding starts the correct closed booth without a mismatched-owner emergency");
        controller.Reset();controller.Step(new(3,0,true,true,wrong));f.Set("tracked",wrong.Key);
        f.Call("ApplyBarrierBinding",null,passed);
        assert(controller.Tracking&&f.Get<string>("tracked")==wrong.Key,"Passing another mapped booth cannot clear unrelated tracking");
    }
}
