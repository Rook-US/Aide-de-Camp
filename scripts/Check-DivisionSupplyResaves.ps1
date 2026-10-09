param(
    [Parameter(Mandatory=$true)][string]$PausedSave,
    [Parameter(Mandatory=$true)][string]$AdvancedSave,
    [string]$InputSave = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G\ADC-TEST-9-DIVISION-50PCT-STOCK'
)

# Read-only comparison across three fully parsed 1.142 saves. File line numbers
# are used only while parsing each save; saved identity and parent IDs make links.
$directories=@($InputSave,$PausedSave,$AdvancedSave | ForEach-Object {
    [IO.Path]::GetFullPath($_).TrimEnd('\')
})
if(($directories | Select-Object -Unique).Count -ne 3){
    throw 'Input, paused game resave, and time-advanced game resave must be three distinct folders'
}
function Read-Snapshot([string]$directory) {
    if (([IO.File]::ReadAllText((Join-Path $directory 'version.dat'))).Trim() -ne '1.142') {
        throw "Unsupported version: $directory"
    }
    $groupLines = [IO.File]::ReadAllLines((Join-Path $directory 'groups.dat'))
    $groupCount = [int]$groupLines[0]
    if ($groupLines.Length -ne 1 + 32*$groupCount) { throw "Bad group count: $directory" }
    $groups = @(for($i=0;$i -lt $groupCount;$i++) {
        $s=1+32*$i
        [pscustomobject]@{
            Id=[int]$groupLines[$s]; Name=$groupLines[$s+1]; Parent=[int]$groupLines[$s+2]
            Faction=[int]$groupLines[$s+3]; Commander=[int]$groupLines[$s+4]
            Tier=[int]$groupLines[$s+17]
            Stores=@($groupLines[($s+6)..($s+9)])
        }
    })
    $division = @($groups | Where-Object {
        $_.Name -ceq '1st Infantry Division' -and $_.Faction -eq 0 -and
        $_.Commander -eq 1 -and $_.Tier -eq 15
    })
    $corps = @($groups | Where-Object {
        $_.Name -ceq 'I. CORPS' -and $_.Faction -eq 0 -and
        $_.Commander -eq 0 -and $_.Parent -eq -1 -and $_.Tier -eq 16
    })
    if ($division.Count -ne 1 -or $corps.Count -ne 1) { throw "HQ identity missing or ambiguous: $directory" }
    $ids=@{$division[0].Id=$true}
    do {
        $old=$ids.Count
        foreach($group in $groups) { if($ids.ContainsKey($group.Parent)){$ids[$group.Id]=$true} }
    } while($ids.Count -ne $old)
    if($ids.Count -ne 6){throw "Unexpected division command count: $directory"}

    $pathRecords=@(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $directory)
    $pathMap=@{}
    foreach($path in $pathRecords){
        $key=ConvertTo-Json -InputObject @($path.Name,$path.Abbreviation,$path.UnitType,$path.Commander) -Compress
        if($pathMap.ContainsKey($key)){throw "Duplicate path identity: $key"}
        $pathMap[$key]=$path
    }
    $lines=[IO.File]::ReadAllLines((Join-Path $directory 'regiments.dat'))
    $count=[int]$lines[0]
    if($lines.Length -ne 1+39*$count){throw "Bad combat count: $directory"}
    if($pathRecords.Count -ne $groupCount+$count){throw "Unexpected path record count: $directory"}
    $units=@{}
    for($i=0;$i -lt $count;$i++){
        $s=1+39*$i
        if(-not $ids.ContainsKey([int]$lines[$s+3])){continue}
        $type=[int]$lines[$s+4]
        if($type -notin @(0,1,2)){throw "Unexpected unit type: $($lines[$s+1])"}
        $key=ConvertTo-Json -InputObject @($lines[$s+1],$lines[$s+2],$type,[int]$lines[$s+5]) -Compress
        if($units.ContainsKey($key) -or -not $pathMap.ContainsKey($key)){throw "Missing or duplicate unit path identity: $key"}
        $path=$pathMap[$key]
        if($path.SupplyStock.Count -ne 4){throw "Missing stock: $key"}
        $men=[int]$lines[$s+6]
        $sick=[double]::Parse($lines[$s+9],[Globalization.CultureInfo]::InvariantCulture)
        $strength=[int][Math]::Round($men*(1-$sick/100),0,[MidpointRounding]::AwayFromZero)
        if($strength -le 0 -or [Math]::Abs(100*($men-$strength)/$men-$sick) -gt 0.0001){throw "Bad strength: $key"}
        $stock=@($path.SupplyStock | ForEach-Object { [double]::Parse($_,[Globalization.CultureInfo]::InvariantCulture) })
        $units[$key]=[pscustomobject]@{
            Name=$lines[$s+1]; Type=$type; Strength=$strength; Men=$men
            Wounded=[int]$lines[$s+7]; Stock=$stock
        }
    }
    if($units.Count -ne 18){throw "Unexpected division unit count: $directory"}
    return [pscustomobject]@{ Division=$division[0]; Corps=$corps[0]; Units=$units }
}

$input=Read-Snapshot $InputSave
$paused=Read-Snapshot $PausedSave
$advanced=Read-Snapshot $AdvancedSave
foreach($key in $input.Units.Keys){
    if(-not $paused.Units.ContainsKey($key) -or -not $advanced.Units.ContainsKey($key)){
        throw "Target unit identity changed across saves: $key"
    }
    $u=$input.Units[$key]
    for($slot=0;$slot -lt 4;$slot++){
        if([Math]::Abs($u.Stock[$slot]-$u.Strength/2) -gt 0.0001){throw "Input test stock is not 50%: $key slot $slot"}
    }
}
Write-Output "MATCHED_DIVISION_COMBAT_UNITS $($input.Units.Count)"
foreach($entry in @(@('INPUT',$input),@('PAUSED',$paused),@('ADVANCED',$advanced))){
    $label=$entry[0]; $snapshot=$entry[1]
    Write-Output "$label DIVISION_STORES $($snapshot.Division.Stores -join ',')"
    Write-Output "$label I_CORPS_STORES $($snapshot.Corps.Stores -join ',')"
    $categoryMeans=@(for($slot=0;$slot -lt 4;$slot++){
        $relevant=@($snapshot.Units.Values|Where-Object {
            ($slot -eq 0 -and $_.Type -in @(0,1)) -or
            ($slot -eq 1 -and $_.Type -eq 2) -or
            ($slot -eq 2) -or
            ($slot -eq 3 -and $_.Type -in @(1,2))
        })
        ($relevant|ForEach-Object {$_.Stock[$slot]/($_.Men+$_.Wounded)}|Measure-Object -Average).Average*100
    })
    Write-Output "$label NAIVE_RELEVANT_UNIT_MEANS $($categoryMeans -join ',')"
    foreach($type in @(0,1,2)){
        $sample=@($snapshot.Units.Values|Where-Object Type -eq $type)
        $slot=if($type -eq 2){1}else{0}
        $ammoMean=($sample|ForEach-Object {$_.Stock[$slot]/$_.Strength}|Measure-Object -Average).Average*100
        $provisionsMean=($sample|ForEach-Object {$_.Stock[2]/$_.Strength}|Measure-Object -Average).Average*100
        Write-Output "$label TYPE_$type COUNT $($sample.Count) MEAN_RELEVANT_AMMO_PCT $ammoMean MEAN_PROVISIONS_PCT $provisionsMean"
    }
}
foreach($pair in @(@('INPUT_TO_PAUSED',$input,$paused),@('PAUSED_TO_ADVANCED',$paused,$advanced))){
    $changed=0
    $slotChanges=@(0,0,0,0)
    foreach($key in $pair[1].Units.Keys){
        $a=$pair[1].Units[$key];$b=$pair[2].Units[$key]
        $unitChanged=$false
        for($slot=0;$slot -lt 4;$slot++){
            if([Math]::Abs($a.Stock[$slot]-$b.Stock[$slot]) -gt 0.00001){$slotChanges[$slot]++;$unitChanged=$true}
        }
        if($unitChanged){$changed++}
    }
    Write-Output "$($pair[0]) CHANGED_UNITS $changed CHANGED_SLOTS $($slotChanges -join ',')"
}
