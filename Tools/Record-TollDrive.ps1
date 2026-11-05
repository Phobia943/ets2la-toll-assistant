#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EtsRoot,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(1,3600)][int]$DurationSeconds=600,
    [ValidateRange(50,1000)][int]$IntervalMs=100
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'CaptureFormat.psm1') -Force
# Independent observer: neither ETS2LA output memory nor game configuration is opened.
# Layouts: RenCloud telemetry revision 12; ETS2LA semaphore SDK, 40 x 48 bytes.
$statusPath=Join-Path $EtsRoot 'MautAssistent/status.json'
$target=[IO.Path]::GetFullPath($OutputPath)
if(Test-Path -LiteralPath $target){throw 'Die Zieldatei existiert bereits. Bitte einen neuen Dateinamen verwenden.'}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
$maps=@();$views=@();$stream=$null;$writer=$null
try {
    foreach($spec in @(@('Local\SCSTelemetry',2300),@('Local\ETS2LASemaphore',1920))) {
        $map=[IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting($spec[0],[IO.MemoryMappedFiles.MemoryMappedFileRights]::Read)
        $maps+=,$map
        $views+=,$map.CreateViewAccessor(0,$spec[1],[IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
    }
    $stream=[IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $telemetry=[byte[]]::new(2300);$semaphores=[byte[]]::new(1920)
    $lastStatus='';$statusUtc=$null;$status=$null;$nextStatus=0.0;$nextFlush=0.0;$samples=0
    $lastStamp=$null;$lastAdvance=0.0
    while($clock.Elapsed.TotalSeconds -lt $DurationSeconds) {
        $t=$clock.Elapsed.TotalSeconds;$issues=[Collections.Generic.List[string]]::new()
        $views[0].ReadArray(0,$telemetry,0,$telemetry.Length)|Out-Null
        if([BitConverter]::ToUInt32($telemetry,40) -ne 12 -or [BitConverter]::ToUInt32($telemetry,52) -notin @(1,2)) {
            throw 'Das Telemetrieformat ist nicht die unterstützte Revision 12 für ETS2/ATS.'
        }
        $stamp=[BitConverter]::ToUInt64($telemetry,8)
        if($stamp -ne $views[0].ReadUInt64(8)){$issues.Add('TelemetryChangedDuringRead')}
        if($stamp -ne $lastStamp){$lastStamp=$stamp;$lastAdvance=$t}
        if($t-$lastAdvance -gt .5){$issues.Add('TelemetryClockNotAdvancing')}
        $views[1].ReadArray(0,$semaphores,0,$semaphores.Length)|Out-Null
        $decoded=Read-TollGateBuffer $semaphores
        $gates=$decoded.gates
        foreach($issue in $decoded.issues){$issues.Add($issue)}
        $newStatus=$null
        if($t -ge $nextStatus) {
            $nextStatus=$t+.2
            try {
                $status=Get-Content -LiteralPath $statusPath -Raw|ConvertFrom-Json
                $statusUtc=if($status.utc -is [DateTime]){[DateTimeOffset]$status.utc}else{
                    [DateTimeOffset]::Parse($status.utc,[Globalization.CultureInfo]::InvariantCulture)
                }
                if($status.utc -ne $lastStatus) {
                    $lastStatus=$status.utc
                    $newStatus=$status|Select-Object utc,version,assists,active,mode,phase,targetSpeedKph,gate,candidate,
                        selectedLane,selectedPayment,plaza,faultBraking,pathError,driveAction,connectedMetresAhead,
                        requiredLookahead,telemetryAge,gatesAge,pathAge,options,routing,routeFault,plannedStop,stopDistance,
                        steeringFeed,steeringAge,steeringFault
                }
            } catch {$issues.Add('StatusUnreadable')}
        }
        $age=if($null -ne $statusUtc){([DateTimeOffset]::UtcNow-$statusUtc).TotalSeconds}else{$null}
        if($null -eq $age -or $age -lt -.5 -or $age -gt 1.5){$issues.Add('StatusNotCurrent')}
        $speed=[double][BitConverter]::ToSingle($telemetry,948)
        $x=[BitConverter]::ToDouble($telemetry,2200);$y=[BitConverter]::ToDouble($telemetry,2208);$z=[BitConverter]::ToDouble($telemetry,2216)
        $heading=[BitConverter]::ToDouble($telemetry,2224)
        if(@($speed,$x,$y,$z,$heading)|Where-Object{![double]::IsFinite($_)}) {throw 'Ungültige Telemetriewerte.'}
        $row=[ordered]@{
            schema=1;t=$t;utc=[DateTimeOffset]::UtcNow;sdkTimestamp=$stamp;active=($telemetry[0] -ne 0);paused=($telemetry[4] -ne 0)
            speedMps=$speed;x=$x;y=$y;z=$z;heading=$heading;parkingBrake=($telemetry[1566] -ne 0)
            userThrottle=[BitConverter]::ToSingle($telemetry,960);userBrake=[BitConverter]::ToSingle($telemetry,964)
            gameThrottle=[BitConverter]::ToSingle($telemetry,976);gameBrake=[BitConverter]::ToSingle($telemetry,980)
            gates=$gates;statusUtc=$lastStatus;statusAgeSeconds=$age;status=$newStatus;issues=$issues.ToArray()
        }
        $writer.WriteLine(($row|ConvertTo-Json -Depth 9 -Compress));$samples++
        if($t -ge $nextFlush){$writer.Flush();$nextFlush=$t+1}
        Start-Sleep -Milliseconds $IntervalMs
    }
    [pscustomobject]@{file=$target;samples=$samples;seconds=$clock.Elapsed.TotalSeconds;vehicleOutputWritten=$false}
} finally {
    if($writer){$writer.Dispose()}elseif($stream){$stream.Dispose()}
    foreach($view in $views){$view.Dispose()}
    foreach($map in $maps){$map.Dispose()}
}
