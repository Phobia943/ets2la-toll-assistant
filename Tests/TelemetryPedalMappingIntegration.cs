using System.Reflection;
using ETS2LA.Game.Telemetry;

public static class TelemetryPedalMappingIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true);
        f.Call("StopDiagnostics");
        var telemetry=new GameTelemetryData {sdkActive=true,time=100,simulatedTime=1000000,renderTime=1000000};
        telemetry.truckPlacement.coordinate=new(f.Point.X,f.Point.Y,f.Point.Z);
        telemetry.truckPlacement.rotation=new(.5,0,0);telemetry.truckFloat.speed=8;
        telemetry.truckFloat.userThrottle=.81f;telemetry.truckFloat.gameThrottle=.12f;
        telemetry.truckFloat.userBrake=.34f;telemetry.truckFloat.gameBrake=.56f;
        telemetry.truckFloat.userClutch=.23f;telemetry.truckFloat.gameClutch=.91f;
        telemetry.truckInt.gear=12;telemetry.truckInt.gearDashboard=11;telemetry.truckFloat.engineRpm=1450;
        f.Call("OnTelemetry",telemetry);
        var observation=f.Get<object>("physicsObservation");
        T Read<T>(string name)=>(T)observation.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public)!.GetValue(observation)!;
        assert(Read<float>("ReceivedThrottle")==.81f&&Read<float>("GameThrottle")==.12f&&
            Read<float>("ReceivedBrake")==.34f&&Read<float>("GameBrake")==.56f,
            "Actual OnTelemetry preserves separately received virtual-capable inputs and simulation-effective pedals");
        assert(Read<float>("EffectiveClutch")==.91f&&Read<int>("Gear")==12&&Read<int>("DashboardGear")==11&&Read<float>("EngineRpm")==1450,
            "Actual OnTelemetry copies effective clutch, engine gear, dashboard gear and RPM without substituting input clutch or equal defaults");
        telemetry.truckFloat.userThrottle=0;telemetry.truckFloat.gameThrottle=1;telemetry.truckFloat.gameClutch=0;
        telemetry.truckInt.gear=0;telemetry.truckInt.gearDashboard=7;telemetry.truckFloat.engineRpm=900;
        assert(Read<float>("ReceivedThrottle")==.81f&&Read<float>("GameThrottle")==.12f&&Read<float>("EffectiveClutch")==.91f&&Read<int>("Gear")==12,
            "Mutating the publisher's telemetry object cannot alter the already delivered coherent diagnostic frame");
        telemetry.time++;telemetry.simulatedTime+=20000;f.Call("OnTelemetry",telemetry);observation=f.Get<object>("physicsObservation");
        assert(Read<int>("Gear")==0&&Read<int>("DashboardGear")==7&&Read<float>("EngineRpm")==900,
            "Neutral engine gear and a different dashboard gear remain independent observations, not an inferred shift flag");
        telemetry.truckInt.gear=-2;telemetry.truckInt.gearDashboard=-1;telemetry.time++;telemetry.simulatedTime+=20000;
        f.Call("OnTelemetry",telemetry);observation=f.Get<object>("physicsObservation");
        assert(Read<int>("Gear")==-2&&Read<int>("DashboardGear")==-1&&!f.State.EnableAssists,
            "Signed reverse gears are preserved and passive telemetry receipt cannot arm driving");
    }
}
