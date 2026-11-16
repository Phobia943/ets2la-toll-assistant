#requires -Version 7.0
param(
 [Parameter(Mandatory)][string]$SdkSource,
 [Parameter(Mandatory)][string]$Zig,
 [Parameter(Mandatory)][string]$TestDirectory)
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath($SdkSource)
$compiler=[IO.Path]::GetFullPath($Zig)
if((& $compiler version) -ne '0.15.2'){throw 'Expected Zig 0.15.2'}
$output=[IO.Path]::GetFullPath($TestDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new test directory'}
New-Item -ItemType Directory -Path $output|Out-Null
$previousGlobal=$env:ZIG_GLOBAL_CACHE_DIR;$previousLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
 $env:ZIG_GLOBAL_CACHE_DIR=Join-Path $output 'zig-global'
 $env:ZIG_LOCAL_CACHE_DIR=Join-Path $output 'zig-local'
 foreach($test in @('IndependentCollectorTests','ProducerContractTests')){
  $binary=Join-Path $output ($test+'.exe')
  & $compiler c++ -target x86_64-windows-gnu -std=c++17 -O2 ('-I'+(Join-Path $source 'src')) ('-I'+(Join-Path $source 'vendor/scs_sdk/include')) (Join-Path $PSScriptRoot ($test+'.cpp')) (Join-Path $source 'src/prism/common.cpp') -o $binary
  if($LASTEXITCODE){throw "Failed to build $test"}
  & $binary
  if($LASTEXITCODE){throw "Failed $test"}
 }
} finally {$env:ZIG_GLOBAL_CACHE_DIR=$previousGlobal;$env:ZIG_LOCAL_CACHE_DIR=$previousLocal}
