[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$dotnetPath = & (Join-Path $PSScriptRoot 'Get-DotNet.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src/Timely/Timely.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw 'Timely.csproj must specify a valid release version.'
}

$packageName = "Timely-$version-$Runtime"
$distRoot = Join-Path $repoRoot 'dist'
$zipPath = Join-Path $distRoot "$packageName.zip"
if (Test-Path -LiteralPath $zipPath) {
    throw "Package already exists: $zipPath. Move it elsewhere or update the version before rebuilding."
}

# Each run has its own staging directory; no existing checkout or output is deleted.
$stagingRoot = Join-Path $distRoot ('.staging/' + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $stagingRoot $packageName
$buildRoot = Join-Path $stagingRoot 'build'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

& $dotnetPath publish $projectPath -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
    --artifacts-path $buildRoot -o $packageRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

foreach ($name in @('README.md', 'LICENSE', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $packageRoot
}
$docsPath = Join-Path $repoRoot 'docs'
if (Test-Path -LiteralPath $docsPath) {
    Copy-Item -LiteralPath $docsPath -Destination $packageRoot -Recurse
}

# Include the license notices from the exact runtime packs resolved by this build.
$assetsPath = Join-Path $buildRoot 'obj/Timely/project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$noticesRoot = Join-Path $packageRoot 'licenses'
New-Item -ItemType Directory -Path $noticesRoot | Out-Null
$runtimePacks = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies |
    Where-Object { $_.name -in @("Microsoft.NETCore.App.Runtime.$Runtime", "Microsoft.WindowsDesktop.App.Runtime.$Runtime") })
if ($runtimePacks.Count -ne 2) { throw 'Expected both .NET and Windows Desktop runtime packs.' }
foreach ($pack in $runtimePacks) {
    $packVersion = $pack.version.Trim('[', ']').Split(',')[0].Trim()
    $packPath = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($pack.name.ToLowerInvariant() + '/' + $packVersion)
        if (Test-Path -LiteralPath $candidate) { $packPath = $candidate; break }
    }
    if (-not $packPath) { throw "Runtime pack not found: $($pack.name) $packVersion" }
    $noticeFiles = @(Get-ChildItem -LiteralPath $packPath -File |
        Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' })
    if ($noticeFiles.Count -eq 0) { throw "License not found in $packPath" }
    foreach ($notice in $noticeFiles) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticesRoot ($pack.name + '-' + $notice.Name))
    }
}

$metadata = [ordered]@{
    version = $version
    runtime = $Runtime
    sdk = (& $dotnetPath --version | Out-String).Trim()
    runtimePacks = @($runtimePacks | ForEach-Object { [ordered]@{ name = $_.name; version = $_.version } })
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageRoot 'build-info.json') -Encoding utf8

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $packageName.zip" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii
Write-Output "Package: $zipPath"
Write-Output "SHA-256: $hash"
