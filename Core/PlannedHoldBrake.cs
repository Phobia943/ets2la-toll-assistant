namespace MautAssistent;

// An acceleration target is not proof of a stationary vehicle. This policy
// owns a minimum brake pedal only after a keyed planned stop has been reached.
public sealed class PlannedHoldBrake
{
    string? key;
    double? movingSince;
    double originDistance,lastTime=double.NegativeInfinity;
    double? fullSince;
    double fullSpeed,progressAt,fullDistance;
    float floor;
    public void Reset(){key=null;movingSince=fullSince=null;lastTime=double.NegativeInfinity;floor=0;}
    public float Step(string stopKey,double time,double speed,double remaining,bool holding)
    {
        if(holding&&key==stopKey&&floor>0&&double.IsFinite(speed)&&Math.Abs(speed)>2.5)
            throw new InvalidOperationException("Geplanter Mauthalt fehlgeschlagen: Lkw beschleunigt trotz Haltebremse.");
        if(!holding||string.IsNullOrWhiteSpace(stopKey)||!double.IsFinite(time)||!double.IsFinite(speed)||
            !double.IsFinite(remaining)||Math.Abs(speed)>2.5||time<lastTime) {Reset();return 0;}
        if(key!=stopKey) {Reset();key=stopKey;originDistance=remaining;floor=.35f;}
        lastTime=time;
        if(Math.Abs(speed)>.005)movingSince??=time;
        if(Math.Abs(remaining-originDistance)>=.2)floor=Math.Max(floor,.65f);
        if(Math.Abs(remaining-originDistance)>=.4)floor=1;
        if(Math.Abs(speed)>.005&&movingSince is double began) {
            if(time-began>=.35)floor=Math.Max(floor,.65f);
            if(time-began>=.8)floor=1;
        }
        if(floor==1&&fullSince==null){fullSince=time;progressAt=time;fullSpeed=Math.Abs(speed);fullDistance=remaining;}
        if(fullSince is double full) {
            if(Math.Abs(speed)<=.03){progressAt=time;fullSpeed=Math.Abs(speed);fullSince=time;fullDistance=remaining;}
            else if(Math.Abs(speed)<fullSpeed-.05){progressAt=time;fullSpeed=Math.Abs(speed);}
            if(Math.Abs(remaining-originDistance)>=1.5||Math.Abs(speed)>.03&&
                (time-progressAt>=1.5||time-fullSince!.Value>=4||Math.Abs(remaining-fullDistance)>=2))
                throw new InvalidOperationException("Geplanter Mauthalt fehlgeschlagen: keine ausreichende Verzögerung trotz voller Haltebremse.");
        }
        // Never ease the brake again within the same held stop, including
        // stopping after a creep episode or a slight rollback on a slope.
        return floor;
    }
}
