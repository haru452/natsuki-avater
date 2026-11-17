plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.haru452.vrctranslator"
    compileSdk = 35
    buildToolsVersion = "34.0.0"
    defaultConfig {
        applicationId = "com.haru452.vrctranslator"
        minSdk = 29
        targetSdk = 35
        versionCode = 1
        versionName = "0.1"
        ndk { abiFilters += "arm64-v8a" }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true }
    packaging { jniLibs { useLegacyPackaging = true } }
}

dependencies {
    implementation(files("libs/sherpa-onnx-static-link-onnxruntime-2.0.1.aar"))
    implementation("com.atilika.kuromoji:kuromoji-ipadic:0.9.0")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation(platform("androidx.compose:compose-bom:2024.10.01"))
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    testImplementation("junit:junit:4.13.2")
}
