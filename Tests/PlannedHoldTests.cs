using MautAssistent;
public static class PlannedHoldTests
{
    public static void Authority(Action<bool,string> assert)
    {
        var hold=new PlannedHoldBrake();
        assert(hold.Step("A",0,.384,3,true)==.35f,"A reached stop starts with a physical brake floor, not an acceleration target");
        assert(hold.Step("A",.36,.384,2.85,true)>=.65f,"Observed continued creeping strengthens the brake promptly");
        assert(hold.Step("A",.81,.384,2.68,true)==1,"Continued movement reaches full pedal before the old six-metre drift");
        assert(hold.Step("A",1,0,2.68,true)==1,"Reaching standstill cannot weaken an escalated hold");
        assert(hold.Step("B",1.1,0,10,true)==.35f,"Another stop never inherits the previous creep history");
        assert(hold.Step("B",1.2,0,10,false)==0,"Cancellation, pause or missing fresh authority releases the policy");
        assert(hold.Step("B",1.3,double.NaN,10,true)==0,"Invalid speed cannot retain a lease");
        assert(hold.Step("B",1.4,3,10,true)==0,"A brake hold cannot become an arbitrary high-speed pedal override");
        var slow=new PlannedHoldBrake();slow.Step("slow",0,.002,3,true);
        assert(slow.Step("slow",50,.002,2.79,true)>=.65f,"Accumulated drift escalates even below the speed-noise threshold");
        var rollback=new PlannedHoldBrake();rollback.Step("rollback",0,-.02,3,true);
        assert(rollback.Step("rollback",.81,-.02,3.02,true)==1,"Slow sustained rollback also reaches a strong floor without six metres of travel");
        var lost=new PlannedHoldBrake();lost.Step("lost",0,.384,3,true);lost.Step("lost",.81,.384,2.7,true);
        bool fault=false;try{lost.Step("lost",2.4,.384,2.1,true);}catch(InvalidOperationException){fault=true;}
        assert(fault,"Full brake without meaningful slowing becomes an explicit hold failure");
        var plateau=new PlannedHoldBrake();plateau.Step("plateau",0,.384,3,true);plateau.Step("plateau",.81,.384,2.7,true);
        plateau.Step("plateau",1,.32,2.6,true);
        bool stalled=false;try{plateau.Step("plateau",2.6,.32,2.1,true);}catch(InvalidOperationException){stalled=true;}
        assert(stalled,"One initial speed decrease cannot excuse a later permanent creep equilibrium");
        var quiet=new PlannedHoldBrake();quiet.Step("quiet",0,.384,3,true);quiet.Step("quiet",.81,.384,2.7,true);
        quiet.Step("quiet",1,0,2.7,true);quiet.Step("quiet",10,0,2.7,true);
        assert(quiet.Step("quiet",10.05,.031,2.7,true)==1&&quiet.Step("quiet",10.1,0,2.7,true)==1,
            "A tiny impulse after a long correct hold is not retroactively four seconds of movement");
        quiet.Step("quiet",10.2,.32,2.7,true);
        bool resumed=false;try{quiet.Step("quiet",11.8,.32,2.2,true);}catch(InvalidOperationException){resumed=true;}
        assert(resumed,"Renewed sustained creeping after a quiet phase gets a fresh bounded window");
        bool accelerated=false;try{hold.Step("fast",2,0,3,true);hold.Step("fast",2.1,2.6,3,true);}catch(InvalidOperationException){accelerated=true;}
        assert(accelerated,"An already active hold must not silently relinquish authority if movement grows");
    }
    public static void ObservedCreep(Action<bool,string> assert)
    {
        foreach(double direction in new[]{1d,-1d}) {
            var hold=new PlannedHoldBrake();double speed=.384*direction,distance=3,time=0,travel=0;
            var delayed=new Queue<(double At,float Pedal)>();float pedal=.089f;double stoppedAt=double.NaN;
            while(time<5) {
                float requested=hold.Step("recorded-plaza",time,speed,distance,true);
                delayed.Enqueue((time+.3,requested));
                while(delayed.TryPeek(out var next)&&next.At<=time){pedal=delayed.Dequeue().Pedal;}
                // Adversarial plant: the recorded 8.9% native brake exactly
                // balances idle/grade force. Requested acceleration is NEVER
                // equated with vehicle deceleration in this regression.
                double deceleration=Math.Max(0,pedal-.089)*1.2;
                speed=Math.CopySign(Math.Max(0,Math.Abs(speed)-deceleration*.02),speed);
                double moved=speed*.02;distance-=moved;travel+=Math.Abs(moved);time+=.02;
                if(Math.Abs(speed)<.001&&double.IsNaN(stoppedAt))stoppedAt=time;
            }
            assert(double.IsFinite(stoppedAt)&&stoppedAt<2&&travel<.5,
                "Delayed physical brake floor stops the observed forward/rollback equilibrium within 0.5 m");
        }
    }
}
