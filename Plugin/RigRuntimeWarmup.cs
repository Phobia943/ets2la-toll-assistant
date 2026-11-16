using System.Numerics;
namespace MautAssistent;

// Runs once IN THE ETS2LA PROCESS before driving readiness. Only pure local
// geometry/data types are used: no SDK, MMF, events, controls or app singletons.
internal static class RigRuntimeWarmup
{
    static readonly Lazy<Task> work=new(()=>Task.Run(Prepare),LazyThreadSafetyMode.ExecutionAndPublication);
    internal static void Start()=>_ =work.Value;
    internal static bool Ready=>work.IsValueCreated&&work.Value.IsCompletedSuccessfully;
    internal static Task Completion=>work.Value;
    internal static string Status=>work.IsValueCreated&&work.Value.IsFaulted?"Vorhersage-Initialisierung fehlgeschlagen":
        "Vorhersage wird einmalig automatisch initialisiert";
    static void Prepare()
    {
        var origin=new Vector3(33544,0,12345);var hook=new Vector3(0,1,2);var trailerHook=new Vector3(0,1,-5);
        var truck=new VehicleBody(origin,Quaternion.Identity,new(-1.54f,.2f,-4),new(1.54f,4,4));
        var trailer=new VehicleBody(origin+new Vector3(0,0,7),Quaternion.Identity,new(-1.3f,.2f,-5.5f),new(1.3f,4,8));
        var road=new RouteTrace([origin+new Vector3(0,0,30),origin,origin+new Vector3(0,0,-80)],[],["warm","warm"]);
        var input=new RigPredictionInput(truck,trailer,hook,trailerHook,new(0,.5f,4),false,false,
            [new(origin+hook,0,30),new(origin+hook+new Vector3(0,0,-35),0,65)],0,.08);
        var prediction=ArticulatedRigPrediction.Predict(input);if(!prediction.Supported)throw new InvalidOperationException(prediction.Issue);
        // A deliberately intersecting face reaches the real triangle kernel;
        // a clear face exercises full traversal and the ordinary return path.
        foreach(float x in new[]{0f,1.69f,20f}) {
            var geometry=new RigStaticGeometry([new(new(x,.3f,-50),new(x,5,-50),new(x,5,50))]);
            geometry.Check(prediction,Matrix4x4.CreateTranslation(origin),_=>0,bodyRoadPlane:(cell,body)=>RigRoadSurface.ForBody(road,cell,body));
        }
        // Reach the clear, deep-BVH, repeated face/SAT workload as well as
        // early collisions. A three-face smoke call alone left the first real
        // large toll traversal outside its output-origin lease on .NET 10.
        var loadFace=new StaticTriangle(new(1.6f,4.3f,-50),new(2.1f,3,-50),new(2.1f,4.3f,50));
        var load=new RigStaticGeometry(Enumerable.Repeat(loadFace,10000));
        for(int pass=0;pass<2;pass++) {
            var result=load.Check(prediction,Matrix4x4.CreateTranslation(origin),_=>0,3000000,
                bodyRoadPlane:(cell,body)=>RigRoadSurface.ForBody(road,cell,body));
            if(!result.Clear||result.FaceChecks<10000)throw new InvalidOperationException(result.Issue??"Geometrie-Initialisierung erreicht den vollständigen Prüfpfad nicht");
        }
        RigStaticGeometry.CheckMovers(prediction,[new(new(0,2,-10),1),new(new(10,2,-10),1)],Matrix4x4.CreateTranslation(origin));
        var shape=new VehicleGeometry(1,1,2,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),truck,true,trailer);
        var wheels=Array.AsReadOnly(new[]{new WheelSample(new(-1,.5f,4),true,false,false,true,0,0),new WheelSample(new(1,.5f,4),true,false,false,true,0,0)});
        var context=new EnvelopeContext("pure warmup",false,false,[new("warm","","box","single","warm",trailer.Position){Hook=trailerHook,RotationTurns=Vector3.Zero,Wheels=wheels}]) {
            UtcMs=shape.UtcMs,TruckHook=hook};
        var envelope=new PredictionEnvelope(shape,4.5f,3.4f,context.Identity,new object(),context.UtcMs);
        _=RigPredictionRuntime.Build(road,30,envelope,context,truck.Position,0,5,shape.UtcMs,0,.08);
        var obstacle=new SceneBody(1,2,0,origin+new Vector3(0,2,-10),Quaternion.Identity,new(1,2,2),1);
        _=SceneAcuteHazard.MeasuredDistance(obstacle,truck,road,truck.Position,5,0);
        var monitor=new RigModelMonitor();var axle=WheelAxle.Trailer(trailerHook,wheels);
        _=monitor.Capture(input,prediction,shape,"warm","warm",axle,shape.UtcMs);
        _=monitor.Check(shape,"warm","warm",axle,30,shape.UtcMs);
        var motion=new SceneMotionPrediction();
        var scene=new SceneSnapshot(1,1,2,shape.UtcMs,1000000,1000000,1,1000000,truck.Position,59,1,1,double.PositiveInfinity,[obstacle],[new(1,3)]);
        motion.Capture(scene,shape.UtcMs);_=motion.Predict(obstacle,.25,shape.UtcMs);
    }
}
