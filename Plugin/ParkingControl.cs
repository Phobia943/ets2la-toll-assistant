namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly ReverseMotionEvidence reverseMotion=new();
    bool CurrentParkingStandstill(TelemetryFrame? frame=null)
    {
        frame??=Volatile.Read(ref latestDeliveredTelemetry);
        double age=frame==null?double.PositiveInfinity:clock.Elapsed.TotalSeconds-frame.Arrived;
        return frame is {Active:true,Paused:false}&&age is >=0 and <.2&&frame.Physics.ParkingBrake&&
            !reverseMotion.Confirmed&&!reverseMotion.StationaryDisplaced&&double.IsFinite(frame.Speed)&&
            Math.Abs(frame.Speed)<=(longitudinal.ParkingHeld?ForwardMotion.StationarySpeed:ForwardMotion.EnterStationarySpeed);
    }
    bool ObserveReverseMotion(long epoch)
    {
        var frame=appliedTelemetry;
        if(frame==null){reverseMotion.Reset();return false;}
        return reverseMotion.Observe(new(frame.Physics.SimulatedTime,frame.Arrived,epoch,frame.Position,frame.Heading,frame.Speed,
            frame.Active&&!frame.Paused&&clock.Elapsed.TotalSeconds-frame.Arrived is >=0 and <.2));
    }
    void SuspendNormalHoldsForParking(long epoch,long issuedAt)
    {
        // Reset only ordinary hold adaptation, never a fault latch or route
        // constraint. The same physical reasons are evaluated again on release.
        holdBrake.Reset();sceneHold.Reset();holdPedalFloor=scenePedalFloor=0;
        PublishPlannedHold(epoch,null,0,issuedAt);
    }
}
