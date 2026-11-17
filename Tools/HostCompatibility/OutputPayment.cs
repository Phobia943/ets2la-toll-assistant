using System.Diagnostics;

namespace MautHostCompatibility;

internal sealed partial class OutputTemplate
{
    // Semantic game input, never an OS key or the asynchronous ToggleBool path.
    // The callback is a nonblocking read of immutable plugin evidence plus the
    // foreground PID. It must not acquire plugin/route locks or perform I/O.
    public static Func<long,bool>? MautPaymentPermission;
    public static void MautRegisterPaymentPermission(Func<long,bool> callback)
        =>Interlocked.Exchange(ref MautPaymentPermission,callback);
    public static void MautClearPaymentPermission(Func<long,bool> expected)
        =>Interlocked.CompareExchange(ref MautPaymentPermission,null,expected);
    static long MautPaymentAttempt,MautPaymentConsumed,MautPaymentEpoch,MautPaymentSourceEpoch;
    static long MautPaymentIssuedAt,MautPaymentSourceAt,MautPaymentUntil;

    public static void MautSetPaymentLease(long attempt,long epoch,long sourceEpoch,long issuedAt,long sourceAt)
    {
        lock(MautGate) {
            long now=Stopwatch.GetTimestamp();
            if(epoch!=MautEpoch||!MautAssists||MautMode!=1||sourceEpoch!=MautOwnSourceEpoch||
                attempt<=0||attempt>issuedAt||issuedAt>now||now-issuedAt>Stopwatch.Frequency/5||
                sourceAt<=0||sourceAt>now||now-sourceAt>Stopwatch.Frequency/5||
                issuedAt<=MautPaymentIssuedAt)return;
            // A renewed lease can finish an existing pulse but cannot create a
            // second edge, even following cancellation or a source reconnect.
            if(attempt==MautPaymentAttempt&&(epoch!=MautPaymentEpoch||sourceEpoch!=MautPaymentSourceEpoch)||
                attempt<=MautPaymentConsumed&&attempt!=MautPaymentAttempt)return;
            MautPaymentAttempt=attempt;MautPaymentEpoch=epoch;MautPaymentSourceEpoch=sourceEpoch;
            MautPaymentIssuedAt=issuedAt;MautPaymentSourceAt=sourceAt;
        }
    }
    public static void MautCancelPayment(long epoch)
    {
        lock(MautGate) {
            if(epoch!=MautEpoch)return;
            if(MautPaymentAttempt>0)MautPaymentConsumed=Math.Max(MautPaymentConsumed,MautPaymentAttempt);
            MautPaymentAttempt=0;MautPaymentUntil=0;
        }
    }
    public static void MautCancelPaymentAttempt(long epoch,long attempt)
    {
        lock(MautGate) {
            if(attempt>0&&attempt==MautPaymentAttempt)MautCancelPayment(epoch);
        }
    }
    void MautPaymentFrame(long now,bool fault)
    {
        long attempt=MautPaymentAttempt;
        bool valid=!fault&&MautAssists&&MautMode==1&&MautPaymentAttempt>0&&MautPaymentEpoch==MautEpoch&&
            MautPaymentSourceEpoch==MautOwnSourceEpoch&&MautPaymentIssuedAt>0&&MautPaymentIssuedAt<=now&&
            now-MautPaymentIssuedAt<=Stopwatch.Frequency/5&&MautPaymentSourceAt>0&&MautPaymentSourceAt<=now&&
            now-MautPaymentSourceAt<=Stopwatch.Frequency/5;
        if(valid) {
            try {valid=MautPaymentPermission?.Invoke(attempt)==true;}
            catch {valid=false;}
        }
        // A supposedly cheap observer may still be preempted or delayed by
        // collection. Re-read time AFTER it; never use the frame-entry clock
        // to turn an expired request into a new physical pulse.
        now=Stopwatch.GetTimestamp();
        valid=valid&&attempt==MautPaymentAttempt&&MautAssists&&MautMode==1&&MautPaymentEpoch==MautEpoch&&
            MautPaymentSourceEpoch==MautOwnSourceEpoch&&MautPaymentIssuedAt<=now&&MautPaymentSourceAt<=now&&
            now-MautPaymentIssuedAt<=Stopwatch.Frequency/5&&now-MautPaymentSourceAt<=Stopwatch.Frequency/5;
        if(!valid) {
            MautPaymentConsumed=Math.Max(MautPaymentConsumed,MautPaymentAttempt);
            MautPaymentAttempt=0;MautPaymentUntil=0;
        } else if(MautPaymentAttempt>MautPaymentConsumed) {
            MautPaymentConsumed=MautPaymentAttempt;
            // Deadline is the original request, not a delayed output frame.
            MautPaymentUntil=MautPaymentAttempt+Stopwatch.Frequency/10;
        }
        bool high=valid&&now<MautPaymentUntil;
        // Always clear our previous edge, including an otherwise empty frame.
        MautRawWriteBool(legacyAccessor,legacyShmOffsets["activate"],high);
    }
}
