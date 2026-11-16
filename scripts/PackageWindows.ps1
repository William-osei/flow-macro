param([string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ([String]::IsNullOrWhiteSpace($ExecutablePath)) { $ExecutablePath = Join-Path $projectRoot 'FarmMacro.exe' }
$executable = Get-Item -LiteralPath $ExecutablePath
if (-not $executable.FullName.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose an executable inside this project.' }
$version = [Version]$executable.VersionInfo.FileVersion
$release = '{0}.{1}.{2}' -f $version.Major, $version.Minor, [Math]::Max(0, $version.Build)
$buildRoot = Join-Path $projectRoot 'build'
$dist = Join-Path $projectRoot 'dist'
$staging = Join-Path $buildRoot ('package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging,$dist -Force | Out-Null

function Add-ProjectFile([string]$RelativePath) {
    $source = Join-Path $projectRoot $RelativePath
    $destination = Join-Path $staging $RelativePath
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

Copy-Item -LiteralPath $executable.FullName -Destination (Join-Path $staging 'FarmMacro.exe')
foreach ($file in @('Start Farm Macro.cmd','Start Farm Macro as Administrator.cmd','README.md','CHANGELOG.md','CONTRIBUTING.md','AGENTS.md','build.ps1','test.ps1')) { Add-ProjectFile $file }
foreach ($file in Get-ChildItem -LiteralPath $projectRoot -File -Filter '*.cs') { Add-ProjectFile $file.Name }
foreach ($folder in @('tests','scripts','assets','docs','android/app/src/main','android/tests')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse) {
        if ($file.Extension -notin @('.cs','.ps1','.mjs','.ico','.svg','.png','.md','.java','.xml')) { continue }
        Add-ProjectFile $file.FullName.Substring($projectRoot.Length + 1)
    }
}
foreach ($file in @('android/README.md','android/dist/FlowMacro-Android.apk','android/dist/FlowMacro-Android.apk.sha256','android/dist/FarmMacro-Android.apk','android/dist/FarmMacro-Android.apk.sha256','android/dist/README.md')) { Add-ProjectFile $file }
foreach ($preset in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'presets') -File -Filter '*.json') {
    Add-ProjectFile $preset.FullName.Substring($projectRoot.Length + 1)
}
$preview = Join-Path $buildRoot ('previews-' + $release + '/clicker-900.png')
if (-not (Test-Path -LiteralPath $preview)) { $preview = Join-Path $buildRoot 'previews-1.4/clicker-900.png' }
if (Test-Path -LiteralPath $preview) { Copy-Item -LiteralPath $preview -Destination (Join-Path $staging 'Preview.png') }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryZip = Join-Path $buildRoot ('package-' + [Guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
$archive = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
try {
    $appEntry = $archive.GetEntry('FarmMacro.exe')
    if ($null -eq $appEntry -or $appEntry.Length -ne $executable.Length) { throw 'The packaged executable is missing or incomplete.' }
    $hasher = [Security.Cryptography.SHA256]::Create()
    $entryStream = $appEntry.Open()
    try { $packagedHash = [BitConverter]::ToString($hasher.ComputeHash($entryStream)).Replace('-','') }
    finally { $entryStream.Dispose(); $hasher.Dispose() }
    if ($packagedHash -ne (Get-FileHash -LiteralPath $executable.FullName -Algorithm SHA256).Hash) { throw 'Packaged executable checksum does not match the selected build.' }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '(^|/)(build|\.git|\.codex)/|\.keystore$|microsoft-openjdk') { throw 'A private or cached build file entered the package.' }
    }
    $entryCount = $archive.Entries.Count
} finally { $archive.Dispose() }
$output = Join-Path $dist ('FlowMacro-' + $release + '-Windows.zip')
if (Test-Path -LiteralPath $output) { [IO.File]::Replace($temporaryZip, $output, (Join-Path $buildRoot ('previous-package-' + [Guid]::NewGuid().ToString('N') + '.zip'))) }
else { [IO.File]::Move($temporaryZip, $output) }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
[IO.File]::WriteAllText($output + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($output) + [Environment]::NewLine)
Write-Output ('Packaged {0} files: {1}' -f $entryCount, $output)
