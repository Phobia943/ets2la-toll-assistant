using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ETS2LA.Shared;
using ETS2LA.Backend;
using ETS2LA.Backend.Events;
using ETS2LA.Game.SDK;
using ETS2LA.Game.Telemetry;
using ETS2LA.State;
using ETS2LA.Logging;
using PathLib;
using ETS2LA.Controls;
using ETS2LA.Controls.Defaults;
using ETS2LA.Game.Output;

namespace MautAssistent;

public sealed class TollPlugin : Plugin
{
    public static TollPlugin? Current { get; private set; }
    public override PluginInformation Info => new() {
        Id = "local.mautassistent", Name = "Mautassistent", Version = "0.2.10",
        Description = "Langsame Mautdurchfahrt. Bereitet sich im Stand automatisch vor; Fahrt startet erst mit F8.",
        AuthorName = "Lokale Anpassung", Dependencies = ["tumppi066.pathlib"],
        PluginPageButtonName = "Einstellungen"
    };
    public override float TickRate => 20;
    readonly object sync = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly AccBridge bridge = new();
    readonly StartupGuard startup = new();
    readonly AutomaticPreparation automaticPreparation = new();
    readonly PassageMemory passage=new();
    string lastPathError="";
    object? routeTransition;
    readonly PreferenceStore preferences = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ETS2LA-Mautassistent","settings.json"));
    readonly TollRouter router;
    readonly Func<ApplicationState> readState;
    readonly Func<IPlugin?> readAcc;
    readonly Action<string,string> notify;
    readonly FaultBrake faultBrake;
    readonly TrackingGuard trackingGuard=new();
    RouteTrace? safetyTrace;
    double? safetyStarted;
    double headingTurns;
    ApplicationState State => readState();
    public TollPlugin() : this(() => ApplicationState.Current, Notify,
        output=>Events.Current.Publish(GameOutput.Current.EventString,output)) { }
    internal TollPlugin(Func<ApplicationState> readState, Action<string,string> notify,Action<ControlEvent>? output=null,Func<IPlugin?>? readAcc=null) {
        this.readState=readState;this.readAcc=readAcc??FindAcc;this.notify=notify;router=new(preferences);faultBrake=new(output??(_=>{}));
    }
    string preparationReason="Automatische Vorbereitung wartet auf Stillstand und verbundene Module.";
    double lastStartWarning=double.NegativeInfinity;
    static void Notify(string title,string content) {
        ETS2LA.Notifications.NotificationHandler.Current.SendNotification(new() {
            Id="local.mautassistent.takeover", Title=title, Content=content,
            Level=ETS2LA.Notifications.NotificationLevel.Warning, CloseAfter=20});
        _=Task.Run(()=> {try {if(OperatingSystem.IsWindows())Console.Beep(880,250);}catch{}});
    }
    // Runs from both incoming data callbacks and the plugin tick. This is a
    // restart interlock, not an atomic veto in the host's input/output system.
    bool RejectUnpreparedStart()
    {
        if(active || preview || !State.EnableAssists)return false;
        State.EnableAssists=false;
        if(clock.Elapsed.TotalSeconds-lastStartWarning>=5) {
            lastStartWarning=clock.Elapsed.TotalSeconds;
            notify("Fahrassistenz nicht gestartet",preparationReason+" Im Stand auf Bereitschaft warten; danach F8.");
        }
        return true;
    }
    public TollRouter.LaneChoice[] LaneChoices {get {lock(sync)return router.Choices.ToArray();}}
    public int SelectedLane => router.SelectedGate ?? -1;
    string? routeFault;
    object? lastFault;
    bool preview;
    double previewAt;
    string previewStatus="Noch keine Routenprobe";
    public string RouteStatus {get {lock(sync)return previewStatus+" · "+router.Status;}}
    TollOptions options = new();
    TollController controller = new(new());
    TollDecision decision = TollDecision.Idle("Plugin ausgeschaltet");
    PlannedPathData? path;
    ETS2LA.Game.SDK.Semaphore[] gates = [];
    Vector3 position;
    double speed, telemetryAt = -100, gatesAt = -100, pathAt = -100, gameTimeChangedAt = -100, evaluatedAt = -100;
    ulong gameTime;
    bool sdk, paused, active, subscribed, decisionArmed;
    string? tracked;
    Gate? candidate;
    double lastWrite = -100, lastMapWrite = -100;
    string lastLogged = "";
    public string DiagnosticPath => Path.Combine(AppContext.BaseDirectory, "MautAssistent", "status.json");
    public TollOptions Options { get { lock(sync) return options; } }
    public bool Active { get { lock(sync) return active; } }
    public bool FaultBraking { get { lock(sync) return faultBrake.Latched; } }
    public string Status { get { lock(sync) return faultBrake.Latched ? "Fehlerbremsung aktiv – F9 zur manuellen Übernahme: "+preparationReason : preview ? "Routenprobe ohne Fahrsteuerung" : active ? (State.EnableAssists ? "Bremsbegrenzung: "+decision.Phase : "Bereit – F8 startet die Fahrassistenz") : "Nicht vorbereitet – F8 gesperrt: "+preparationReason; } }
    public string Details { get { lock(sync) return $"Tempo {speed*3.6:F1} km/h · Vorschlag {(decision.TargetSpeed is double cap ? (cap*3.6).ToString("F1") : "–")} km/h · Schranke {candidate?.Key ?? tracked ?? "–"} · Abstand {candidate?.Distance.ToString("F1") ?? "–"} m"; } }
    public string DataStatus { get { lock(sync) {
        double now = clock.Elapsed.TotalSeconds;
        if (!sdk || now-telemetryAt >= .5) return "Warte auf Spieltelemetrie.";
        if (paused) return "Das Spiel meldet Pause.";
        if (now-gameTimeChangedAt >= .5) return "Die Spielzeit steht; warte auf laufende Simulation.";
        if (now-gatesAt >= .5) return "Warte auf Schranken-SDK.";
        if (path == null || now-pathAt >= 1) return "Warte auf Pathfinding-Routendaten.";
        if (path.HasError) return !State.EnableAssists ? "Verbindung bereit. Die Route wird nach F8 berechnet." : "Routenplanung: " + path.ErrorMessage;
        return $"Spiel, Schranken-SDK und Route verbunden. {gates.Count(g=>g.type==SemaphoreType.GATE)} Schranken in den SDK-Daten.";
    } } }

    public override void Init() {
        Current = this;
        try {
            preferences.MigrateFrom(Path.Combine(AppContext.BaseDirectory,"MautAssistent","settings.json"));
            preferences.Load(); options=preferences.Options;controller=new(options);
        }
        catch(Exception ex) {Logger.Warn("Maut-Einstellungen konnten nicht geladen werden: "+ex.Message);}
    }
    public override void OnEnable()
    {
        lock(sync)
        {
            if (subscribed) return;
            _=State; // Register host controls before our cancel listener.
            faultBrake.Release();trackingGuard.Reset();safetyTrace=null;safetyStarted=null;
            active = false; controller.Reset(); tracked = null; passage.Reset();
            automaticPreparation.Request();
            preparationReason="Automatische Vorbereitung wartet auf Stillstand und verbundene Module.";
            telemetryAt = gatesAt = pathAt = gameTimeChangedAt = -100;
            // Ensure the providers exist before subscribing to their published frames.
            Events.Current.Subscribe<GameTelemetryData>(GameTelemetry.Current.EventString, OnTelemetry);
            Events.Current.Subscribe<SemaphoreData>(SemaphoreProvider.Current.EventString, OnGates);
            Events.Current.Subscribe<PlannedPathData>("Pathfinding.PlannedPathData", OnPath);
            ControlsBackend.Current.On(DefaultControls.Cancel.Id,OnCancel);
            subscribed = true;
        }
        base.OnEnable();
        Logger.Info("Mautassistent: automatische Vorbereitung im Stand; Fahrassistenz bleibt ausgeschaltet.");
    }

    void OnTelemetry(GameTelemetryData data)
    {
        lock(sync)
        {
            RejectUnpreparedStart();
            telemetryAt = clock.Elapsed.TotalSeconds;
            if (gameTime != data.time) { gameTimeChangedAt = telemetryAt; gameTime = data.time; }
            sdk = data.sdkActive; paused = data.paused; speed = data.truckFloat.speed;
            position = data.truckPlacement.coordinate.ToVector3();
            headingTurns=data.truckPlacement.rotation.X;
            SafetyWatchdog(telemetryAt);
        }
    }
    void OnCancel(object? sender,ControlChangeEventArgs e)
    {
        // Same release edge as the host's F9 / Cancel control.
        if(e.NewValue is not false)return;
        lock(sync) {
            faultBrake.Release();trackingGuard.Reset();safetyTrace=null;safetyStarted=null;
            if(!active) {
                automaticPreparation.Request();
                preparationReason="Übernahme bestätigt. Automatische Vorbereitung wartet auf Stillstand und verbundene Module.";
            }
        }
    }
    void SafetyWatchdog(double now)
    {
        if(faultBrake.Latched) {State.EnableAssists=false;faultBrake.Refresh();return;}
        if(!active||!State.EnableAssists||preview) {safetyStarted=null;trackingGuard.Reset();safetyTrace=null;return;}
        safetyStarted??=now;
        if(!sdk||now-telemetryAt>=.5) {Disarm("Spieltelemetrie verloren.");return;}
        if(paused)return;
        if(path?.HasError==true && path.ErrorMessage is not "Assists are disabled." and not "Game is paused.") {
            Disarm("Routenverfolgung verloren: "+path.ErrorMessage);return;
        }
        bool freshRoute=path!=null&&!path.HasError&&now-pathAt<1;
        if((trackingGuard.HasRoute&&!freshRoute)||(!trackingGuard.HasRoute&&now-safetyStarted.Value>=2)) {
            Disarm("Keine aktuelle verfolgte Route verfügbar.");return;
        }
        if(safetyTrace!=null&&freshRoute) {
            string? fault=trackingGuard.Check(safetyTrace,position,headingTurns,speed,State.DrivingMode!=DrivingMode.AdaptiveCruiseControlOnly);
            if(fault!=null)Disarm(fault);
        }
    }
    void OnGates(SemaphoreData data)
    {
        lock(sync)
        {
            gates = data.semaphores.Select(s => new ETS2LA.Game.SDK.Semaphore {
                position=s.position, cx=s.cx, cy=s.cy, rotation=s.rotation, type=s.type, state=s.state, id=s.id
            }).ToArray();
            gatesAt = clock.Elapsed.TotalSeconds;
        }
    }
    void OnPath(PlannedPathData data) { lock(sync) {
        path = data; pathAt = clock.Elapsed.TotalSeconds;
        if(RejectUnpreparedStart())return;
        // Brake before diagnostic I/O and independently of ACC's evaluation.
        if(active&&State.EnableAssists&&!preview&&data.HasError&&data.ErrorMessage is not "Assists are disabled." and not "Game is paused.") {
            Disarm("Routenverfolgung verloren: "+data.ErrorMessage);return;
        }
        if(data.HasError && data.ErrorMessage!=lastPathError && data.ErrorMessage is not "Assists are disabled." and not "Game is paused.") {
            routeTransition=new {utc=DateTimeOffset.UtcNow,error=data.ErrorMessage,active,
                assists=State.EnableAssists,speedKph=speed*3.6,
                x=position.X,y=position.Y,z=position.Z,tracked,decision.Phase};
            try {Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath)!);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(DiagnosticPath)!,"route-error.json"),JsonSerializer.Serialize(routeTransition));}
            catch(IOException){} catch(UnauthorizedAccessException){}
        }
        lastPathError=data.HasError?data.ErrorMessage??"":"";
        routeFault=null;
        if(!data.HasError) {
            try {router.Discover(data,position,gates);}
            catch(Exception ex) {routeFault="Mautspuren konnten nicht geprüft werden: "+ex.Message;if(active)Disarm(routeFault);}
        }
        if((active || preview) && !data.HasError) {
            try {router.Apply(data,position,out routeFault);}
            catch(Exception ex){routeFault="Maut-Spurwahl: "+ex.Message;}
            if(routeFault!=null) Disarm(routeFault);
            if(active&&State.EnableAssists&&!preview&&sdk&&!paused) {
                try {
                    var trace=RouteMatcher.SampleForTracking(data,position,speed);
                    var fault=trackingGuard.Check(trace,position,headingTurns,speed,State.DrivingMode!=DrivingMode.AdaptiveCruiseControlOnly);
                    if(fault!=null)Disarm(fault);else safetyTrace=trace;
                } catch(Exception ex) {Disarm("Routenverfolgung nicht möglich: "+ex.Message);}
            }
            if(preview && (routeFault!=null || clock.Elapsed.TotalSeconds-previewAt>1)) {
                try {
                    var selected=RouteMatcher.Select(data,position,gates,null);
                    previewStatus=routeFault ?? (selected!=null?$"Routenprobe: Schranke {selected.Key}, {selected.Distance:F1} m":"Routenprobe: Route vorhanden, keine zugeordnete Mautschranke");
                    var report=new {utc=DateTimeOffset.UtcNow,routeFault,selected,router.Status,
                        geometry=RouteMatcher.Diagnose(data,position,gates),
                        sampledTruckProjection=RouteMatcher.SampleForTracking(data,position,speed).Project(position),
                        items=data.PlannedPath.Select(i=>new{kind=i.item.GetType().Name,length=i.GetLength(),
                            prefab=i.prefabPath?.Prefab.Prefab.Uid.ToString(),
                            curves=i.prefabPath?.Curves.Select(c=>new {id=i.prefabPath.Prefab.Descriptor!.NavCurves.IndexOf(c),gate=c.SemaphoreId}),
                            direction=i.direction.ToString()})};
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(DiagnosticPath)!,"preview.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                } catch(Exception ex) {previewStatus="Routenprobe: "+ex.Message;}
                finally {preview=false;State.EnableAssists=false;}
            }
        }
    } }
    public string PreviewRoute()
    {
        lock(sync) {
            if(faultBrake.Latched)return "Zuerst mit F9 die Fehlerbremsung quittieren und selbst die Bremse halten.";
            if(!_IsRunning || !SensorsFresh(clock.Elapsed.TotalSeconds) || Math.Abs(speed)>.1 || State.EnableAssists)
                return "Für die Routenprobe muss der Lkw stehen und die Fahrassistenz aus sein.";
            var plugins=PluginBackend.Current!.PluginHandler!.LoadedPlugins;
            if(plugins.Any(p=>p._IsRunning && (p.Info.Id is "tumppi066.laneassist" or "tumppi066.adaptivecruisecontrol")))
                return "Für die reine Routenprobe Lane Assist und ACC im Manager ausschalten.";
            if(!plugins.Any(p=>p._IsRunning && p.Info.Id=="tumppi066.pathfinding"))return "Zuerst Pathfinding einschalten.";
            active=false;bridge.Detach();controller.Reset();tracked=null;
            automaticPreparation.Request();
            preview=true;previewAt=clock.Elapsed.TotalSeconds;previewStatus="Routenprobe läuft ohne Gas und Lenkung";
            State.EnableAssists=true;
            return previewStatus;
        }
    }
    bool SensorsFresh(double now) => sdk && !paused && now-telemetryAt < .5 && now-gameTimeChangedAt < .5 && now-gatesAt < .5;
    bool Fresh(double now) => SensorsFresh(now) && now-pathAt < 1 && path != null && !path.HasError;

    public string ConfirmLane(int gate,TollPayment payment)
    {
        lock(sync) {
            if(Math.Abs(speed)>.1 || State.EnableAssists)return "Bitte zuerst anhalten und F9 drücken.";
            try {router.Confirm(gate,payment);return payment==TollPayment.Automatic?
                "Automatische Lkw-Mautspur gespeichert. Im Stand auf Bereitschaft warten, dann F8.":
                "Zahlart gespeichert. Manuelle oder unbekannte Zahlspuren werden selbst gefahren; bei Bedarf F9 drücken und bis zur Zahlungsaufforderung vorrollen.";}
            catch(Exception ex) {return "Spur nicht gespeichert: "+ex.Message;}
        }
    }

    public string Configure(TollOptions newOptions)
    {
        lock(sync)
        {
            if (Math.Abs(speed) > .1 || State.EnableAssists) return "Bitte im Stand bei ausgeschalteter Fahrassistenz ändern.";
            try { newOptions.Validate(); } catch (ArgumentException e) { return e.Message; }
            try {preferences.Save(newOptions,preferences.Gates);} catch(Exception ex) {return "Einstellungen nicht gespeichert: "+ex.Message;}
            options = newOptions; controller = new(options); tracked = null; active = false; startup.Reset(); bridge.Detach();
            automaticPreparation.Request();
            preparationReason="Einstellungen gespeichert. Automatische Vorbereitung wartet auf verbundene Module.";
            return "Einstellungen dauerhaft gespeichert; Vorbereitung erfolgt automatisch im Stand.";
        }
    }
    string? PreparationBlock(double now)
    {
        if(faultBrake.Latched)return "Zuerst F9 zur manuellen Übernahme drücken und selbst die Bremse halten.";
        if(!_IsRunning)return "Zuerst das Mautassistent-Plugin einschalten.";
        if(preview)return "Routenprobe läuft noch.";
        if(!double.IsFinite(speed)||Math.Abs(speed)>.1||State.EnableAssists)return "Warte auf Stillstand mit ausgeschalteter Fahrassistenz.";
        if(!SensorsFresh(now)||now-pathAt>=1||path==null)return "Warte auf frische Spiel-, Routen- und Schrankendaten.";
        if(path.HasError&&path.ErrorMessage!="Assists are disabled.")return "Warte auf Routenbereitschaft: "+path.ErrorMessage;
        return null;
    }
    void TryAutomaticPreparation(double now)
    {
        if(active||!automaticPreparation.Requested)return;
        var blocked=PreparationBlock(now);
        if(!automaticPreparation.ShouldAttempt(now,blocked==null)) {
            preparationReason=blocked??"Automatische Vorbereitung: Stillstand wird geprüft.";
            return;
        }
        var result=SetActive(true);
        if(!active)preparationReason=result;
    }
    public string SetActive(bool value)
    {
        lock(sync)
        {
            if (!value) { automaticPreparation.Cancel(); active = false; State.EnableAssists=false; faultBrake.Release(); trackingGuard.Reset(); safetyTrace=null;safetyStarted=null; preparationReason="Beobachtungsmodus. F9 gibt die automatische Vorbereitung wieder frei."; bridge.Detach(); startup.Reset(); controller.Reset(); tracked = null; passage.Reset(); return "Manuelle Übernahme, Fehlerbremsung gelöst. Automatische Vorbereitung pausiert; mit F9 wieder freigeben."; }
            var blocked=PreparationBlock(clock.Elapsed.TotalSeconds);
            if(blocked!=null)return blocked;
            var acc = readAcc();
            if (acc == null) return "Eine geprüfte Adaptive-Cruise-Control-Version (2.1.0 oder 2.1.1) muss eingeschaltet sein.";
            try { bridge.Attach(acc, Evaluate, () => Status); }
            catch(Exception ex) { return "ACC-Verbindung fehlgeschlagen: " + ex.Message; }
            if (bridge.Constraint == null) return "ACC wird noch initialisiert. Bitte erneut versuchen.";
            controller.Reset(); startup.Reset(); tracked = null; decisionArmed = false; active = true;
            automaticPreparation.Cancel();
            trackingGuard.Reset();safetyTrace=null;safetyStarted=null;
            preparationReason="";lastStartWarning=double.NegativeInfinity;
            return "Bremsbegrenzung bereit. F8 startet die Fahrassistenz; F9 beendet sie weiterhin.";
        }
    }
    static IPlugin? FindAcc() => PluginBackend.Current?.PluginHandler?.LoadedPlugins.FirstOrDefault(p =>
        p.Info.Id == "tumppi066.adaptivecruisecontrol" && AccBridge.SupportsVersion(p.Info.Version) && p._IsRunning);

    void Disarm(string reason)
    {
        automaticPreparation.Cancel();
        bool brake=active&&State.EnableAssists&&!preview;
        preparationReason=reason;
        active=false; State.EnableAssists=false;
        if(brake)faultBrake.Trip();
        object? geometry;
        try {geometry=path!=null&&!path.HasError?RouteMatcher.Diagnose(path,position,gates):null;}
        catch(Exception ex) {geometry=new{error=ex.Message};}
        // Capture before disabling assists: the next path frame otherwise overwrites
        // the actual fault with the generic "Assists are disabled" message.
        lastFault=new {utc=DateTimeOffset.UtcNow, reason, speedKph=speed*3.6,
            phase=decision.Phase, tracked, candidate, pathError=path?.ErrorMessage,
            geometry};
        try {File.WriteAllText(Path.Combine(Path.GetDirectoryName(DiagnosticPath)!,"last-fault.json"),
            JsonSerializer.Serialize(lastFault,new JsonSerializerOptions{WriteIndented=true}));} catch(IOException){} catch(UnauthorizedAccessException){}
        Logger.Warn("Mautassistent: "+reason+" Fahrassistenz ausgeschaltet; bitte manuell übernehmen.");
        notify(brake?"Fehlerbremsung aktiv":"Mautassistenz beendet",reason+(brake?
            " Gas aus, Bremsung angefordert. Selbst lenken; F9 löst die Bremsung zur manuellen Übernahme.":" Bitte selbst lenken und bremsen.")+" F8 bleibt bis zur erneuten Vorbereitung im Stand gesperrt.");
    }

    float Evaluate()
    {
        lock(sync)
        {
            SafetyWatchdog(clock.Elapsed.TotalSeconds);
            if(faultBrake.Latched)return -5f;
            if(RejectUnpreparedStart())return float.NaN;
            if(paused)return float.NaN;
            var state = State;
            double now = clock.Elapsed.TotalSeconds;
            bool enabled = active && _IsRunning && state.EnableAssists && state.DrivingMode != DrivingMode.LaneAssistOnly;
            var start = startup.Check(enabled, decisionArmed && Fresh(now) && now-evaluatedAt <= .3, now);
            if (start == StartupPermission.Neutral) return float.NaN;
            // Pathfinding publishes an error until F8. Hold briefly while its first route is built.
            if (start == StartupPermission.Hold && SensorsFresh(now)) return -.35f;
            var permission = ControlGuard.Check(active, _IsRunning, state.EnableAssists,
                state.DrivingMode == DrivingMode.LaneAssistOnly, Fresh(now), now-evaluatedAt,
                start == StartupPermission.Disarm || decision.Phase.Contains("manuell übernehmen"));
            if (permission == ControlPermission.Neutral) return float.NaN;
            if (permission == ControlPermission.Disarm)
            {
                // Never resume automatically after losing the barrier or route while controlling.
                Disarm(routeFault ?? (!Fresh(now) ? DataStatus : decision.Phase));
                return faultBrake.Latched?-5f:float.NaN;
            }
            return decision.MaxAcceleration;
        }
    }

    public override void Tick()
    {
        lock(sync)
        {
            if (!subscribed) return;
            SafetyWatchdog(clock.Elapsed.TotalSeconds);
            RejectUnpreparedStart();
            double now = clock.Elapsed.TotalSeconds;
            if(preview && now-previewAt>5) {preview=false;State.EnableAssists=false;previewStatus="Routenprobe abgebrochen: "+(path?.ErrorMessage??"Keine Route");}
            TryAutomaticPreparation(now);
            try
            {
                var state = State;
                bool allow = state.EnableAssists && (!active || state.DrivingMode != DrivingMode.LaneAssistOnly);
                if (!state.EnableAssists) {startup.Reset();passage.Reset();controller.Reset();tracked=null;}
                RouteTrace? sampled=null;
                Gate? gate = Fresh(now) && path != null ? RouteMatcher.Select(path, position, gates, tracked,out sampled,speed) : null;
                if(gate==null && tracked!=null && Fresh(now)) {
                    var live=gates.FirstOrDefault(g=>g.type==SemaphoreType.GATE && RouteMatcher.Key(g)==tracked);
                    if(live!=null)gate=passage.AfterCrossing(tracked,position,live.GetWorldCoordinates(),
                        live.state is >=0 and <=3?(BarrierState)live.state:BarrierState.Unknown,
                        sampled!=null&&sampled.Project(position).Offset<=3,decision.Phase=="Langsame Durchfahrt");
                    // An observer must be able to reacquire a new gate. An active
                    // constraint must instead retain the lost target and disarm.
                    if(gate==null && !active) {controller.Reset();tracked=null;passage.Reset();
                        gate=RouteMatcher.Select(path!,position,gates,null,out sampled,speed);}
                }
                if(gate!=null && sampled!=null) {
                    var live=gates.FirstOrDefault(g=>g.type==SemaphoreType.GATE && RouteMatcher.Key(g)==gate.Key);
                    if(live!=null)passage.Capture(gate.Key,live.GetWorldCoordinates(),sampled);
                }
                candidate = gate;
                decision = controller.Step(new(now, speed, allow, Fresh(now), gate));
                tracked = decision.GateKey;
                if(tracked==null)passage.Reset();
                evaluatedAt = now;
                decisionArmed = state.EnableAssists && Fresh(now);
                if (active && readAcc() == null) { Disarm("Tempomat ausgeschaltet oder nicht verfügbar"); controller.Reset(); tracked = null; passage.Reset(); decision = TollDecision.Idle("Tempomat ausgeschaltet"); }
            }
            catch(Exception ex)
            {
                decision = new("Datenfehler – manuell übernehmen", 0, -2.5f, tracked);
                if (active) Disarm("Datenfehler: "+ex.Message);
                Logger.Warn("Mautassistent: " + ex.Message);
            }
            if (now-lastWrite > 1)
            {
                lastWrite = now;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath)!);
                    if (sdk && lastMapWrite < 0) { lastMapWrite=now; try { MapDiagnostics.Write(position,Path.GetDirectoryName(DiagnosticPath)!); } catch(Exception ex) { Logger.Warn("Maut-Kartenprüfung: " + ex.Message); } }
                    File.WriteAllText(DiagnosticPath, JsonSerializer.Serialize(new {
                        version="0.2.10", utc=DateTimeOffset.UtcNow, active, fresh=Fresh(now), speedKph=speed*3.6,
                        automaticPreparationPending=automaticPreparation.Requested,
                        startBlocked=!active&&!preview,preparationReason,selectedLane=router.SelectedGate,plaza=router.CurrentKey,
                        faultBraking=faultBrake.Latched,headingTurns,
                        pendingRouteGap=safetyTrace?.EndReason,
                        connectedMetresAhead=safetyTrace!=null?safetyTrace.Length-safetyTrace.Project(position).Along:(double?)null,
                        requiredLookahead=TrackingGuard.RequiredLookahead(speed),
                        phase=decision.Phase, targetSpeedKph=decision.TargetSpeed*3.6, gate=tracked, candidate, dataStatus=DataStatus,
                        telemetryAge=now-telemetryAt, gatesAge=now-gatesAt, pathAge=now-pathAt, gameTimeAge=now-gameTimeChangedAt,
                        sdk, paused, pathError=path?.ErrorMessage,
                        gateCount=gates.Count(g=>g.type==SemaphoreType.GATE), options,
                        geometry=RouteMatcher.Diagnose(path,position,gates), routing=router.Status, routeFault, lastFault, previewStatus,routeTransition,
                        assists=State.EnableAssists,mode=State.DrivingMode.ToString()
                    }, new JsonSerializerOptions { WriteIndented=true }));
                    var log = Path.ChangeExtension(DiagnosticPath, ".log");
                    string logState = active + ":" + decision.Phase + ":" + tracked;
                    if (logState != lastLogged)
                    {
                        if (File.Exists(log) && new FileInfo(log).Length > 2_000_000) File.Move(log, log+".previous", true);
                        File.AppendAllText(log, $"{DateTimeOffset.UtcNow:O} {Status} | {Details}\n");
                        lastLogged = logState;
                    }
                }
                catch(IOException) { /* Diagnostic storage must not interfere with driving. */ }
                catch(UnauthorizedAccessException) { }
            }
        }
    }
    public override void OnDisable()
    {
        lock(sync)
        {
            if(active || preview || faultBrake.Latched)State.EnableAssists=false;
            active = false;
            automaticPreparation.Cancel();
            faultBrake.Release();trackingGuard.Reset();safetyTrace=null;safetyStarted=null;
            preview=false;
            base.OnDisable();
            if (subscribed)
            {
                Events.Current.Unsubscribe<GameTelemetryData>(GameTelemetry.Current.EventString, OnTelemetry);
                Events.Current.Unsubscribe<SemaphoreData>(SemaphoreProvider.Current.EventString, OnGates);
                Events.Current.Unsubscribe<PlannedPathData>("Pathfinding.PlannedPathData", OnPath);
                ControlsBackend.Current.UnregisterListener(DefaultControls.Cancel.Id,OnCancel);
                subscribed = false;
            }
            bridge.Detach(); startup.Reset(); controller.Reset(); tracked = null; passage.Reset();
            decision = TollDecision.Idle("Plugin ausgeschaltet");
        }
    }
    public override void Shutdown() { OnDisable(); if (ReferenceEquals(Current,this)) Current = null; }
}
