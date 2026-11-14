#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'CaptureFormat.psm1') -Force
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('toll-recording-test-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary)|Out-Null
$analyzer=Join-Path $PSScriptRoot 'Measure-TollDrive.ps1'
$key='7:12.3:-45.6';$neighbor='7:16.8:-45.6';$checks=0
function Assert($condition,[string]$message) {if(!$condition){throw $message}}
function Fixture {
    @(for($i=0;$i -lt 11;$i++) {
        [pscustomobject]@{schema=1;t=$i/10;utc='2026-11-05T12:00:00Z';sdkTimestamp=1000000+$i*100000;
            active=$true;paused=$false;speedMps=0;userThrottle=0;userBrake=0;issues=@();
            gates=@([pscustomobject]@{key=$key;state=3},[pscustomobject]@{key=$neighbor;state=3});
            statusUtc='2026-11-05T12:00:00Z';statusAgeSeconds=0;
            status=[pscustomobject]@{utc='2026-11-05T12:00:00Z';gate=$key;candidate=[pscustomobject]@{Key=$key;Distance=8};
                assists=$true;faultBraking=$false;phase='Waiting';selectedLane=7;selectedPayment='Automatic'}}
    })
}
function InvokeRecordingAnalysis($records,[switch]$Automatic) {
    $path=Join-Path $temporary ([guid]::NewGuid().ToString('N')+'.jsonl')
    $records|ForEach-Object{$_|ConvertTo-Json -Depth 8 -Compress}|Set-Content -LiteralPath $path -Encoding utf8
    $arguments=@('-NoProfile','-File',$analyzer,'-Recording',$path)
    if(!$Automatic){$arguments+=@('-GateKey',$key)}
    $json=& (Join-Path $PSHOME 'pwsh.exe') @arguments
    if($LASTEXITCODE -notin @(0,2)){throw 'Auswertung unerwartet fehlgeschlagen.'}
    $json|ConvertFrom-Json
}
try {
    $airBytes=[byte[]]::new(2300);[BitConverter]::GetBytes([uint32]12).CopyTo($airBytes,40)
    [BitConverter]::GetBytes([single]4.5).CopyTo($airBytes,992)
    [BitConverter]::GetBytes([single]5).CopyTo($airBytes,720)
    [BitConverter]::GetBytes([single]4.25).CopyTo($airBytes,724);$airBytes[1568]=1
    $air=Read-TruckAirBuffer $airBytes
    Assert ($air.pressure -eq 4.5 -and $air.warningThreshold -eq 5 -and $air.emergencyThreshold -eq 4.25 -and
        $air.warning -and !$air.emergency -and $air.issues.Count -eq 0) 'Read pressure, vehicle-specific limits and actual warning flags without inferred units.';$checks++
    [BitConverter]::GetBytes([single]-.024423394).CopyTo($airBytes,992);$airBytes[1569]=1
    $air=Read-TruckAirBuffer $airBytes
    $savedAir=$air|ConvertTo-Json -Compress|ConvertFrom-Json
    Assert ($null -eq $savedAir.pressure -and [Math]::Abs($savedAir.rawPressure+.024423394) -lt .000001 -and
        $savedAir.warning -and $savedAir.emergency -and $savedAir.issues -contains 'InvalidAirPressure:pressure') 'A finite negative near-empty source reading must survive capture without being reported as valid pressure or clearing warning flags.';$checks++
    [BitConverter]::GetBytes([single]::NaN).CopyTo($airBytes,992);$airBytes[1569]=2
    $air=Read-TruckAirBuffer $airBytes
    Assert ($null -eq $air.pressure -and $null -eq $air.rawPressure -and $null -eq $air.emergency -and $air.issues.Count -eq 2) 'Bad pressure and warning flags cannot be reported as safe readings.';$checks++
    $airBytes[40]=13;$rejected=$false
    try{Read-TruckAirBuffer $airBytes|Out-Null}catch{$rejected=$true}
    Assert $rejected 'Unknown telemetry layout cannot decode air pressure.';$checks++
    $telemetry=[byte[]]::new(2300);[BitConverter]::GetBytes([uint32]12).CopyTo($telemetry,40)
    $offset=1036
    foreach($wear in @(.01,.02,.03,.04,.05)){[BitConverter]::GetBytes([single]$wear).CopyTo($telemetry,$offset);$offset+=4}
    $damage=Read-TruckDamageBuffer $telemetry
    Assert ([Math]::Abs($damage.engine-.01) -lt .00001 -and [Math]::Abs($damage.transmission-.02) -lt .00001 -and
        [Math]::Abs($damage.cabin-.03) -lt .00001 -and [Math]::Abs($damage.chassis-.04) -lt .00001 -and
        [Math]::Abs($damage.wheels-.05) -lt .00001 -and $damage.issues.Count -eq 0) 'Read all five wear channels from the verified telemetry offsets.';$checks++
    [BitConverter]::GetBytes([single]::NaN).CopyTo($telemetry,1036);[BitConverter]::GetBytes([single]2).CopyTo($telemetry,1048)
    $damage=Read-TruckDamageBuffer $telemetry
    Assert ($null -eq $damage.engine -and $null -eq $damage.chassis -and $damage.issues.Count -eq 2) 'Invalid damage cannot appear as a valid undamaged truck.';$checks++
    $telemetry[40]=13;$rejected=$false
    try{Read-TruckDamageBuffer $telemetry|Out-Null}catch{$rejected=$true}
    Assert $rejected 'An unknown telemetry layout cannot be decoded as known damage.';$checks++
    $inputBytes=[byte[]]::new(26);$stateBytes=[byte[]]::new(6)
    [BitConverter]::GetBytes([single]-.75).CopyTo($inputBytes,13);$inputBytes[17]=1
    [BitConverter]::GetBytes([double]1000).CopyTo($inputBytes,18)
    [BitConverter]::GetBytes([int]16101).CopyTo($stateBytes,0);$stateBytes[5]=1
    $pedals=Read-ModernPedalBuffer $inputBytes $stateBytes 1000.05
    Assert ($pedals.acceleration -eq -.75 -and $pedals.overrideRequested -and $pedals.overrideReported -and
        $pedals.pluginVersion -eq 16101 -and [Math]::Abs($pedals.commandAgeSeconds-.05) -lt .00001 -and
        !$pedals.atomicSnapshot -and $pedals.issues.Count -eq 0) 'Modern signed brake command, double timestamp and plugin acknowledgement must decode independently of steering.';$checks++
    [BitConverter]::GetBytes([single]::NaN).CopyTo($inputBytes,13)
    [BitConverter]::GetBytes([double]::PositiveInfinity).CopyTo($inputBytes,18);$inputBytes[17]=2;$stateBytes[5]=2
    $pedals=Read-ModernPedalBuffer $inputBytes $stateBytes 1000
    Assert ($null -eq $pedals.acceleration -and $null -eq $pedals.timestamp -and $null -eq $pedals.commandAgeSeconds -and
        $null -eq $pedals.overrideRequested -and $null -eq $pedals.overrideReported -and $pedals.issues.Count -eq 3) 'Invalid modern output fields must remain unknown and serialize without nonfinite numbers.';$checks++
    $rejected=$false
    try {Read-ModernPedalBuffer ([byte[]]::new(25)) $stateBytes 1000|Out-Null}catch{$rejected=$true}
    Assert $rejected 'A different modern output layout must not be guessed.';$checks++
    $culture=[Threading.Thread]::CurrentThread.CurrentCulture
    try {
        [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo('de-DE')
        foreach($fraction in @(.05,.15,.25,.35,.45,.55)) {
            $bytes=[byte[]]::new(1920)
            [BitConverter]::GetBytes([single]$fraction).CopyTo($bytes,0)
            [BitConverter]::GetBytes([int16]-83).CopyTo($bytes,12)
            [BitConverter]::GetBytes([int16]51).CopyTo($bytes,14)
            [BitConverter]::GetBytes([int]2).CopyTo($bytes,32)
            [BitConverter]::GetBytes([int]3).CopyTo($bytes,40)
            [BitConverter]::GetBytes([int]7).CopyTo($bytes,44)
            $native=[Numerics.Vector3]::new([single]$fraction,0,0)+[Numerics.Vector3]::new(-83,0,51)*512
            $expected='7:'+$native.X.ToString('F1',[Globalization.CultureInfo]::InvariantCulture)+':'+$native.Z.ToString('F1',[Globalization.CultureInfo]::InvariantCulture)
            $decoded=Read-TollGateBuffer $bytes
            Assert ($decoded.gates.Count -eq 1 -and $decoded.gates[0].key -ceq $expected) 'Gate identity must match native Vector3 rounding, independent of German decimal formatting.'
        }
        $checks++
        [BitConverter]::GetBytes([int]9).CopyTo($bytes,40)
        $decoded=Read-TollGateBuffer $bytes
        Assert ($decoded.gates.Count -eq 0 -and $decoded.issues.Count -eq 1) 'Invalid barrier state must be reported, not treated as open.';$checks++
    } finally {[Threading.Thread]::CurrentThread.CurrentCulture=$culture}
    $r=InvokeRecordingAnalysis (Fixture)
    Assert ($r.observedOpenWindows.Count -eq 1 -and [math]::Abs($r.observedOpenWindows[0].observedSeconds-1) -lt .0001) 'Continuous open observations must have their measured duration.';$checks++
    foreach($mode in @('closed','missing','paused','duplicate','gap','issue')) {
        $rows=Fixture
        switch($mode) {
            'closed' {$rows[5].gates[0].state=1}
            'missing' {$rows[5].gates=@([pscustomobject]@{key=$neighbor;state=3})}
            'paused' {$rows[5].paused=$true}
            'duplicate' {$rows[5].sdkTimestamp=$rows[4].sdkTimestamp}
            'gap' {for($i=5;$i -lt $rows.Count;$i++){$rows[$i].t+=1}}
            'issue' {$rows[5].issues=@('TelemetryChangedDuringRead')}
        }
        $r=InvokeRecordingAnalysis $rows
        Assert ($r.observedOpenWindows.Count -eq 2) ('Open continuity must reset for '+$mode)
        Assert (@($r.observedOpenWindows|Where-Object{$_.observedSeconds -ge 1}).Count -eq 0) ('No false long-open window for '+$mode)
        Assert (!$r.fullPassValidated) 'Measurements cannot certify an entire drive.';$checks++
    }
    $rows=Fixture;foreach($row in $rows){$row.statusAgeSeconds=4;$row.gates[0].state=1}
    $r=InvokeRecordingAnalysis $rows
    Assert ($r.staleStatusSamples -eq 11 -and $r.stationaryBeforeClosedGateSamples -eq 0) 'Old assist status must not prove an autonomous stop.';$checks++
    $rows=Fixture;foreach($row in $rows){$row.status=$null}
    $r=InvokeRecordingAnalysis $rows -Automatic
    Assert ($r.result -eq 'Incomplete' -and !$r.fullPassValidated) 'No assigned gate must remain incomplete.';$checks++
    $rows=Fixture;$rows[6].status.gate=$neighbor
    $r=InvokeRecordingAnalysis $rows -Automatic
    Assert ($r.result -eq 'Incomplete' -and $r.gateKeys.Count -eq 2) 'Different gate assignments require explicit selection.';$checks++
    function PositionedFixture {
        $rows=Fixture
        foreach($row in $rows) {
            $row.gates=@(
                [pscustomobject]@{key=$key;id=7;x=12.3;y=4;z=-45.6;state=3},
                [pscustomobject]@{key='7:12.3:-45.3';id=7;x=12.3;y=4;z=-45.25;state=3},
                [pscustomobject]@{key=$neighbor;id=7;x=16.8;y=4;z=-45.6;state=1},
                [pscustomobject]@{key=$key;id=8;x=12.3;y=14;z=-45.6;state=1})
            # The other level uses its own key, just as the real decoder does.
            $row.gates[3].key='8:12.3:-45.6'
        }
        return $rows
    }
    $rows=PositionedFixture;$rows[5].gates=@($rows[5].gates[1..3])
    $r=InvokeRecordingAnalysis $rows
    Assert ($r.observedOpenWindows.Count -eq 1 -and $r.missingOrAmbiguousGateSamples -eq 0 -and $r.duplicateBoothObjectSamples -eq 10) 'A known physical booth must survive one raw replica disappearing without adopting the nearby other booth.';$checks++
    $rows=PositionedFixture;$rows[5].gates[1].state=1
    $r=InvokeRecordingAnalysis $rows
    Assert ($r.observedOpenWindows.Count -eq 2 -and $r.conflictingBoothStateSamples -eq 1) 'Conflicting same-booth objects must break the open window.';$checks++
    $rows=PositionedFixture;foreach($row in $rows){$row.gates=@($row.gates[1..3])}
    $r=InvokeRecordingAnalysis $rows
    Assert ($r.observedOpenWindows.Count -eq 0 -and $r.missingOrAmbiguousGateSamples -eq 11) 'A nearby replica without a previously observed exact 3D anchor cannot establish identity.';$checks++
    $rows=PositionedFixture
    # Establish the real level, then present the same numeric ID and rounded key
    # on another level. The stored anchor must neither jump nor grant clearance.
    for($i=5;$i -lt $rows.Count;$i++){$rows[$i].gates=@([pscustomobject]@{key=$key;id=7;x=12.3;y=14;z=-45.6;state=3})}
    $r=InvokeRecordingAnalysis $rows
    Assert ($r.observedOpenWindows.Count -eq 1 -and $r.observedOpenWindows[0].observedSeconds -lt .5 -and $r.missingOrAmbiguousGateSamples -eq 6) 'A reused key at another height cannot replace a remembered booth.';$checks++
    $rows=PositionedFixture;foreach($row in $rows){$row.gates[0].id=8}
    $r=InvokeRecordingAnalysis $rows
    Assert ($r.observedOpenWindows.Count -eq 0) 'An inconsistent raw key and numeric signal ID cannot establish an anchor.';$checks++
    $rows=PositionedFixture
    foreach($row in $rows){$row.gates[3].key=$key;$row.gates[3].id=7}
    $r=InvokeRecordingAnalysis $rows
    Assert ($r.observedOpenWindows.Count -eq 0 -and $r.duplicateBoothObjectSamples -eq 0) 'An ambiguous first key at two heights must not be counted as a proven physical booth replica.';$checks++
    function DepartureFixture {
        $rows=PositionedFixture
        for($i=0;$i -lt $rows.Count;$i++) {
            $row=$rows[$i];$row.t=$i/5;$row.sdkTimestamp=1000000+$i*200000;$row.speedMps=20
            foreach($pair in @(@('x',(12.3+4*$i)),@('y',4),@('z',-45.6),@('heading',.75))){
                $row|Add-Member -Force NoteProperty $pair[0] $pair[1]
            }
            foreach($pair in @(@('steeringFeed','Healthy'),@('steeringAge',0),@('telemetryAge',0),@('pathAge',0),@('connectedMetresAhead',100),@('requiredLookahead',25),@('pathError',''),@('routeFault',$null))){
                $row.status|Add-Member -Force NoteProperty $pair[0] $pair[1]
            }
            $row.status.candidate.Distance=-4*$i
            if($i -ge 8){$row.status.gate=$null;$row.status.candidate=$null}
        }
        return $rows
    }
    $r=InvokeRecordingAnalysis (DepartureFixture)
    Assert ($r.observedExitBeyond30mSamples -eq 0 -and $r.departureAfterGateRelease.beyondGatePlane30mSamples -eq 3 -and
        !$r.departureAfterGateRelease.isRouteDistance -and !$r.fullPassValidated) 'SDK departure remains observable after normal gate release, without claiming route distance or full-drive certification.';$checks++
    $rows=DepartureFixture;for($i=8;$i -lt $rows.Count;$i++){$rows[$i].gates=@()}
    $r=InvokeRecordingAnalysis $rows
    Assert ($r.departureAfterGateRelease.beyondGatePlane30mSamples -eq 3) 'A barrier disappearing behind the truck does not erase independent healthy SDK departure evidence.';$checks++
    foreach($mode in @('noapproach','wrongside','reverse','gap','stale','fault','othergate','jump','height','duplicate','loststeering','oldroute','oldtelemetry','oldsteering')) {
        $rows=DepartureFixture
        switch($mode) {
            'noapproach' {foreach($row in $rows){if($row.status.candidate){$row.status.candidate.Distance=-10}}}
            'wrongside' {for($i=0;$i -lt $rows.Count;$i++){$rows[$i].x=12.3-4*$i}}
            'reverse' {$rows[8].speedMps=-20}
            'gap' {for($i=8;$i -lt $rows.Count;$i++){$rows[$i].t+=1}}
            'stale' {$rows[8].statusAgeSeconds=4}
            'fault' {$rows[8].status.faultBraking=$true}
            'othergate' {$rows[8].status.gate=$neighbor}
            'jump' {$rows[8].x+=100}
            'height' {for($i=8;$i -lt $rows.Count;$i++){$rows[$i].y=14}}
            'duplicate' {$rows[8].sdkTimestamp=$rows[7].sdkTimestamp}
            'loststeering' {$rows[8].status.steeringFeed='Lost'}
            'oldroute' {$rows[8].status.pathAge=2}
            'oldtelemetry' {$rows[8].status.telemetryAge=1}
            'oldsteering' {$rows[8].status.steeringAge=.2}
        }
        $r=InvokeRecordingAnalysis $rows
        Assert ($r.departureAfterGateRelease.beyondGatePlane30mSamples -eq 0) ('No inferred departure across '+$mode)
        Assert (!$r.fullPassValidated) 'A departure metric never certifies the full trip.';$checks++
    }
    Write-Output "$checks recording checks passed. No game or ETS2LA output opened."
} finally {
    # Only files created by this test in its unique temporary directory are removed.
    $resolved=[IO.Path]::GetFullPath($temporary)
    $root=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected temporary path.'}
    Get-ChildItem -LiteralPath $resolved -File | ForEach-Object{Remove-Item -LiteralPath $_.FullName}
    Remove-Item -LiteralPath $resolved
}
# Missing evidence intentionally returns 2 from the child analyzer. Successful
# negative tests must not leave that native exit code as the CI job's result.
exit 0
