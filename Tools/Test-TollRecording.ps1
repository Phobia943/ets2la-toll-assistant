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
    Write-Output "$checks recording checks passed. No game or ETS2LA output opened."
} finally {
    # Only files created by this test in its unique temporary directory are removed.
    $resolved=[IO.Path]::GetFullPath($temporary)
    $root=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected temporary path.'}
    Get-ChildItem -LiteralPath $resolved -File | ForEach-Object{Remove-Item -LiteralPath $_.FullName}
    Remove-Item -LiteralPath $resolved
}
