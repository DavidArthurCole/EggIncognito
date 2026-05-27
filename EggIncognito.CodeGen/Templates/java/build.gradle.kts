plugins {
    java
    application
    id("com.github.johnrengelman.shadow") version "8.1.1"
}

repositories { mavenCentral() }

dependencies {
    implementation("io.javalin:javalin:6.3.0")
    implementation("org.slf4j:slf4j-simple:2.0.13")
}

application {
    mainClass.set("com.egginc.mock.Server")
}
