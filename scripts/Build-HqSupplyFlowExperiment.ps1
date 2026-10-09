$root = 'G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G'
$source = Join-Path $root 'Save7_10_2026_22_35_17'
$target = Join-Path $root 'ADC-TEST-8-I-CORPS-SUPPLY-FLOW'
if (Test-Path $target) { throw "Refusing to overwrite $target" }
if (([IO.File]::ReadAllText((Join-Path $source 'version.dat'))).Trim() -ne '1.142') {
    throw 'Unsupported source version'
}
$records = @(& (Join-Path $PSScriptRoot 'Inspect-PathRecords.ps1') -SaveDirectory $source)
$matching = @($records | Where-Object {
    $_.Name -ceq 'I. CORPS' -and $_.Abbreviation -ceq '' -and
    $_.UnitType -eq 16 -and $_.Commander -eq 0
})
if ($matching.Count -ne 1) { throw 'I. CORPS path identity is missing or ambiguous' }
$path = $matching[0]
if ($path.SupplyHeaderLine -lt 0 -or ($path.SupplyHeader[0..3] -join ',') -ne '1,0,0,1') {
    throw 'I. CORPS supply header differs from the inspected game baseline'
}
$groups = [IO.File]::ReadAllLines((Join-Path $source 'groups.dat'))
$count = [int]$groups[0]
if ($groups.Length -ne 1 + 32 * $count) { throw 'Malformed command count' }
$command = @(for($i=0;$i -lt $count;$i++) {
    $s = 1 + 32 * $i
    if ($groups[$s+1] -ceq 'I. CORPS' -and $groups[$s+2] -eq '-1' -and
        $groups[$s+3] -eq '0' -and $groups[$s+4] -eq '0' -and
        $groups[$s+17] -eq '16') { $s }
})
if ($command.Count -ne 1) { throw 'Independent I. CORPS command is missing or ambiguous' }
Copy-Item -LiteralPath $source -Destination $target -Recurse
$original = [IO.File]::ReadAllLines((Join-Path $source 'paths.dat'))
$edited = [string[]]$original.Clone()
$line = $path.SupplyHeaderLine + 3
$edited[$line] = '0.5'
[IO.File]::WriteAllLines((Join-Path $target 'paths.dat'), $edited, [Text.UTF8Encoding]::new($false))
$scenario = [IO.File]::ReadAllLines((Join-Path $target 'scenario.dat'))
if (($scenario[2..4] -join ',') -ne '11,7,1861') { throw 'Unexpected scenario date' }
$scenario[24] = 'ADC Test 8 - I Corps Supply Flow'
[IO.File]::WriteAllLines((Join-Path $target 'scenario.dat'), $scenario, [Text.UTF8Encoding]::new($false))
$actual = [IO.File]::ReadAllLines((Join-Path $target 'paths.dat'))
$diff = @(for($i=0;$i -lt $original.Length;$i++) {
    if ($original[$i] -cne $actual[$i]) { $i }
})
if ($actual.Length -ne $original.Length -or ($diff -join ',') -ne "$line") {
    throw "Unexpected paths.dat diff: $($diff -join ',')"
}
$changed = @(Get-ChildItem -LiteralPath $target -File | Where-Object {
    (Get-FileHash -LiteralPath $_.FullName).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $source $_.Name)).Hash
} | Select-Object -ExpandProperty Name | Sort-Object)
if (($changed -join ',') -ne 'paths.dat,scenario.dat') {
    throw "Unexpected changed files: $($changed -join ',')"
}
Write-Output "test_save $target"
Write-Output "command $($path.Name) saved_group_id=$($groups[$command[0]]) native_tier=16"
Write-Output "path_supplystate_before 1 path_supplystate_in_test 0.5"
Write-Output "group_supply_stores_unchanged $($groups[($command[0]+6)..($command[0]+9)] -join ',')"
Write-Output "changed_files $($changed -join ',')"
