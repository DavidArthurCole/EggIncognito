plugins {
    kotlin("jvm") version "2.0.21"
    application
    id("com.github.johnrengelman.shadow") version "8.1.1"
}
repositories { mavenCentral() }
dependencies {
    implementation("io.ktor:ktor-server-core:3.0.0")
    implementation("io.ktor:ktor-server-netty:3.0.0")
    implementation("ch.qos.logback:logback-classic:1.5.8")
}
application { mainClass.set("ServerKt") }
