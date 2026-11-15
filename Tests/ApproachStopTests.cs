using MautAssistent;

public static class ApproachStopTests
{
    public static void RecordedCases(Action<bool,string> assert)
    {
        // Speeds and later retained distances are deliberately not presented
        // as an atomic replay of the original trigger frame.
        foreach(var (kph,distance) in new[]{(2.504242730140686,4.1346988194413825),
            (2.0766745805740356,3.9147806738803865),(1.3156429409980774,-1.0095278551545164)}) {
            var hold=new ApproachStopController();
            var result=hold.Step("recorded-plaza",kph/3.6,distance);
            assert(!result.RequiresTakeover&&result.TargetSpeed==0&&result.MaxAcceleration<-.5,
                "Observed soft-target conditions must request a native-published hold, not a latched emergency");
            assert(hold.Step("recorded-plaza",0,8).MaxAcceleration<-.5&&hold.Holding,
                "Position/distance jitter cannot restart a reached hold");
            assert(hold.Step("next-plaza",0,200).MaxAcceleration>0&&!hold.Holding,
                "A different stop cannot inherit a previous holding latch without an intervening tick");
            hold.Reset();assert(hold.StopKey==null&&!hold.Holding,"Manual reset clears stop identity");
        }
        assert(ApproachStop.Evaluate(2,-1.1).MaxAcceleration<0,
            "The old soft-line counterexample still has physical stopping room before the protected boundary");
        var controller=new ApproachStopController();controller.Step("same",0,0);
        bool rejected=false;try{controller.Step("same",0,-7.01);}catch(InvalidOperationException){rejected=true;}
        assert(rejected,"Holding must retain the real protected-boundary check");
    }

    public static void DelayedHold(Action<bool,string> assert)
    {
        foreach(double responseDelay in new[]{1d,1.5d}) {
            var controller=new ApproachStopController();
            var queue=new Queue<double>(Enumerable.Repeat(0d,(int)(responseDelay/.05)));
            double speed=25,distance=300,lastPublished=0,minDistance=distance;
            for(int step=0;step<2400;step++) {
                var result=controller.Step("plaza",speed,distance);
                // Reproduce the installed ACC's no-publish band. An unchanged
                // previous positive command must not survive the actual hold.
                if(!(speed<.5&&result.MaxAcceleration is >=-.5f and <=.1f))lastPublished=result.MaxAcceleration;
                queue.Enqueue(lastPublished);double acceleration=queue.Dequeue();
                double next=Math.Max(0,speed+acceleration*.05);
                distance-=(speed+next)*.025;speed=next;minDistance=Math.Min(minDistance,distance);
            }
            assert(speed<.01&&controller.Holding&&minDistance>0&&lastPublished<-.5,
                $"Delayed host simulation must settle before the soft target: delay={responseDelay}, speed={speed}, distance={distance}");
        }
    }
}
