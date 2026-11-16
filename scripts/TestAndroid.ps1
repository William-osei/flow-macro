param([string]$JdkHome)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ([String]::IsNullOrWhiteSpace($JdkHome)) {
    $toolFile = Join-Path $projectRoot 'build/android-tools/tools.json'
    if (Test-Path -LiteralPath $toolFile) { $JdkHome = (Get-Content -LiteralPath $toolFile -Raw | ConvertFrom-Json).JdkHome }
    elseif (-not [String]::IsNullOrWhiteSpace($env:JAVA_HOME)) { $JdkHome = $env:JAVA_HOME }
    else { throw 'Prepare the project Android tools or set JAVA_HOME to a JDK 17 installation.' }
}
$java = Join-Path $JdkHome 'bin/java.exe'
$javac = Join-Path $JdkHome 'bin/javac.exe'
foreach ($tool in @($java, $javac)) { if (-not (Test-Path -LiteralPath $tool)) { throw "Missing Java tool: $tool" } }

# org.json is supplied by Android on devices. This pinned jar is for JVM tests only.
$dependencyFolder = Join-Path $projectRoot 'build/test-deps'
New-Item -ItemType Directory -Path $dependencyFolder -Force | Out-Null
$jsonJar = Join-Path $dependencyFolder 'json-20260814.jar'
$expectedHash = '7DD2DE1533E766E2B6C07D8194DA85145AD313A167A4760050F5C9B6839604FE'
if (-not (Test-Path -LiteralPath $jsonJar)) {
    $temporaryJar = Join-Path $dependencyFolder ([Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        Invoke-WebRequest -Uri 'https://repo.maven.apache.org/maven2/org/json/json/20260814/json-20260814.jar' -OutFile $temporaryJar -UseBasicParsing
        if ((Get-FileHash -LiteralPath $temporaryJar -Algorithm SHA256).Hash -ne $expectedHash) { throw 'The Android test dependency checksum differs.' }
        [IO.File]::Move($temporaryJar, $jsonJar)
    } finally { if (Test-Path -LiteralPath $temporaryJar) { Remove-Item -LiteralPath $temporaryJar } }
}
if ((Get-FileHash -LiteralPath $jsonJar -Algorithm SHA256).Hash -ne $expectedHash) { throw 'The cached Android test dependency checksum differs.' }
$classes = Join-Path $projectRoot ('build/android-host-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $classes -Force | Out-Null
$sources = @('android/app/src/main/java/dev/farmmacro/app/TapAction.java', 'android/app/src/main/java/dev/farmmacro/app/TapProfile.java', 'android/app/src/main/java/dev/farmmacro/app/TapScheduler.java', 'android/app/src/main/java/dev/farmmacro/app/GestureGeometry.java', 'android/tests/TapSchedulerTest.java', 'android/tests/TapProfileTest.java', 'android/tests/GestureGeometryTest.java') | ForEach-Object { Join-Path $projectRoot $_ }
& $javac -encoding UTF-8 -cp $jsonJar -d $classes $sources
if ($LASTEXITCODE -ne 0) { throw 'Android host test compilation failed.' }
$classpath = $classes + [IO.Path]::PathSeparator + $jsonJar
foreach ($suite in @('TapSchedulerTest','TapProfileTest','GestureGeometryTest')) {
    & $java -cp $classpath $suite
    if ($LASTEXITCODE -ne 0) { throw "Android $suite tests failed." }
}
