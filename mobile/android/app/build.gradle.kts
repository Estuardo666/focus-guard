import java.security.MessageDigest

plugins {
    id("com.android.application")
}

val sodiumJniLibs = layout.buildDirectory.dir("generated/focusguardSodiumJniLibs")
val stageFocusGuardSodium by tasks.registering(Sync::class) {
    val userProfile = System.getenv("USERPROFILE")
        ?: throw GradleException("USERPROFILE is required to locate the pinned libsodium package.")
    val runtimes = file("$userProfile/.nuget/packages/libsodium/1.0.22/runtimes")
    val abiSources = linkedMapOf(
        "arm64-v8a" to ("android-arm64/native/libsodium.so" to "F4382F2139F1DDD3A32DCE406C98F2D41B472E02351BD18DF362F8CB29AF1952"),
        "armeabi-v7a" to ("android-arm/native/libsodium.so" to "FA86323E7BA7F483B787F58CA8D388727AEEC0185FEF48BD69E2B1AB05D1D8BA"),
        "x86_64" to ("android-x64/native/libsodium.so" to "B5D4F955A191B0ED72B2B360E5EBEBCD0069912696FD69B71C092F1608332818"),
        "x86" to ("android-x86/native/libsodium.so" to "3999CA602E95FF8902FE2879338F4E3BF447BA49F98690CBFEB309501BC3B087")
    )
    abiSources.forEach { (abi, pair) ->
        val source = runtimes.resolve(pair.first)
        inputs.file(source)
        from(source) { into(abi) }
    }
    into(sodiumJniLibs)
    doFirst {
        abiSources.forEach { (_, pair) ->
            val source = runtimes.resolve(pair.first)
            if (!source.isFile) throw GradleException("Pinned libsodium 1.0.22 runtime is missing: ${pair.first}")
            val digest = MessageDigest.getInstance("SHA-256").digest(source.readBytes())
                .joinToString("") { byte -> "%02X".format(byte.toInt() and 0xff) }
            if (digest != pair.second) throw GradleException("Pinned libsodium runtime checksum mismatch: ${pair.first}")
        }
    }
}

android {
    namespace = "com.totemmassmedia.focusguard"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.totemmassmedia.focusguard"
        minSdk = 26
        targetSdk = 36
        versionCode = 3
        versionName = "0.2.0-alpha.3"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        manifestPlaceholders["appAuthRedirectScheme"] = "com.totemmassmedia.focusguard"
        buildConfigField("String", "FOCUSGUARD_ISSUER", "\"https://auth.focusguard.totemmassmedia.com/realms/focusguard\"")
        buildConfigField("String", "FOCUSGUARD_API_ORIGIN", "\"https://api.focusguard.totemmassmedia.com\"")
        buildConfigField("String", "FOCUSGUARD_OIDC_CLIENT_ID", "\"focusguard-android\"")
        buildConfigField("String", "FOCUSGUARD_REDIRECT_URI", "\"com.totemmassmedia.focusguard:/oauth2redirect\"")
    }

    buildFeatures {
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    sourceSets {
        getByName("main").java {
            srcDir(fileTree("../../../prototypes/cloud-phase0/android/src") {
                include("app/focusguard/phase0/AndroidDeviceVault.java")
                include("app/focusguard/phase0/AndroidDeviceIdentity.java")
                include("app/focusguard/phase0/AndroidSodium.java")
                include("app/focusguard/phase0/AndroidSignedKeyEnvelopeCodec.java")
                include("app/focusguard/phase0/AndroidContentDocumentCodec.java")
                include("app/focusguard/phase0/AndroidMembershipVerifier.java")
                include("app/focusguard/phase0/AndroidPairingAcceptance.java")
            })
            srcDir(fileTree("../../../prototypes/cloud-phase0/android/jcs-reference") { include("*.java") })
        }
        getByName("main").jniLibs.srcDir(sodiumJniLibs)
    }

}

tasks.named("preBuild").configure { dependsOn(stageFocusGuardSodium) }

configurations.configureEach {
    resolutionStrategy.force(
        "androidx.annotation:annotation:1.11.0",
        "androidx.appcompat:appcompat:1.8.0",
        "androidx.browser:browser:1.10.0"
    )
}

dependencies {
    implementation("net.openid:appauth:0.11.1")
    implementation("net.java.dev.jna:jna:5.19.1@aar")
    implementation("androidx.annotation:annotation:1.11.0")
    implementation("androidx.appcompat:appcompat:1.8.0")
    implementation("androidx.browser:browser:1.10.0")
    androidTestImplementation("androidx.test:runner:1.7.0")
    androidTestImplementation("androidx.test.ext:junit:1.3.0")
}
