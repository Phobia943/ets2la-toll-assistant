using System.Numerics;
using ETS2LA.Game.Telemetry;
namespace MautAssistent;
internal static class WheelTelemetryIntegration
{
    internal static void Snapshot(Action<bool,string> assert)
    {
        var data=new GameTelemetryData();data.sdkActive=true;data.configString.truckId="fixture.truck";
        data.configString.truckLicensePlate="fixture";data.configVector.truckHookPosition=new(0,1,2);
        data.configUI.truckWheelCount=4;
        for(int i=0;i<4;i++) {
            data.configVector.truckWheelPositions[i]=new(i%2==0?-1:1,.5f,i<2?-3:2);
            data.configBool.truckWheelSimulated[i]=true;data.configBool.truckWheelSteerable[i]=i<2;
            data.truckBool.truckWheelOnGround[i]=true;
        }
        var trailer=data.trailers[0];trailer.comBool.attached=true;trailer.conString.id="fixture.trailer";
        trailer.comVector.hookPosition=new(0,1,-5);trailer.comUI.wheelCount=6;
        for(int i=0;i<6;i++) {
            trailer.comVector.wheelPositions[i]=new(i%2==0?-1:1,.5f,3+i/2*1.3f);
            trailer.conBool.wheelSimulated[i]=true;trailer.comBool.wheelOnGround[i]=true;
        }
        var snapshot=EnvelopeContext.Capture(data)!;
        assert(snapshot.TruckWheels?.Count==4&&snapshot.Trailers.Single().Wheels?.Count==6,
            "Actual reported counts select full truck and trailer wheel observations");
        assert(WheelAxle.Trailer(snapshot.Trailers[0].Hook!.Value,snapshot.Trailers[0].Wheels).Supported,
            "Typed trailer telemetry supplies the fixed axle model with measured local coordinates");
        var original=trailer.comVector.wheelPositions[0];trailer.comVector.wheelPositions[0]=new(9,9,9);
        trailer.conBool.wheelSteerable[0]=true;
        assert(snapshot.Trailers[0].Wheels![0].Position==original&&!snapshot.Trailers[0].Wheels![0].Steerable,
            "Changing a provider array cannot mutate an earlier wheel snapshot");
        assert(snapshot.Identity!=EnvelopeContext.Capture(data)!.Identity,"Changed static wheel coordinates or steering configuration invalidate source identity");
        trailer.comVector.wheelPositions[0]=original;trailer.conBool.wheelSteerable[0]=false;
        trailer.comBool.wheelOnGround[0]=false;trailer.comFloat.wheelLift[0]=1;
        assert(snapshot.Identity==EnvelopeContext.Capture(data)!.Identity,"Dynamic ground/lift state does not discard the immutable cargo proof");
        trailer.comUI.wheelCount=17;
        assert(EnvelopeContext.Capture(data)!.Trailers[0].Wheels==null,"Over-capacity counts are unknown, never silently truncated to sixteen wheels");
        trailer.comUI.wheelCount=6;trailer.comVector.wheelPositions=new Vector3[2];
        assert(EnvelopeContext.Capture(data)!.Trailers[0].Wheels==null,"Incomplete typed arrays cannot invent missing axle positions");
        trailer.comVector.wheelPositions=new Vector3[16];trailer.comVector.wheelPositions[0]=new(float.NaN,0,0);
        assert(EnvelopeContext.Capture(data)!.Trailers[0].Wheels==null,"Nonfinite source values become unknown without throwing during identity serialization");
    }
}
