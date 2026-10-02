import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// Datos de la llave de firma de release. android/key.properties NO se versiona (ver docs/release.md):
//   storeFile=<ruta al .jks, relativa a android/>  storePassword=...  keyAlias=...  keyPassword=...
val claves = Properties().apply {
    val archivo = rootProject.file("key.properties")
    if (archivo.exists()) archivo.inputStream().use { load(it) }
}
val hayLlaveDeRelease = claves.isNotEmpty()

android {
    // El namespace es el paquete del código (MainActivity); el applicationId es la identidad de la app instalada.
    namespace = "co.com.boterosoto.traza_autos"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // Identidad propia (distinta de la app Expo, co.com.boterosoto.inventariovin) para que ambas puedan convivir
        // durante la ventana de transición.
        applicationId = "co.com.boterosoto.trazaautos"
        // flutter_secure_storage exige API 23 o superior; la web de descarga anuncia Android 7.0 (API 24).
        minSdk = maxOf(flutter.minSdkVersion, 24)
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    // AGP 9 exige habilitar los valores de recurso para que cada sabor defina su app_name.
    buildFeatures {
        resValues = true
    }

    flavorDimensions += "entorno"
    productFlavors {
        create("dev") {
            dimension = "entorno"
            applicationIdSuffix = ".dev"
            versionNameSuffix = "-dev"
            resValue("string", "app_name", "Traza Autos DEV")
        }
        create("qa") {
            dimension = "entorno"
            applicationIdSuffix = ".qa"
            versionNameSuffix = "-qa"
            resValue("string", "app_name", "Traza Autos QA")
        }
        create("prod") {
            dimension = "entorno"
            resValue("string", "app_name", "Traza Autos 360")
        }
    }

    signingConfigs {
        if (hayLlaveDeRelease) {
            create("release") {
                storeFile = rootProject.file(claves.getProperty("storeFile"))
                storePassword = claves.getProperty("storePassword")
                keyAlias = claves.getProperty("keyAlias")
                keyPassword = claves.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            // Con llave propia se firma con ella. Sin ella solo se permiten los sabores de prueba (dev, qa), que usan la
            // llave de depuración; el sabor prod se niega a compilar (ver la comprobación más abajo).
            signingConfig = if (hayLlaveDeRelease) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }
}

// Un APK de producción firmado con la llave de depuración no sirve: cualquiera podría reemplazarlo.
gradle.taskGraph.whenReady {
    val compilaProduccion = allTasks.any { it.name.matches(Regex("(assemble|bundle|package).*ProdRelease", RegexOption.IGNORE_CASE)) }
    if (compilaProduccion && !hayLlaveDeRelease) {
        throw GradleException("Falta android/key.properties: el sabor prod en release debe firmarse con la llave propia. Ver mobile/docs/release.md.")
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}
