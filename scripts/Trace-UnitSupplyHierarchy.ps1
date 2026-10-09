param([Parameter(Mandatory=$true)][string]$SaveDirectory)

# Trace a validated current-save parent chain; IDs are used only within this save.
if (([IO.File]::ReadAllText((Join-Path $SaveDirectory 'version.dat'))).Trim() -ne '1.142') {
    throw 'Only the observed save version 1.142 is supported'
}
$regiments = [IO.File]::ReadAllLines((Join-Path $SaveDirectory 'regiments.dat'))
$groups = [IO.File]::ReadAllLines((Join-Path $SaveDirectory 'groups.dat'))
$unitCount = [int]$regiments[0]
$groupCount = [int]$groups[0]
if ($regiments.Length -ne 1 + 39 * $unitCount -or $groups.Length -ne 1 + 32 * $groupCount) {
    throw 'Malformed combat or command count'
}
$matches = @(for($i=0;$i -lt $unitCount;$i++) {
    $start = 1 + 39 * $i
    if ($regiments[$start+1] -ceq 'ADC Test 1-1 Infantry' -and
        $regiments[$start+2] -ceq 'ADC Test 1-1 Infantry' -and
        $regiments[$start+4] -eq '0' -and $regiments[$start+5] -eq '91') { $start }
})
if ($matches.Count -ne 1) { throw 'Target infantry is missing or ambiguous' }
$unit = $matches[0]
$paths = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $SaveDirectory)
$pathMatches = @($paths | Where-Object {
    $_.Name -ceq 'ADC Test 1-1 Infantry' -and
    $_.Abbreviation -ceq 'ADC Test 1-1 Infantry' -and
    $_.UnitType -eq 0 -and $_.Commander -eq 91
})
if ($pathMatches.Count -ne 1) { throw 'Target path is missing or ambiguous' }
Write-Output "unit $($regiments[$unit+1]) stock=$($pathMatches[0].SupplyStock -join ',') flow=$($pathMatches[0].SupplyHeader[3])"
$parent = [int]$regiments[$unit+3]
$seen = @{}
while ($parent -ne -1) {
    if ($seen.ContainsKey($parent)) { throw "Hierarchy cycle at group $parent" }
    $seen[$parent] = $true
    $matches = @(for($i=0;$i -lt $groupCount;$i++) {
        $start = 1 + 32 * $i
        if ([int]$groups[$start] -eq $parent) { $start }
    })
    if ($matches.Count -ne 1) { throw "Parent group $parent is missing or ambiguous" }
    $group = $matches[0]
    Write-Output "parent id=$parent name=$($groups[$group+1]) native_tier=$($groups[$group+17]) stores=$($groups[($group+6)..($group+9)] -join ',')"
    $parent = [int]$groups[$group+2]
}
