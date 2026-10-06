param([string]$RuntimeVersion = '8.0.30')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$project = Join-Path $repoRoot 'src/AideDeCamp/AideDeCamp.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = $projectXml.Project.PropertyGroup.Version
$packageName = "Aide-de-Camp-$version-win-x64"
$outputRoot = Join-Path $repoRoot 'artifacts'
$stage = Join-Path $outputRoot "stage/$packageName"
if (Test-Path -LiteralPath $stage) { throw "Stage already exists: $stage. Use a fresh checkout or archive the prior artifacts first." }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:PublishTrimmed=false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')) {
    $path=Join-Path $repoRoot $name
    if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination $stage}
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/USER_GUIDE.md') -Destination (Join-Path $stage 'USER_GUIDE.md')
$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$licenses = Join-Path $stage 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $runtimePath = Join-Path $cache "$runtime/$RuntimeVersion"
    $notices = @(Get-ChildItem -LiteralPath $runtimePath -File | Where-Object Name -Match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$')
    if (!$notices.Count) { throw "Runtime license notices missing: $runtimePath" }
    foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses "$runtime-$($notice.Name)") }
}
$archive=Join-Path $outputRoot "$packageName.zip"
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256","$hash  $packageName.zip`n",[Text.UTF8Encoding]::new($false))
Write-Output "Created $archive"
