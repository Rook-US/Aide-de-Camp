param(
    [string]$SourceSave = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G\Save7_10_2026_22_35_17',
    [string]$TargetSave = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G\ADC-TEST-9-DIVISION-50PCT-STOCK'
)

# Disposable, version-specific experiment. Select records by saved hierarchy and
# the game's four-part path identity, never by their positions in the file.
$culture = [Globalization.CultureInfo]::InvariantCulture
if (([IO.File]::ReadAllText((Join-Path $SourceSave 'version.dat'))).Trim() -ne '1.142') {
    throw 'Only the observed 1.142 save version is supported'
}
if (Test-Path -LiteralPath $TargetSave) { throw "Refusing to overwrite $TargetSave" }

$groupLines = [IO.File]::ReadAllLines((Join-Path $SourceSave 'groups.dat'))
$groupCount = [int]$groupLines[0]
if ($groupLines.Length -ne 1 + 32 * $groupCount) { throw 'Malformed groups.dat count' }
$groups = @(for ($i = 0; $i -lt $groupCount; $i++) {
    $s = 1 + 32 * $i
    [pscustomobject]@{
        Id = [int]$groupLines[$s]; Name = $groupLines[$s+1]
        Parent = [int]$groupLines[$s+2]; Faction = [int]$groupLines[$s+3]
        Commander = [int]$groupLines[$s+4]; Tier = [int]$groupLines[$s+17]
    }
})
$division = @($groups | Where-Object {
    $_.Name -ceq '1st Infantry Division' -and $_.Faction -eq 0 -and
    $_.Commander -eq 1 -and $_.Tier -eq 15
})
if ($division.Count -ne 1) { throw 'Union 1st Infantry Division missing or ambiguous' }
$descendants = @{$division[0].Id = $true}
do {
    $oldCount = $descendants.Count
    foreach ($group in $groups) {
        if ($descendants.ContainsKey($group.Parent)) { $descendants[$group.Id] = $true }
    }
} while ($oldCount -ne $descendants.Count)

$regimentLines = [IO.File]::ReadAllLines((Join-Path $SourceSave 'regiments.dat'))
$regimentCount = [int]$regimentLines[0]
if ($regimentLines.Length -ne 1 + 39 * $regimentCount) { throw 'Malformed regiments.dat count' }
$targets = @{}
for ($i = 0; $i -lt $regimentCount; $i++) {
    $s = 1 + 39 * $i
    if (-not $descendants.ContainsKey([int]$regimentLines[$s+3])) { continue }
    $type = [int]$regimentLines[$s+4]
    if ($type -notin @(0,1,2)) { throw "Unexpected combat type $type for $($regimentLines[$s+1])" }
    $key = ConvertTo-Json -InputObject @(
        $regimentLines[$s+1], $regimentLines[$s+2],
        $type, [int]$regimentLines[$s+5]
    ) -Compress
    if ($targets.ContainsKey($key)) { throw "Ambiguous combat identity $key" }
    $totalMen = [int]$regimentLines[$s+6]
    $sickPercent = [double]::Parse($regimentLines[$s+9], $culture)
    if ($totalMen -le 0 -or $sickPercent -lt 0 -or $sickPercent -ge 100) {
        throw "Invalid strength or sickness for $key"
    }
    # The game writes total men = strength + sick, then sick as a percentage
    # of that total. GetAvgSupplyState uses live strength as its divisor.
    $activeStrength = [int][Math]::Round(
        $totalMen * (1 - $sickPercent / 100), 0,
        [MidpointRounding]::AwayFromZero
    )
    $reconstructedSickPercent = 100 * ($totalMen - $activeStrength) / $totalMen
    if ([Math]::Abs($reconstructedSickPercent - $sickPercent) -gt 0.0001) {
        throw "Saved sickness cannot reconstruct active strength for $key"
    }
    $targets[$key] = [pscustomobject]@{
        Name = $regimentLines[$s+1]; Strength = $activeStrength
    }
}
if ($descendants.Count -ne 6 -or $targets.Count -ne 18) {
    throw "Unexpected hierarchy size: groups=$($descendants.Count), combat units=$($targets.Count)"
}

$paths = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $SourceSave)
$allPathKeys = @{}
$pathTargets = @{}
foreach ($path in $paths) {
    $key = ConvertTo-Json -InputObject @(
        $path.Name, $path.Abbreviation, $path.UnitType, $path.Commander
    ) -Compress
    if ($allPathKeys.ContainsKey($key)) { throw "Ambiguous path identity $key" }
    $allPathKeys[$key] = $true
    if (-not $targets.ContainsKey($key)) { continue }
    if ($path.SupplyStockLine -lt 0 -or $path.SupplyStock.Count -ne 4) {
        throw "Missing four-value stock for $key"
    }
    $pathTargets[$key] = $path
}
if ($pathTargets.Count -ne $targets.Count) {
    $missing = @($targets.Keys | Where-Object { -not $pathTargets.ContainsKey($_) })
    throw "Missing path identities: $($missing -join '; ')"
}

$original = [IO.File]::ReadAllLines((Join-Path $SourceSave 'paths.dat'))
$edited = [string[]]$original.Clone()
$changedLines = [Collections.Generic.List[int]]::new()
foreach ($path in $pathTargets.Values) {
    $key = ConvertTo-Json -InputObject @(
        $path.Name, $path.Abbreviation, $path.UnitType, $path.Commander
    ) -Compress
    for ($slot = 0; $slot -lt 4; $slot++) {
        $line = $path.SupplyStockLine + $slot
        $stock = [decimal]::Parse($original[$line], $culture)
        if ($stock -le 0) { throw "Nonpositive stock for $($path.Name), slot $slot" }
        $targetAmount = ([decimal]$targets[$key].Strength / 2).ToString('0.############################', $culture)
        if ($targetAmount -ceq $original[$line]) { throw "Unchanged stock for $($path.Name), slot $slot" }
        $edited[$line] = $targetAmount
        $changedLines.Add($line)
    }
}
if ($changedLines.Count -ne 72 -or ($changedLines | Select-Object -Unique).Count -ne 72) {
    throw 'Expected exactly 72 distinct stock fields'
}

$scenario = [IO.File]::ReadAllLines((Join-Path $SourceSave 'scenario.dat'))
if (($scenario[2..4] -join ',') -ne '11,7,1861') { throw 'Unexpected scenario date' }
$scenario[24] = 'ADC Test 9 - 1st Division 50 Percent Stock'

Copy-Item -LiteralPath $SourceSave -Destination $TargetSave -Recurse
[IO.File]::WriteAllLines((Join-Path $TargetSave 'paths.dat'), $edited, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines((Join-Path $TargetSave 'scenario.dat'), $scenario, [Text.UTF8Encoding]::new($false))

$reparsed = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $TargetSave)
if ($reparsed.Count -ne $paths.Count) { throw 'Test copy path record count changed' }
$actual = [IO.File]::ReadAllLines((Join-Path $TargetSave 'paths.dat'))
$actualDiff = @(for ($i = 0; $i -lt $original.Length; $i++) {
    if ($original[$i] -cne $actual[$i]) { $i }
})
if ($actual.Length -ne $original.Length -or
    (($actualDiff | Sort-Object) -join ',') -ne (($changedLines | Sort-Object) -join ',')) {
    throw 'Unexpected paths.dat changes'
}
$changedFiles = @(Get-ChildItem -LiteralPath $TargetSave -File | Where-Object {
    (Get-FileHash -LiteralPath $_.FullName).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $SourceSave $_.Name)).Hash
} | Select-Object -ExpandProperty Name | Sort-Object)
if (($changedFiles -join ',') -ne 'paths.dat,scenario.dat') {
    throw "Unexpected changed files: $($changedFiles -join ',')"
}
Write-Output "TEST_SAVE $TargetSave"
Write-Output "SOURCE_SAVE $SourceSave"
Write-Output "DIVISION_ID $($division[0].Id) DESCENDANT_GROUPS $($descendants.Count - 1)"
Write-Output "COMBAT_UNITS $($targets.Count) FIFTY_PERCENT_STOCK_FIELDS $($changedLines.Count)"
Write-Output "CHANGED_FILES $($changedFiles -join ',')"
Write-Output "SCENARIO_LABEL $($scenario[24])"
