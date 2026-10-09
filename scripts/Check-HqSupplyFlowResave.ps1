param([Parameter(Mandatory=$true)][string]$GameSaveDirectory)

$root = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G'
$input = Join-Path $root 'ADC-TEST-8-I-CORPS-SUPPLY-FLOW'
if ([IO.Path]::GetFullPath($GameSaveDirectory).TrimEnd('\') -eq
    [IO.Path]::GetFullPath($input).TrimEnd('\')) {
    throw 'A separate game-written save is required'
}
function Find-Hq([string]$directory) {
    $paths = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $directory)
    $matching = @($paths | Where-Object {
        $_.Name -ceq 'I. CORPS' -and $_.Abbreviation -ceq '' -and
        $_.UnitType -eq 16 -and $_.Commander -eq 0
    })
    if ($matching.Count -ne 1) { throw "I. CORPS path identity is missing or ambiguous in $directory" }
    $groups = [IO.File]::ReadAllLines((Join-Path $directory 'groups.dat'))
    $count = [int]$groups[0]
    if ($groups.Length -ne 1 + 32 * $count) { throw "Malformed groups.dat in $directory" }
    $commands = @(for($i=0;$i -lt $count;$i++) {
        $s = 1 + 32 * $i
        if ($groups[$s+1] -ceq 'I. CORPS' -and $groups[$s+2] -eq '-1' -and
            $groups[$s+3] -eq '0' -and $groups[$s+4] -eq '0' -and
            $groups[$s+17] -eq '16') { $s }
    })
    if ($commands.Count -ne 1) { throw "I. CORPS command identity is missing or ambiguous in $directory" }
    return [pscustomobject]@{
        Flow = $matching[0].SupplyHeader[3]
        Stock = $matching[0].SupplyStock -join ','
        GroupStores = $groups[($commands[0]+6)..($commands[0]+9)] -join ','
    }
}
$before = Find-Hq $input
$after = Find-Hq $GameSaveDirectory
if ($before.Flow -ne '0.5') { throw 'Prepared input is not the half-flow test' }
Write-Output "game_save $GameSaveDirectory"
Write-Output "input_flow $($before.Flow) game_flow $($after.Flow)"
Write-Output "input_HQ_stock $($before.Stock) game_HQ_stock $($after.Stock)"
Write-Output "input_group_stores $($before.GroupStores) game_group_stores $($after.GroupStores)"
