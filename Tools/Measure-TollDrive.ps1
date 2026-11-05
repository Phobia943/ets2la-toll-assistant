#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Recording,
    [string]$GateKey
)
$ErrorActionPreference='Stop'
$rows=[Collections.Generic.List[object]]::new()
$status=$null;$lastT=[double]::NegativeInfinity
foreach($line in [IO.File]::ReadLines([IO.Path]::GetFullPath($Recording))) {
    $r=$line|ConvertFrom-Json
    if($r.schema -ne 1 -or ![double]::IsFinite([double]$r.t) -or $r.t -le $lastT){throw 'Unbekanntes Format oder ungültige Zeitfolge.'}
    $lastT=$r.t
    if($null -ne $r.status){$status=$r.status}
    # A status reference only names a sample; it cannot make old status fresh.
    $validStatus=$null -ne $status -and $status.utc -eq $r.statusUtc -and
        $null -ne $r.statusAgeSeconds -and $r.statusAgeSeconds -ge -.5 -and $r.statusAgeSeconds -le 1.5
    $r|Add-Member -NotePropertyName currentStatus -NotePropertyValue $(if($validStatus){$status}else{$null})
    $rows.Add($r)
}
if(!$rows.Count){throw 'Leere Aufzeichnung.'}
$keys=@($rows|ForEach-Object{$_.currentStatus.gate}|Where-Object{$_}|Sort-Object -Unique)
if(!$GateKey -and $keys.Count -eq 1){$GateKey=$keys[0]}
if(!$GateKey) {
    [ordered]@{result='Incomplete';reason=$(if(!$keys.Count){'Keine zugeordnete Mautschranke aufgezeichnet.'}else{'Mehrere Schrankenschlüssel; -GateKey ausdrücklich wählen.'});samples=$rows.Count;gateKeys=$keys;fullPassValidated=$false}|ConvertTo-Json -Depth 6
    exit 2
}
$windows=[Collections.Generic.List[object]]::new();$points=[Collections.Generic.List[object]]::new()
$openStart=$null;$previous=$null;$previousStamp=$null;$issues=0;$missing=0;$stale=0;$faults=0
foreach($r in $rows) {
    $s=$r.currentStatus
    if(!$s){$stale++}
    if($s.faultBraking){$faults++}
    $matched=@($r.gates|Where-Object{$_.key -ceq $GateKey})
    $valid=$r.active -and !$r.paused -and @($r.issues).Count -eq 0 -and $matched.Count -eq 1 -and
        $r.sdkTimestamp -ne $previousStamp -and ($null -eq $previous -or $r.t-$previous -le .5)
    if(@($r.issues).Count){$issues++}
    if($matched.Count -ne 1){$missing++}
    if($valid -and $matched[0].state -eq 3) {
        if($null -eq $openStart){$openStart=$r.t}
    } else {
        if($null -ne $openStart){$windows.Add([ordered]@{from=$openStart;lastObservation=$previous;observedSeconds=$previous-$openStart})}
        $openStart=$null
    }
    if($valid) {
        $gate=$matched[0];$distance=$null
        if($s.gate -ceq $GateKey -and $s.candidate.Key -ceq $GateKey){$distance=$s.candidate.Distance}
        $points.Add([pscustomobject]@{t=$r.t;speedKph=$r.speedMps*3.6;barrierState=$gate.state;
            consecutiveOpenSeconds=$(if($null -ne $openStart){$r.t-$openStart}else{0});
            distanceAlongRoute=$distance;statusAge=$r.statusAgeSeconds;assists=$s.assists;phase=$s.phase;
            manualBrake=$r.userBrake;manualThrottle=$r.userThrottle;selectedLane=$s.selectedLane;payment=$s.selectedPayment})
    }
    $previous=$r.t;$previousStamp=$r.sdkTimestamp
}
if($null -ne $openStart){$windows.Add([ordered]@{from=$openStart;lastObservation=$previous;observedSeconds=$previous-$openStart})}
$near=@($points|Where-Object{$null -ne $_.distanceAlongRoute -and $_.distanceAlongRoute -ge 0 -and $_.distanceAlongRoute -le 30})
$held=@($near|Where-Object{$_.speedKph -ge -.1 -and $_.speedKph -le .36 -and $_.barrierState -ne 3 -and $_.assists})
$passage=@($points|Where-Object{$null -ne $_.distanceAlongRoute -and $_.distanceAlongRoute -lt -1 -and $_.assists})
$exit=@($points|Where-Object{$null -ne $_.distanceAlongRoute -and $_.distanceAlongRoute -lt -30 -and $_.assists})
$switched=@($rows|Where-Object{$_.currentStatus.assists -and $_.currentStatus.gate -and $_.currentStatus.gate -cne $GateKey})
[ordered]@{
    result='EvidenceOnly';gateKey=$GateKey;samples=$rows.Count;selectedGateObservations=$points.Count
    missingOrAmbiguousGateSamples=$missing;captureIssueSamples=$issues;staleStatusSamples=$stale;faultBrakeSamples=$faults
    observedOpenWindows=$windows.ToArray();nearGateSamples=$near.Count;stationaryBeforeClosedGateSamples=$held.Count
    nearGatePeakSpeedKph=($near.speedKph|Measure-Object -Maximum).Maximum
    observedPassageSamples=$passage.Count;observedExitBeyond30mSamples=$exit.Count
    otherAssignedGateSamples=$switched.Count
    paymentEvidence=@($points.payment|Where-Object{$_}|Sort-Object -Unique)
    fullPassValidated=$false
    limitations=@(
        'Statusdaten werden etwa einmal pro Sekunde geschrieben; gleiche Referenzen sind keine neuen Steuerentscheidungen.'
        'Die Schrankendaten besitzen keinen eigenen Quellzeitstempel; beobachtetes Offenbleiben ist kein Beweis aller internen SDK-Frames.'
        'Spurhöhe, Kollisionen und die Eignung der Durchfahrt benötigen Spielbild beziehungsweise Fahrerbestätigung.'
        'Abtastlücken, pausierte Simulation und fehlende Schranke unterbrechen jedes Offenzeitfenster.'
        'Die Messübersicht erteilt keine automatische Freigabe der vollständigen Mautdurchfahrt.'
    )
}|ConvertTo-Json -Depth 8
