#requires -Version 7.0
param([Parameter(Mandatory)][string]$SdkSource)
$ErrorActionPreference='Stop'
$sdk=[IO.Path]::GetFullPath($SdkSource)
$core=Join-Path $sdk 'src/core.cpp'
$traffic=Join-Path $sdk 'src/processing/traffic.cpp'
$ffb=Join-Path $sdk 'src/processing/ffb/ffb_processor.win32.cpp'
foreach($entry in @(
 @($core,'F3C23F93AD9323507B0CAAFAF68F7D398F261E469857870075A7E2CAF8B867BD'),
 @($traffic,'3DFBDB876AAC6492BDC9190D3CC79CBDD0ACECC3D695C69D906D18F47BD2BA31'),
 @($ffb,'26D6051C20BB0E4E100E08A76981F436391C648111BC83082EC84313B9979F19'))){
 if((Get-FileHash -LiteralPath $entry[0]).Hash -ne $entry[1]){throw "Pinned SDK input changed: $($entry[0])"}
}
$text=[IO.File]::ReadAllText($core).Replace("`r`n","`n")
$text=$text.Replace('#include "processing/traffic.hpp"',"#include `"processing/traffic.hpp`"`n#include `"maut_scene/SceneSnapshot.hpp`"")
$text=$text.Replace('void CCore::tick() const'+"`n    {",'void CCore::tick() const'+"`n    {`n        maut_scene::Tick(this->truck_pos);")
$text=$text.Replace('telemetry_store_dplacement,' ,'maut_scene_store_dplacement,')
$needle='SCSAPI_VOID telemetry_tick('
$insert=@'
SCSAPI_VOID maut_scene_store_dplacement(const scs_string_t name,const scs_u32_t index,const scs_value_t* const value,const scs_context_t context)
    {
        telemetry_store_dplacement(name,index,value,context);
        maut_scene::WorldPlacementSeen();
    }

    SCSAPI_VOID telemetry_tick(
'@
if(-not $text.Contains($needle)){throw 'Missing callback anchor'}
$text=$text.Replace($needle,$insert)
$anchor='if (this->init_params_->register_for_event(SCS_TELEMETRY_EVENT_frame_end, telemetry_tick, nullptr) != SCS_RESULT_ok)'
$text=$text.Replace($anchor,"maut_scene::Initialize(this->init_params_);`n`n        "+$anchor)
$text=$text.Replace('void CCore::destroy()'+"`n    {",'void CCore::destroy()'+"`n    {`n        maut_scene::Shutdown();")
if(-not $text.Contains('maut_scene::Tick(this->truck_pos);') -or -not $text.Contains('maut_scene::Shutdown();')){throw 'Pinned SDK newline/anchor mismatch'}
[IO.File]::WriteAllText($core,$text,[Text.UTF8Encoding]::new($false))
$text=[IO.File]::ReadAllText($traffic).Replace("`r`n","`n")
$text=$text.Replace("auto slave_trailer = traffic_actor->slave_trailer;`n            while (slave_trailer != nullptr)","auto slave_trailer = traffic_actor->slave_trailer;`n            std::unordered_set<const prism::traffic_parked_trailer_t*> seen_trailers;`n            while (slave_trailer != nullptr && seen_trailers.size() < 32 && seen_trailers.insert(slave_trailer).second)")
$needle="slave_trailer->placement.get_distance_to(this->truck_pos),`n                        traffic_actor"
if(-not $text.Contains($needle)){throw 'Missing legacy trailer pointer anchor'}
$text=$text.Replace($needle,"slave_trailer->placement.get_distance_to(this->truck_pos),`n                        static_cast<const prism::traffic_actor_t*>(slave_trailer)")
[IO.File]::WriteAllText($traffic,$text,[Text.UTF8Encoding]::new($false))
$text=[IO.File]::ReadAllText($ffb).Replace("`r`n","`n")
$text="#include `"maut_scene/SceneSnapshot.hpp`"`n"+$text
$anchor='static_cast<FfbProcessor*>(context)->on_drive_state(event == SCS_TELEMETRY_EVENT_started);'
if(-not $text.Contains($anchor)){throw 'Missing original FFB drive callback'}
$text=$text.Replace('(void)event_info;',"(void)event_info;`n            maut_scene::DriveEvent(event);")
$pause='init_params->register_for_event(SCS_TELEMETRY_EVENT_paused, telemetry_on_drive_state, this);'
$started='init_params->register_for_event(SCS_TELEMETRY_EVENT_started, telemetry_on_drive_state, this);'
if(-not $text.Contains($pause) -or -not $text.Contains($started)){throw 'Missing FFB event registrations'}
$text=$text.Replace($pause,'const bool maut_pause_ok = '+$pause.TrimEnd(';')+' == SCS_RESULT_ok;')
$text=$text.Replace($started,'const bool maut_started_ok = '+$started.TrimEnd(';')+" == SCS_RESULT_ok;`n            maut_scene::DriveCallbackRegistration(maut_pause_ok && maut_started_ok);")
[IO.File]::WriteAllText($ffb,$text,[Text.UTF8Encoding]::new($false))
$destination=Join-Path $sdk 'src/maut_scene'
New-Item -ItemType Directory -Path $destination -Force|Out-Null
foreach($name in @('Protocol.h','SceneCollector.hpp','SceneSnapshot.hpp','SceneSnapshot.cpp')){
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destination $name)
}
Write-Output "Applied MautScene v1 to pinned SDK in $sdk"
