#requires -Version 7.0
param([string]$Zig='zig',[string]$SdkRoot=(Join-Path $PSScriptRoot '../../References/TruckersMP'),[switch]$Test)
$ErrorActionPreference='Stop'
$headers=@{
    'TruckersMP.h'='F70CB0D86D7485388676AF8F46B8E5B573BB4E598802D66982F41D1D3B02F679'
    'TruckersMP_Base.h'='2C877A0E6130C4CAA0076914D9CAC970183FA8206A8403CF9788EB9BACCD075C'
}
foreach($name in $headers.Keys) {
    $path=Join-Path $SdkRoot ('include/TruckersMP/'+$name)
    if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $headers[$name]) {
        throw "SDK header absent or changed: $name. Use TruckersMP/GameClientSDK commit bee3b9e2b314c2b47fee9f94635e9945a71924e1."
    }
}
$out=Join-Path $PSScriptRoot 'bin';New-Item -ItemType Directory -Path $out -Force|Out-Null
$previousGlobal=$env:ZIG_GLOBAL_CACHE_DIR
$previousLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
$env:ZIG_GLOBAL_CACHE_DIR=Join-Path $out 'zig-global-cache'
$env:ZIG_LOCAL_CACHE_DIR=Join-Path $out 'zig-local-cache'
& $Zig cc -target x86_64-windows-gnu -std=c11 -O2 -shared '-Wall' '-Wextra' "-I$(Join-Path $SdkRoot 'include')" (Join-Path $PSScriptRoot 'Observer.c') -o (Join-Path $out 'MautGeometry.dll')
if($LASTEXITCODE){throw 'Native observer build failed'}
if($Test) {
    & $Zig cc -target x86_64-windows-gnu -std=c11 -O2 '-Wall' '-Wextra' "-I$(Join-Path $SdkRoot 'include')" (Join-Path $PSScriptRoot 'FakeHostTest.c') -o (Join-Path $out 'GeometryHostTest.exe')
    if($LASTEXITCODE){throw 'Native fake-host build failed'}
    & (Join-Path $out 'GeometryHostTest.exe') (Join-Path $out 'MautGeometry.dll') (Join-Path $out 'native-fixture.bin')
    if($LASTEXITCODE){throw 'Native fake-host checks failed'}
}
Get-FileHash -LiteralPath (Join-Path $out 'MautGeometry.dll')|Select-Object Hash,Path
} finally {
    $env:ZIG_GLOBAL_CACHE_DIR=$previousGlobal
    $env:ZIG_LOCAL_CACHE_DIR=$previousLocal
}
