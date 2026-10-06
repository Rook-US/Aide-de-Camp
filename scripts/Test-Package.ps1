param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference='Stop'
$archivePath=(Resolve-Path -LiteralPath $Archive).Path
$checkRoot=Join-Path (Split-Path $archivePath) ('package-check-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archivePath -DestinationPath $checkRoot
$executables=@(Get-ChildItem -LiteralPath $checkRoot -Recurse -Filter 'Aide-de-Camp.exe')
if($executables.Count -ne 1){throw 'Expected exactly one Aide-de-Camp.exe in the extracted package.'}
$exe=$executables[0]
$appRoot=$exe.Directory.FullName
foreach($required in @('coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll','System.Private.CoreLib.dll')) {
    if(!(Test-Path -LiteralPath (Join-Path $appRoot $required))){throw "Missing bundled runtime component: $required"}
}
$config=Get-Content -LiteralPath (Join-Path $appRoot 'Aide-de-Camp.runtimeconfig.json') -Raw | ConvertFrom-Json
if($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks){throw 'Package unexpectedly needs a shared .NET runtime.'}
$savedData=$env:AIDE_DE_CAMP_DATA;$savedRoot=$env:DOTNET_ROOT;$savedX64=$env:DOTNET_ROOT_X64
try {
    $env:AIDE_DE_CAMP_DATA=Join-Path $checkRoot 'test-preferences'
    $env:DOTNET_ROOT=Join-Path $checkRoot 'no-shared-runtime'
    $env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
    $process=Start-Process -FilePath $exe.FullName -ArgumentList '--smoke-test' -WorkingDirectory $appRoot -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(45000)){Stop-Process -Id $process.Id;throw 'Packaged startup verification timed out.'}
    if($process.ExitCode -ne 0){throw "Packaged app exited with code $($process.ExitCode)."}
    $result=Get-Content -LiteralPath (Join-Path $appRoot 'smoke-test.json') -Raw | ConvertFrom-Json
    if(!$result.success -or !$result.title.StartsWith('Aide-de-Camp')){throw 'Packaged UI startup failed.'}
    if(![IO.Path]::GetFullPath($result.runtime).StartsWith($appRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'App did not use its bundled runtime.'}
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path $archivePath) 'package-verification.json')
    Write-Output "PASS: extracted package opened its branded WPF window using the bundled $($result.framework) runtime."
} finally {$env:AIDE_DE_CAMP_DATA=$savedData;$env:DOTNET_ROOT=$savedRoot;$env:DOTNET_ROOT_X64=$savedX64}
