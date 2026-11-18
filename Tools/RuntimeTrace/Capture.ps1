#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$TargetProcessId,
    [Parameter(Mandatory)][string]$EtsRoot,
    [Parameter(Mandatory)][string]$TraceTool,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(5,180)][int]$Seconds=60,
    [switch]$IncludeManagedStacks
)
$ErrorActionPreference='Stop'
# This script attaches a diagnostics observer. It neither starts/restarts the
# target nor opens vehicle command maps. No UI activation or input injection.
$root=[IO.Path]::GetFullPath($EtsRoot).TrimEnd('\','/')
$target=Get-Process -Id $TargetProcessId -ErrorAction Stop
$exe=[IO.Path]::GetFullPath($target.Path)
if(!$exe.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Der ausgewählte Prozess gehört nicht zur angegebenen ETS2LA-Installation.'
}
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Aufnahmeordner muss neu sein.'}
$tool=(Resolve-Path -LiteralPath $TraceTool).Path
$version=& $tool --version
if($LASTEXITCODE){throw 'Diagnosewerkzeug nicht ausführbar.'}
[IO.Directory]::CreateDirectory($out)|Out-Null
$profile=if($IncludeManagedStacks){'dotnet-common,dotnet-sampled-thread-time'}else{'dotnet-common'}
$metadata=[ordered]@{schema=1;startUtc=[DateTimeOffset]::UtcNow.ToString('o');processId=$TargetProcessId;
    processStartUtc=$target.StartTime.ToUniversalTime().ToString('o');executable=$exe;
    traceToolVersion=($version -join ' ');profile=$profile;durationSeconds=$Seconds;
    vehicleOutputWritten=$false;limitation='Diagnostic tracing adds overhead; no native scheduler/CPU attribution.'}
$metadata|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out 'capture.json') -Encoding utf8
$duration=[TimeSpan]::FromSeconds($Seconds).ToString('dd\:hh\:mm\:ss')
# dotnet-common plus Contention (0x4000); retain all its other runtime keywords.
& $tool collect --process-id $TargetProcessId --profile $profile --providers 'Microsoft-Windows-DotNETRuntime:0x100003C01D:4' --resume-runtime:false --buffersize 64 --duration $duration --output (Join-Path $out 'runtime.nettrace') 2>&1 |
    Tee-Object -FilePath (Join-Path $out 'capture.log')
$result=$LASTEXITCODE
$metadata.endUtc=[DateTimeOffset]::UtcNow.ToString('o')
$metadata.exitCode=$result
$metadata|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out 'capture.json') -Encoding utf8
if($result){throw "Ablaufaufnahme fehlgeschlagen: $result"}
