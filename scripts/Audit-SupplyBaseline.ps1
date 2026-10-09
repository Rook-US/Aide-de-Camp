param([Parameter(Mandatory=$true)][string]$SaveDirectory)

# Read-only cross-file stock audit for the installed save format.
$culture = [Globalization.CultureInfo]::InvariantCulture
if (([IO.File]::ReadAllText((Join-Path $SaveDirectory 'version.dat'))).Trim() -ne '1.142') {
    throw 'Only the observed 1.142 save version is supported'
}
$paths = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $SaveDirectory)
$pathMap = @{}
foreach ($path in $paths) {
    $key = ConvertTo-Json -InputObject @($path.Name,$path.Abbreviation,$path.UnitType,$path.Commander) -Compress
    if ($pathMap.ContainsKey($key)) { throw "Ambiguous path identity: $key" }
    $pathMap[$key] = $path
}
$lines = [IO.File]::ReadAllLines((Join-Path $SaveDirectory 'regiments.dat'))
$count = [int]$lines[0]
if ($lines.Length -ne 1 + 39 * $count) { throw 'Malformed regiments.dat count' }
$units = @(for ($i=0; $i -lt $count; $i++) {
    $s = 1 + 39 * $i
    $type = [int]$lines[$s+4]
    if ($type -notin @(0,1,2)) { continue }
    $key = ConvertTo-Json -InputObject @($lines[$s+1],$lines[$s+2],$type,[int]$lines[$s+5]) -Compress
    if (-not $pathMap.ContainsKey($key)) { throw "Missing path identity: $key" }
    $path = $pathMap[$key]
    if ($path.SupplyStock.Count -ne 4) { throw "Missing four stock values: $key" }
    $men = [int]$lines[$s+6]
    $wounded = [int]$lines[$s+7]
    $sickPercent = [double]::Parse($lines[$s+9], $culture)
    if ($men -le 0 -or $wounded -lt 0 -or $sickPercent -lt 0 -or $sickPercent -ge 100) {
        throw "Invalid strength fields: $key"
    }
    $strength = [int][Math]::Round($men*(1-$sickPercent/100),0,[MidpointRounding]::AwayFromZero)
    if ([Math]::Abs(100*($men-$strength)/$men-$sickPercent) -gt 0.0001) {
        throw "Strength cannot be reconstructed: $key"
    }
    $stock = @($path.SupplyStock | ForEach-Object { [double]::Parse($_,$culture) })
    [pscustomobject]@{
        Name=$lines[$s+1]; Type=$type; Strength=$strength; Men=$men; Wounded=$wounded
        CampaignRefillTarget=$men+$wounded; Stock=$stock
        RatioToStrength=@($stock | ForEach-Object { $_/$strength })
    }
})
Write-Output "SAVE $SaveDirectory"
Write-Output "MATCHED_COMBAT_UNITS $($units.Count)"
foreach ($type in @(0,1,2)) {
    $sample = @($units | Where-Object Type -eq $type)
    $over = @(foreach ($unit in $sample) {
        for ($slot=0; $slot -lt 4; $slot++) {
            if ($unit.RatioToStrength[$slot] -gt 1.00001) { $unit }
        }
    }).Count
    $overCampaignTarget = @(foreach ($unit in $sample) {
        for ($slot=0; $slot -lt 4; $slot++) {
            if ($unit.Stock[$slot] -gt $unit.CampaignRefillTarget + 0.00001) { $unit }
        }
    }).Count
    $largestActiveRatio = @($sample | ForEach-Object { $_.RatioToStrength } | Measure-Object -Maximum)[0].Maximum
    Write-Output "TYPE $type COUNT $($sample.Count) STOCK_SLOTS_ABOVE_ACTIVE_STRENGTH $over ABOVE_CAMPAIGN_TARGET $overCampaignTarget MAX_RATIO_TO_ACTIVE $largestActiveRatio"
    $relevantSlots = if ($type -eq 0) { @(0,2) } elseif ($type -eq 1) { @(0,2,3) } else { @(1,2,3) }
    foreach ($slot in $relevantSlots) {
        $above = @($sample | Where-Object { $_.Stock[$slot] -gt $_.CampaignRefillTarget + 0.00001 })
        $example = $above | Select-Object -First 1
        $maximum = @($sample | Sort-Object { $_.Stock[$slot] } -Descending | Select-Object -First 1)[0]
        Write-Output "TYPE $type RELEVANT_SLOT $slot ABOVE_CAMPAIGN_TARGET $($above.Count) EXAMPLE $(if($example){$example.Name + ':' + $example.Stock[$slot] + '/' + $example.CampaignRefillTarget}else{'none'}) MAX_AMOUNT $($maximum.Stock[$slot]) MAX_UNIT $($maximum.Name)"
    }
}
foreach ($name in @('ADC Test 1-1 Infantry','ADC Test Division Cavalry','ADC Test 1st Artillery Battalion','1st US Cavalry')) {
    $sample = @($units | Where-Object Name -ceq $name)
    if ($sample.Count -eq 1) {
        $u = $sample[0]
        Write-Output "EXAMPLE $name type=$($u.Type) strength=$($u.Strength) men=$($u.Men) wounded=$($u.Wounded) campaign_target=$($u.CampaignRefillTarget) stock=$($u.Stock -join ',') ratio_to_strength=$($u.RatioToStrength -join ',')"
    }
}
