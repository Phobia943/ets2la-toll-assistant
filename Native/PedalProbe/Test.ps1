#requires -Version 7.0
param([Parameter(Mandatory)][string]$Zig,[Parameter(Mandatory)][string]$OutputDirectory,[string]$CacheDirectory)
$ErrorActionPreference='Stop'
$compiler=[IO.Path]::GetFullPath($Zig)
if((& $compiler version) -ne '0.15.2'){throw 'Expected Zig 0.15.2'}
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a new isolated test directory'}
New-Item -ItemType Directory -Path $out|Out-Null
$previousGlobal=$env:ZIG_GLOBAL_CACHE_DIR;$previousLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
 $env:ZIG_GLOBAL_CACHE_DIR=if($CacheDirectory){[IO.Path]::GetFullPath($CacheDirectory)}else{Join-Path $out 'global'};$env:ZIG_LOCAL_CACHE_DIR=Join-Path $out 'local'
 $define='-DMAUT_PEDAL_PROBE_TEST_ID=probe_'+[Guid]::NewGuid().ToString('N')
 $exe=Join-Path $out 'Probe.Test.exe'
 & $compiler c++ -target x86_64-windows-gnu -std=c++17 -fno-exceptions -fno-rtti -O2 -Wall -Wextra -Werror $define (Join-Path $PSScriptRoot 'Tests.cpp') (Join-Path $PSScriptRoot 'Probe.cpp') -o $exe
 if($LASTEXITCODE){throw 'Observer test build failed'}
 & $exe (Join-Path $out 'fixture.bin')
 if($LASTEXITCODE){throw 'Observer assertions failed'}
} finally {$env:ZIG_GLOBAL_CACHE_DIR=$previousGlobal;$env:ZIG_LOCAL_CACHE_DIR=$previousLocal}
