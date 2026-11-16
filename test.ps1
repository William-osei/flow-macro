$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}
$buildPath = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $buildPath -Force | Out-Null
$testExecutable = Join-Path $buildPath 'EngineTests.exe'
$sourcePaths = @(
    (Join-Path $projectRoot 'MacroEngine.cs'),
    (Join-Path $projectRoot 'QuickClickSettings.cs'),
    (Join-Path $projectRoot 'NativeInput.cs'),
    (Join-Path $projectRoot 'InputPermissions.cs'),
    (Join-Path $projectRoot 'SequenceEngine.cs'),
    (Join-Path $projectRoot 'MacroRecorder.cs'),
    (Join-Path $projectRoot 'tests\EngineTests.cs')
)
& $compilerPath /nologo /target:exe "/out:$testExecutable" @sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExecutable
if ($LASTEXITCODE -ne 0) { throw 'Engine tests failed.' }

$sequenceExecutable = Join-Path $buildPath 'SequenceTests.exe'
$sequenceSources = @(
    (Join-Path $projectRoot 'MacroEngine.cs'),
    (Join-Path $projectRoot 'NativeInput.cs'),
    (Join-Path $projectRoot 'InputPermissions.cs'),
    (Join-Path $projectRoot 'SequenceEngine.cs'),
    (Join-Path $projectRoot 'MacroRecorder.cs'),
    (Join-Path $projectRoot 'tests\SequenceTests.cs')
)
& $compilerPath /nologo /target:exe "/out:$sequenceExecutable" @sequenceSources
if ($LASTEXITCODE -ne 0) { throw 'Sequence test compilation failed.' }
& $sequenceExecutable
if ($LASTEXITCODE -ne 0) { throw 'Sequence and recorder tests failed.' }

$profileExecutable = Join-Path $buildPath 'ProfileTests.exe'
$profileSources = @((Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -File).FullName)
$profileSources += Join-Path $projectRoot 'tests\ProfileTests.cs'
& $compilerPath /nologo /target:exe /main:ProfileTests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$profileExecutable" @profileSources
if ($LASTEXITCODE -ne 0) { throw 'Profile test compilation failed.' }
& $profileExecutable
if ($LASTEXITCODE -ne 0) { throw 'Profile library tests failed.' }

$transferExecutable = Join-Path $buildPath 'ProfileTransferTests.exe'
$transferSources = @((Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -File).FullName)
$transferSources += Join-Path $projectRoot 'tests\ProfileTransferTests.cs'
& $compilerPath /nologo /target:exe /main:ProfileTransferTests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$transferExecutable" @transferSources
if ($LASTEXITCODE -ne 0) { throw 'Profile transfer test compilation failed.' }
& $transferExecutable
if ($LASTEXITCODE -ne 0) { throw 'Portable profile tests failed.' }

$keyboardExecutable = Join-Path $buildPath 'KeyboardPacketTests.exe'
$keyboardSources = @(
    (Join-Path $projectRoot 'MacroEngine.cs'),
    (Join-Path $projectRoot 'NativeInput.cs'),
    (Join-Path $projectRoot 'InputPermissions.cs'),
    (Join-Path $projectRoot 'SequenceEngine.cs'),
    (Join-Path $projectRoot 'MacroRecorder.cs'),
    (Join-Path $projectRoot 'tests\KeyboardPacketTests.cs')
)
& $compilerPath /nologo /target:exe "/out:$keyboardExecutable" @keyboardSources
if ($LASTEXITCODE -ne 0) { throw 'Keyboard packet test compilation failed.' }
& $keyboardExecutable
if ($LASTEXITCODE -ne 0) { throw 'Keyboard packet tests failed.' }

$mobileExecutable = Join-Path $buildPath 'MobileProfileExportTests.exe'
$mobileSources = @((Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -File).FullName)
$mobileSources += Join-Path $projectRoot 'tests\MobileProfileExportTests.cs'
& $compilerPath /nologo /target:exe /main:MobileProfileExportTests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$mobileExecutable" @mobileSources
if ($LASTEXITCODE -ne 0) { throw 'Mobile export test compilation failed.' }
& $mobileExecutable
if ($LASTEXITCODE -ne 0) { throw 'Mobile export tests failed.' }

$permissionsExecutable = Join-Path $buildPath 'InputPermissionsTests.exe'
$permissionsSources = @((Join-Path $projectRoot 'InputPermissions.cs'), (Join-Path $projectRoot 'tests\InputPermissionsTests.cs'))
& $compilerPath /nologo /target:exe "/out:$permissionsExecutable" @permissionsSources
if ($LASTEXITCODE -ne 0) { throw 'Input permission test compilation failed.' }
& $permissionsExecutable
if ($LASTEXITCODE -ne 0) { throw 'Input permission tests failed.' }

$presetExecutable = Join-Path $buildPath 'BundledPresetTests.exe'
$presetSources = @((Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -File).FullName)
$presetSources += Join-Path $projectRoot 'tests\BundledPresetTests.cs'
& $compilerPath /nologo /target:exe /main:BundledPresetTests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$presetExecutable" @presetSources
if ($LASTEXITCODE -ne 0) { throw 'Bundled preset test compilation failed.' }
& $presetExecutable $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Bundled preset tests failed.' }
