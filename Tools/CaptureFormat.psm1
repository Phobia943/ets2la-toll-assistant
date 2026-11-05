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
Export-ModuleMember -Function Read-TollGateBuffer
