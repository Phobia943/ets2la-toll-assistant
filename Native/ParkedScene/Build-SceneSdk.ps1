#requires -Version 7.0
param(
 [Parameter(Mandatory)][string]$SdkArchive,
 [Parameter(Mandatory)][string]$FmtArchive,
 [Parameter(Mandatory)][string]$Zig,
 [Parameter(Mandatory)][string]$BuildDirectory,
 [string]$CacheDirectory)
$ErrorActionPreference='Stop'
foreach($entry in @(
 @($SdkArchive,'D3B13C1904BEF44BAFE8009DB500579A5B473E63F8D99B1143D4F829143E2716'),
 @($FmtArchive,'2CAE9AB497FB6445C4F747C106C042C95E1EE5A1604CF7956A64C8D9D7619A32'))){
 if((Get-FileHash -LiteralPath $entry[0]).Hash -ne $entry[1]){throw "Archive does not match pinned upstream revision: $($entry[0])"}
}
$compiler=[IO.Path]::GetFullPath($Zig)
if((& $compiler version) -ne '0.15.2'){throw 'Expected Zig 0.15.2'}
$build=[IO.Path]::GetFullPath($BuildDirectory)
if(Test-Path -LiteralPath $build){throw 'Use a new empty build directory; no existing files are replaced'}
New-Item -ItemType Directory -Path $build|Out-Null
function Expand-PinnedArchive([string]$archive,[string]$destination) {
 New-Item -ItemType Directory -Path $destination|Out-Null
 $container=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($archive))
 try {
  $boundary=[IO.Path]::GetFullPath($destination)+[IO.Path]::DirectorySeparatorChar
  foreach($entry in $container.Entries){
   $target=[IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
   if(-not $target.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Archive path escapes build directory'}
  }
 } finally {$container.Dispose()}
 [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($archive),$destination)
}
Expand-PinnedArchive $SdkArchive (Join-Path $build 'sdk')
Expand-PinnedArchive $FmtArchive (Join-Path $build 'fmt')
$source=Join-Path $build 'sdk/plugin-31f1cf1406d03f0d647a10c72887eaab5c2a748e'
$fmt=Join-Path $build 'fmt/fmt-9ff9c695db8aeadf70eb26498a0e1cdceeeb849a'
& (Join-Path $PSScriptRoot 'Apply-SdkPatch.ps1') -SdkSource $source
$version=[IO.File]::ReadAllText((Join-Path $source 'version.hpp.in')).Replace('@PLUGIN_VERSION_MAJOR@','1').Replace('@PLUGIN_VERSION_MINOR@','62').Replace('@PLUGIN_VERSION_PATCH@','0').Replace('@GAME_VERSION_MAJOR@','1').Replace('@GAME_VERSION_MINOR@','62').Replace('@GAME_VERSION_PATCH@','x')
[IO.File]::WriteAllText((Join-Path $source 'src/version.hpp'),$version,[Text.UTF8Encoding]::new($false))
$names=@('core.cpp','main.cpp','memory/memory_utils.shared.cpp','memory/virtual/memory_handler.cpp','prism/common.cpp','prism/game_actor.cpp','prism/camera/camera_manager.cpp','prism/controllers/base_ctrl.cpp','prism/controllers/game_ctrl.cpp','prism/navigation/gps_manager.cpp','prism/traffic/game_traffic.cpp','prism/vehicles/game_trailer_actor.cpp','prism/vehicles/game_physics_vehicle.cpp','prism/vehicles/vehicle_shared.cpp','prism/input/input_mix.cpp','processing/traffic.cpp','memory/memory_utils.win32.cpp','processing/ffb/ffb_processor.win32.cpp','maut_scene/SceneSnapshot.cpp')
$sources=$names|ForEach-Object{Join-Path $source ('src/'+$_)}
$output=Join-Path $build 'ets2la_plugin.dll'
$oldGlobal=$env:ZIG_GLOBAL_CACHE_DIR;$oldLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
 $env:ZIG_GLOBAL_CACHE_DIR=if($CacheDirectory){[IO.Path]::GetFullPath($CacheDirectory)}else{Join-Path $build 'zig-global'}
 $env:ZIG_LOCAL_CACHE_DIR=Join-Path $build 'zig-local'
 $arguments=@('c++','-target','x86_64-windows-gnu','-std=c++17','-shared','-O2','-fms-extensions','-DFMT_HEADER_ONLY','-DETS2LA_PLUGIN_EXPORTS','-DUNICODE','-D_UNICODE','-I'+(Join-Path $source 'src'),'-I'+(Join-Path $source 'vendor/scs_sdk/include'),'-I'+(Join-Path $fmt 'include'))+$sources+@((Join-Path $source 'ets2la_plugin.def'),'-ldinput8','-ldxguid','-lversion','-lpsapi','-o',$output)
 & $compiler @arguments
 if($LASTEXITCODE){throw 'Native SDK build failed'}
 Copy-Item -LiteralPath (Join-Path $source 'LICENSE.md') -Destination (Join-Path $build 'ETS2LA-SDK-LICENSE.md')
 Copy-Item -LiteralPath (Join-Path $fmt 'LICENSE') -Destination (Join-Path $build 'FMT-LICENSE')
 Get-FileHash -LiteralPath $output|Select-Object Hash,Path
} finally {$env:ZIG_GLOBAL_CACHE_DIR=$oldGlobal;$env:ZIG_LOCAL_CACHE_DIR=$oldLocal}
