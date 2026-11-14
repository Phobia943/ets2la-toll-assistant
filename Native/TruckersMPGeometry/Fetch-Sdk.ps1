#requires -Version 7.0
param([string]$SdkRoot=(Join-Path $PSScriptRoot '../../References/TruckersMP'))
$ErrorActionPreference='Stop'
$commit='bee3b9e2b314c2b47fee9f94635e9945a71924e1'
$headers=@{
    'TruckersMP.h'='F70CB0D86D7485388676AF8F46B8E5B573BB4E598802D66982F41D1D3B02F679'
    'TruckersMP_Base.h'='2C877A0E6130C4CAA0076914D9CAC970183FA8206A8403CF9788EB9BACCD075C'
}
$destination=Join-Path $SdkRoot 'include/TruckersMP'
New-Item -ItemType Directory -Path $destination -Force|Out-Null
foreach($name in $headers.Keys) {
    $path=Join-Path $destination $name
    if((Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path).Hash -eq $headers[$name]) {continue}
    $temporary=Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
    try {
        Invoke-WebRequest "https://raw.githubusercontent.com/TruckersMP/GameClientSDK/$commit/include/TruckersMP/$name" -OutFile $temporary
        if((Get-FileHash -LiteralPath $temporary).Hash -ne $headers[$name]) {throw "SDK hash mismatch: $name"}
        Copy-Item -LiteralPath $temporary -Destination $path -Force
    } finally {if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
}
Write-Output "Verified official SDK headers at $commit. No game files changed."
