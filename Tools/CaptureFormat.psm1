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
        if(![single]::IsFinite($x) -or ![single]::IsFinite($y) -or ![single]::IsFinite($z) -or $state -notin 0..3) {
            $issues.Add('InvalidGateData');continue
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
Export-ModuleMember -Function Read-TollGateBuffer,Resolve-TollGateObservation
