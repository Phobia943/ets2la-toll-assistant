#requires -Version 7.0
param(
    [ValidateSet('ValidateOnly','Install','Restore')][string]$Mode='ValidateOnly',
    [string]$GamePluginDirectory='D:\SteamLibrary\steamapps\common\Euro Truck Simulator 2\bin\win_x64\plugins',
    [string]$ApplicationDirectory='D:\ETS2LA\current',
    [string]$CandidatePath=(Join-Path $PSScriptRoot 'bin/scs_sdk_controller.candidate.dll'),
    [string]$BackupDirectory,
    [string]$BackupFile
)
$ErrorActionPreference='Stop'
$script:BridgeOriginalHash='D9FBD5ACE5E76EED70B7E10B78C450B111566AD6AA814DFF65D6244AD23E687E'
$script:BridgeCandidateHash='D92DA97344E33DABA0BC80E3811942DEDBA51C1367DDFC240F9EB0377A628B9B'

function Assert-BridgeHash([string]$Path,[string]$Expected,[string]$Code) {
    if(!(Test-Path -LiteralPath $Path -PathType Leaf)) {throw "[$Code] Datei fehlt: $Path"}
    $item=Get-Item -LiteralPath $Path -Force
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw "[REPARSE] Verknüpfte Datei wird nicht verwendet: $Path"}
    $actual=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if($actual -ne $Expected) {throw "[$Code] Unerwartete Dateiidentität: $Path ($actual)"}
}

function Assert-BridgeDirectory([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    $item=Get-Item -LiteralPath $full -Force
    if(!$item.PSIsContainer) {throw "[DIRECTORY] Kein Verzeichnis: $full"}
    # A junction in any ancestor must not silently redirect a replacement.
    for($node=$item;$null -ne $node;$node=$node.Parent) {
        if($node.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw "[REPARSE] Verknüpfter Zielpfad wird nicht verwendet: $($node.FullName)"}
    }
    return $item.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
}

function Read-BridgePeInfo([string]$Path) {
    # Read PE bytes only. Never LoadLibrary / execute a candidate for validation.
    [byte[]]$bytes=[IO.File]::ReadAllBytes($Path)
    function U16([int]$At) {if($At -lt 0 -or $At+2 -gt $bytes.Length){throw '[PE] Abgeschnittene Datei'};[BitConverter]::ToUInt16($bytes,$At)}
    function U32([int]$At) {if($At -lt 0 -or $At+4 -gt $bytes.Length){throw '[PE] Abgeschnittene Datei'};[BitConverter]::ToUInt32($bytes,$At)}
    if($bytes.Length -lt 256 -or (U16 0) -ne 0x5A4D) {throw '[PE] Kein PE-Abbild'}
    [long]$pe=U32 60
    if($pe+24 -gt $bytes.Length -or (U32 $pe) -ne 0x4550 -or (U16 ($pe+4)) -ne 0x8664) {throw '[PE] Erwartet wird eine x64-PE-Datei'}
    $count=U16 ($pe+6);$optionalSize=U16 ($pe+20);$optional=$pe+24
    if($count -lt 1 -or $count -gt 96 -or $optionalSize -lt 120 -or
        (U16 ($pe+22) -band 0x2000) -eq 0 -or (U16 $optional) -ne 0x20B -or (U32 ($optional+108)) -lt 1) {
        throw '[PE] Ungültige x64-DLL-Struktur'
    }
    $sectionStart=$optional+$optionalSize
    if($sectionStart+40*$count -gt $bytes.Length) {throw '[PE] Abgeschnittene Abschnittstabelle'}
    $sections=@(for($i=0;$i -lt $count;$i++) {
        $at=$sectionStart+40*$i
        [pscustomobject]@{VirtualSize=(U32 ($at+8));Rva=(U32 ($at+12));RawSize=(U32 ($at+16));Raw=(U32 ($at+20))}
    })
    $headers=U32 ($optional+60)
    function Offset([long]$Rva,[int]$Length=1) {
        if($Rva -ge 0 -and $Rva+$Length -le $headers -and $Rva+$Length -le $bytes.Length){return [int]$Rva}
        foreach($section in $sections) {
            $delta=$Rva-[long]$section.Rva
            if($delta -ge 0 -and $delta+$Length -le $section.RawSize -and $section.Raw+$delta+$Length -le $bytes.Length) {
                return [int]($section.Raw+$delta)
            }
        }
        throw '[PE] Export verweist außerhalb der Datei'
    }
    [long]$exportRva=U32 ($optional+112);[long]$exportSize=U32 ($optional+116)
    if($exportRva -eq 0 -or $exportSize -lt 40) {throw '[PE] Exportverzeichnis fehlt'}
    $export=Offset $exportRva 40
    $functionCount=U32 ($export+20);$nameCount=U32 ($export+24)
    if($functionCount -ne 2 -or $nameCount -ne 2) {throw '[PE_EXPORTS] Erwartet werden genau zwei benannte SCS-Exporte'}
    $functions=Offset (U32 ($export+28)) 8
    $names=Offset (U32 ($export+32)) 8
    $ordinals=Offset (U32 ($export+36)) 4
    $found=@(for($i=0;$i -lt 2;$i++) {
        $ordinal=U16 ($ordinals+2*$i)
        if($ordinal -ge 2){throw '[PE_EXPORTS] Ungültiger Exportindex'}
        [long]$functionRva=U32 ($functions+4*$ordinal)
        if($functionRva -eq 0 -or ($functionRva -ge $exportRva -and $functionRva -lt $exportRva+$exportSize)) {throw '[PE_EXPORTS] Leerer oder weitergeleiteter Export'}
        $null=Offset $functionRva
        $start=Offset (U32 ($names+4*$i));$end=$start
        while($end -lt $bytes.Length -and $end-$start -lt 128 -and $bytes[$end] -ne 0){$end++}
        if($end -ge $bytes.Length -or $bytes[$end] -ne 0){throw '[PE_EXPORTS] Ungültiger Exportname'}
        [Text.Encoding]::ASCII.GetString($bytes,$start,$end-$start)
    })
    if((($found | Sort-Object) -join ',') -ne 'scs_input_init,scs_input_shutdown') {throw '[PE_EXPORTS] Unerwartete Exportnamen'}
    $ascii=[Text.Encoding]::ASCII.GetString($bytes)
    if($ascii.Contains('MautInputBridgeTest') -or $ascii.Contains('UNSAFE-NEGATIVE-TEST')) {throw '[TEST_BINARY] Testdateien sind nicht installierbar'}
    if(!$ascii.Contains('Local\SCSControls') -or !$ascii.Contains('Local\MautInputBridgeStatus_v1')) {throw '[PE_PROTOCOL] Erwartete Produktkanäle fehlen'}
    [pscustomobject]@{Machine='x64';Exports=$found;Bytes=$bytes.Length}
}

function Read-BridgeProcessSnapshot {
    @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop | Select-Object ProcessId,Name,ExecutablePath,CommandLine)
}

function Assert-BridgeAppsClosed([string]$ApplicationDirectory,[string]$GamePluginDirectory) {
    try {$processes=@(Read-BridgeProcessSnapshot)} catch {throw "[PROCESS_QUERY] Prozessprüfung nicht möglich: $($_.Exception.Message)"}
    if($processes.Count -eq 0) {throw '[PROCESS_QUERY] Leere Prozessliste ist kein Nachweis geschlossener Anwendungen'}
    $appRoot=[IO.Path]::GetFullPath($ApplicationDirectory).TrimEnd('\')+'\'
    $gameRoot=[IO.Directory]::GetParent($GamePluginDirectory).FullName.TrimEnd('\')+'\'
    $blocked=@($processes | Where-Object {
        $_.Name -match '(?i)ETS2LA|LaneAssist|eurotrucks2|amtrucks|TruckersMP' -or
        ($_.ExecutablePath -and ($_.ExecutablePath.StartsWith($appRoot,[StringComparison]::OrdinalIgnoreCase) -or
            ($_.ExecutablePath.StartsWith($gameRoot,[StringComparison]::OrdinalIgnoreCase) -and $_.Name -match '(?i)truck'))) -or
        ($_.Name -match '^(?i)(dotnet|python|pythonw)\.exe$' -and $_.CommandLine -and
            $_.CommandLine.IndexOf($appRoot,[StringComparison]::OrdinalIgnoreCase) -ge 0)
    })
    if($blocked.Count) {throw '[PROCESS_RUNNING] Spiel/TruckersMP und ETS2LA müssen vollständig geschlossen sein: '+(($blocked | ForEach-Object {"$($_.Name) PID $($_.ProcessId)"}) -join ', ')}
}

function Assert-NoBridgeDuplicate([string]$Directory,[string]$Target) {
    foreach($file in Get-ChildItem -LiteralPath $Directory -Filter '*.dll' -File -Force) {
        if([string]::Equals($file.FullName,$Target,[StringComparison]::OrdinalIgnoreCase)){continue}
        $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if($hash -in @($script:BridgeOriginalHash,$script:BridgeCandidateHash)) {throw "[DUPLICATE] Weitere Controller-DLL im Pluginordner: $($file.Name)"}
    }
}

function Invoke-BridgeInstall {
    param([ValidateSet('ValidateOnly','Install','Restore')][string]$Mode='ValidateOnly',
        [string]$GamePluginDirectory,[string]$ApplicationDirectory,[string]$CandidatePath,
        [string]$BackupDirectory,[string]$BackupFile)
    $directory=Assert-BridgeDirectory $GamePluginDirectory
    $target=Join-Path $directory 'scs_sdk_controller.dll'
    if($Mode -eq 'Restore') {
        if(!$BackupFile){throw '[BACKUP_REQUIRED] Restore benötigt das ausdrücklich ausgewählte Originalbackup'}
        $source=[IO.Path]::GetFullPath($BackupFile)
        Assert-BridgeHash $source $script:BridgeOriginalHash 'BACKUP_HASH'
        Assert-BridgeHash $target $script:BridgeCandidateHash 'RESTORE_TARGET_HASH'
        $expectedSource=$script:BridgeOriginalHash;$expectedTarget=$script:BridgeCandidateHash
    } else {
        $source=[IO.Path]::GetFullPath($CandidatePath)
        Assert-BridgeHash $source $script:BridgeCandidateHash 'PACKAGE_HASH'
        $null=Read-BridgePeInfo $source
        Assert-BridgeHash $target $script:BridgeOriginalHash 'ORIGINAL_HASH'
        $expectedSource=$script:BridgeCandidateHash;$expectedTarget=$script:BridgeOriginalHash
    }
    if([string]::Equals($source,$target,[StringComparison]::OrdinalIgnoreCase)){throw '[SOURCE_TARGET] Quelle und Ziel dürfen nicht identisch sein'}
    Assert-NoBridgeDuplicate $directory $target
    Assert-BridgeAppsClosed $ApplicationDirectory $directory
    if($Mode -eq 'ValidateOnly') {
        return [pscustomobject]@{Mode='ValidateOnly';Changed=$false;Target=$target;OriginalSha256=$expectedTarget;CandidateSha256=$expectedSource;Result='PASS - keine Datei verändert'}
    }
    if(!$BackupDirectory){$BackupDirectory=Join-Path $directory 'MautInputBridgeBackups'}
    $backupRoot=[IO.Path]::GetFullPath($BackupDirectory)
    # Validate the existing parent before creating one immediate backup directory.
    if(!(Test-Path -LiteralPath $backupRoot)) {
        $null=Assert-BridgeDirectory ([IO.Directory]::GetParent($backupRoot).FullName)
        $null=New-Item -ItemType Directory -Path $backupRoot
    }
    $backupRoot=Assert-BridgeDirectory $backupRoot
    $id=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N')
    $saved=Join-Path $backupRoot ('scs_sdk_controller.'+$Mode+'.'+$id+'.bin')
    $pending=Join-Path $directory ('.maut-inputbridge-'+$id+'.pending')
    $committed=$false
    try {
        [IO.File]::Copy($target,$saved,$false)
        $backupStream=[IO.File]::Open($saved,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Read)
        try {$backupStream.Flush($true)} finally {$backupStream.Dispose()}
        Assert-BridgeHash $saved $expectedTarget 'BACKUP_HASH'
        [IO.File]::Copy($source,$pending,$false)
        $pendingStream=[IO.File]::Open($pending,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Read)
        try {$pendingStream.Flush($true)} finally {$pendingStream.Dispose()}
        Assert-BridgeHash $pending $expectedSource 'STAGED_HASH'
        Assert-BridgeHash $target $expectedTarget 'TARGET_CHANGED'
        Assert-BridgeHash $saved $expectedTarget 'BACKUP_HASH'
        Assert-NoBridgeDuplicate $directory $target
        # Final external-state check immediately before atomic same-volume replace.
        Assert-BridgeAppsClosed $ApplicationDirectory $directory
        [IO.File]::Replace($pending,$target,[NullString]::Value)
        $committed=$true
        Assert-BridgeHash $target $expectedSource 'POST_REPLACE_HASH'
        Assert-BridgeHash $saved $expectedTarget 'BACKUP_HASH'
        [pscustomobject]@{Mode=$Mode;Changed=$true;Target=$target;InstalledSha256=$expectedSource;Backup=$saved;BackupSha256=$expectedTarget;Result='PASS'}
    } catch {
        if($committed){throw "[POST_COMMIT_FAILURE] Austausch erfolgte; Nachprüfung fehlgeschlagen. Keine automatische weitere Änderung. Sicherung: $saved. $($_.Exception.Message)"}
        throw
    } finally {
        # Only this invocation's exact .pending file; never recurse or remove DLLs.
        if([IO.File]::Exists($pending)){[IO.File]::Delete($pending)}
    }
}

# Dot-sourcing exposes helpers solely for tests; direct execution defaults to read-only.
if($MyInvocation.InvocationName -ne '.') {
    Invoke-BridgeInstall -Mode $Mode -GamePluginDirectory $GamePluginDirectory -ApplicationDirectory $ApplicationDirectory `
        -CandidatePath $CandidatePath -BackupDirectory $BackupDirectory -BackupFile $BackupFile
}
