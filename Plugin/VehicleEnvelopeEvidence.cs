using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ETS2LA.Game.Telemetry;
using TruckLib;
namespace MautAssistent;

internal sealed record EnvelopeTrailer(string Id,string Accessory,string Body,string Chain,string Plate,Vector3 Position)
{ internal Vector3? Hook {get;init;} internal IReadOnlyList<WheelSample>? Wheels {get;init;} internal Vector3? RotationTurns {get;init;} }
internal sealed record EnvelopeContext(string Identity,bool Loaded,bool Special,EnvelopeTrailer[] Trailers)
{
    internal long UtcMs {get;init;}
    internal Vector3? TruckHook {get;init;}
    internal IReadOnlyList<WheelSample>? TruckWheels {get;init;}
    internal static EnvelopeContext? Capture(GameTelemetryData data)
    {
        var c=data.configString;
        if(!data.sdkActive||string.IsNullOrWhiteSpace(c.truckId)||string.IsNullOrWhiteSpace(c.truckLicensePlate)||
            !float.IsFinite(data.configFloat.cargoMass)||data.configFloat.cargoMass<0)return null;
        static Vector3? ProvidedHook(Vector3 value)=>SceneSnapshotProtocol.Finite(value)&&value.LengthSquared()>1e-8f?value:null;
        var truckHook=ProvidedHook(data.configVector.truckHookPosition);
        var truckWheels=WheelTelemetryEvidence.Copy(data.configUI.truckWheelCount,data.configVector.truckWheelPositions,
            data.configBool.truckWheelSimulated,data.configBool.truckWheelSteerable,data.configBool.truckWheelLiftable,
            data.truckBool.truckWheelOnGround,data.truckFloat.truckWheelLift,data.truckFloat.truckWheelSteering);
        var trailers=data.trailers.Where(t=>t.comBool.attached).Select(t=>new EnvelopeTrailer(t.conString.id,t.conString.cargoAccessoryId,
            t.conString.bodyType,t.conString.chainType,t.conString.licensePlate,t.comDouble.worldPosition){Hook=ProvidedHook(t.comVector.hookPosition),
                RotationTurns=SceneSnapshotProtocol.Finite(t.comDouble.worldRotation)?t.comDouble.worldRotation:null,
                Wheels=WheelTelemetryEvidence.Copy(t.comUI.wheelCount,t.comVector.wheelPositions,t.conBool.wheelSimulated,t.conBool.wheelSteerable,
                    t.conBool.wheelLiftable,t.comBool.wheelOnGround,t.comFloat.wheelLift,t.comFloat.wheelSteering)}).ToArray();
        var identity=JsonSerializer.Serialize(new{c.truckId,c.truckLicensePlate,c.cargoId,c.cityDstId,c.compDstId,c.citySrcId,c.compSrcId,
            data.configBool.isCargoLoaded,data.configBool.specialJob,data.configFloat.cargoMass,data.configUI.timeAbsDelivery,
            truckHook=truckHook is Vector3 hook?new{hook.X,hook.Y,hook.Z}:null,
            wheels=WheelTelemetryEvidence.Identity(truckWheels),
            trailers=trailers.Select(t=>new{t.Id,t.Accessory,t.Body,t.Chain,t.Plate,hook=t.Hook is Vector3 p?new{p.X,p.Y,p.Z}:null,
                wheels=WheelTelemetryEvidence.Identity(t.Wheels)}).ToArray()});
        return new(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))),data.configBool.isCargoLoaded,data.configBool.specialJob,trailers)
            {UtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),TruckHook=truckHook,TruckWheels=truckWheels};
    }
}
internal sealed record PredictionEnvelope(VehicleGeometry Shape,float PlanningHeight,float PlanningWidth,
    string ContextIdentity,object AssetIdentity,long ContextUtcMs);

// A separate derived enclosure proof. The native observer's diagnostic
// CargoCoverageKnown flag remains unchanged; auxiliary definitions add evidence.
internal sealed class VehicleEnvelopeEvidence(Func<IFileSystem?> readFiles,Func<long>? utcNow=null,
    Func<IFileSystem,EnvelopeContext,VehicleGeometry,VehicleEnvelopeEvidence.Proof>? inspect=null)
{
    internal sealed record Proof(float Height,float Width,string Source,string? Issue)
    {internal Vector3? TrailerLow {get;init;} internal Vector3? TrailerHigh {get;init;}}
    readonly object sync=new();
    readonly Dictionary<string,Proof> results=new();
    readonly HashSet<string> pending=new();
    IFileSystem? source;
    string? lastContext;
    long stableSince;
    RouteTrace? knownTail;
    internal string Status {get;private set;}="Fahrzeughülle noch nicht geprüft.";
    internal object? AssetIdentity=>readFiles();
    // Immutable LOCAL model dimensions are suitable for lane preplanning only.
    // This does not borrow a road/yaw corridor or renew either source capture.
    internal PredictionEnvelope? ReadForPrediction(EnvelopeContext? context,VehicleGeometry? shape,Vector3 position,double speed,double heading)
    {
        if(context==null||shape==null||context.Special||context.Trailers.Length!=1||!shape.TrailerKnown||shape.Trailer==null||
            context.Trailers[0].Chain!="single")return null;
        var files=readFiles();if(files==null)return null;
        long now=utcNow?.Invoke()??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock(sync) {
            if(!ReferenceEquals(source,files)||!Coherent(context,shape,position,speed,heading,now)||
                lastContext!=Key(context,shape)||!results.TryGetValue(lastContext,out var proof)||proof.Issue!=null||
                proof.TrailerLow is not Vector3 low||proof.TrailerHigh is not Vector3 high)return null;
            var expanded=shape with{Trailer=shape.Trailer with{Minimum=low,Maximum=high}};
            return new(expanded,proof.Height,proof.Width,context.Identity,files,context.UtcMs);
        }
    }
    internal float ReadReach(EnvelopeContext context,VehicleGeometry shape,Vector3 position,double speed,double heading)
    {
        lock(sync) {
            long now=utcNow?.Invoke()??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(context.Special||context.Trailers.Length>1||!ReferenceEquals(source,readFiles())||
                !Coherent(context,shape,position,speed,heading,now)||
                lastContext!=Key(context,shape)||!results.TryGetValue(lastContext,out var proof)||proof.Issue!=null)return float.NaN;
            if(shape.Trailer is { } trailer) {
                if(context.Trailers.Length!=1||proof.TrailerLow is not Vector3 low||proof.TrailerHigh is not Vector3 high)return float.NaN;
                shape=shape with{Trailer=trailer with{Minimum=low,Maximum=high}};
            }
            return RigReach.Radius(shape,context.TruckHook,context.Trailers.SingleOrDefault()?.Hook);
        }
    }
    internal (float Height,float Width) Read(EnvelopeContext? context,VehicleGeometry? shape,Vector3 position,double speed,double heading,RouteTrace? road)
    {
        if(context==null||shape==null||context.Special||context.Trailers.Length>1||!shape.TrailerKnown||
            (context.Trailers.Length==1)!=(shape.Trailer!=null)) {Status="Fahrzeug-/Ladungshülle nicht vollständig bekannt; keine Freiraumfreigabe.";return(float.NaN,float.NaN);}
        var files=readFiles();if(files==null)return(float.NaN,float.NaN);
        long observedNow=utcNow?.Invoke()??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if(!Coherent(context,shape,position,speed,heading,observedNow)) {
            Status="Fahrzeughülle: aktuelle Spiel- und Fahrzeugmessung passen nicht zusammen.";return(float.NaN,float.NaN);
        }
        string key=Key(context,shape);
        lock(sync) {
            if(!ReferenceEquals(source,files)){source=files;results.Clear();lastContext=null;knownTail=null;}
            long now=observedNow;
            if(lastContext!=key){lastContext=key;stableSince=now;knownTail=null;}
            if(results.TryGetValue(key,out var proof)) {
                if(proof.Issue!=null){Status=proof.Issue;return(float.NaN,float.NaN);}
                try {
                    var ground=road!=null&&knownTail!=null?road.WithVerifiedTail(knownTail,position)??road:road;
                    var profile=ProfileFor(proof,shape,ground);
                    knownTail=ground;
                    Status=$"Fahrzeughülle an geprüfter Navi-Linie: {profile.Height:F1} m hoch, {profile.Width:F1} m breit (mit Reserve).";
                    return profile;
                } catch(InvalidDataException ex){Status=ex.Message;return(float.NaN,float.NaN);}
            }
            if(pending.Contains(key)){Status="Fahrzeug- und Ladungsmodelle werden geprüft.";return(float.NaN,float.NaN);}
            if(now-stableSince<700||shape.UtcMs<stableSince+100||Math.Abs(speed)>.1) {
                Status="Fahrzeughülle: einmal im Stand auf eine stabile passende Messung warten.";return(float.NaN,float.NaN);
            }
            string[]? archives=inspect==null?IndependentAssets.SnapshotPaths(files):[];if(archives==null)return(float.NaN,float.NaN);
            if(pending.Count>=1)return(float.NaN,float.NaN);
            pending.Add(key);
            ThreadPool.QueueUserWorkItem(_=> {
                Proof result;
                try {
                    if(inspect!=null)result=inspect(files,context,shape);
                    else {using var assets=new IndependentAssets(archives);result=Inspect(assets.Files,context,shape);}
                } catch(Exception ex) {result=new(6,3,"Unknown","Fahrzeughülle nicht freigegeben: "+ex.Message);}
                lock(sync){pending.Remove(key);if(ReferenceEquals(source,files)){if(results.Count>=32)results.Clear();results[key]=result;}}
            });
            Status="Fahrzeug- und Ladungsmodelle werden geprüft.";return(float.NaN,float.NaN);
        }
    }
    internal static string Key(EnvelopeContext context,VehicleGeometry shape)=>JsonSerializer.Serialize(new{context.Identity,shape.ProcessId,shape.Session,
        shape.Truck.Minimum,shape.Truck.Maximum,trailerLow=shape.Trailer?.Minimum,trailerHigh=shape.Trailer?.Maximum},new JsonSerializerOptions{IncludeFields=true});
    internal static bool Coherent(EnvelopeContext context,VehicleGeometry shape,Vector3 position,double speed,double heading,long now)
    {
        if(!double.IsFinite(speed)||!double.IsFinite(heading)||now-shape.UtcMs is < -100 or >300||now-context.UtcMs is < -100 or >300)return false;
        double skew=Math.Abs((double)shape.UtcMs-context.UtcMs)/1000;
        double tolerance=.5+Math.Abs(speed)*skew;
        var forward=Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(shape.Truck.Rotation));forward.Y=0;
        return forward.LengthSquared()>.9&&Vector3.Dot(Vector3.Normalize(forward),TrackingGuard.TruckForward(heading))>=.98&&
            Vector3.Distance(shape.Truck.Position,position)<=tolerance&&
            (context.Trailers.Length==0||shape.Trailer!=null&&Vector3.Distance(context.Trailers[0].Position,shape.Trailer.Position)<=tolerance);
    }
    internal static (float Height,float Width) ProfileFor(Proof proof,VehicleGeometry shape,RouteTrace? road)
    {
        if(road==null)throw new InvalidDataException("Fahrzeughülle: frisch geprüfter Navi-Höhenbezug fehlt.");
        var bodies=new List<VehicleBody>{shape.Truck};
        if(shape.Trailer is { } trailer) {
            if(proof.TrailerLow is not Vector3 low||proof.TrailerHigh is not Vector3 high)throw new InvalidDataException("Ladungshülle fehlt.");
            bodies.Add(trailer with{Minimum=low,Maximum=high});
        }
        float height=0,width=0;
        foreach(var body in bodies) {
            var root=road.ProjectForTracking(body.Position);
            if(root.Offset>3||Math.Abs(root.HeightOffset)>.35||root.Along<.05||root.Along>road.Length-.05)
                throw new InvalidDataException("Fahrzeughülle: Straßenebene oder Fahrzeugursprung nicht eindeutig zugeordnet.");
            var forward=Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(body.Rotation));forward.Y=0;
            if(forward.LengthSquared()<.9)throw new InvalidDataException("Fahrzeughülle: ungewöhnliche Fahrzeugneigung.");
            float bodyTop=body.Maximum.Y;
            float bodyHalfWidth=Math.Max(Math.Abs(body.Minimum.X),Math.Abs(body.Maximum.X));
            double reach=body.Corners().Max(c=>Vector3.Distance(c,body.Position))+1;
            foreach(var corner in body.Corners()) {
                var ground=road.ProjectNearForTracking(corner,root.Along,reach);
                if(ground.Along<.05||ground.Along>road.Length-.05||ground.Offset>5)
                    throw new InvalidDataException("Fahrzeughülle: Straßenfläche unter dem Gespann nicht vollständig verfolgbar.");
                bodyTop=Math.Max(bodyTop,(float)ground.HeightOffset);
                // The clearance corridor is centred on the road, not on the
                // articulated body. Include yaw and lateral root displacement.
                bodyHalfWidth=Math.Max(bodyHalfWidth,(float)ground.Offset);
            }
            height=Math.Max(height,bodyTop+.5f);width=Math.Max(width,2*bodyHalfWidth+.3f);
        }
        height=MathF.Ceiling(height*10)/10;width=MathF.Ceiling(width*10)/10;
        if(height is <3 or >8||width is <2 or >5)throw new InvalidDataException("Fahrzeughülle außerhalb der unterstützten Maße.");
        return(height,width);
    }
    internal static Proof Inspect(IFileSystem files,EnvelopeContext context,VehicleGeometry shape)
    {
        var bounds=new List<(Vector3 Low,Vector3 High)>{(shape.Truck.Minimum,shape.Truck.Maximum)};
        (Vector3 Minimum,Vector3 Maximum)? expanded=null;
        if(context.Trailers.Length==1&&shape.Trailer is { } trailer) {
            var metadata=context.Trailers[0];
            if(metadata.Chain!="single"||metadata.Id=="")throw new InvalidDataException("Unvollständige Aufliegeridentität.");
            if(metadata.Accessory!="")expanded=AuxiliaryCargoEnvelope.Expand(files,metadata.Id,metadata.Accessory,trailer);
            else if(!context.Loaded)expanded=(trailer.Minimum,trailer.Maximum);
            else throw new InvalidDataException("Geladene Ladung ohne auflösbare Definition.");
            bounds.Add(expanded.Value);
        } else if(context.Loaded)throw new InvalidDataException("Ladung ohne zugeordneten Auflieger.");
        float height=bounds.Max(b=>Math.Max(b.High.Y,b.High.Y-b.Low.Y))+.5f;
        float width=bounds.Max(b=>2*Math.Max(Math.Abs(b.Low.X),Math.Abs(b.High.X)))+.3f;
        height=MathF.Ceiling(height*10)/10;width=MathF.Ceiling(width*10)/10;
        if(height is <3 or >8||width is <2 or >5)throw new InvalidDataException("Fahrzeughülle außerhalb der unterstützten Maße.");
        return new(height,width,"Local native model bounds + resolved auxiliary cargo; road/pose checked separately",null)
            {TrailerLow=expanded?.Minimum,TrailerHigh=expanded?.Maximum};
    }
}
