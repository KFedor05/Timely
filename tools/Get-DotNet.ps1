# Resolve SDK 10 from PATH or the official per-user installation directory.
$candidates = @()
$installed = Get-Command dotnet -ErrorAction SilentlyContinue
if ($installed) { $candidates += $installed.Source }
$candidates += Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'
foreach ($candidate in ($candidates | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $candidate)) { continue }
    $sdks = & $candidate --list-sdks
    if ($sdks -match '^10\.') { return $candidate }
}
throw 'Install .NET SDK 10 (x64) before building Timely.'
