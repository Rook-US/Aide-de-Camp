param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference='Stop'
$archivePath=(Resolve-Path -LiteralPath $Archive).Path
$checkRoot=Join-Path (Split-Path $archivePath) ('package-check-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archivePath -DestinationPath $checkRoot
$executables=@(Get-ChildItem -LiteralPath $checkRoot -Recurse -Filter 'Aide-de-Camp.exe')
if($executables.Count -ne 1){throw 'Expected exactly one Aide-de-Camp.exe in the extracted package.'}
$exe=$executables[0]
$appRoot=$exe.Directory.FullName
if(@(Get-ChildItem -LiteralPath $appRoot -File | Where-Object Name -NotIn @('Aide-de-Camp.exe','README.md')).Count){throw 'Release root contains unexpected files.'}
if(@(Get-ChildItem -LiteralPath $appRoot -Recurse -File | Where-Object Extension -In @('.cs','.xaml','.csproj','.pdb','.ps1')).Count){throw 'Source or debug files leaked into the release.'}
$savedExtract=$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR;$savedSmoke=$env:AIDE_DE_CAMP_SMOKE_RESULT
$savedData=$env:AIDE_DE_CAMP_DATA;$savedRoot=$env:DOTNET_ROOT;$savedX64=$env:DOTNET_ROOT_X64
try {
    $env:AIDE_DE_CAMP_DATA=Join-Path $checkRoot 'test-preferences'
    $env:DOTNET_ROOT=Join-Path $checkRoot 'no-shared-runtime'
    $env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $checkRoot 'runtime-cache'
    $env:AIDE_DE_CAMP_SMOKE_RESULT=Join-Path $checkRoot 'smoke-test.json'
    $process=Start-Process -FilePath $exe.FullName -ArgumentList '--smoke-test' -WorkingDirectory $appRoot -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(45000)){Stop-Process -Id $process.Id;throw 'Packaged startup verification timed out.'}
    if($process.ExitCode -ne 0){throw "Packaged app exited with code $($process.ExitCode)."}
    $result=Get-Content -LiteralPath $env:AIDE_DE_CAMP_SMOKE_RESULT -Raw | ConvertFrom-Json
    if(!$result.success -or !$result.title.StartsWith('Aide-de-Camp')){throw 'Packaged UI startup failed.'}
    if(![IO.Path]::GetFullPath($result.runtime).StartsWith($env:DOTNET_BUNDLE_EXTRACT_BASE_DIR,[StringComparison]::OrdinalIgnoreCase)){throw 'App did not use its bundled runtime.'}
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path $archivePath) 'package-verification.json')
    Write-Output "PASS: extracted package opened its branded WPF window using the bundled $($result.framework) runtime."
} finally {$env:AIDE_DE_CAMP_DATA=$savedData;$env:DOTNET_ROOT=$savedRoot;$env:DOTNET_ROOT_X64=$savedX64;$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=$savedExtract;$env:AIDE_DE_CAMP_SMOKE_RESULT=$savedSmoke}
