#requires -Version 7.0
param(
    [string]$Zig=(Join-Path $PSScriptRoot '../../../toll-research/tools/zig-x86_64-windows-0.15.2/zig.exe'),
    [string]$SdkArchive=(Join-Path $PSScriptRoot '../../../toll-research/background-official-20261118/scs_sdk_1_16.zip'),
    [string]$OutputDirectory=(Join-Path $PSScriptRoot 'bin'),
    [string]$CacheDirectory=(Join-Path $PSScriptRoot '../../../../../../.inputbridge-zig-cache'),
    [switch]$Test
)
$ErrorActionPreference='Stop'
$sdkPin='49821E25568F4D7E462135F577F8FEF2AB197326404F2C1DAEF139B8F3DC5E69'
if(!(Test-Path -LiteralPath $SdkArchive) -or (Get-FileHash -LiteralPath $SdkArchive).Hash -ne $sdkPin) {
    throw 'Expected pinned official SCS SDK 1.16 archive. No automatic download or SDK substitution.'
}
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$sdk=Join-Path $out 'sdk'
if(!(Test-Path -LiteralPath (Join-Path $sdk 'include/scssdk_input.h'))) {
    [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($SdkArchive),$sdk,$true)
}
# Always restore header bytes from the pinned archive, including after an old build.
$archive=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($SdkArchive))
try {
    foreach($entry in $archive.Entries | Where-Object { $_.FullName -match '^include/[^/]+\.h$' }) {
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $sdk $entry.FullName),$true)
    }
} finally {$archive.Dispose()}
$previousGlobal=$env:ZIG_GLOBAL_CACHE_DIR
$previousLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
    # A short dedicated cache avoids Win32 path limits in Zig's CRT sub-build.
    $cache=[IO.Path]::GetFullPath($CacheDirectory)
    $env:ZIG_GLOBAL_CACHE_DIR=Join-Path $cache 'global'
    $env:ZIG_LOCAL_CACHE_DIR=Join-Path $cache 'local'
    # No C++ standard-library use; cc still compiles .cpp as C++ without libc++.
    $flags=@('cc','-target','x86_64-windows-gnu','-std=c++17','-fno-exceptions','-fno-rtti','-O2','-Wall','-Wextra','-Werror',"-I$(Join-Path $sdk 'include')")
    & $Zig @flags -shared (Join-Path $PSScriptRoot 'Bridge.cpp') (Join-Path $PSScriptRoot 'Bridge.def') -o (Join-Path $out 'scs_sdk_controller.candidate.dll')
    if($LASTEXITCODE){throw 'Isolated bridge build failed'}
    if($Test) {
        $testDefine='-DMAUT_INPUT_TEST_ID=run_'+[Guid]::NewGuid().ToString('N')
        & $Zig @flags -shared $testDefine (Join-Path $PSScriptRoot 'Bridge.cpp') (Join-Path $PSScriptRoot 'Bridge.def') -o (Join-Path $out 'InputBridge.Test.dll')
        if($LASTEXITCODE){throw 'Test-only bridge build failed'}
        & $Zig @flags $testDefine (Join-Path $PSScriptRoot 'FakeHostTest.cpp') -o (Join-Path $out 'InputBridgeTest.exe')
        if($LASTEXITCODE){throw 'Fake SDK host build failed'}
        & (Join-Path $out 'InputBridgeTest.exe') (Join-Path $out 'InputBridge.Test.dll') (Join-Path $out 'diagnostic-fixture.bin')
        if($LASTEXITCODE){throw 'Input bridge assertions failed'}
        & python (Join-Path $PSScriptRoot 'TestStatus.py') (Join-Path $out 'diagnostic-fixture.bin')
        if($LASTEXITCODE){throw 'Native diagnostic reader contract failed'}
        # Intentionally broken writer synchronization, compiled ONLY together
        # with a new GUID test namespace. Never name/package this as a candidate.
        $negativeDefine='-DMAUT_INPUT_TEST_ID=negative_'+[Guid]::NewGuid().ToString('N')
        $unsafeDefine='-DMAUT_INPUT_TEST_UNSERIALIZED=1'
        & $Zig @flags -shared $negativeDefine $unsafeDefine (Join-Path $PSScriptRoot 'Bridge.cpp') (Join-Path $PSScriptRoot 'Bridge.def') -o (Join-Path $out 'InputBridge.UNSAFE-NEGATIVE-TEST.dll')
        if($LASTEXITCODE){throw 'Negative test-only bridge build failed'}
        & $Zig @flags $negativeDefine $unsafeDefine (Join-Path $PSScriptRoot 'FakeHostTest.cpp') -o (Join-Path $out 'InputBridgeNegativeTest.exe')
        if($LASTEXITCODE){throw 'Negative fake SDK host build failed'}
        & (Join-Path $out 'InputBridgeNegativeTest.exe') (Join-Path $out 'InputBridge.UNSAFE-NEGATIVE-TEST.dll') (Join-Path $out 'negative-diagnostic-fixture.bin')
        if($LASTEXITCODE){throw 'Negative control failed to reproduce the original writer race'}
    }
    Get-FileHash -LiteralPath (Join-Path $out 'scs_sdk_controller.candidate.dll') | Select-Object Hash,Path
} finally {
    $env:ZIG_GLOBAL_CACHE_DIR=$previousGlobal
    $env:ZIG_LOCAL_CACHE_DIR=$previousLocal
}
