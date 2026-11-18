#requires -Version 7.0
param([Parameter(Mandatory)][string]$SdkSource)
$ErrorActionPreference='Stop'
$core=Join-Path ([IO.Path]::GetFullPath($SdkSource)) 'src/core.cpp'
$text=[IO.File]::ReadAllText($core).Replace("`r`n","`n")
if($text.Contains('maut_pedal_probe')){throw 'Pedal probe already applied; use a fresh pinned source build'}
function Replace-Once([string]$from,[string]$to) {
 if(([regex]::Matches($script:text,[regex]::Escape($from))).Count -ne 1){throw 'Pinned SDK pedal observer anchor is missing or ambiguous'}
 $script:text=$script:text.Replace($from,$to)
}
Replace-Once '#include "maut_scene/SceneSnapshot.hpp"' ('#include "maut_scene/SceneSnapshot.hpp"'+"`n"+'#include "maut_pedal_probe/Probe.hpp"')
Replace-Once 'maut_scene::Initialize(this->init_params_);' ('maut_scene::Initialize(this->init_params_);'+"`n        maut_pedal_probe::Initialize();")
Replace-Once 'maut_scene::Shutdown();' ("maut_pedal_probe::Shutdown();`n        maut_scene::Shutdown();")
Replace-Once '                if (custom_acceleration > 0)' ("                const auto maut_pedal_sample = maut_pedal_probe::Begin(custom_acceleration, acceleration_timestamp, throttle_inp->get_value(), brake_inp->get_value());`n                if (custom_acceleration > 0)")
$anchor=@'
                    brake_inp->set_value(-custom_acceleration);
                }
'@
Replace-Once ($anchor.Replace("`r`n","`n")) ($anchor.Replace("`r`n","`n")+"`n                maut_pedal_probe::Finish(maut_pedal_sample, throttle_inp->get_value(), brake_inp->get_value());")
$destination=Join-Path ([IO.Path]::GetFullPath($SdkSource)) 'src/maut_pedal_probe'
if(Test-Path -LiteralPath $destination){throw 'Probe directory already exists'}
New-Item -ItemType Directory -Path $destination|Out-Null
foreach($name in @('Probe.hpp','Probe.cpp','Protocol.h')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destination $name)}
[IO.File]::WriteAllText($core,$text,[Text.UTF8Encoding]::new($false))
Write-Output 'Added optional read-only pedal write observation; control behavior unchanged.'
