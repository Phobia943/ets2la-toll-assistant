namespace MautAssistent;

// SI units. Pitch is positive uphill. Callers bind all measurements and limits
// to the current source/route epoch before invoking this pure controller.
public sealed record LongitudinalInput(double Time,long Epoch,bool Enabled,bool Paused,bool RouteKnown,
    double Speed,double Acceleration,double TargetSpeed,double? AccelerationSafetyLimit=null,
    double? StopDistance=null,bool PlannedHold=false,bool Emergency=false,bool OutputAvailable=true,
    double Pitch=0,float? ActualThrottle=null,float? ActualBrake=null,bool CoastEligible=false,string? StopIdentity=null,
    float? ReceivedThrottle=null,float? ReceivedBrake=null,bool ParkingBrake=false,bool ReverseGear=false,bool RollbackDetected=false,bool StationaryDisplacement=false)
{
    public PedalFocusSample? Focus {get;init;}
}

public sealed record LongitudinalOutput(float Throttle,float Brake,double TargetAcceleration,string Reason,
    bool Holding=false,bool Fault=false)
{
    public bool Coasting {get;init;}
    public bool DeliveryMissing {get;init;}
    public bool ThrottleDeliveryMissing {get;init;}
    public bool ThrottleEffectLimited {get;init;}
    public bool BrakeDeliveryMissing {get;init;}
    public bool BrakeEffectLimited {get;init;}
    public bool ParkingHold {get;init;}
    public bool ManualRequired {get;init;}
    public bool FocusSuspended {get;init;}
}

// Own speed/acceleration-to-pedal controller. No game objects, native ACC/PID,
// learned vehicle model, timers or output calls are accessed here. Feedback
// coefficients are a bounded control model, not a guarantee of brake physics.
public sealed class LongitudinalController
{
    public const double MaximumAcceleration=1.2,MaximumBrakeDeceleration=4.5,ComfortJerk=.8;
    public const double StandstillTolerance=ForwardMotion.StationarySpeed,MaximumStep=.25,CoastOverspeed=6/3.6;
    double? at;
    long epoch;
    double filteredAcceleration,accelerationCommand,driveIntegral,brakeIntegral,deliveryMismatch,throttleMismatch;
    float throttle,brake;
    bool holding,brakeDeliverySuspended,throttleDeliveryFault,throttleEffectLimited,brakeEffectLimited,wasStopping;
    double? coastSince;
    double coastTarget;
    bool coastBlocked;
    string? stopOwner;
    int pedalMode;
    PedalFocusRecovery focusRecovery=new();
    public bool ParkingHeld {get;private set;}
    public bool FocusReturnPending=>focusRecovery.ReturnPending;
    public bool FocusSuspended=>focusRecovery.Suspended;

    public void Reset()
    {
        at=null;epoch=0;filteredAcceleration=accelerationCommand=driveIntegral=brakeIntegral=deliveryMismatch=throttleMismatch=0;
        throttle=brake=0;holding=brakeDeliverySuspended=throttleDeliveryFault=throttleEffectLimited=brakeEffectLimited=wasStopping=false;stopOwner=null;pedalMode=0;
        coastSince=null;coastTarget=0;coastBlocked=false;
        ParkingHeld=false;
        focusRecovery=new();
    }
    public void Pause()
    {
        bool gasSuspended=throttleDeliveryFault,brakeSuspended=brakeDeliverySuspended;
        var focus=focusRecovery;
        Reset();focusRecovery=focus;focusRecovery.Interrupt();throttleDeliveryFault=gasSuspended;brakeDeliverySuspended=brakeSuspended;
    }
    void DropPedalHistory()
    {driveIntegral=brakeIntegral=accelerationCommand=0;throttle=brake=0;pedalMode=0;}
    // Reduction-only reconciliation with the actual final command. This is
    // not a logical receipt, reset or permit to clear any failure/provenance.
    public void ReduceUnpublishedPedals(float publishedThrottle,float publishedBrake)
    {
        if(!float.IsFinite(publishedThrottle)||!float.IsFinite(publishedBrake)||publishedThrottle<0||publishedBrake<0)return;
        if(publishedThrottle<throttle){focusRecovery.RetainPendingWindows();throttle=publishedThrottle;driveIntegral=0;}
        if(publishedBrake<brake){focusRecovery.RetainPendingWindows();brake=publishedBrake;brakeIntegral=0;}
    }
    public bool FocusCommitAllowed(PedalFocusSample? evaluated,PedalFocusSample? latest)
    {
        bool allowed=focusRecovery.CanCommit(evaluated,latest);
        if(!allowed&&focusRecovery.ReturnPending)focusRecovery.RejectReturn(latest);
        return allowed;
    }
    public void AcceptFocusReturn(PedalFocusSample? evaluated,PedalFocusSample? latest)
    {if(FocusCommitAllowed(evaluated,latest))focusRecovery.AcceptReturn(evaluated,latest);}
    LongitudinalOutput Neutral(string reason,bool fault=false)
    {Reset();return new(0,0,0,reason,Fault:fault);}
    public LongitudinalOutput Step(LongitudinalInput input)
    {
        var result=StepCore(input);
        if(focusRecovery.Suspended&&result.Throttle>0){ReduceUnpublishedPedals(0,result.Brake);result=result with{Throttle=0,Reason="Expected focus interruption: propulsion suspended"};}
        return result with {DeliveryMissing=result.DeliveryMissing||throttleDeliveryFault||brakeDeliverySuspended,FocusSuspended=focusRecovery.Suspended,
            ThrottleDeliveryMissing=throttleDeliveryFault,ThrottleEffectLimited=throttleEffectLimited,
            BrakeDeliveryMissing=brakeDeliverySuspended,BrakeEffectLimited=brakeEffectLimited};
    }
    LongitudinalOutput StepCore(LongitudinalInput input)
    {
        if(!input.Enabled)return Neutral("Manual control");
        if(input.Paused){Pause();return new(0,0,0,"Paused");}
        if(!double.IsFinite(input.Time)||input.Time<0||input.Epoch<=0) {
            if(input.Emergency){Reset();return new(0,1,-MaximumBrakeDeceleration,"Emergency brake requested without a valid control clock",Fault:true);}
            return Neutral("Invalid control clock",true);
        }
        if(at.HasValue&&epoch!=input.Epoch)Reset();
        double dt=at.HasValue?input.Time-at.Value:.02;
        if(!double.IsFinite(dt)||dt<=0||dt>MaximumStep) {
            Reset();at=input.Time;epoch=input.Epoch;
            return new(0,input.Emergency?1:0,input.Emergency?-MaximumBrakeDeceleration:0,"Control clock did not advance within its bounded step",Fault:true);
        }
        bool first=!at.HasValue;at=input.Time;epoch=input.Epoch;dt=Math.Min(dt,.1);
        focusRecovery.BeginStep(input.Focus);
        if(focusRecovery.Transition)DropPedalHistory();
        if(input.Emergency) {
            ObserveDelivery(input,dt);
            driveIntegral=brakeIntegral=0;throttle=0;brake=1;accelerationCommand=-MaximumBrakeDeceleration;
            return new(0,1,accelerationCommand,"Emergency brake requested",Fault:!input.OutputAvailable);
        }
        if(!double.IsFinite(input.Speed)||input.Speed>100||!double.IsFinite(input.Acceleration)||Math.Abs(input.Acceleration)>20||
            !double.IsFinite(input.Pitch)||Math.Abs(input.Pitch)>.35)return Neutral("Invalid control measurement",true);
        if(input.Speed < -StandstillTolerance||input.RollbackDetected)return Neutral("Reverse travel is outside forward control",true);
        if(input.ReverseGear)return Neutral("Reverse gear selected: manual control required") with{ManualRequired=true};
        if(!input.OutputAvailable)return Neutral("Pedal transport unavailable",true);
        bool parkStand=input.ParkingBrake&&!input.StationaryDisplacement&&Math.Abs(input.Speed)<=(ParkingHeld?ForwardMotion.StationarySpeed:ForwardMotion.EnterStationarySpeed);
        if(parkStand||ParkingHeld) {
            // Never fight a confirmed stationary parking brake or integrate
            // absent acceleration against it. Preserve a latched propulsion
            // failure; releasing the parking brake is not acknowledgement.
            focusRecovery.RetainPendingWindows();
            driveIntegral=brakeIntegral=deliveryMismatch=throttleMismatch=0;
            throttle=brake=0;accelerationCommand=0;filteredAcceleration=input.Acceleration;
            holding=wasStopping=false;stopOwner=null;pedalMode=0;
        }
        ParkingHeld=parkStand;
        if(parkStand)return new(0,0,0,"Stationary parking brake: pedals neutral"){ParkingHold=true};
        double speed=Math.Max(0,input.Speed);
        filteredAcceleration=first?input.Acceleration:filteredAcceleration+(input.Acceleration-filteredAcceleration)*(1-Math.Exp(-dt/.15));
        if(input.AccelerationSafetyLimit is double invalidLimit&&!double.IsFinite(invalidLimit)||
            input.StopDistance is double invalidDistance&&(!double.IsFinite(invalidDistance)||invalidDistance is <0 or >10000)||
            input.RouteKnown&&(!double.IsFinite(input.TargetSpeed)||input.TargetSpeed<0||input.TargetSpeed>50))
            return Neutral("Invalid longitudinal plan or safety limit",true);

        bool known=input.RouteKnown;
        double target=known?input.TargetSpeed:0;
        double hardLimit=input.AccelerationSafetyLimit??double.PositiveInfinity;
        if(!known)hardLimit=Math.Min(hardLimit,-1.2);
        if(input.PlannedHold||input.ParkingBrake){target=0;hardLimit=Math.Min(hardLimit,-1.2);}
        bool stopping=input.StopDistance.HasValue||input.PlannedHold||input.ParkingBrake||!known;
        bool shouldHold=speed<=.12&&(input.PlannedHold||input.ParkingBrake||target<=.01||input.StopDistance is <=.3);
        string? owner=input.StopIdentity;
        if(holding!=shouldHold||stopOwner!=owner||wasStopping&&!stopping) {
            driveIntegral=brakeIntegral=deliveryMismatch=0;accelerationCommand=0;throttle=brake=0;pedalMode=0;
        }
        holding=shouldHold;stopOwner=owner;wasStopping=stopping;
        bool mismatched=ObserveDelivery(input,dt);
        if(focusRecovery.Suspended)DropPedalHistory();
        if(brakeDeliverySuspended||throttleDeliveryFault) {
            // A transport interruption is not an obstacle. Remove learned and
            // slew-limited pedal history before evaluating THIS frame's scene
            // limits; focus return must never replay an expired brake request.
            driveIntegral=brakeIntegral=accelerationCommand=0;
            throttle=brake=0;pedalMode=0;
        }
        bool actualKnown=Pedal(input.ActualThrottle)&&Pedal(input.ActualBrake);
        if(hardLimit<=-MaximumBrakeDeceleration) {
            driveIntegral=brakeIntegral=0;throttle=0;brake=1;accelerationCommand=hardLimit;
            return new(0,1,hardLimit,"Safety bound requires full braking");
        }
        if(shouldHold) {
            driveIntegral=brakeIntegral=0;throttle=0;brake=.4f;accelerationCommand=Math.Min(0,hardLimit);
            if(!double.IsFinite(accelerationCommand))accelerationCommand=0;
            if(hardLimit<0)brake=Math.Max(brake,(float)Math.Clamp(-hardLimit/MaximumBrakeDeceleration,0,1));
            return new(0,brake,accelerationCommand,known?"Planned standstill":"Route unknown: planned standstill",true);
        }

        if(input.StopDistance is double stopDistance) {
            double usable=Math.Max(0,stopDistance-.25-speed*.35);
            double envelope=Math.Min(Math.Sqrt(2*1.2*usable),.6*Math.Max(0,stopDistance-.25));
            target=Math.Min(target,envelope);
            if(speed>envelope+.02)hardLimit=Math.Min(hardLimit,-speed*speed/(2*Math.Max(.05,usable)));
        }
        if(hardLimit<=-MaximumBrakeDeceleration) {
            driveIntegral=brakeIntegral=0;throttle=0;brake=1;accelerationCommand=hardLimit;
            return new(0,1,hardLimit,"Stop envelope requires full braking");
        }
        double error=target-speed;
        if(target!=coastTarget||speed<=target){coastSince=null;coastBlocked=false;coastTarget=target;}
        bool coastCandidate=input.CoastEligible&&!stopping&&hardLimit>=0&&error<0&&-error<=CoastOverspeed&&input.Pitch>=-.005;
        if(coastCandidate&&!coastBlocked) {
            coastSince??=input.Time;
            // Releasing gas has a physical/measurement lag. A short bounded
            // neutral interval avoids a brake blip from that previous gas, but
            // sustained acceleration cannot perpetually renew the interval.
            if(input.Time-coastSince.Value>.75&&filteredAcceleration>.05)coastBlocked=true;
        } else coastSince=null;
        bool coast=coastCandidate&&!coastBlocked;
        if(coast) {
            throttle=brake=0;driveIntegral=brakeIntegral=deliveryMismatch=0;accelerationCommand=0;pedalMode=0;
            return new(0,0,0,"Coasting toward soft cruise target"){Coasting=true};
        }
        double desired=Math.Clamp(error*.55,-1.8,MaximumAcceleration);
        // StopDistance caps the speed profile; it must not turn a distant stop
        // into a negative acceleration at standstill. The negative bound above
        // applies only when the truck is actually faster than that profile.
        desired=Math.Min(desired,hardLimit);
        desired=Math.Clamp(desired,-MaximumBrakeDeceleration,MaximumAcceleration);
        double comfortable=Move(accelerationCommand,desired,ComfortJerk*dt);
        // A binding safety/stop limit is an upper bound, never jerk-smoothed
        // into a larger acceleration. Pedal response still needs verification.
        accelerationCommand=Math.Min(comfortable,hardLimit);
        accelerationCommand=Math.Clamp(accelerationCommand,-MaximumBrakeDeceleration,MaximumAcceleration);

        if(brakeDeliverySuspended) {
            driveIntegral=brakeIntegral=0;throttle=0;
            brake=hardLimit<0?(float)Math.Clamp(-hardLimit/MaximumBrakeDeceleration,.05,1):0;
            return new(0,brake,Math.Min(0,accelerationCommand),"Brake input not received: propulsion suspended; current scene braking only");
        }
        double gravity=9.81*Math.Sin(input.Pitch);
        double force=accelerationCommand+gravity;
        bool startingForward=speed<=.2&&target>speed&&hardLimit>=0;
        bool braking=hardLimit<0||accelerationCommand<-.03||force<-.08&&!startingForward;
        if(braking) {
            if(pedalMode!= -1){brakeIntegral=0;driveIntegral=0;}pedalMode=-1;
            double errorAcceleration=filteredAcceleration-accelerationCommand;
            double request=Math.Max(0,-force/MaximumBrakeDeceleration+errorAcceleration*.12+brakeIntegral);
            if(actualKnown&&!mismatched&&speed>.2&&request<.98)
                brakeIntegral=Math.Clamp(brakeIntegral+errorAcceleration*.25*dt,-.2,.65);
            float wanted=(float)Math.Clamp(request,0,1);
            throttle=0;brake=(float)Move(brake,wanted,.9*dt);
            if(hardLimit<0)brake=Math.Max(brake,(float)Math.Clamp(-hardLimit/MaximumBrakeDeceleration,.02,1));
            return new(0,brake,accelerationCommand,known?"Bounded deceleration":"Route unknown: planned deceleration");
        }
        if(pedalMode!=1){driveIntegral=0;brakeIntegral=0;}pedalMode=1;
        // Missing gas is not an obstacle or a reason to remove healthy steering.
        // Suspend only propulsion until explicit reset. Current stopping and
        // deceleration bounds above remain authoritative, including full brake.
        if(throttleDeliveryFault) {
            driveIntegral=0;throttle=brake=0;
            return new(0,0,Math.Min(0,accelerationCommand),"Throttle input not received: propulsion suspended until explicit reset");
        }
        double accelerationError=accelerationCommand-filteredAcceleration;
        double drive=.08+.006*speed+force/1.6+accelerationError*.18+driveIntegral;
        if(actualKnown&&!mismatched&&speed>.2&&drive is >0 and <.98)
            driveIntegral=Math.Clamp(driveIntegral+accelerationError*.18*dt,-.25,.65);
        if(speed<=.2) {driveIntegral=0;drive=Math.Min(drive,.55);} // never relearn full throttle while stalled
        float wantedThrottle=(float)Math.Clamp(drive,0,1);
        // A temporary simulation-side cut must not train a higher pedal while
        // the drivetrain is disconnected. Reductions remain immediate.
        if(mismatched)wantedThrottle=Math.Min(wantedThrottle,throttle);
        brake=0;throttle=(float)Move(throttle,wantedThrottle,wantedThrottle<throttle?1:.45*dt);
        return new(throttle,0,accelerationCommand,"Tracking bounded cruise speed");
    }
    bool ObserveDelivery(LongitudinalInput input,double dt)
    {
        // SCS input.throttle acknowledges an input; effective.throttle is the
        // simulation's processed pedal (attack/drive-train/cruise effects).
        // Do not infer transport loss from effective throttle alone.
        throttleEffectLimited=Pedal(input.ActualThrottle)&&throttle>.15f&&input.ActualThrottle!.Value<throttle-.15f;
        bool missingThrottle=Pedal(input.ReceivedThrottle)&&throttle>.15f&&input.ReceivedThrottle!.Value<throttle-.15f;
        bool gasReceipt=Pedal(input.ReceivedThrottle)&&throttle>.15f&&input.ReceivedThrottle!.Value>=throttle-.15f;
        if(focusRecovery.Observe(true,missingThrottle,gasReceipt,dt,input.Focus))throttleDeliveryFault=true;
        throttleMismatch=missingThrottle?throttleMismatch+dt:0;
        brakeEffectLimited=Pedal(input.ActualBrake)&&brake>.15f&&input.ActualBrake!.Value<brake-.15f;
        // Logical receipt and simulated effect are separate observations for
        // the brake too. Neither is, by itself, a physical emergency reason.
        bool missingBrake=Pedal(input.ReceivedBrake)&&brake>.15f&&input.ReceivedBrake!.Value<brake-.15f;
        bool brakeReceipt=Pedal(input.ReceivedBrake)&&brake>.15f&&input.ReceivedBrake!.Value>=brake-.15f;
        if(focusRecovery.Observe(false,missingBrake,brakeReceipt,dt,input.Focus))brakeDeliverySuspended=true;
        deliveryMismatch=missingBrake?deliveryMismatch+dt:0;
        // Freeze adaptation on either missing input or suppressed effective gas.
        return missingThrottle||throttleEffectLimited||missingBrake||brakeEffectLimited;
    }
    static bool Pedal(float? value)=>value is >=0 and <=1&&float.IsFinite(value.Value);
    static double Move(double value,double target,double amount)=>target>value?Math.Min(target,value+amount):Math.Max(target,value-amount);
}
