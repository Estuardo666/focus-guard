param(
    [Parameter(Mandatory)][string]$JdkRoot,
    [Parameter(Mandatory)][string]$AndroidPlatformRoot,
    [Parameter(Mandatory)][string]$AndroidBuildToolsRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$SignForLocalTesting
)
$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$java = Join-Path $JdkRoot 'bin/java.exe'
$javac = Join-Path $JdkRoot 'bin/javac.exe'
$jar = Join-Path $JdkRoot 'bin/jar.exe'
$androidJar = Join-Path $AndroidPlatformRoot 'android.jar'
foreach ($probeFile in @($java,$javac,$jar,$androidJar)) {
    if (-not (Test-Path -LiteralPath $probeFile -PathType Leaf)) { throw "Missing prerequisite: $probeFile" }
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($out) | Out-Null
$classes = Join-Path $out 'classes'
$dex = Join-Path $out 'dex'
$jnaJar = Join-Path $out 'jna/classes.jar'
if(-not(Test-Path -LiteralPath $jnaJar)){throw 'Verified JNA Android AAR must be prepared first'}
[IO.Directory]::CreateDirectory($classes) | Out-Null
[IO.Directory]::CreateDirectory($dex) | Out-Null
$jcsSources=@(Get-ChildItem -LiteralPath (Join-Path $probeRoot 'jcs-reference') -Filter '*.java' | Select-Object -ExpandProperty FullName)
if($jcsSources.Count -ne 3){throw 'Pinned Java JCS reference sources are missing'}
& $javac --release 17 -encoding UTF-8 -Xlint:deprecation -Werror -classpath "$androidJar;$jnaJar" -d $classes (Join-Path $probeRoot 'src/app/focusguard/phase0/ProbeActivity.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/CryptoRuntimeProbe.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidSodium.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidDeviceIdentity.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidIdentityRuntimeProbe.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidContentDocumentCodec.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidE2eeRuntimeProbe.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidSignedKeyEnvelopeCodec.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidKeyEnvelopeRuntimeProbe.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidMembershipVerifier.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidMembershipRuntimeProbe.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidDeviceVault.java') (Join-Path $probeRoot 'src/app/focusguard/phase0/AndroidVaultRuntimeProbe.java') @jcsSources
if ($LASTEXITCODE) { throw 'javac failed' }
$classJar = Join-Path $out 'probe-classes.jar'
& $jar --create --file $classJar -C $classes .
if ($LASTEXITCODE) { throw 'jar failed' }
& $java -cp (Join-Path $AndroidBuildToolsRoot 'lib/d8.jar') com.android.tools.r8.D8 --lib $androidJar --min-api 26 --output $dex $classJar $jnaJar
if ($LASTEXITCODE) { throw 'd8 failed' }
$apk = Join-Path $out 'focusguard-phase0-unsigned.apk'
& (Join-Path $AndroidBuildToolsRoot 'aapt2.exe') link -I $androidJar --manifest (Join-Path $probeRoot 'AndroidManifest.xml') -o $apk
if ($LASTEXITCODE) { throw 'aapt2 link failed' }
& $jar --update --file $apk -C $dex classes.dex
if ($LASTEXITCODE) { throw 'dex packaging failed' }
& $jar --update --file $apk -C (Join-Path $out 'jna') lib
if ($LASTEXITCODE) { throw 'native packaging failed' }
Get-FileHash -LiteralPath $apk -Algorithm SHA256
Write-Output 'Built unsigned capability probe. Not installed; does not enforce blocking.'
if ($SignForLocalTesting) {
    # Public development password for an isolated, disposable test certificate.
    # Never use this keystore/password for production, identity or sync encryption.
    $keystore = Join-Path $out 'phase0-development-only.p12'
    if (-not (Test-Path -LiteralPath $keystore)) {
        & (Join-Path $JdkRoot 'bin/keytool.exe') -genkeypair -alias phase0 -keystore $keystore -storetype PKCS12 -storepass phase0-development-only -keyalg RSA -keysize 2048 -validity 30 -dname 'CN=FocusGuard Phase0 Disposable Test'
        if ($LASTEXITCODE) { throw 'Test key generation failed' }
    }
    $aligned = Join-Path $out 'phase0-aligned.apk'
    & (Join-Path $AndroidBuildToolsRoot 'zipalign.exe') -f 4 $apk $aligned
    if ($LASTEXITCODE) { throw 'zipalign failed' }
    $signed = Join-Path $out 'focusguard-phase0-test.apk'
    & $java -jar (Join-Path $AndroidBuildToolsRoot 'lib/apksigner.jar') sign --ks $keystore --ks-pass pass:phase0-development-only --ks-key-alias phase0 --out $signed $aligned
    if ($LASTEXITCODE) { throw 'Test signing failed' }
    & $java -jar (Join-Path $AndroidBuildToolsRoot 'lib/apksigner.jar') verify --verbose $signed
    if ($LASTEXITCODE) { throw 'APK signature verification failed' }
    Get-FileHash -LiteralPath $signed -Algorithm SHA256
    Write-Output 'Signed with disposable local test certificate, NOT production. Installation requires device owner.'
}
