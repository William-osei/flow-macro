param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The Windows .NET Framework C# compiler is required.' }
$sourcePaths = @('Program.cs', 'AppInstance.cs', 'AssemblyInfo.cs', 'BrandAssets.cs', 'HelpForm.cs', 'InputPreferences.cs', 'InputDiagnosticsForm.cs', 'InputPermissions.cs', 'MacroEngine.cs', 'QuickClickSettings.cs', 'QuickClickPanel.cs', 'PointRouteModel.cs', 'PointRouteEditor.cs', 'NativeInput.cs', 'MacroRecorder.cs', 'SequenceEngine.cs', 'SequencePanel.cs', 'UiTheme.cs', 'ProfileLibrary.cs', 'ProfilePanel.cs', 'ProfileTransfer.cs', 'BundledPresetCatalog.cs', 'WindowsDevicesPanel.cs', 'MobileProfileExport.cs') | ForEach-Object { Join-Path $taskRoot $_ }
$iconPath = Join-Path $taskRoot 'assets/farm-macro.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $taskRoot 'scripts/BuildBrand.ps1') }
if ([String]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $taskRoot 'FarmMacro.exe' }
$outputPath = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($outputPath) -ne '.exe') { throw 'Choose an .exe build output.' }
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($outputPath)) -Force | Out-Null
$stagedPath = $outputPath + '.build-' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    & $compilerPath /nologo /target:winexe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/win32icon:$iconPath" "/resource:$iconPath,FarmMacro.AppIcon" "/out:$stagedPath" $sourcePaths
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; the previous executable was kept.' }
    if (Test-Path -LiteralPath $outputPath) {
        $backupFolder = Join-Path $taskRoot 'build'
        New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
        $backup = Join-Path $backupFolder ('previous-build-' + [Guid]::NewGuid().ToString('N') + '.exe')
        [IO.File]::Replace($stagedPath, $outputPath, $backup)
    }
    else { [IO.File]::Move($stagedPath, $outputPath) }
} finally {
    if (Test-Path -LiteralPath $stagedPath) { Remove-Item -LiteralPath $stagedPath }
}
Write-Output "Built $outputPath"
