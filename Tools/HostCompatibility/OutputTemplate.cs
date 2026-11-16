using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using ETS2LA.Game;
using ETS2LA.Game.Output;

namespace MautHostCompatibility;

// Authored replacement methods, transplanted only into the exactly verified
// host. This template is never constructed or executed by the preparation tool.
#pragma warning disable CS0649
internal sealed class OutputTemplate
{
    public Dictionary<string,ControlChannel> Channels=null!;
    Dictionary<string,List<Tuple<float,float>>> curFrameFloats=null!;
    Dictionary<string,int> legacyShmOffsets=null!;
    MemoryMappedViewAccessor legacyAccessor=null!,modernAccessor=null!;
    float TickRate;
    bool IsReset;
    bool MemoryAccessAvailable=>throw new NotSupportedException(); // maps to host getter
    void TryOpenMemory()=>throw new NotSupportedException();
    void ProcessChannel(ControlChannel channel)=>throw new NotSupportedException();
    void WriteFloat(MemoryMappedViewAccessor target,int offset,float value)=>throw new NotSupportedException();
    void WriteBool(MemoryMappedViewAccessor target,int offset,bool value)=>throw new NotSupportedException();
    void WriteDouble(MemoryMappedViewAccessor target,int offset,double value)=>throw new NotSupportedException();

    public static object MautGate=null!; // initialized in the real host cctor, not its singleton
    public static long MautEpoch;
    public static bool MautAssists;
    public static int MautMode;
    public const string MautCompatibility="0.3.18-host-3";
    static string? MautHoldKey;
    static long MautHoldEpoch,MautHoldUntil;
    static float MautHoldFloor;
    static long MautHoldIssuedAt;
    static Dictionary<string,long>? MautNativeOrigins;
    static void MautResetOrigins(){MautHoldIssuedAt=0;MautNativeOrigins?.Clear();}
    static void MautClearPlannedHold(){MautHoldKey=null;MautHoldUntil=0;MautHoldFloor=0;}
    public static void MautSetPlannedHold(long epoch,string? key,float floor,long issuedAt)
    {
        lock(MautGate) {
            if(epoch!=MautEpoch)return;
            long now=Stopwatch.GetTimestamp();
            if(issuedAt<=0||issuedAt>now||now-issuedAt>Stopwatch.Frequency/5)return;
            if(issuedAt<=MautHoldIssuedAt)return;
            MautHoldIssuedAt=issuedAt;
            if(!MautAssists||MautMode is <0 or >1||string.IsNullOrWhiteSpace(key)||
                !float.IsFinite(floor)||floor is <.35f or >1) {MautClearPlannedHold();return;}
            MautHoldKey=key;MautHoldEpoch=epoch;MautHoldFloor=floor;
            MautHoldUntil=issuedAt+Stopwatch.Frequency/4;
        }
    }
    public static long MautTrafficAt,MautParkedAt,MautSemaphoreAt;
    public static object? MautTrafficData,MautParkedData,MautSemaphoreData;
    public static Func<bool>? MautCoastPermission;
    public static Func<object,bool>? MautNativePathPermission;
    public static bool MautNativePathKnown(object path)=>MautNativePathPermission?.Invoke(path)==true;
    [ThreadStatic] public static bool MautCurveComplete;
    [ThreadStatic] public static float MautCurveRaw;
    [ThreadStatic] static bool MautCurveFailed,MautLeadingKnown,MautSemaphoreKnown;
    static bool MautCoastBlocked;
    static long MautCoastEpoch;
    [ThreadStatic] static long MautCycleTrafficAt,MautCycleParkedAt;
    static float MautCoastDesired;
    public static void MautBeginCurve(){MautCurveComplete=false;MautCurveFailed=false;MautCurveRaw=float.PositiveInfinity;}
    public static void MautBeginEvaluation() {
        MautBeginCurve();MautLeadingKnown=MautSemaphoreKnown=false;
        MautCycleTrafficAt=Interlocked.Read(ref MautTrafficAt);MautCycleParkedAt=Interlocked.Read(ref MautParkedAt);
    }
    public static void MautMarkNoLimit(int kind,string reason,bool inputsKnown,object? first,object? second) {
        if(kind==0)MautLeadingKnown=inputsKnown&&first!=null&&second!=null&&ReferenceEquals(first,MautTrafficData)&&ReferenceEquals(second,MautParkedData)&&
            (reason=="\nNo significant vehicle. "||reason.StartsWith("\nMost significant vehicle is not in front (",StringComparison.Ordinal));
        if(kind==1)MautSemaphoreKnown=inputsKnown&&first!=null&&ReferenceEquals(first,MautSemaphoreData)&&reason=="No upcoming semaphores.";
    }
    public static void MautCurveSample(float raw){MautCurveRaw=Math.Min(MautCurveRaw,raw);}
    public static void MautInvalidateCurve(){MautCurveFailed=true;}
    public static void MautFinishCurve(){MautCurveComplete=!MautCurveFailed;}
    public static bool MautCanCoast(long epoch,float speed,float desired,float acceleration,Dictionary<string,float> values)
    {
        // This is only a comfort mode. Every binding native safety constraint
        // keeps its original output; unavailable evidence grants no permission.
        double now=Stopwatch.GetTimestamp(),age=Stopwatch.Frequency*.2;
        long traffic=Interlocked.Read(ref MautTrafficAt),parked=Interlocked.Read(ref MautParkedAt);
        bool blocked;
        lock(MautGate) {
            if(epoch!=MautEpoch||!MautAssists)return false;
            if(epoch!=MautCoastEpoch||desired!=MautCoastDesired||speed<=desired) {
                MautCoastEpoch=epoch;MautCoastDesired=desired;MautCoastBlocked=false;
            }
            // Latch belongs to the session, including ticks on another worker.
            if(speed>desired&&(acceleration>.03f||speed-desired>6/3.6f))MautCoastBlocked=true;
            blocked=MautCoastBlocked;
        }
        long semaphore=Interlocked.Read(ref MautSemaphoreAt);
        if(!float.IsFinite(speed)||!float.IsFinite(desired)||!float.IsFinite(acceleration)||speed<50/3.6f||
            desired<50/3.6f||speed<=desired||blocked||
            !MautCurveComplete||float.IsNaN(MautCurveRaw)||MautCurveRaw<speed||
            traffic==0||parked==0||MautCycleTrafficAt==0||MautCycleParkedAt==0||
            now-MautCycleTrafficAt>age||now-MautCycleParkedAt>age||
            semaphore==0||now-semaphore is <0||now-semaphore>age||
            now-traffic is <0||now-parked is <0||now-traffic>age||now-parked>age)return false;
        if(!values.ContainsKey("Path Curvature")||!values.ContainsKey("Leading Vehicle")||!values.ContainsKey("Path Semaphore")||
            !values.ContainsKey("Mautassistent (experimentell)"))return false;
        foreach(var item in values) {
            if(item.Key=="Path Curvature") {if(!float.IsFinite(item.Value)||item.Value>=0)return false;continue;}
            if(float.IsNaN(item.Value)) {
                if(item.Key=="Leading Vehicle"&&MautLeadingKnown||item.Key=="Path Semaphore"&&MautSemaphoreKnown||item.Key=="Mautassistent (experimentell)")continue;
                return false;
            }
            if(!float.IsFinite(item.Value)||item.Value<0)return false;
        }
        if(MautCoastPermission?.Invoke()!=true)return false;
        lock(MautGate)return MautAssists&&MautMode==1&&epoch==MautEpoch&&!MautCoastBlocked&&MautCoastDesired==desired;
    }
    long MautPauseUntil,MautPauseEpoch;
    public static long MautCaptureEpoch(){lock(MautGate)return MautEpoch;}
    public static void MautSetAssists(bool enabled,int actualMode)
    {lock(MautGate){if(MautAssists!=enabled||MautMode!=actualMode){MautClearPlannedHold();MautResetOrigins();MautMode=actualMode;MautAssists=enabled;MautEpoch++;}}}
    public static void MautSetMode(int mode)
    {lock(MautGate){if(MautMode!=mode){MautClearPlannedHold();MautResetOrigins();MautMode=mode;MautEpoch++;}}}
    static long MautDefinitionEpoch(ControlChannelDefinition definition)=>throw new NotSupportedException(); // generated field accessor
    static long MautDefinitionIssuedAt(ControlChannelDefinition definition)=>throw new NotSupportedException(); // generated field accessor
    static ControlChannelDefinition MautSnapshotDefinition(ControlChannelDefinition definition)=>throw new NotSupportedException(); // generated clone
    static bool MautNative(string id)=>id is "AdaptiveCruiseControl.Acceleration" or "LaneAssist.SteeringCorrection" or "LaneAssist.PauseWhenUnstable";
    static bool MautAccept(ControlChannelDefinition definition)
    {
        if(!MautNative(definition.Id))return true;
        long issued=MautDefinitionIssuedAt(definition),now=Stopwatch.GetTimestamp();
        // Age belongs to the native calculation's entry, never ingestion or
        // publication. A same-session blocked tick cannot refresh stale gas.
        if(issued<=0||issued>now||now-issued>Stopwatch.Frequency/5)return false;
        if(!MautAssists||MautMode is <0 or >2||MautDefinitionEpoch(definition)!=MautEpoch)return false;
        return definition.Id=="AdaptiveCruiseControl.Acceleration"?MautMode!=2:MautMode!=0;
    }
    public void OnControlEvent(ControlEvent input)
    {
        lock(MautGate) {
            var definition=input.ChannelDefinition;
            if(definition==null||string.IsNullOrEmpty(definition.Id))return;
            if(!MautAccept(definition)) {
                if(Channels.TryGetValue(definition.Id,out var old)&&!MautAccept(old.Definition))Channels.Remove(definition.Id);
                return;
            }
            if(MautNative(definition.Id)) {
                MautNativeOrigins??=new(StringComparer.Ordinal);
                long origin=MautDefinitionIssuedAt(definition);
                if(MautNativeOrigins.TryGetValue(definition.Id,out long accepted)&&origin<=accepted)return;
                MautNativeOrigins[definition.Id]=origin;
            }
            if(input.Variables==null||input.Properties==null){Channels.Remove(definition.Id);return;}
            if(!Channels.TryGetValue(definition.Id,out var channel)) {
                channel=new ControlChannel {Definition=definition,Properties=input.Properties,Variables=input.Variables};
                Channels.Add(definition.Id,channel);
            }
            // Snapshot both concrete origin fields. Reusing the publisher's
            // definition must not relabel the stored result's epoch or age.
            channel.Definition=MautSnapshotDefinition(definition);
            channel.Properties=input.Properties;channel.Variables=input.Variables;
            channel.LastUpdate.Restart();channel.BoolsProcessed=false;
        }
    }
    // One deterministic frame: no singleton, geometry, callbacks or sleeps
    // under the gate. The same method is exercised with isolated output sinks.
    public void MautFrame(bool modern)
    {
        lock(MautGate) {
            curFrameFloats.Clear();
            long now=Stopwatch.GetTimestamp();
            bool holdActive=MautAssists&&MautMode is 0 or 1&&MautHoldKey!=null&&MautHoldEpoch==MautEpoch&&now<MautHoldUntil;
            if(MautPauseUntil!=0&&(now>=MautPauseUntil||MautPauseEpoch!=MautEpoch||!MautAssists||MautMode==0)) {
                WriteBool(legacyAccessor,legacyShmOffsets["pause"],false);MautPauseUntil=0;
            }
            foreach(var channel in Channels.Values.ToArray()) {
                if(channel.Definition==null)continue;
                if(channel.Properties==null||channel.Variables==null||!MautAccept(channel.Definition)||
                    channel.LastUpdate.Elapsed.TotalSeconds>channel.Definition.Timeout) {
                    Channels.Remove(channel.Definition.Id);continue;
                }
                if(channel.Definition.Id=="LaneAssist.PauseWhenUnstable") {
                    // No deferred ToggleBool task may outlive its origin session.
                    if(!channel.BoolsProcessed&&channel.Variables.pause==true) {
                        WriteBool(legacyAccessor,legacyShmOffsets["pause"],true);
                        MautPauseUntil=now+Stopwatch.Frequency/20;MautPauseEpoch=MautDefinitionEpoch(channel.Definition);
                    }
                    channel.BoolsProcessed=true;
                } else ProcessChannel(channel);
            }
            // Missing owners are neutral AFTER aggregation; a valid fault or
            // other pedal owner keeps its own value, without averaging a zero in.
            if(!curFrameFloats.ContainsKey("acceleration"))curFrameFloats.Add("acceleration",[Tuple.Create(1f,0f)]);
            if(!curFrameFloats.ContainsKey("steering"))curFrameFloats.Add("steering",[Tuple.Create(1f,0f)]);
            double stamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000d;
            foreach(var frame in curFrameFloats) {
                double sum=0,weight=0;
                foreach(var value in frame.Value)if(float.IsFinite(value.Item1)&&value.Item1>0&&float.IsFinite(value.Item2)) {
                    sum+=value.Item1*value.Item2;weight+=value.Item1;
                }
                float output=weight>0?(float)Math.Clamp(sum/weight,-1,1):0;
                if(frame.Key=="steering") {
                    WriteFloat(modernAccessor,0,output);WriteBool(modernAccessor,4,output!=0);WriteDouble(modernAccessor,5,stamp);
                } else if(frame.Key=="acceleration") {
                    // Apply the authorized planned hold AFTER channel weighting,
                    // not as a competing zero/floor owner. It cannot raise or
                    // dilute an existing stronger safety brake.
                    if(holdActive)
                        output=Math.Min(output,-MautHoldFloor);
                    // Clear the path that is no longer selected as well.
                    WriteFloat(modernAccessor,13,modern?output:0);WriteBool(modernAccessor,17,modern&&output!=0);
                    WriteDouble(modernAccessor,18,stamp);
                    WriteFloat(legacyAccessor,legacyShmOffsets["aforward"],modern?0:Math.Max(0,output));
                    WriteFloat(legacyAccessor,legacyShmOffsets["abackward"],modern?0:Math.Max(0,-output));
                } else WriteFloat(legacyAccessor,legacyShmOffsets[frame.Key],output);
            }
            if(Channels.Count==0&&!IsReset) {
                foreach(var field in typeof(ControlVariables).GetFields()) {
                    if(holdActive&&field.Name is "aforward" or "abackward")continue;
                    if(field.FieldType==typeof(float?))WriteFloat(legacyAccessor,legacyShmOffsets[field.Name],0);
                    else if(field.FieldType==typeof(bool?))WriteBool(legacyAccessor,legacyShmOffsets[field.Name],false);
                }
            }
            IsReset=Channels.Count==0;
            modernAccessor.Flush();legacyAccessor.Flush();curFrameFloats.Clear();
        }
    }
    public void Tick()
    {
        var clock=Stopwatch.StartNew();
        while(true) {
            double wait=TickRate-clock.Elapsed.TotalSeconds;
            if(wait>0&&wait<TickRate&&wait*1000>.5){Thread.Sleep((int)(wait*1000));continue;}
            if(!MemoryAccessAvailable){TryOpenMemory();clock.Restart();continue;}
            // Configuration loading stays outside the session/output gate.
            bool modern=GameSettings.Current.EnableModernOutputForPedals;
            MautFrame(modern);clock.Restart();
        }
    }
}
