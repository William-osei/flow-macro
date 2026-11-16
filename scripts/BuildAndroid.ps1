param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $projectRoot 'build/android-tools'
$toolsJson = Join-Path $toolsRoot 'tools.json'
if (-not (Test-Path -LiteralPath $toolsJson)) { & (Join-Path $PSScriptRoot 'PrepareAndroidTools.ps1') }
$toolInfo = Get-Content -LiteralPath $toolsJson -Raw | ConvertFrom-Json
$javaHome = [System.IO.Path]::GetFullPath($toolInfo.JdkHome)
$androidJar = [System.IO.Path]::GetFullPath($toolInfo.AndroidJar)
$sdkBuildTools = [System.IO.Path]::GetFullPath($toolInfo.BuildTools)
$java = Join-Path $javaHome 'bin/java.exe'
$javac = Join-Path $javaHome 'bin/javac.exe'
$jar = Join-Path $javaHome 'bin/jar.exe'
$keytool = Join-Path $javaHome 'bin/keytool.exe'
$aapt = Join-Path $sdkBuildTools 'aapt2.exe'
$zipalign = Join-Path $sdkBuildTools 'zipalign.exe'
$d8 = Join-Path $sdkBuildTools 'lib/d8.jar'
$signer = Join-Path $sdkBuildTools 'lib/apksigner.jar'
$lambdaStubs = Join-Path $sdkBuildTools 'core-lambda-stubs.jar'
foreach ($tool in @($java, $javac, $jar, $keytool, $aapt, $zipalign, $d8, $signer, $androidJar, $lambdaStubs)) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "Missing Android build tool: $tool" }
}

function Check-Exit([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE" }
}

$sourceRoot = Join-Path $projectRoot 'android/app/src/main'
$workRoot = Join-Path $projectRoot ('build/android-apk-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$generated = Join-Path $workRoot 'generated'
$classes = Join-Path $workRoot 'classes'
$dex = Join-Path $workRoot 'dex'
$dist = Join-Path $projectRoot 'android/dist'
foreach ($folder in @($workRoot, $generated, $classes, $dex, $dist)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }

if (-not $SkipTests) {
    $testClasses = Join-Path $workRoot 'test-classes'
    New-Item -ItemType Directory -Path $testClasses -Force | Out-Null
    & $javac -encoding UTF-8 -d $testClasses (Join-Path $sourceRoot 'java/dev/farmmacro/app/TapAction.java') (Join-Path $sourceRoot 'java/dev/farmmacro/app/TapScheduler.java') (Join-Path $projectRoot 'android/tests/TapSchedulerTest.java')
    Check-Exit 'Android scheduler test compilation'
    & $java -cp $testClasses TapSchedulerTest
    Check-Exit 'Android scheduler tests'
}

$compiledResources = Join-Path $workRoot 'resources.zip'
& $aapt compile --dir (Join-Path $sourceRoot 'res') -o $compiledResources
Check-Exit 'Resource compilation'
$unsigned = Join-Path $workRoot 'unsigned.apk'
& $aapt link -o $unsigned -I $androidJar --manifest (Join-Path $sourceRoot 'AndroidManifest.xml') --min-sdk-version 26 --target-sdk-version 36 --version-code 140 --version-name 1.4.0 --java $generated $compiledResources
Check-Exit 'Resource linking'

$javaFiles = @((Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'java') -Filter '*.java' -Recurse -File).FullName)
$javaFiles += @((Get-ChildItem -LiteralPath $generated -Filter '*.java' -Recurse -File).FullName)
$argumentsFile = Join-Path $workRoot 'java-sources.txt'
$quotedSources = @($javaFiles | ForEach-Object { '"' + $_.Replace('\', '/') + '"' })
[System.IO.File]::WriteAllLines($argumentsFile, $quotedSources, [System.Text.UTF8Encoding]::new($false))
$bootClasspath = $androidJar + [System.IO.Path]::PathSeparator + $lambdaStubs
& $javac -encoding UTF-8 -source 8 -target 8 -bootclasspath $bootClasspath -d $classes ("@" + $argumentsFile)
Check-Exit 'Java compilation'
$classJar = Join-Path $workRoot 'classes.jar'
& $jar --create --file $classJar -C $classes .
Check-Exit 'Class packaging'
& $java -cp $d8 com.android.tools.r8.D8 --release --min-api 26 --lib $androidJar --output $dex $classJar
Check-Exit 'DEX compilation'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($unsigned, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $classesDex = Join-Path $dex 'classes.dex'
    if (-not (Test-Path -LiteralPath $classesDex)) { throw 'DEX output is missing.' }
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $classesDex, 'classes.dex', [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally { $archive.Dispose() }

$aligned = Join-Path $workRoot 'aligned.apk'
& $zipalign -f 4 $unsigned $aligned
Check-Exit 'APK alignment'
$keystore = Join-Path $toolsRoot 'farmmacro-development.keystore'
if (-not (Test-Path -LiteralPath $keystore)) {
    & $keytool -genkeypair -noprompt -keystore $keystore -storepass android -keypass android -alias farmmacro-development -dname 'CN=Farm Macro Development' -keyalg RSA -keysize 2048 -validity 10000
    Check-Exit 'Development signing key creation'
}
$output = Join-Path $dist 'FlowMacro-Android.apk'
& $java -jar $signer sign --ks $keystore --ks-key-alias farmmacro-development --ks-pass pass:android --key-pass pass:android --out $output $aligned
Check-Exit 'APK signing'
& $zipalign -c 4 $output
Check-Exit 'Signed APK alignment verification'
& $java -jar $signer verify --verbose --print-certs $output
Check-Exit 'APK signature verification'
$permissions = & $aapt dump permissions $output
Check-Exit 'APK permission inspection'
if ($permissions -match 'android.permission.INTERNET') { throw 'Unexpected INTERNET permission in the APK.' }
& $aapt dump badging $output | Select-String 'package:|sdkVersion:|targetSdkVersion:|application-label:|launchable-activity:'
Check-Exit 'APK metadata inspection'
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
[System.IO.File]::WriteAllText((Join-Path $dist 'FlowMacro-Android.apk.sha256'), $hash + '  FlowMacro-Android.apk' + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $output -Destination (Join-Path $dist 'FarmMacro-Android.apk') -Force
[System.IO.File]::WriteAllText((Join-Path $dist 'FarmMacro-Android.apk.sha256'), $hash + '  FarmMacro-Android.apk' + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $projectRoot 'android/README.md') -Destination (Join-Path $dist 'README.md') -Force
Write-Output "Built and signature-verified Android APK: $output"
