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

public sealed partial class TollPlugin : Plugin
{
    public static TollPlugin? Current { get; private set; }
    public override PluginInformation Info => new() {
        Id = "local.mautassistent", Name = "Mautassistent", Version = typeof(TollPlugin).Assembly.GetName().Version!.ToString(3),
        Description = "Langsame Mautdurchfahrt. F8 startet nach Routenprüfung auch während der Fahrt; F9 übernimmt manuell.",
        AuthorName = "Lokale Anpassung", Dependencies = ["tumppi066.pathlib"],
        PluginPageButtonName = "Einstellungen"
    };
    public override float TickRate => 20;
    readonly object sync = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly AccBridge bridge = new();
    readonly DriveSupervisor supervisor = new();
    readonly ConnectorHistory connectorHistory = new();
    readonly Queue<(DriveCheckpoint Before, DriveSample Sample)> driveHistory = new();
    DriveAction driveAction;
    bool connectorAttempted;
    bool connectorPending;
    bool connectorSeeded;
    readonly AutomaticPreparation automaticPreparation = new();
    readonly PassageMemory passage=new();
    string lastPathError="";
    object? routeTransition;
    readonly PreferenceStore preferences = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ETS2LA-Mautassistent","settings.json"));
    readonly TollRouter router;
    readonly Func<ETS2LA.Game.Data.ParsedPrefab,IReadOnlyDictionary<int,TollPayment>>? readPayments;
    readonly Func<ApplicationState> readState;
    readonly Func<IPlugin?> readAcc;
    readonly Func<IPlugin?> readPlanner;
    readonly Func<IPlugin?> readLaneAssist;
    readonly Func<IPlugin,Vector3,double,Vector3[],PlannedPathData?> probeRoute;
    readonly Action<string,string> notify;
    readonly FaultBrake faultBrake;
    readonly TrackingGuard trackingGuard=new();
    RouteTrace? safetyTrace;
    double headingTurns;
    ApplicationState State => readState();
    public TollPlugin() : this(() => ApplicationState.Current, Notify,
        output=>Events.Current.Publish(GameOutput.Current.EventString,output),
        readPayments: new TollModelEvidence(() => ApplicationState.Current.RunningGame?.GetFileSystem(),
            ()=>GameTelemetry.Current.GetCurrentData().configBool.specialJob).Read,monitorSteering:true) { }
    internal TollPlugin(Func<ApplicationState> readState, Action<string,string> notify,Action<ControlEvent>? output=null,Func<IPlugin?>? readAcc=null,
        Func<ETS2LA.Game.Data.ParsedPrefab,IReadOnlyDictionary<int,TollPayment>>? readPayments=null, Func<IPlugin?>? readPlanner=null,
        Func<IPlugin,Vector3,double,Vector3[],PlannedPathData?>? probeRoute=null,
        Func<IPlugin,PlannedPathData,PlannedPathData,Vector3,double,bool>? installStart=null,bool monitorSteering=false,
        Func<IPlugin?>? readLaneAssist=null) {
        this.monitorSteering=monitorSteering;
        this.readState=readState;this.readAcc=readAcc??FindAcc;this.notify=notify;this.readPayments=readPayments;
        this.readPlanner=readPlanner??FindPlanner;
        this.readLaneAssist=readLaneAssist??FindLaneAssist;
        this.probeRoute=probeRoute??ConnectorStart.Probe;
        this.installStart=installStart??ConnectorStart.TryInstallStart;
        publishOutput=output??(_=>{});
        router=new(preferences,readPayments);faultBrake=new(output??(_=>{}));
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
        if(!State.EnableAssists)return false;
        if(brakeHeld || clock.Elapsed.TotalSeconds<takeoverUntil) {State.EnableAssists=false;return true;}
        if(active && !preview && (supervisor.Checkpoint.Started.HasValue || CanStartNow(clock.Elapsed.TotalSeconds)))return false;
        State.EnableAssists=false;
        if (rollingStartPending) return true;
        if (Math.Abs(speed)>.1 && RequestRollingStart()) return true;
        if (active && !preview)
        {
            active=false;
            automaticPreparation.Request();
            preparationReason="Die Startroute muss im Stand erneut geprüft werden.";
        }
        if(clock.Elapsed.TotalSeconds-lastStartWarning>=5) {
            lastStartWarning=clock.Elapsed.TotalSeconds;
            notify("Fahrassistenz nicht gestartet",preview ? "Routenprobe läuft. Danach im Stand erneut F8 drücken."
                : preparationReason+" Im Stand auf Bereitschaft warten; danach F8.");
        }
        return true;
    }
    public TollRouter.LaneChoice[] LaneChoices {get {lock(sync)return (previewRouter ?? router).Choices.ToArray();}}
    public int SelectedLane {get {lock(sync)return (previewRouter ?? router).SelectedGate ?? -1;}}
    string? routeFault;
    object? lastFault;
    string? warnedStopKey;
    bool preview;
    bool previewCalculating;
    int previewGeneration;
    Vector3 previewPosition;
    TollRouter? previewRouter;
    internal sealed record StartCheck(Vector3 Position,double Heading,ulong Navigation,string? Failure);
    StartCheck? startCheck;
    ulong previewNavigation;

    bool CheckMatches() => startCheck is { } check &&
        Vector3.DistanceSquared(position,check.Position)<=.0625f &&
        Vector3.Dot(TrackingGuard.TruckForward(headingTurns),TrackingGuard.TruckForward(check.Heading))>=.995f &&
        check.Navigation==ConnectorStart.NavigationKey(readPlanner());

    bool CanStartNow(double now) => _IsRunning && active && !preview && !brakeHeld && now>=takeoverUntil && !faultBrake.Latched && SensorsFresh(now) &&
        Math.Abs(speed)<=.1 && CheckMatches() && startCheck!.Failure==null;
    double previewAt;
    string previewStatus="Noch keine Routenprobe";
    public string RouteStatus {get {lock(sync)return previewStatus+" · "+(previewRouter ?? router).Status;}}
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
    public string Status { get { lock(sync) return faultBrake.Latched ? "Fehlerbremsung aktiv – F9 zur manuellen Übernahme: "+preparationReason : rollingStartPending ? "Start angefordert – Route wird geprüft; noch selbst steuern. F9 bricht ab." : preview ? "Routenprobe ohne Fahrsteuerung" : active ? (State.EnableAssists ? driveAction switch {
        DriveAction.AwaitRoute => "Warte auf eine gültige Startroute",
        DriveAction.Recover => "Kurze Datenlücke – kein Gas",
        DriveAction.Paused => "Spiel pausiert",
        _ => "Bremsbegrenzung: "+decision.Phase
    } : CanStartNow(clock.Elapsed.TotalSeconds) ? "Bereit – F8 startet die Fahrassistenz" : "Warte auf bestätigte Startroute und aktuelle Spieldaten") : "Nicht vorbereitet – F8 gesperrt: "+preparationReason; } }
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
        ControlsBackend.Current.RegisterControl(BrakeControl);
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
            faultBrake.Release();ResetDriving();
            motionHistory.Clear();lastMotionAt=-100;
            connectorAttempted = connectorPending = false;
            connectorSeeded = false;
            connectorHistory.Clear();
            active = false; controller.Reset(); tracked = null; passage.Reset();
            CancelRollingStart();
            startCheck=null;
            automaticPreparation.Request();
            preparationReason="Automatische Vorbereitung wartet auf Stillstand und verbundene Module.";
            telemetryAt = gatesAt = pathAt = gameTimeChangedAt = -100;
            Interlocked.Exchange(ref incomingTelemetry,null);
            Interlocked.Exchange(ref latestDeliveredTelemetry,null);
            Interlocked.Exchange(ref incomingGates,null);
            // Ensure the providers exist before subscribing to their published frames.
            Events.Current.Subscribe<GameTelemetryData>(GameTelemetry.Current.EventString, OnTelemetry);
            Events.Current.Subscribe<SemaphoreData>(SemaphoreProvider.Current.EventString, OnGates);
            Events.Current.Subscribe<PlannedPathData>("Pathfinding.PlannedPathData", OnPath);
            Events.Current.Subscribe<ControlEvent>(GameOutput.Current.EventString,OnControlOutput);
            ControlsBackend.Current.On(DefaultControls.Cancel.Id,OnCancel);
            ControlsBackend.Current.On(DefaultControls.Increase.Id,OnStart);
            ControlsBackend.Current.On(DefaultControls.Decrease.Id,OnStart);
            ControlsBackend.Current.On(BrakeControl.Id,OnBrakeInput);
            subscribed = true;
        }
        base.OnEnable();
        StartSteeringMonitor();
        Logger.Info("Mautassistent: automatische Vorbereitung im Stand; Fahrassistenz bleibt ausgeschaltet.");
    }

    void OnStart(object? sender,ControlChangeEventArgs e)
    {
        // The host enables on both edges. Intercept immediately after its
        // listener instead of waiting for the next telemetry/ACC/plugin tick.
        lock(sync) {DrainInputFrames();RejectUnpreparedStart();}
    }
    void OnCancel(object? sender,ControlChangeEventArgs e)
    {
        // Same release edge as the host's F9 / Cancel control.
        if(e.NewValue is not false)return;
        CancelSteeringSession();
        lock(sync) {
            // The host cycles modes when it sees assists off. A pending start
            // is still a cancel operation, so retain the requested driving mode.
            if (rollingStartPending) State.DrivingMode=rollingStartMode;
            CancelRollingStart();
            State.EnableAssists=false;
            faultBrake.Release();ResetDriving();
            startCheck=null;
            active=false;
            preview=previewCalculating=false;
            previewRouter=null;
            previewGeneration++;
            connectorAttempted = connectorPending = false;
            router.CancelPlannedStop();warnedStopKey=null;
            if(!active) {
                automaticPreparation.Request();
                preparationReason="Übernahme bestätigt. Automatische Vorbereitung wartet auf Stillstand und verbundene Module.";
            }
        }
    }
    void ResetDriving()
    {
        supervisor.Reset();
        trackingGuard.Reset();
        safetyTrace = null;
        decisionArmed = false;
        driveHistory.Clear();
        driveAction = DriveAction.Idle;
    }

    DriveAction SafetyWatchdog(double now)
    {
        DrainInputFrames();
        CheckSteeringOutput();
        DrainSteeringFailure();
        RecordMotion(now);
        if (faultBrake.Latched)
        {
            State.EnableAssists = false;
            faultBrake.Refresh();
            return DriveAction.EmergencyStop;
        }
        if (!active || !State.EnableAssists || preview)
        {
            ResetDriving();
            return DriveAction.Idle;
        }

        double routeAge = now - pathAt;
        var route = path switch
        {
            null => RouteFeed.Waiting,
            { HasError: false } when safetyTrace != null && routeAge < 1 => RouteFeed.Valid,
            { HasError: false } => RouteFeed.Waiting,
            { ErrorMessage: "Assists are disabled." } => RouteFeed.AssistsOff,
            { ErrorMessage: "Game is paused." } => RouteFeed.Waiting,
            { ErrorMessage: ConnectorStart.MissingRoad } when connectorPending => RouteFeed.Loading,
            { ErrorMessage: "Waiting for map data..." } when routeAge < 1 => RouteFeed.Loading,
            _ => RouteFeed.Rejected
        };
        bool telemetryFresh = sdk && now - telemetryAt < .5;
        bool laneOnly = State.DrivingMode == DrivingMode.LaneAssistOnly;
        bool mayRecover = false;
        if (telemetryFresh && !paused && route == RouteFeed.Valid && safetyTrace is { } trace)
        {
            var fault = trackingGuard.Check(trace, position, headingTurns, speed,
                State.DrivingMode != DrivingMode.AdaptiveCruiseControlOnly);
            if (fault != null)
            {
                Disarm(fault);
                return DriveAction.EmergencyStop;
            }
            bool gateApproach = candidate != null && candidate.Distance <= controller.ApproachHorizon(speed);
            mayRecover = !laneOnly && !trace.EndsAtGap && !controller.Tracking && !gateApproach &&
                !router.StopBoundary.HasValue && !router.HasShortChange &&
                trace.Length - trace.Project(position).Along > TrackingGuard.RequiredLookahead(speed);
        }

        bool steeringReady=!monitorSteering || State.DrivingMode==DrivingMode.AdaptiveCruiseControlOnly || steeringFeedState==SteeringFeedState.Healthy;
        bool decisionReady = steeringReady && (laneOnly || (Fresh(now) && decisionArmed && now - evaluatedAt <= .3));
        var frame = new DriveFrame(now, speed, true, paused, telemetryFresh, route, routeAge, decisionReady, mayRecover);
        var before = supervisor.Checkpoint;
        var action = supervisor.Step(frame);
        if (driveHistory.Count == 512) driveHistory.Dequeue();
        driveHistory.Enqueue((before, new(frame, action)));
        driveAction = action;

        if (action is DriveAction.RejectStart or DriveAction.EmergencyStop)
        {
            string reason = !telemetryFresh ? "Spieltelemetrie verloren."
                : action == DriveAction.RejectStart && route == RouteFeed.AssistsOff && Math.Abs(speed) > .1
                    ? "Start während der Fahrt abgelehnt. Bitte im Stand erneut einschalten."
                : path?.HasError == true && route is RouteFeed.Rejected or RouteFeed.Loading
                    ? "Routenplanung: " + path.ErrorMessage
                    : route != RouteFeed.Valid || routeAge >= .5 ? "Keine aktuelle verfolgte Route verfügbar."
                    : !SensorsFresh(now) ? DataStatus : "Keine stabile Fahrentscheidung verfügbar.";
            StopDriving(reason, action, action == DriveAction.RejectStart && route == RouteFeed.AssistsOff && Math.Abs(speed) > .1);
        }
        return action;
    }
    void ProcessPath(PlannedPathData data)
    {
        int probe = 0;
        int rollingProbe = 0;
        IPlugin? planner = null;
        Vector3 startPosition = default;
        double startHeading = 0;
        Vector3[] history = [];
        lock (sync)
        {
            DrainInputFrames();
            path = data;
            pathAt = clock.Elapsed.TotalSeconds;
            pathReceivedAt=pathAt;
            if (!State.EnableAssists) connectorAttempted = false;
            connectorPending = false;
            if (RejectUnpreparedStart()) return;
            if (rollingStartPending && !rollingStartCalculating && data is { HasError: true, ErrorMessage: "Assists are disabled." })
            {
                rollingStartCalculating=true;
                rollingProbe=rollingStartGeneration;
                planner=readPlanner();
                startPosition=position;
                startHeading=headingTurns;
                history=connectorHistory.Snapshot(pathAt,position);
            }
            if (preview && !previewCalculating && data is { HasError: true, ErrorMessage: "Assists are disabled." })
            {
                previewCalculating = true;
                probe = previewGeneration;
                planner = readPlanner();
                startPosition = position;
                startHeading = headingTurns;
                history = connectorHistory.Snapshot(pathAt, position);
            }
            if (data.HasError && data.ErrorMessage == ConnectorStart.MissingRoad && !connectorAttempted &&
                active && !supervisor.Checkpoint.Drove && safetyTrace == null &&
                State.EnableAssists && State.DrivingMode != DrivingMode.LaneAssistOnly &&
                SensorsFresh(pathAt) && Math.Abs(speed) <= .1)
            {
                planner = readPlanner();
                if (planner != null)
                {
                    connectorAttempted = connectorPending = true;
                    connectorSeeded = false;
                    startPosition = position;
                    startHeading = headingTurns;
                    history = connectorHistory.Snapshot(pathAt, position);
                }
            }
            if (probe == 0 && rollingProbe == 0 && planner == null)
            {
                HandlePath(data);
                return;
            }
        }

        if (rollingProbe != 0)
        {
            CompleteRollingStart(rollingProbe,planner,data,startPosition,startHeading,history);
            return;
        }
        if (probe != 0)
        {
            CompletePreview(probe, planner, startPosition, startHeading, history);
            return;
        }
        bool seeded = false;
        if (planner != null)
        {
            try
            {
                seeded = ConnectorStart.TrySeed(planner, startPosition, startHeading, history, commit =>
                {
                    lock (sync)
                    {
                        return ReferenceEquals(path, data) && connectorPending && State.EnableAssists &&
                            active && SensorsFresh(clock.Elapsed.TotalSeconds) && Math.Abs(speed) <= .1 &&
                            Vector3.Distance(position, startPosition) <= .25 && commit();
                    }
                });
            }
            catch (Exception ex) { Logger.Warn("Kartenverbinder konnte nicht als Start geprüft werden: " + ex.Message); }
            if (seeded) Logger.Info("Start auf geprüftem Kartenverbinder; warte auf die native Route.");
        }

        lock (sync)
        {
            if (!ReferenceEquals(path, data)) return;
            connectorPending = seeded && connectorPending;
            if (connectorPending) connectorSeeded = true;
            HandlePath(data);
        }
    }

    void HandlePath(PlannedPathData data)
    {
        // Evaluate explicit route errors before diagnostic I/O, independent of ACC.
        if (data.HasError && SafetyWatchdog(clock.Elapsed.TotalSeconds) is DriveAction.RejectStart or DriveAction.EmergencyStop) return;
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
            try {
                int? previousGate=router.SelectedGate;
                string? previousPlaza=router.CurrentKey;
                router.Discover(data,position,gates,speed);
                if (previousGate != router.SelectedGate || previousPlaza != router.CurrentKey)
                {
                    controller.Reset();passage.Reset();tracked=null;candidate=null;
                    decisionArmed=false;
                    if (active && State.EnableAssists && !preview && router.SelectedGate is int gate &&
                        router.PaymentFor(gate)==TollPayment.Manual)
                        notify("Lkw-Mautspur gewählt", "Hier ist eine manuelle Zahlung nötig. Die Assistenz hält vor der Schranke. Wenn der Zahlpunkt noch nicht erreichbar ist, mit F9 übernehmen.");
                }
            }
            catch(Exception ex) {routeFault="Mautspuren konnten nicht geprüft werden: "+ex.Message;if(active)Disarm(routeFault);}
        }
        DrainInputFrames(); // Discovery may be expensive; consume the latest coherent SDK frames.
        if(active && !data.HasError) {
            try {router.Apply(data,position,out routeFault,speed);}
            catch(Exception ex){routeFault="Maut-Spurwahl: "+ex.Message;}
            DrainInputFrames();
            if(routeFault!=null) Disarm(routeFault);
            if(active && State.EnableAssists && !preview && router.StopBoundary.HasValue) {
                try {
                    if(State.DrivingMode==DrivingMode.LaneAssistOnly)
                        throw new InvalidOperationException("Unbestätigte Mautspur: Ohne Tempomat ist kein geplanter Halt möglich. Bitte übernehmen.");
                    decision=ApproachStop.Evaluate(speed,router.UpdateStopDistance(data,position,speed)!.Value);
                    evaluatedAt=clock.Elapsed.TotalSeconds;decisionArmed=true;
                    if(warnedStopKey!=router.StopKey) {
                        controller.Reset();passage.Reset();tracked=null;candidate=null;
                        warnedStopKey=router.StopKey;
                        notify("Mautspur noch nicht bestätigt", "Der Lkw bremst auf der Zufahrt zum Halt ab. F9 drücken und die geeignete Spur selbst wählen oder im Stand bestätigen.");
                    }
                } catch(Exception ex) {Disarm(ex.Message);}
            }
            if(active&&State.EnableAssists&&!preview&&sdk&&!paused) {
                try {
                    if (ExitHandoff.RepairRoadConnections(data,position)>0)
                        Logger.Info("Eindeutig anschließende Straßenspur am Kartenübergang wiederhergestellt.");
                    DrainInputFrames();
                    var trace=RouteMatcher.SampleForTracking(data,position,speed);
                    var fault=trackingGuard.Check(trace,position,headingTurns,speed,State.DrivingMode!=DrivingMode.AdaptiveCruiseControlOnly);
                    if(fault!=null)Disarm(fault);else {
                        safetyTrace=trace;
                        // Geometry was checked against the latest pose just now.
                        // Keep source receipt age separately for timing diagnostics.
                        pathAt=clock.Elapsed.TotalSeconds;
                    }
                } catch(Exception ex) {Disarm("Routenverfolgung nicht möglich: "+ex.Message);}
            }
        }
    }

    static IPlugin? FindPlanner() => PluginBackend.Current?.PluginHandler?.LoadedPlugins.FirstOrDefault(p =>
        p._IsRunning && p.Info.Id == "tumppi066.pathfinding");
    static IPlugin? FindLaneAssist() => PluginBackend.Current?.PluginHandler?.LoadedPlugins.FirstOrDefault(p =>
        p.GetType().FullName=="LaneAssist.LaneAssistPlugin");

    void CompletePreview(int request, IPlugin? planner, Vector3 origin, double heading, Vector3[] history)
    {
        PlannedPathData? data = null;
        string? failure = null;
        try { if (planner != null) data = probeRoute(planner, origin, heading, history); }
        catch (Exception ex) { failure = ex.GetBaseException().Message; }

        lock (sync)
        {
            DrainInputFrames();
            if (!preview || request != previewGeneration) return;
            preview = previewCalculating = false;
            if (!SensorsFresh(clock.Elapsed.TotalSeconds) || State.EnableAssists || Math.Abs(speed) > .1 ||
                Vector3.DistanceSquared(position, origin) > .0625f ||
                Vector3.Dot(TrackingGuard.TruckForward(headingTurns), TrackingGuard.TruckForward(heading)) < .995f ||
                previewNavigation!=ConnectorStart.NavigationKey(readPlanner()))
            {
                previewStatus = "Routenprobe abgebrochen. Bitte im Stand erneut prüfen.";
                return;
            }
            if (data == null)
            {
                FailStartCheck(origin,heading,failure ?? "Keine sichere Startroute gefunden.");
                return;
            }
            try
            {
                var testRouter = new TollRouter(preferences, readPayments);
                testRouter.Discover(data, origin, gates);
                testRouter.Apply(data, origin, out var issue);
                ExitHandoff.RepairRoadConnections(data,origin);
                var trace = RouteMatcher.SampleForTracking(data, origin, 0);
                issue ??= new TrackingGuard().Check(trace, origin, heading, 0);
                var projection = trace.Project(origin);
                if (trace.Length - projection.Along < TrackingGuard.RequiredLookahead(0))
                    issue ??= "Zu wenig zusammenhängende Strecke für den Start.";
                var selected = RouteMatcher.Select(data, origin, gates, null);
                previewStatus = issue ?? (selected == null ? "Routenprobe: Route vorhanden, keine zugeordnete Mautschranke"
                    : $"Routenprobe: Schranke {selected.Key}, {selected.Distance:F1} m");
                if (issue == null) {
                    previewRouter = testRouter; previewPosition = origin;
                    startCheck=new(origin,heading,previewNavigation,null);
                }
                else FailStartCheck(origin,heading,issue);
                var report = new { utc=DateTimeOffset.UtcNow, routeFault=issue, selected, testRouter.Status,
                    geometry=RouteMatcher.Diagnose(data,origin,gates), sampledTruckProjection=projection,
                    tracking=trace.Diagnose(origin), headingTurns=heading,
                    items=data.PlannedPath.Select(i=>new{kind=i.item.GetType().Name,length=i.GetLength(),
                        prefab=i.prefabPath?.Prefab.Prefab.Uid.ToString(),
                        curves=i.prefabPath?.Curves.Select(c=>new {id=i.prefabPath.Prefab.Descriptor!.NavCurves.IndexOf(c),gate=c.SemaphoreId}),
                        direction=i.direction.ToString()}) };
                Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath)!);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(DiagnosticPath)!,"preview.json"),
                    JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            }
            catch (Exception ex) { FailStartCheck(origin,heading,ex.GetBaseException().Message); }
        }
    }

    void FailStartCheck(Vector3 origin,double heading,string reason)
    {
        if (active) automaticPreparation.Request();
        active=false;
        startCheck=new(origin,heading,previewNavigation,reason);
        preparationReason=reason;
        previewStatus="Routenprobe: "+reason;
    }

    public string PreviewRoute()
    {
        lock(sync) {
            DrainInputFrames();
            if(faultBrake.Latched)return "Zuerst mit F9 die Fehlerbremsung quittieren und selbst die Bremse halten.";
            if(!_IsRunning || !SensorsFresh(clock.Elapsed.TotalSeconds) || Math.Abs(speed)>.1 || State.EnableAssists)
                return "Für die Routenprobe muss der Lkw stehen und die Fahrassistenz aus sein.";
            if (preview) return previewStatus;
            if (readPlanner() == null) return "Zuerst Pathfinding einschalten.";
            CancelRollingStart();
            previewRouter=null;
            startCheck=null;
            previewCalculating=false;
            previewGeneration++;
            previewNavigation=ConnectorStart.NavigationKey(readPlanner());
            preview=true;previewAt=clock.Elapsed.TotalSeconds;previewStatus="Routenprobe läuft ohne Gas und Lenkung";
            return previewStatus;
        }
    }
    bool SensorsFresh(double now) => sdk && !paused && now-telemetryAt < .5 && now-gameTimeChangedAt < .5 && now-gatesAt < .5;
    bool Fresh(double now) => SensorsFresh(now) && now-pathAt < 1 && path != null && !path.HasError;

    public string ConfirmLane(int gate,TollPayment payment)
    {
        lock(sync) {
            DrainInputFrames();
            if(Math.Abs(speed)>.1 || State.EnableAssists)return "Bitte zuerst anhalten und F9 drücken.";
            CancelRollingStart();
            try {
                (previewRouter ?? router).Confirm(gate,payment);
                if (previewRouter != null) router.ResetSelection();
                startCheck=null;active=false;automaticPreparation.Request();
                return payment==TollPayment.Automatic?
                "Automatische Lkw-Mautspur gespeichert. Im Stand auf Bereitschaft warten, dann F8.":
                "Zahlart gespeichert. Manuelle oder unbekannte Zahlspuren werden selbst gefahren; bei Bedarf F9 drücken und bis zur Zahlungsaufforderung vorrollen.";}
            catch(Exception ex) {return "Spur nicht gespeichert: "+ex.Message;}
        }
    }

    public string Configure(TollOptions newOptions)
    {
        lock(sync)
        {
            DrainInputFrames();
            if (Math.Abs(speed) > .1 || State.EnableAssists) return "Bitte im Stand bei ausgeschalteter Fahrassistenz ändern.";
            CancelRollingStart();
            try { newOptions.Validate(); } catch (ArgumentException e) { return e.Message; }
            try {preferences.Save(newOptions,preferences.Gates);} catch(Exception ex) {return "Einstellungen nicht gespeichert: "+ex.Message;}
            options = newOptions; controller = new(options); tracked = null; active = false;  bridge.Detach();
            startCheck=null;
            automaticPreparation.Request();
            preparationReason="Einstellungen gespeichert. Automatische Vorbereitung wartet auf verbundene Module.";
            return "Einstellungen dauerhaft gespeichert; Vorbereitung erfolgt automatisch im Stand.";
        }
    }
    string? PreparationBlock(double now)
    {
        if(faultBrake.Latched)return "Zuerst F9 zur manuellen Übernahme drücken und selbst die Bremse halten.";
        if(brakeHeld || now<takeoverUntil)return "Manuelle Bremsübernahme: zum Starten zuerst die Bremse loslassen.";
        if(!_IsRunning)return "Zuerst das Mautassistent-Plugin einschalten.";
        if(preview)return "Routenprobe läuft noch.";
        if(!double.IsFinite(speed)||Math.Abs(speed)>.1||State.EnableAssists)return "Warte auf Stillstand mit ausgeschalteter Fahrassistenz.";
        if(!SensorsFresh(now)||now-pathAt>=1||path==null)return "Warte auf frische Spiel-, Routen- und Schrankendaten.";
        if(readPlanner() is { } planner && ConnectorStart.IsLoadingMap(planner))return "Warte, bis Pathfinding die lokale Karte geladen hat.";
        if(path.HasError&&path.ErrorMessage!="Assists are disabled.")return "Warte auf Routenbereitschaft: "+path.ErrorMessage;
        if(CheckMatches() && startCheck!.Failure is { } failure)return failure+" Für einen erneuten Versuch die Routenprobe starten.";
        return null;
    }
    void TryAutomaticPreparation(double now)
    {
        if(active && !State.EnableAssists && !preview && !CanStartNow(now))
        {
            active=false;
            automaticPreparation.Request();
        }
        if(active||rollingStartPending||!automaticPreparation.Requested)return;
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
            DrainInputFrames();
            CancelRollingStart();
            if (!value) { startCheck=null;preview=previewCalculating=false; router.CancelPlannedStop();warnedStopKey=null; automaticPreparation.Cancel(); active = false; State.EnableAssists=false; faultBrake.Release(); ResetDriving(); preparationReason="Beobachtungsmodus. F9 gibt die automatische Vorbereitung wieder frei."; bridge.Detach();  controller.Reset(); tracked = null; passage.Reset(); return "Manuelle Übernahme, Fehlerbremsung gelöst. Automatische Vorbereitung pausiert; mit F9 wieder freigeben."; }
            var blocked=PreparationBlock(clock.Elapsed.TotalSeconds);
            if(blocked!=null)return blocked;
            var acc = readAcc();
            if (acc == null) return "Eine geprüfte Adaptive-Cruise-Control-Version (2.1.0 oder 2.1.1) muss eingeschaltet sein.";
            if (!CheckMatches())
            {
                var result=PreviewRoute();
                preparationReason=result;
                return result;
            }
            try { bridge.Attach(acc, Evaluate, () => Status); }
            catch(Exception ex) { return "ACC-Verbindung fehlgeschlagen: " + ex.Message; }
            if (bridge.Constraint == null) return "ACC wird noch initialisiert. Bitte erneut versuchen.";
            controller.Reset();  tracked = null; decisionArmed = false; active = true;
            automaticPreparation.Cancel();
            ResetDriving();
            preparationReason="";lastStartWarning=double.NegativeInfinity;
            return "Bremsbegrenzung bereit. F8 startet die Fahrassistenz; F9 beendet sie weiterhin.";
        }
    }
    static IPlugin? FindAcc() => PluginBackend.Current?.PluginHandler?.LoadedPlugins.FirstOrDefault(p =>
        p.Info.Id == "tumppi066.adaptivecruisecontrol" && AccBridge.SupportsVersion(p.Info.Version) && p._IsRunning);

    void Disarm(string reason) => StopDriving(reason, DriveAction.EmergencyStop);

    void StopDriving(string reason, DriveAction action, bool retryAtStandstill = false)
    {
        if (retryAtStandstill) automaticPreparation.Request(); else automaticPreparation.Cancel();
        driveAction = action;
        RecordMotion(clock.Elapsed.TotalSeconds,true);
        bool brake=action==DriveAction.EmergencyStop && (faultBrake.Latched || active&&State.EnableAssists&&!preview);
        preparationReason=reason;
        active=false; State.EnableAssists=false;
        startCheck=null;
        if(brake)faultBrake.Trip();
        object? geometry;object? routePlan=null;
        try {
            var snapshot=path!=null&&!path.HasError?RouteSnapshot.Capture(path):null;
            geometry=snapshot!=null?RouteMatcher.Diagnose(snapshot,position,gates):null;
            routePlan=snapshot?.PlannedPath.Select(i=>new {
                kind=i.item.GetType().Name,start=i.startNode.Uid,end=i.endNode.Uid,
                lane=i.laneIndex,targetLane=i.targetLaneIndex,direction=i.direction.ToString(),
                prefab=i.prefabPath?.Prefab.Prefab.Uid,
                roads=i.item is ETS2LA.Game.Data.ParsedRoadList list?list.Roads.Select(r=>r.Road.Uid).ToArray():
                    i.item is ETS2LA.Game.Data.ParsedRoad road?new[]{road.Road.Uid}:Array.Empty<ulong>()
            }).ToArray();
        }
        catch(Exception ex) {geometry=new{error=ex.Message};}
        // Capture before disabling assists: the next path frame otherwise overwrites
        // the actual fault with the generic "Assists are disabled" message.
        lastFault=new {utc=DateTimeOffset.UtcNow, reason, speedKph=speed*3.6,
            phase=decision.Phase, tracked, candidate, pathError=path?.ErrorMessage,
            action=action.ToString(), geometry,routeTracking=safetyTrace?.Diagnose(position),tracking=trackingGuard.LastDirectionFailure,
            steeringFailure=lastSteeringFailure,motion=motionHistory.ToArray(),routePlan,
            driveRecording=new DriveRecording(driveHistory.TryPeek(out var first) ? first.Before : default,
                driveHistory.Select(entry => entry.Sample).ToArray())};
        try {File.WriteAllText(Path.Combine(Path.GetDirectoryName(DiagnosticPath)!,"last-fault.json"),
            JsonSerializer.Serialize(lastFault,new JsonSerializerOptions{WriteIndented=true,
                NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));}
        catch(IOException){} catch(UnauthorizedAccessException){}
        Logger.Warn("Mautassistent: "+reason+" Fahrassistenz ausgeschaltet; bitte manuell übernehmen.");
        notify(brake?"Fehlerbremsung aktiv":action==DriveAction.RejectStart?"Fahrassistenz nicht gestartet":"Mautassistenz beendet",reason+(brake?
            " Gas aus, Bremsung angefordert. Selbst lenken; F9 löst die Bremsung zur manuellen Übernahme.":" Bitte selbst lenken und bremsen.")+" F8 bleibt bis zur erneuten Vorbereitung im Stand gesperrt.");
    }

    float Evaluate()
    {
        lock(sync)
        {
            DrainInputFrames();
            if (RejectUnpreparedStart() && !faultBrake.Latched) return float.NaN;
            var action = SafetyWatchdog(clock.Elapsed.TotalSeconds);
            if (faultBrake.Latched) return -5f;
            if (RejectUnpreparedStart() || !_IsRunning || State.DrivingMode == DrivingMode.LaneAssistOnly) return float.NaN;
            if (action == DriveAction.AwaitRoute) return -.35f;
            if (action == DriveAction.Recover) return -.5f;
            if (action != DriveAction.Drive) return float.NaN;
            if (decision.RequiresTakeover) { Disarm(decision.Phase); return -5f; }

            try
            {
                float limit = router.StopBoundary.HasValue
                    ? ApproachStop.Evaluate(speed,router.UpdateStopDistance(path!,position,speed)!.Value).MaxAcceleration
                    : decision.MaxAcceleration;
                if (safetyTrace is { EndsAtGap: true } trace)
                {
                    float routeLimit = ApproachStop.BeforeRouteGap(speed, trace.Length - trace.Project(position).Along);
                    limit = float.IsFinite(limit) ? Math.Min(limit, routeLimit) : routeLimit;
                }
                return limit;
            }
            catch(Exception ex) {Disarm(ex.Message);return -5f;}
        }
    }

    public override void Tick()
    {
        lock(sync)
        {
            if (!subscribed) return;
            DrainInputFrames();
            RejectUnpreparedStart();
            SafetyWatchdog(clock.Elapsed.TotalSeconds);
            double now = clock.Elapsed.TotalSeconds;
            if(now<takeoverUntil) ReleaseAssistOutputs();
            if(rollingStartPending && now-rollingStartAt>RollingStartTimeout)
                FailRollingStart("Zeitlimit der Routenprüfung erreicht. Bitte selbst weiterfahren und bei Bedarf erneut F8 drücken.");
            if(preview && now-previewAt>8) {
                preview=false;previewCalculating=false;
                FailStartCheck(position,headingTurns,"Pathfinding antwortet nicht auf die Routenprobe.");
            }
            TryAutomaticPreparation(now);
            try
            {
                var state = State;
                bool allow = state.EnableAssists && (!active || state.DrivingMode != DrivingMode.LaneAssistOnly);
                if (!state.EnableAssists) {passage.Reset();controller.Reset();tracked=null;}
                RouteTrace? sampled=null;
                Gate? gate = Fresh(now) && path != null ? RouteMatcher.Select(path, position, gates, tracked,out sampled,speed) : null;
                if(gate==null && tracked!=null && Fresh(now)) {
                    var live=RouteMatcher.FindLiveGate(path,gates,tracked,passage.PositionFor(tracked));
                    if(live!=null)gate=passage.AfterCrossing(tracked,position,live.GetWorldCoordinates(),
                        live.state is >=0 and <=3?(BarrierState)live.state:BarrierState.Unknown,
                        sampled!=null&&sampled.ProjectForTracking(position).Offset<=3,decision.Phase=="Langsame Durchfahrt");
                    // An observer must be able to reacquire a new gate. An active
                    // constraint must instead retain the lost target and disarm.
                    if(gate==null && !active) {controller.Reset();tracked=null;passage.Reset();
                        gate=RouteMatcher.Select(path!,position,gates,null,out sampled,speed);}
                }
                if(gate!=null && sampled!=null) {
                    var live=RouteMatcher.FindLiveGate(path,gates,gate.Key,passage.PositionFor(gate.Key));
                    if(live!=null)passage.Capture(gate.Key,live.GetWorldCoordinates(),sampled);
                }
                candidate = gate;
                bool manual = router.SelectedGate is int selected && router.PaymentFor(selected)==TollPayment.Manual &&
                    gates.Any(g=>g.id==selected && gate?.Key==RouteMatcher.Key(g));
                decision = controller.Step(new(now, speed, allow, Fresh(now), gate, manual));
                if(allow && Fresh(now) && router.StopBoundary.HasValue)
                    decision=ApproachStop.Evaluate(speed,router.UpdateStopDistance(path!,position,speed)!.Value);
                else if (allow && sampled != null && path != null)
                    decision=router.LimitShortChange(path,sampled,position,speed,decision);
                tracked = decision.GateKey;
                if(tracked==null)passage.Reset();
                evaluatedAt = now;
                decisionArmed = state.EnableAssists && Fresh(now);
                if (active && allow && decisionArmed && decision.RequiresTakeover) Disarm(decision.Phase);
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
                        version=Info.Version, utc=DateTimeOffset.UtcNow, active, fresh=Fresh(now), speedKph=speed*3.6,
                        connectorAttempted, connectorPending, connectorSeeded,
                        driveAction=driveAction.ToString(),
                        plannedStop=router.StopBoundary.HasValue,stopDistance=router.StopDistance,
                        automaticPreparationPending=automaticPreparation.Requested,
                        startBlocked=!(active&&State.EnableAssists)&&!CanStartNow(now), readyToStart=CanStartNow(now),
                        startRouteFailure=startCheck?.Failure,preparationReason,selectedLane=(previewRouter??router).SelectedGate,plaza=(previewRouter??router).CurrentKey,
                        rollingStartPending,rollingStartCalculating,rollingStartStatus,
                        brakeHeld,brakeInputValue,
                        selectedPayment=router.SelectedGate is int gateId ? router.PaymentFor(gateId).ToString() : null,
                        faultBraking=faultBrake.Latched,headingTurns,
                        pendingRouteGap=safetyTrace?.EndReason,
                        connectedMetresAhead=safetyTrace!=null?safetyTrace.Length-safetyTrace.Project(position).Along:(double?)null,
                        requiredLookahead=TrackingGuard.RequiredLookahead(speed),
                        phase=decision.Phase, targetSpeedKph=decision.TargetSpeed*3.6, gate=tracked, candidate, dataStatus=DataStatus,
                        telemetryAge=now-telemetryAt, gatesAge=now-gatesAt, pathAge=now-pathAt, gameTimeAge=now-gameTimeChangedAt,
                        pathReceivedAge=now-pathReceivedAt,routeWorkMilliseconds,maxRouteWorkMilliseconds,
                        steeringFeed=steeringFeedState.ToString(),steeringAge=lastSteeringCommand!=null?now-lastSteeringCommand.At:(double?)null,
                        steeringFault=lastSteeringFailure,
                        sdk, paused, pathError=path?.ErrorMessage,
                        gateCount=gates.Count(g=>g.type==SemaphoreType.GATE), options,
                        geometry=RouteMatcher.Diagnose(path,position,gates), routing=router.Status, routeFault, lastFault, previewStatus,routeTransition,
                        assists=State.EnableAssists,mode=State.DrivingMode.ToString()
                    }, new JsonSerializerOptions { WriteIndented=true,
                        NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
                    var log = Path.ChangeExtension(DiagnosticPath, ".log");
                    string logState = active + ":" + driveAction + ":" + decision.Phase + ":" + tracked;
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
        StopSteeringMonitor();
        lock(sync)
        {
            if(active || preview || faultBrake.Latched)State.EnableAssists=false;
            active = false;
            CancelRollingStart();
            automaticPreparation.Cancel();
            faultBrake.Release();ResetDriving();
            preview=previewCalculating=false;
            previewRouter=null;
            startCheck=null;
            base.OnDisable();
            if (subscribed)
            {
                Events.Current.Unsubscribe<GameTelemetryData>(GameTelemetry.Current.EventString, OnTelemetry);
                Events.Current.Unsubscribe<SemaphoreData>(SemaphoreProvider.Current.EventString, OnGates);
                Events.Current.Unsubscribe<PlannedPathData>("Pathfinding.PlannedPathData", OnPath);
                Events.Current.Unsubscribe<ControlEvent>(GameOutput.Current.EventString,OnControlOutput);
                ControlsBackend.Current.UnregisterListener(DefaultControls.Cancel.Id,OnCancel);
                ControlsBackend.Current.UnregisterListener(DefaultControls.Increase.Id,OnStart);
                ControlsBackend.Current.UnregisterListener(DefaultControls.Decrease.Id,OnStart);
                ControlsBackend.Current.UnregisterListener(BrakeControl.Id,OnBrakeInput);
                subscribed = false;
            }
            bridge.Detach(); controller.Reset(); tracked = null; passage.Reset();
            decision = TollDecision.Idle("Plugin ausgeschaltet");
        }
    }
    public override void Shutdown() { OnDisable(); if (ReferenceEquals(Current,this)) Current = null; }
}
