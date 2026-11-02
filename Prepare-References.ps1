param([string]$EtsRoot = $env:EtsRoot)
$ErrorActionPreference = 'Stop'
if (!$EtsRoot) { throw 'EtsRoot auf den ETS2LA-Installationsordner setzen oder -EtsRoot angeben.' }
$referenceDir = Join-Path $PSScriptRoot 'References'
$required = @(
    @{ Relative='Libraries\tumppi066.pathlib\PathLib.dll'; Hash='156EA9783D61758031F89561F8B1F793331CEE3625673C5BB16F45B2A0F6E7B7' },
    @{ Relative='Libraries\tumppi066.pidlib\PIDLib.dll'; Hash='06BBA8430B2D6F3A88E2DCCC203EF19091604DAC11B46A58364B906F57604187' },
    @{ Relative='Plugins\tumppi066.adaptivecruisecontrol\AdaptiveCruiseControl.dll'; Hash='A2C6DE279A2881AFDA40E5F4C336AEB7068E8F04ACD069DA06F6C36FAB4818B4' }
)
foreach ($dependency in $required) {
    $sourceFile = Join-Path $EtsRoot $dependency.Relative
    if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $dependency.Hash) {
        throw "Andere Plugin-Version: $sourceFile. Kompatibilitaet zuerst pruefen."
    }
}
New-Item -ItemType Directory -Path $referenceDir -Force | Out-Null
foreach ($dependency in $required) {
    Copy-Item -LiteralPath (Join-Path $EtsRoot $dependency.Relative) -Destination $referenceDir -Force
}
Write-Output 'Passende Bibliotheken fuer den lokalen Build vorbereitet. Keine ETS2LA-Dateien geaendert.'
