#requires -Version 7.0
param(
    [string]$OriginalDll='D:\SteamLibrary\steamapps\common\Euro Truck Simulator 2\bin\win_x64\plugins\scs_sdk_controller.dll',
    [string]$CandidateDll=(Join-Path $PSScriptRoot 'bin/scs_sdk_controller.candidate.dll')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Install.ps1')
# These tests never enumerate/control real applications and never load a DLL.
# The only live-path read is the pinned original fixture, copied to this workspace.
$script:ProcessReads=0
$script:ProcessRows=@([pscustomobject]@{ProcessId=1;Name='fixture-system.exe';ExecutablePath='C:\fixture-system.exe';CommandLine=''})
$script:SecondReadStartsGame=$false
$script:FailProcessRead=$false
function Read-BridgeProcessSnapshot {
    $script:ProcessReads++
    if($script:FailProcessRead){throw 'simulated process query failure'}
    if($script:SecondReadStartsGame -and $script:ProcessReads -ge 2) {
        return @([pscustomobject]@{ProcessId=22;Name='eurotrucks2.exe';ExecutablePath=$null;CommandLine=$null})
    }
    return $script:ProcessRows
}
$script:Assertions=0
function Check([bool]$Condition,[string]$Description) {
    if(!$Condition){throw "FAIL $Description"}
    $script:Assertions++;Write-Host "PASS $Description"
}
function Reject([scriptblock]$Action,[string]$Code,[string]$Description) {
    try {& $Action | Out-Null} catch {
        if($_.Exception.Message.Contains("[$Code]")) {$script:Assertions++;Write-Host "PASS $Description";return}
        throw
    }
    throw "FAIL expected [$Code]: $Description"
}
Assert-BridgeHash $OriginalDll $script:BridgeOriginalHash 'FIXTURE_ORIGINAL'
Assert-BridgeHash $CandidateDll $script:BridgeCandidateHash 'FIXTURE_CANDIDATE'
$suiteRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'bin/installer-tests'))
$root=Join-Path $suiteRoot ([Guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $root -Force
try {
    $plugins=Join-Path $root 'plugins';$null=New-Item -ItemType Directory -Path $plugins
    $target=Join-Path $plugins 'scs_sdk_controller.dll'
    $candidate=Join-Path $root 'candidate.bin'
    [IO.File]::Copy([IO.Path]::GetFullPath($OriginalDll),$target)
    [IO.File]::Copy([IO.Path]::GetFullPath($CandidateDll),$candidate)
    $arguments=@{GamePluginDirectory=$plugins;ApplicationDirectory=(Join-Path $root 'ETS2LA');CandidatePath=$candidate}
    $before=@(Get-ChildItem -LiteralPath $root -Recurse -File).Count
    $validation=Invoke-BridgeInstall @arguments
    Check ($validation.Mode -eq 'ValidateOnly' -and !$validation.Changed) 'Default invocation validates without applying'
    Check (@(Get-ChildItem -LiteralPath $root -Recurse -File).Count -eq $before) 'Validation creates no backup, staging or output file'
    Assert-BridgeHash $target $script:BridgeOriginalHash 'TEST_TARGET'
    $pe=Read-BridgePeInfo $candidate
    Check ($pe.Machine -eq 'x64' -and $pe.Exports.Count -eq 2) 'Actual candidate PE has exactly two required exports'
    $bad=Join-Path $root 'bad.bin';[IO.File]::WriteAllText($bad,'invalid')
    Reject {Invoke-BridgeInstall @arguments -CandidatePath $bad} 'PACKAGE_HASH' 'Wrong candidate rejected before any target change'
    Reject {Read-BridgePeInfo $bad} 'PE' 'Non-PE content rejected'
    Reject {Read-BridgePeInfo (Join-Path $PSScriptRoot 'bin/InputBridge.Test.dll')} 'PE_EXPORTS' 'Protected test DLL rejected by export contract'
    Reject {Read-BridgePeInfo (Join-Path $PSScriptRoot 'bin/InputBridge.UNSAFE-NEGATIVE-TEST.dll')} 'PE_EXPORTS' 'Intentionally unsafe test DLL rejected by export contract'
    $script:ProcessRows=@([pscustomobject]@{ProcessId=20;Name='ETS2LA.exe';ExecutablePath=$null;CommandLine=$null})
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'PROCESS_RUNNING' 'Running ETS2LA rejects replacement even without executable path'
    $script:ProcessRows=@([pscustomobject]@{ProcessId=21;Name='eurotrucks2.exe';ExecutablePath=$null;CommandLine=$null})
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'PROCESS_RUNNING' 'Running game rejects replacement'
    $script:ProcessRows=@([pscustomobject]@{ProcessId=22;Name='TruckersMP-Launcher.exe';ExecutablePath=$null;CommandLine=$null})
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'PROCESS_RUNNING' 'TruckersMP launcher rejects replacement'
    $script:ProcessRows=@([pscustomobject]@{ProcessId=23;Name='dotnet.exe';ExecutablePath='C:\dotnet\dotnet.exe';CommandLine='dotnet "'+$arguments.ApplicationDirectory+'\ETS2LA.dll"'})
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'PROCESS_RUNNING' 'Application under dotnet is detected through command line'
    $script:ProcessRows=@()
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'PROCESS_QUERY' 'Empty inventory cannot prove apps closed'
    $script:FailProcessRead=$true
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'PROCESS_QUERY' 'Failed process query rejects replacement'
    $script:FailProcessRead=$false
    $script:ProcessRows=@([pscustomobject]@{ProcessId=1;Name='fixture-system.exe';ExecutablePath='C:\fixture-system.exe';CommandLine=''})
    $duplicate=Join-Path $plugins 'extra-controller.dll';[IO.File]::Copy($candidate,$duplicate)
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'DUPLICATE' 'Second controller DLL beside original rejected'
    [IO.File]::Delete($duplicate)
    [IO.File]::WriteAllText($target,'unrecognized target')
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'ORIGINAL_HASH' 'Unrecognized original target rejected'
    Reject {Invoke-BridgeInstall @arguments -Mode Restore -BackupFile $OriginalDll} 'RESTORE_TARGET_HASH' 'Restore rejects an unknown current target'
    [IO.File]::Copy([IO.Path]::GetFullPath($OriginalDll),$target,$true)
    $script:ProcessReads=0;$script:SecondReadStartsGame=$true
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'PROCESS_RUNNING' 'Second process check catches game started during preparation'
    Check ($script:ProcessReads -eq 2) 'Two distinct inventories performed before attempted exchange'
    Assert-BridgeHash $target $script:BridgeOriginalHash 'TEST_TARGET'
    Check (@(Get-ChildItem -LiteralPath $plugins -Filter '*.pending' -Force).Count -eq 0) 'Rejected exchange leaves no pending input library'
    $script:SecondReadStartsGame=$false;$script:ProcessReads=0
    # Positive round trip is confined to the freshly created GUID fixture tree.
    $installed=Invoke-BridgeInstall @arguments -Mode Install
    Check ($installed.Changed -and $script:ProcessReads -eq 2) 'Temporary fixture installs only after two closed-app inventories'
    Assert-BridgeHash $target $script:BridgeCandidateHash 'TEST_TARGET'
    Assert-BridgeHash $installed.Backup $script:BridgeOriginalHash 'TEST_BACKUP'
    Check ([IO.Path]::GetExtension($installed.Backup) -eq '.bin' -and @(Get-ChildItem -LiteralPath $plugins -Filter '*.dll' -File).Count -eq 1) 'Verified backup creates no second loadable plugin DLL'
    Reject {Invoke-BridgeInstall @arguments -Mode Install} 'ORIGINAL_HASH' 'Already installed candidate cannot be overwritten as original'
    Reject {Invoke-BridgeInstall @arguments -Mode Restore} 'BACKUP_REQUIRED' 'Restore requires explicit backup selection'
    Reject {Invoke-BridgeInstall @arguments -Mode Restore -BackupFile $bad} 'BACKUP_HASH' 'Corrupted restore backup rejected'
    $script:ProcessReads=0
    $restored=Invoke-BridgeInstall @arguments -Mode Restore -BackupFile $installed.Backup
    Check ($restored.Changed -and $script:ProcessReads -eq 2) 'Known candidate restores pinned original only after two inventories'
    Assert-BridgeHash $target $script:BridgeOriginalHash 'TEST_TARGET'
    Assert-BridgeHash $installed.Backup $script:BridgeOriginalHash 'TEST_BACKUP'
    Assert-BridgeHash $restored.Backup $script:BridgeCandidateHash 'TEST_BACKUP'
    Check (@(Get-ChildItem -LiteralPath $plugins -Filter '*.dll' -File).Count -eq 1) 'Round trip retains one DLL and both verified backup identities'
    Write-Host "$script:Assertions installer checks passed. Only GUID fixture paths were modified; no DLL loaded or live installation performed."
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if(!$resolved.StartsWith($suiteRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^[0-9a-f]{32}$') {throw 'Refusing cleanup outside exact installer test workspace'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
