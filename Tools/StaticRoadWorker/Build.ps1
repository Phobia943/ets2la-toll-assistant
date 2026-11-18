param([string]$EtsRoot='D:\ETS2LA\current')
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot 'StaticRoadWorker.csproj'
& dotnet build $project -c Release --nologo "-p:EtsRoot=$EtsRoot"
if($LASTEXITCODE -ne 0){throw 'Static scene worker build failed'}
$bundle=Join-Path $PSScriptRoot 'bin\Release\net10.0'
$manifest=[ordered]@{}
$expected=@('GisDeflate.dll','MautStaticRoadWorker.deps.json','MautStaticRoadWorker.dll','MautStaticRoadWorker.exe',
    'MautStaticRoadWorker.runtimeconfig.json','RBush.dll','Sprache.dll','System.IO.Hashing.dll',
    'TruckLib.Core.dll','TruckLib.dll','TruckLib.HashFs.dll','TruckLib.Models.dll','TruckLib.Sii.dll')
$runtime=@(Get-ChildItem -LiteralPath $bundle -File | Where-Object { $_.Extension -in '.exe','.dll','.json' })
if(@(Compare-Object ($runtime.Name|Sort-Object) ($expected|Sort-Object)).Count -ne 0){throw 'Unexpected/missing worker runtime files; use a clean build output and inspect the dependency change.'}
$runtime | Sort-Object Name | ForEach-Object {
    $manifest[$_.Name]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$target=Join-Path $PSScriptRoot '..\..\Plugin\StaticRoadWorkerManifest.json'
[IO.File]::WriteAllText([IO.Path]::GetFullPath($target),($manifest|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output "Read-only worker bundle built; pinned manifest written. Rebuild the plugin before packaging $bundle."
