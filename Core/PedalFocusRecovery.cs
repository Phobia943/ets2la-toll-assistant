namespace MautAssistent;

// Optional caller-certified context; absence retains strict receipt behavior.
// ProcessIdentity is an opaque verified process-lifetime token, never a PID
// substitute. Original clocks are neither renewed by reads nor by duplicates.
public sealed record PedalFocusSample(long Now,long Frequency,long ContextOrigin,long PhysicsOrigin,
    long SourceEpoch,int ProcessId,[property:System.Text.Json.Serialization.JsonIgnore] object ProcessIdentity,
    bool Foreground,ulong SdkTime,ulong SimTime,bool ReadyForReturn);

public sealed class PedalFocusRecovery
{
    public const double MissingSeconds=.35,ReturnStableSeconds=.2;
    sealed class Window {
        public double Missing;
        public bool OtherOnly,Retain;
        public PedalFocusSample? Start,Last;
        public void Clear(){Missing=0;OtherOnly=Retain=false;Start=Last=null;}
    }
    readonly Window gas=new(),brake=new();
    PedalFocusSample? binding,lastReturn;
    double returnStable;
    bool revoked;
    public bool Suspended {get;private set;}
    public bool Transition {get;private set;}
    public bool Resumed {get;private set;}
    public bool ReturnPending {get;private set;}
    public void RetainPendingWindows()
    {if(gas.Missing>0)gas.Retain=true;if(brake.Missing>0)brake.Retain=true;}
    static bool Fresh(long origin,long now,long frequency,double age)=>origin>0&&frequency>0&&now>=origin&&now-origin<=frequency*age;
    public static bool Valid(PedalFocusSample? sample)=>sample is {ProcessId:>0,SourceEpoch:>=0,ProcessIdentity:not null} s&&
        s.SdkTime>0&&s.SimTime>0&&Fresh(s.ContextOrigin,s.Now,s.Frequency,.1)&&Fresh(s.PhysicsOrigin,s.Now,s.Frequency,.2);
    static bool Same(PedalFocusSample a,PedalFocusSample b)=>a.ProcessId==b.ProcessId&&a.SourceEpoch==b.SourceEpoch&&
        a.Frequency==b.Frequency&&ReferenceEquals(a.ProcessIdentity,b.ProcessIdentity);
    public void Interrupt()
    {
        if(ReturnPending){Suspended=true;ReturnPending=false;}
        returnStable=0;lastReturn=null;
        if(gas.Missing>0){gas.OtherOnly=false;gas.Retain=true;}
        if(brake.Missing>0){brake.OtherOnly=false;brake.Retain=true;}
    }
    public void BeginStep(PedalFocusSample? sample)
    {
        Transition=Resumed=false;
        if(!Suspended&&!ReturnPending)return;
        if(!Valid(sample)){if(ReturnPending){Suspended=true;ReturnPending=false;}Interrupt();return;}
        var current=sample!;
        if(binding==null||!Same(binding,current)) {revoked=true;Suspended=true;ReturnPending=false;Interrupt();return;}
        if(revoked||!current.Foreground||!current.ReadyForReturn){Suspended=true;ReturnPending=false;returnStable=0;lastReturn=null;return;}
        if(lastReturn is { } priorReturn&&(current.Now<priorReturn.Now||current.ContextOrigin<priorReturn.ContextOrigin||
            current.PhysicsOrigin<priorReturn.PhysicsOrigin||current.SdkTime<priorReturn.SdkTime||current.SimTime<priorReturn.SimTime)) {
            revoked=true;Interrupt();return;
        }
        if(ReturnPending) {
            // A rejected/slow publication cannot keep an old return window
            // alive using only repeated context or physics heartbeats.
            if(lastReturn is not { } pending||current.Now<pending.Now||current.Now-pending.Now>current.Frequency*.1) {
                Interrupt();return;
            }
            if(current.ContextOrigin>pending.ContextOrigin&&current.PhysicsOrigin>pending.PhysicsOrigin&&
                current.SdkTime>pending.SdkTime&&current.SimTime>pending.SimTime)lastReturn=current;
            return;
        }
        if(lastReturn is not { } prior) {
            // A correctly classified focus interruption is not a defect to
            // heal. Begin new foreground receipt monitoring, but never erase
            // a contaminated/genuine pending window from before the tab.
            foreach(var window in new[]{gas,brake})if(window.OtherOnly)window.Clear();
            lastReturn=current;return;
        }
        if(current.Now<prior.Now||current.ContextOrigin<prior.ContextOrigin||current.PhysicsOrigin<prior.PhysicsOrigin||
            current.SdkTime<prior.SdkTime||current.SimTime<prior.SimTime) {revoked=true;Interrupt();return;}
        bool advancing=current.ContextOrigin>prior.ContextOrigin&&current.PhysicsOrigin>prior.PhysicsOrigin&&
            current.SdkTime>prior.SdkTime&&current.SimTime>prior.SimTime;
        if(!advancing) {
            if(current.Now-prior.Now>current.Frequency*.1){returnStable=0;lastReturn=null;}
            return;
        }
        double elapsed=(current.Now-prior.Now)/(double)current.Frequency;
        if(elapsed<=0||elapsed>.1){returnStable=0;lastReturn=current;return;}
        returnStable+=elapsed;lastReturn=current;
        if(returnStable+1e-9<ReturnStableSeconds)return;
        Suspended=false;ReturnPending=Transition=Resumed=true;returnStable=0;lastReturn=current;
        // Only expected-focus windows were consumed by Suspend below. Genuine
        // pending receipt windows and all controller fault latches survive.
    }
    public bool Observe(bool throttle,bool missing,bool positiveReceipt,double dt,PedalFocusSample? sample)
    {
        var window=throttle?gas:brake;
        bool other=Valid(sample)&&sample is {Foreground:false};
        if(window.Missing>0) {
            var prior=window.Last;
            // Retaining a counter during neutralized output must retain the
            // evidence too: foreground/unknown/gaps cannot become invisible.
            window.OtherOnly &=other&&window.Start!=null&&sample!=null&&Same(window.Start,sample)&&prior!=null&&
                sample.ContextOrigin>prior.ContextOrigin&&sample.PhysicsOrigin>=prior.PhysicsOrigin&&
                sample.SdkTime>=prior.SdkTime&&sample.SimTime>=prior.SimTime&&
                sample.Now>=prior.Now&&sample.Now-prior.Now<=sample.Frequency*.1;
            window.Last=sample;
        }
        if(!missing) {
            if(window.Missing>0&&window.Retain&&!positiveReceipt)return false;
            window.Clear();return false;
        }
        if(window.Missing==0) {
            window.OtherOnly=other;window.Start=sample;window.Last=sample;
        }
        window.Missing+=dt;
        if(window.Missing+1e-12<MissingSeconds)return false;
        if(!window.OtherOnly)return true; // Original strict persistent defect.
        if(!Suspended) {
            binding=window.Start;Suspended=Transition=true;returnStable=0;lastReturn=null;
            foreach(var pending in new[]{gas,brake})if(pending.Missing>0&&!pending.OtherOnly)pending.Retain=true;
        }
        window.Clear();return false;
    }
    public bool CanCommit(PedalFocusSample? evaluated,PedalFocusSample? latest)
    {
        if(Suspended)return false;
        if(evaluated==null)return !ReturnPending; // Strict legacy controller, no auto recovery.
        return Valid(latest)&&Same(evaluated,latest!)&&latest!.Foreground==evaluated.Foreground&&
            (!ReturnPending||latest.Foreground&&latest.ReadyForReturn&&lastReturn is { } pending&&
                latest.Now>=pending.Now&&latest.Now-pending.Now<=latest.Frequency*.1&&
                latest.ContextOrigin>=pending.ContextOrigin&&latest.PhysicsOrigin>=pending.PhysicsOrigin&&
                latest.SdkTime>=pending.SdkTime&&latest.SimTime>=pending.SimTime);
    }
    public void AcceptReturn(PedalFocusSample? evaluated,PedalFocusSample? latest)
    {if(ReturnPending&&CanCommit(evaluated,latest)){ReturnPending=false;binding=null;}}
    public void RejectReturn(PedalFocusSample? latest)
    {
        if(!ReturnPending)return;
        if(latest is { } current&&lastReturn is { } previous&&
            (current.Now<previous.Now||current.ContextOrigin<previous.ContextOrigin||current.PhysicsOrigin<previous.PhysicsOrigin||
             current.SdkTime<previous.SdkTime||current.SimTime<previous.SimTime||binding!=null&&!Same(binding,current)))revoked=true;
        Interrupt();
    }
}
