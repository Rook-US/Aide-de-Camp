param([Parameter(Mandatory=$true)][string]$GameSaveDirectory)

$root = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G'
$input = Join-Path $root 'ADC-TEST-6-INFANTRY-HALF-STOCK'
if ([IO.Path]::GetFullPath($GameSaveDirectory).TrimEnd('\') -eq
    [IO.Path]::GetFullPath($input).TrimEnd('\')) {
    throw 'A separate game-written save is required'
}
function Find-Target([string]$directory) {
    $records = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $directory)
    $matches = @($records | Where-Object {
        $_.Name -ceq 'ADC Test 1-1 Infantry' -and
        $_.Abbreviation -ceq 'ADC Test 1-1 Infantry' -and
        $_.UnitType -eq 0 -and $_.Commander -eq 91
    })
    if ($matches.Count -ne 1) { throw "Target path identity missing or ambiguous in $directory" }
    return $matches[0]
}
$before = Find-Target $input
$after = Find-Target $GameSaveDirectory
if (($before.SupplyStock -join ',') -ne '500,1000,500,1000') {
    throw 'Test input has changed from the prepared half-stock values'
}
Write-Output "game_save $GameSaveDirectory"
Write-Output "input_stock $($before.SupplyStock -join ',')"
Write-Output "game_stock $($after.SupplyStock -join ',')"
Write-Output "input_header $($before.SupplyHeader -join ',')"
Write-Output "game_header $($after.SupplyHeader -join ',')"
