function Read-TruckPowertrainBuffer {
    param([Parameter(Mandatory)][byte[]]$Bytes)
    if($Bytes.Length -lt 988 -or [BitConverter]::ToUInt32($Bytes,40) -ne 12){throw 'Nicht unterstütztes Telemetrieformat für den Antriebsstrang.'}
    # Pinned revision-12 GameTelemetry reader: signed TruckInt gear504/508;
    # TruckFloat starts948, userClutch968 and simulation gameClutch984.
    # These inputs include virtual controls; neither is a physical SDL axis.
    $issues=[Collections.Generic.List[string]]::new()
    $received=[BitConverter]::ToSingle($Bytes,968);$effective=[BitConverter]::ToSingle($Bytes,984)
    $userClutch=if([single]::IsFinite($received) -and $received -ge 0 -and $received -le 1){$received}else{
        $issues.Add('InvalidClutch:userClutch');$null
    }
    $gameClutch=if([single]::IsFinite($effective) -and $effective -ge 0 -and $effective -le 1){$effective}else{
        $issues.Add('InvalidClutch:gameClutch');$null
    }
    [pscustomobject]@{gear=[BitConverter]::ToInt32($Bytes,504);gearDashboard=[BitConverter]::ToInt32($Bytes,508);
        userClutch=$userClutch;gameClutch=$gameClutch;issues=$issues.ToArray()}
}
function Read-TollGateBuffer {
    param([Parameter(Mandatory)][byte[]]$Bytes)
    if($Bytes.Length -ne 1920){throw 'Die Schrankendaten müssen exakt 1920 Byte enthalten.'}
    $issues=[Collections.Generic.List[string]]::new()
    $gates=@(for($i=0;$i -lt 40;$i++) {
        $o=$i*48
        if([BitConverter]::ToInt32($Bytes,$o+32) -ne 2){continue}
        # The native world coordinate uses Vector3/float arithmetic. Keep its
        # rounding before formatting; double arithmetic can change the gate key.
        $x=[single]([BitConverter]::ToSingle($Bytes,$o)+512*[BitConverter]::ToInt16($Bytes,$o+12))
        $y=[BitConverter]::ToSingle($Bytes,$o+4)
        $z=[single]([BitConverter]::ToSingle($Bytes,$o+8)+512*[BitConverter]::ToInt16($Bytes,$o+14))
        $state=[BitConverter]::ToInt32($Bytes,$o+40);$id=[BitConverter]::ToInt32($Bytes,$o+44)
        # Pinned SDK SemaphoreObject is 48 bytes: type 32, time 36, state 40,
        # id 44. Type 1 light bits (including green=8/sleep=32) are NOT gate
        # states. Type 2 gate states remain 0 closing/1 closed/2 opening/3 open.
        $badPosition=![single]::IsFinite($x) -or ![single]::IsFinite($y) -or ![single]::IsFinite($z)
        $badState=$state -notin 0..3
        if($badPosition -or $badState) {
            # Preserve the established category for old recorder/analyzer filters.
            # Additional strings survive the recorder's existing issues field;
            # rejected slots cannot silently disappear without diagnostic cause.
            $issues.Add('InvalidGateData')
            if($badState) {
                $issues.Add([string]::Format([Globalization.CultureInfo]::InvariantCulture,
                    'InvalidGateState:slot={0};id={1};rawState={2};rawType=2',$i,$id,$state))
            }
            if($badPosition) {
                # Raw bit patterns are JSON-safe even when floats are NaN/Inf.
                $issues.Add([string]::Format([Globalization.CultureInfo]::InvariantCulture,
                    'InvalidGatePosition:slot={0};id={1};localXBits=0x{2:X8};localYBits=0x{3:X8};localZBits=0x{4:X8};cellX={5};cellZ={6}',
                    $i,$id,[BitConverter]::ToUInt32($Bytes,$o),[BitConverter]::ToUInt32($Bytes,$o+4),
                    [BitConverter]::ToUInt32($Bytes,$o+8),[BitConverter]::ToInt16($Bytes,$o+12),[BitConverter]::ToInt16($Bytes,$o+14)))
            }
            continue
        }
        [ordered]@{key=[string]::Format([Globalization.CultureInfo]::InvariantCulture,'{0}:{1:F1}:{2:F1}',$id,$x,$z);id=$id;x=$x;y=$y;z=$z;state=$state}
    })
    [pscustomobject]@{gates=$gates;issues=$issues.ToArray()}
}
function Resolve-TollGateObservation {
    param([object[]]$Gates,[Parameter(Mandatory)][string]$GateKey,[object]$RememberedAnchor)
    $exact=@($Gates|Where-Object{$_.key -ceq $GateKey})
    $anchor=$RememberedAnchor
    if($null -eq $anchor) {
        if($exact.Count -ne 1){return [pscustomobject]@{state=-1;rawCount=$exact.Count;anchor=$null;source='Unresolved';issue='NoUniqueInitialAnchor'}}
        $first=$exact[0]
        $hasPosition=$null -ne $first.id -and $null -ne $first.x -and $null -ne $first.y -and $null -ne $first.z
        if(!$hasPosition) {
            # Older/synthetic records can prove the exact key only. Never infer
            # neighbouring replicas without an observed three-dimensional anchor.
            return [pscustomobject]@{state=$(if($first.state -in 0..3){$first.state}else{-1});rawCount=1;
                anchor=$null;source='ExactKeyOnly';issue=$(if($first.state -notin 0..3){'InvalidState'}else{$null})}
        }
        if(@($first.x,$first.y,$first.z)|Where-Object{![double]::IsFinite([double]$_)}) {
            return [pscustomobject]@{state=-1;rawCount=0;anchor=$null;source='Unresolved';issue='InvalidAnchor'}
        }
        $encoded=[string]::Format([Globalization.CultureInfo]::InvariantCulture,'{0}:{1:F1}:{2:F1}',$first.id,[single]$first.x,[single]$first.z)
        if($encoded -cne $GateKey){return [pscustomobject]@{state=-1;rawCount=0;anchor=$null;source='Unresolved';issue='InvalidAnchor'}}
        $anchor=[pscustomobject]@{id=$first.id;x=[double]$first.x;y=[double]$first.y;z=[double]$first.z}
    }
    $matched=@($Gates|Where-Object{
        $null -ne $_.id -and $_.id -eq $anchor.id -and
        $null -ne $_.x -and $null -ne $_.y -and $null -ne $_.z -and
        [double]::IsFinite([double]$_.x) -and [double]::IsFinite([double]$_.y) -and [double]::IsFinite([double]$_.z) -and
        (([double]$_.x-$anchor.x)*([double]$_.x-$anchor.x)+([double]$_.y-$anchor.y)*([double]$_.y-$anchor.y)+
         ([double]$_.z-$anchor.z)*([double]$_.z-$anchor.z)) -le 1
    })
    $states=@($matched.state|Sort-Object -Unique)
    $consistent=$matched.Count -gt 0 -and $states.Count -eq 1 -and $states[0] -in 0..3
    [pscustomobject]@{state=$(if($consistent){$states[0]}else{-1});rawCount=$matched.Count;anchor=$anchor;
        source='ObservedRawAnchor';issue=$(if(!$matched.Count){'MissingAnchorGroup'}elseif(!$consistent){'ConflictingOrInvalidStates'}else{$null})}
}
function Read-ModernPedalBuffer {
    param([Parameter(Mandatory)][byte[]]$InputBytes,[Parameter(Mandatory)][byte[]]$StateBytes,
        [Parameter(Mandatory)][double]$UnixSeconds)
    if($InputBytes.Length -ne 26 -or $StateBytes.Length -ne 6){throw 'Die modernen Ein- und Statusdaten müssen exakt 26 beziehungsweise 6 Byte enthalten.'}
    $issues=[Collections.Generic.List[string]]::new()
    $acceleration=[double][BitConverter]::ToSingle($InputBytes,13)
    $stamp=[BitConverter]::ToDouble($InputBytes,18)
    if(![double]::IsFinite($acceleration) -or $acceleration -lt -1 -or $acceleration -gt 1){
        $issues.Add('InvalidModernPedalCommand');$acceleration=$null
    }
    if(![double]::IsFinite($stamp) -or ![double]::IsFinite($UnixSeconds)){
        $issues.Add('InvalidModernPedalTimestamp');$stamp=$null
    }
    if($InputBytes[17] -notin @(0,1) -or $StateBytes[5] -notin @(0,1)){$issues.Add('InvalidModernPedalOverrideFlag')}
    [pscustomobject]@{
        acceleration=$acceleration
        overrideRequested=$(if($InputBytes[17] -in @(0,1)){$InputBytes[17] -eq 1}else{$null})
        timestamp=$stamp;commandAgeSeconds=$(if($null -ne $stamp){$UnixSeconds-$stamp}else{$null})
        pluginVersion=[BitConverter]::ToInt32($StateBytes,0)
        overrideReported=$(if($StateBytes[5] -in @(0,1)){$StateBytes[5] -eq 1}else{$null})
        atomicSnapshot=$false;issues=$issues.ToArray()
    }
}
function Read-TruckDamageBuffer {
    param([Parameter(Mandatory)][byte[]]$Bytes)
    if($Bytes.Length -ne 2300 -or [BitConverter]::ToUInt32($Bytes,40) -ne 12){
        throw 'Schadenswerte erfordern den 2300-Byte-Telemetrieausschnitt der Revision 12.'
    }
    # Verified against the installed 1.62 GameTelemetry reader: TruckFloat
    # starts at 948; wearEngine follows 22 single-precision fields.
    $issues=[Collections.Generic.List[string]]::new()
    $values=[ordered]@{}
    $offset=1036
    foreach($name in @('engine','transmission','cabin','chassis','wheels')) {
        $value=[double][BitConverter]::ToSingle($Bytes,$offset);$offset+=4
        if(![double]::IsFinite($value) -or $value -lt 0 -or $value -gt 1){
            $issues.Add("InvalidDamage:$name");$value=$null
        }
        $values[$name]=$value
    }
    $values.issues=$issues.ToArray()
    [pscustomobject]$values
}
function Read-TruckAirBuffer {
    param([Parameter(Mandatory)][byte[]]$Bytes)
    if($Bytes.Length -ne 2300 -or [BitConverter]::ToUInt32($Bytes,40) -ne 12){
        throw 'Luftdruckwerte erfordern den 2300-Byte-Telemetrieausschnitt der Revision 12.'
    }
    # Verified against the installed GameTelemetry reader. Pressure and its
    # configured limits use the same SDK scale; no unit conversion is assumed.
    $issues=[Collections.Generic.List[string]]::new()
    $values=[ordered]@{}
    # Preserve finite out-of-range source values separately. The game has been
    # observed reporting a slightly negative value with both low-air flags on.
    # Keep pressure invalid rather than silently clamping it to a safe reading.
    $rawPressure=[double][BitConverter]::ToSingle($Bytes,992)
    $values.rawPressure=if([double]::IsFinite($rawPressure)){$rawPressure}else{$null}
    foreach($field in @(@('pressure',992),@('warningThreshold',720),@('emergencyThreshold',724))) {
        $value=[double][BitConverter]::ToSingle($Bytes,$field[1])
        if(![double]::IsFinite($value) -or $value -lt 0){$issues.Add("InvalidAirPressure:$($field[0])");$value=$null}
        $values[$field[0]]=$value
    }
    foreach($field in @(@('warning',1568),@('emergency',1569))) {
        $value=$Bytes[$field[1]]
        if($value -notin @(0,1)){$issues.Add("InvalidAirFlag:$($field[0])");$values[$field[0]]=$null}
        else{$values[$field[0]]=$value -eq 1}
    }
    $values.issues=$issues.ToArray()
    [pscustomobject]$values
}
function Measure-TollDeparture {
    param([Parameter(Mandatory)][object[]]$Rows,[Parameter(Mandatory)][string]$GateKey)
    function Finite($object,[string]$name) {
        if($null -eq $object -or $null -eq $object.$name){return $false}
        try{return [double]::IsFinite([double]$object.$name)}catch{return $false}
    }
    $anchor=$null;$seed=$null;$previous=$null;$approached=$false
    $released=0;$beyond=0;$first=$null;$last=$null;$maxProjection=$null
    foreach($r in $Rows) {
        $s=$r.currentStatus
        $continuous=$null -eq $previous -or ($r.t-$previous.t -le .5 -and $r.sdkTimestamp -ne $previous.sdkTimestamp)
        $good=$r.active -and !$r.paused -and @($r.issues).Count -eq 0 -and $continuous -and
            $null -ne $s -and $s.assists -and !$s.faultBraking -and !$s.pathError -and !$s.routeFault -and
            $s.steeringFeed -eq 'Healthy' -and (Finite $s 'connectedMetresAhead') -and (Finite $s 'requiredLookahead') -and
            (Finite $s 'telemetryAge') -and $s.telemetryAge -ge 0 -and $s.telemetryAge -lt .5 -and
            (Finite $s 'pathAge') -and $s.pathAge -ge 0 -and $s.pathAge -lt 1 -and
            (Finite $s 'steeringAge') -and $s.steeringAge -ge 0 -and $s.steeringAge -le .15 -and
            $s.requiredLookahead -ge 25 -and $s.connectedMetresAhead -ge $s.requiredLookahead -and
            (Finite $r 'x') -and (Finite $r 'y') -and (Finite $r 'z') -and (Finite $r 'heading') -and
            (Finite $r 'speedMps') -and $r.speedMps -ge -.1 -and (!$s.gate -or $s.gate -ceq $GateKey)
        if($good -and $previous -and (Finite $previous 'x') -and (Finite $previous 'y') -and (Finite $previous 'z')) {
            $step=[math]::Sqrt([math]::Pow($r.x-$previous.x,2)+[math]::Pow($r.y-$previous.y,2)+[math]::Pow($r.z-$previous.z,2))
            $allow=[math]::Max(5,([math]::Max([math]::Abs($r.speedMps),[math]::Abs($previous.speedMps))*($r.t-$previous.t)+2))
            if($step -gt $allow){$good=$false}
        }
        if(!$good){$seed=$null;$approached=$false;$previous=$r;continue}
        $observed=Resolve-TollGateObservation -Gates $r.gates -GateKey $GateKey -RememberedAnchor $anchor
        if($observed.state -in 0..3 -and $observed.anchor){$anchor=$observed.anchor}
        $assigned=$s.gate -ceq $GateKey -and $s.candidate.Key -ceq $GateKey -and (Finite $s.candidate 'Distance')
        if($assigned -and $s.candidate.Distance -ge 0){$approached=$true;$seed=$null}
        if(!$seed -and $approached -and $assigned -and $s.candidate.Distance -lt -1 -and $s.candidate.Distance -gt -30 -and
            $observed.state -eq 3 -and $anchor -and $r.speedMps -gt .05) {
            $fx=-[math]::Sin($r.heading*2*[math]::PI);$fz=-[math]::Cos($r.heading*2*[math]::PI)
            $projection=($r.x-$anchor.x)*$fx+($r.z-$anchor.z)*$fz
            if($projection -gt 0 -and $projection -lt 40 -and [math]::Abs($r.y-$anchor.y) -le 3){
                $seed=[pscustomobject]@{x=$anchor.x;y=$anchor.y;z=$anchor.z;fx=$fx;fz=$fz;t=$r.t}
            }
        }
        if($seed) {
            $headingDot=(-[math]::Sin($r.heading*2*[math]::PI))*$seed.fx+(-[math]::Cos($r.heading*2*[math]::PI))*$seed.fz
            if($r.t-$seed.t -gt 60 -or [math]::Abs($r.y-$seed.y) -gt 3 -or $headingDot -le 0){
                $seed=$null;$approached=$false
            } elseif(!$s.gate) {
                $released++
                $projection=($r.x-$seed.x)*$seed.fx+($r.z-$seed.z)*$seed.fz
                if($r.speedMps -gt .05 -and $projection -gt 30) {
                    $beyond++;if($null -eq $first){$first=$r.t};$last=$r.t
                    if($null -eq $maxProjection -or $projection -gt $maxProjection){$maxProjection=$projection}
                }
            }
        }
        $previous=$r
    }
    [pscustomobject]@{healthySamplesAfterRelease=$released;beyondGatePlane30mSamples=$beyond;
        firstBeyond=$first;lastBeyond=$last;maxForwardProjectionMetres=$maxProjection;
        method='SDK pose beyond a frozen gate plane after observed approach and passage; current status reports healthy route/steering with bounded source ages; at most 60 s.';
        isRouteDistance=$false;fullPassValidated=$false}
}
function Select-TollCaptureStatus($Status) {
    $Status|Select-Object utc,version,assists,active,mode,phase,targetSpeedKph,gate,candidate,
        selectedLane,selectedPayment,plaza,faultBraking,pathError,driveAction,connectedMetresAhead,
        requiredLookahead,telemetryAge,gatesAge,pathAge,options,routing,routeFault,plannedStop,stopDistance,
        preparingAutomaticEntry,planningStopDistance,planningStopKey,
        steeringFeed,steeringAge,steeringFault,brakeHeld,brakeInputValue,
        laneAttempts,laneAttemptContext,laneDecisionContext,laneProtection,detailedRigProtection,planningProfile,rigPrediction,
        sceneStopping,sceneStatus,scenePedalFloor,sceneHoldOwner,sceneHolding,holdPedalFloor,tollAccelerationLimit,nativeAcc,longitudinal,pedalFault,pedalSourceCurrent
}
Export-ModuleMember -Function Read-TruckPowertrainBuffer,Read-TollGateBuffer,Resolve-TollGateObservation,Read-ModernPedalBuffer,Read-TruckDamageBuffer,Read-TruckAirBuffer,Measure-TollDeparture,Select-TollCaptureStatus
