$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $projectRoot 'build/android-tools'
New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null

function Download-File([string]$Uri, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { return }
    Write-Host "Downloading $([System.IO.Path]::GetFileName($Destination))"
    & node (Join-Path $PSScriptRoot 'download-android-tool.mjs') $Uri $Destination | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "Download failed for $Uri" }
}

function Find-ToolRoot([string]$Folder, [string]$Filename) {
    if (Test-Path -LiteralPath (Join-Path $Folder $Filename)) { return $Folder }
    foreach ($child in Get-ChildItem -LiteralPath $Folder -Directory) {
        if (Test-Path -LiteralPath (Join-Path $child.FullName $Filename)) { return $child.FullName }
    }
    throw "Could not locate $Filename in $Folder"
}

$jdkFolder = Join-Path $toolsRoot 'jdk'
if (-not (Test-Path -LiteralPath $jdkFolder)) {
    $jdkArchive = Join-Path $toolsRoot 'microsoft-openjdk17.zip'
    Download-File 'https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip' $jdkArchive
    Write-Output 'Extracting project-local OpenJDK 17'
    Expand-Archive -LiteralPath $jdkArchive -DestinationPath $jdkFolder
}
$jdkHome = Find-ToolRoot $jdkFolder 'bin/java.exe'

$repositoryFile = Join-Path $toolsRoot 'repository.xml'
Download-File 'https://dl.google.com/android/repository/repository2-3.xml' $repositoryFile
[xml]$repository = Get-Content -LiteralPath $repositoryFile -Raw

function Get-SdkPackage([string]$PackagePath, [string]$FolderName, [string]$ToolName) {
    $destination = Join-Path $toolsRoot $FolderName
    if (-not (Test-Path -LiteralPath $destination)) {
        $package = @($repository.SelectNodes("//*[local-name()='remotePackage']") | Where-Object { $_.GetAttribute('path') -eq $PackagePath })
        if ($package.Count -ne 1) { throw "Official SDK manifest did not return exactly one $PackagePath package." }
        $archives = $package[0].SelectNodes(".//*[local-name()='archives']/*[local-name()='archive']")
        $archive = @($archives | Where-Object {
            $hostNode = $_.SelectSingleNode("./*[local-name()='host-os']")
            $null -eq $hostNode -or $hostNode.InnerText -eq 'windows'
        })
        if ($archive.Count -ne 1) { throw "No unique Windows/universal archive for $PackagePath" }
        $complete = $archive[0].SelectSingleNode("./*[local-name()='complete']")
        $relativeUrl = $complete.SelectSingleNode("./*[local-name()='url']").InnerText
        $expectedChecksum = $complete.SelectSingleNode("./*[local-name()='checksum']").InnerText.Trim()
        $zipPath = Join-Path $toolsRoot ($FolderName + '.zip')
        Download-File ('https://dl.google.com/android/repository/' + $relativeUrl) $zipPath
        if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA1).Hash -ne $expectedChecksum) { throw "Official SDK checksum mismatch for $PackagePath" }
        Write-Host "Extracting $PackagePath"
        Expand-Archive -LiteralPath $zipPath -DestinationPath $destination
    }
    return Find-ToolRoot $destination $ToolName
}

$platformRoot = Get-SdkPackage 'platforms;android-36' 'platform36' 'android.jar'
$buildToolsRoot = Get-SdkPackage 'build-tools;36.0.0' 'build-tools36' 'aapt2.exe'
$toolInfo = [ordered]@{ JdkHome = $jdkHome; AndroidJar = (Join-Path $platformRoot 'android.jar'); BuildTools = $buildToolsRoot; Api = 36 }
$toolInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $toolsRoot 'tools.json') -Encoding UTF8
& (Join-Path $jdkHome 'bin/java.exe') -version
Write-Output 'Project-local Android build tools are ready.'
