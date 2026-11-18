using MautAssistent;

// Independent behavioral checks for transport interruptions. These use fresh
// advancing synthetic observations; they do not claim a physical brake model.
public static class DeliveryInterruptionReviewTests
{
    static LongitudinalInput Input(double time)=>new(time,1,true,false,true,15,0,25,
        ActualThrottle:0,ActualBrake:0,ReceivedThrottle:0,ReceivedBrake:0);

    static LongitudinalOutput Interrupt(LongitudinalController controller,ref double time,Action<bool,string> check)
    {
        LongitudinalOutput result=new(0,0,0,"");bool noTechnicalFault=true;
        for(int i=0;i<60;i++,time+=.05) {
            result=controller.Step(Input(time) with{AccelerationSafetyLimit=-1.5});
            noTechnicalFault&=!result.Fault&&result.Throttle==0&&float.IsFinite(result.Brake)&&result.Brake is >=0 and <=1;
        }
        check(noTechnicalFault&&result.BrakeDeliveryMissing&&result.DeliveryMissing,
            "Three seconds of absent logical brake receipt warns and blocks propulsion without a technical emergency fault");
        check(result.Brake>0&&result.Brake<1&&result.TargetAcceleration<=-1.5,
            "The current moderate scene braking bound remains effective without becoming a queued full-brake request");
        return result;
    }

    public static void Run(Action<bool,string> check)
    {
        CurrentWorldOverridesOldBrake(check);
        ReceiptAndEffectAreDistinct(check);
        SuspensionNeedsExplicitReset(check);
        RecordedReceiptMagnitudes(check);
    }

    static void CurrentWorldOverridesOldBrake(Action<bool,string> check)
    {
        var controller=new LongitudinalController();double time=1;
        Interrupt(controller,ref time,check);
        var cleared=controller.Step(Input(time));time+=.05;
        check(cleared.BrakeDeliveryMissing&&!cleared.Fault&&cleared.Throttle==0&&cleared.Brake==0,
            "When the current braking reason disappears, a transport warning cannot retain old brake slew or accumulated deceleration");
        bool neutral=true;
        for(int i=0;i<60;i++,time+=.05) {
            var output=controller.Step(Input(time));
            neutral&=output.BrakeDeliveryMissing&&!output.Fault&&output.Throttle==0&&output.Brake==0;
        }
        check(neutral,"Continued missing receipt with a clear current plan never turns the former brake request into delayed braking or gas");

        var newCurve=controller.Step(Input(time) with{AccelerationSafetyLimit=-.8});time+=.05;
        check(!newCurve.Fault&&newCurve.Throttle==0&&newCurve.Brake>0&&newCurve.TargetAcceleration<=-.8,
            "A newly binding curve or traffic deceleration still applies after an established transport warning");
        var emergency=controller.Step(Input(time) with{Emergency=true});time+=.05;
        check(emergency.Throttle==0&&emergency.Brake==1,
            "An explicit current game emergency still demands full braking while pedal reception is unavailable");
        var noEmergency=controller.Step(Input(time));time+=.05;
        check(noEmergency.Brake==0&&noEmergency.Throttle==0&&noEmergency.BrakeDeliveryMissing&&!noEmergency.Fault,
            "Clearing the actual emergency removes its brake immediately instead of preserving it as a delivery-failure latch");

        var closeGate=controller.Step(Input(time) with{StopDistance=1,StopIdentity="new-closed-gate"});time+=.05;
        check(closeGate.Throttle==0&&closeGate.Brake==1,
            "A newly close closed-gate stop envelope remains authoritative even with missing logical brake receipt");
        var gateGone=controller.Step(Input(time));time+=.05;
        check(gateGone.Throttle==0&&gateGone.Brake==0&&gateGone.BrakeDeliveryMissing&&!gateGone.Fault,
            "Removing the current stop envelope cannot leave its old full-brake value waiting for focus recovery");

        var holding=controller.Step(Input(time) with{Speed=0,TargetSpeed=0,PlannedHold=true,StopIdentity="held-gate"});time+=.05;
        check(holding.Holding&&holding.Throttle==0&&holding.Brake>=.35&&!holding.Fault,
            "A confirmed current stationary gate hold survives the transport warning without creating a technical fault");
        var open=controller.Step(Input(time) with{Speed=0,TargetSpeed=10});time+=.05;
        check(!open.Holding&&open.Brake==0&&open.Throttle==0&&open.BrakeDeliveryMissing,
            "A genuinely released gate removes the hold but does not silently acknowledge the propulsion suspension");
        var coast=controller.Step(Input(time) with{TargetSpeed=14,CoastEligible=true,Acceleration=-.05});
        check(coast.Brake==0&&coast.Throttle==0&&!coast.Fault&&coast.BrakeDeliveryMissing,
            "Soft current cruise coasting cannot turn a delivery warning into fault braking");
    }

    static void ReceiptAndEffectAreDistinct(Action<bool,string> check)
    {
        foreach(float? received in new float?[]{null,float.NaN,float.PositiveInfinity}) {
            var controller=new LongitudinalController();bool noInventedReceiptFailure=true;
            for(int i=0;i<60;i++) {
                var output=controller.Step(Input(1+i*.05) with{AccelerationSafetyLimit=-1.5,ReceivedBrake=received});
                noInventedReceiptFailure&=!output.BrakeDeliveryMissing&&!output.Fault&&float.IsFinite(output.Brake);
            }
            check(noInventedReceiptFailure,
                "Unavailable or malformed logical brake feedback does not prove missing receipt from zero effective braking: "+received);
        }
        {
            var controller=new LongitudinalController();LongitudinalOutput previous=new(0,0,0,"");
            bool noFalseDelivery=false,noFault=true;float maximum=0;
            for(int i=0;i<100;i++) {
                previous=controller.Step(Input(1+i*.05) with{AccelerationSafetyLimit=-1.5,ReceivedBrake=previous.Brake,ActualBrake=0});
                noFault&=!previous.Fault&&!previous.BrakeDeliveryMissing;
                maximum=Math.Max(maximum,previous.Brake);
                noFalseDelivery|=previous.Brake>.2;
            }
            check(noFalseDelivery&&noFault,
                "A continuously acknowledged brake command with suppressed simulation effect is not a logical-reception failure");
            check(maximum<.95,
                "Suppressed effective braking cannot train full brake indefinitely against an unchanged moderate scene request");
        }
        foreach(float? effective in new float?[]{null,float.NaN}) {
            var controller=new LongitudinalController();LongitudinalOutput last=new(0,0,0,"");
            for(int i=0;i<60;i++)last=controller.Step(Input(1+i*.05) with{AccelerationSafetyLimit=-1.5,ReceivedBrake=0,ActualBrake=effective});
            check(last.BrakeDeliveryMissing&&!last.Fault&&last.Throttle==0,
                "Known absent logical brake receipt remains diagnosable when effective-brake telemetry is unavailable: "+effective);
        }
    }

    static void SuspensionNeedsExplicitReset(Action<bool,string> check)
    {
        var controller=new LongitudinalController();double time=1;
        Interrupt(controller,ref time,check);
        var pause=controller.Step(Input(time) with{Paused=true});time+=.05;
        check(pause.Throttle==0&&pause.Brake==0&&!pause.Fault,
            "Simulation pause clears physical requests without manufacturing a fault");
        var resumed=controller.Step(Input(time));time+=.05;
        check(resumed.BrakeDeliveryMissing&&resumed.Throttle==0&&resumed.Brake==0&&!resumed.Fault,
            "Pause and returning matching zero feedback do not acknowledge the previous brake-reception suspension");
        var park=controller.Step(Input(time) with{Speed=0,ParkingBrake=true});time+=.05;
        check(park.ParkingHold&&park.Throttle==0&&park.Brake==0&&!park.Fault&&park.BrakeDeliveryMissing,
            "Stationary parking brake keeps transport suspension separate from pedal output and technical faults");
        var unpark=controller.Step(Input(time) with{Speed=0});time+=.05;
        check(unpark.BrakeDeliveryMissing&&unpark.Throttle==0&&unpark.Brake==0,
            "Releasing the parking brake does not silently clear a missing-reception warning");
        controller.Step(Input(time) with{Enabled=false});time+=.05;
        var acknowledged=controller.Step(Input(time) with{Speed=0});
        check(!acknowledged.BrakeDeliveryMissing&&!acknowledged.DeliveryMissing&&!acknowledged.Fault&&
            acknowledged.Brake==0&&acknowledged.Throttle is >0 and <=.03f,
            "Explicit manual cancellation starts a fresh bounded propulsion ramp with no stored braking or transmission latch");
    }

    static void RecordedReceiptMagnitudes(Action<bool,string> check)
    {
        // Targeted synthetic cases at the two recorded receipt magnitudes.
        // The scene bound is authored to begin with that brake request; this
        // is not a reconstruction of either complete drive or physics trace.
        foreach(var sample in new[]{
            (Brake:.241178f,ReceivedGas:.04888548f,EffectiveGas:.0257815f),
            (Brake:.18138894f,ReceivedGas:.28972113f,EffectiveGas:0f)}) {
            var controller=new LongitudinalController();double time=1;
            double bound=-LongitudinalController.MaximumBrakeDeceleration*sample.Brake;
            LongitudinalInput Observation()=>Input(time) with {
                AccelerationSafetyLimit=bound,ReceivedBrake=0,ActualBrake=0,
                ReceivedThrottle=sample.ReceivedGas,ActualThrottle=sample.EffectiveGas
            };
            var first=controller.Step(Observation());time+=.05;
            check(Math.Abs(first.Brake-sample.Brake)<.000001&&first.Throttle==0&&!first.Fault,
                "Synthetic receipt case begins at recorded brake magnitude "+sample.Brake);
            bool noEmergency=true;LongitudinalOutput last=first;
            for(int i=0;i<80;i++,time+=.05) {
                last=controller.Step(Observation());
                noEmergency&=!last.Fault&&last.Throttle==0&&last.Brake is >0 and <1;
            }
            check(noEmergency&&last.BrakeDeliveryMissing&&!last.ThrottleDeliveryMissing,
                "Recorded brake magnitude with absent logical brake and retained gas receipt warns without technical full braking: "+sample.Brake);
            var cleared=controller.Step(Observation() with{AccelerationSafetyLimit=null});
            check(cleared.Brake==0&&cleared.Throttle==0&&!cleared.Fault&&cleared.BrakeDeliveryMissing,
                "Removing the synthetic scene bound at the recorded receipt magnitude clears old braking despite stale received gas: "+sample.Brake);
        }
    }
}
