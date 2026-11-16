using System.Numerics;
namespace MautAssistent;
internal static class WheelTelemetryEvidence
{
    internal static IReadOnlyList<WheelSample>? Copy(int count,Vector3[]? positions,bool[]? simulated,bool[]? steerable,
        bool[]? liftable,bool[]? onGround,float[]? lift,float[]? steering)
    {
        if(count is <2 or >16||positions==null||simulated==null||steerable==null||liftable==null||onGround==null||lift==null||steering==null||
            new[]{positions.Length,simulated.Length,steerable.Length,liftable.Length,onGround.Length,lift.Length,steering.Length}.Any(n=>n<count))return null;
        var snapshot=Enumerable.Range(0,count).Select(i=>new WheelSample(positions[i],simulated[i],steerable[i],liftable[i],onGround[i],lift[i],steering[i])).ToArray();
        if(snapshot.Any(w=>!SceneSnapshotProtocol.Finite(w.Position)||!float.IsFinite(w.Lift)||!float.IsFinite(w.Steering)))return null;
        return Array.AsReadOnly(snapshot);
    }
    // Pose/contact changes do not invalidate immutable cargo/model inspection.
    // Static wheel geometry still belongs to the vehicle source identity.
    internal static object? Identity(IReadOnlyList<WheelSample>? wheels)=>wheels?.Select(w=>new {
        position=new{w.Position.X,w.Position.Y,w.Position.Z},w.Simulated,w.Steerable,w.Liftable}).ToArray();
}
