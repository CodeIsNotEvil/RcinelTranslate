// The Java client: reads the shipped translation files, nothing else. Java 8 bytecode and no
// dependencies, so it fits Android apps, old servers and anything in between.
plugins {
    `java-library`
    `maven-publish`
}

java {
    withSourcesJar()
    withJavadocJar()
}

tasks.withType<JavaCompile>().configureEach {
    options.release.set(8)
    options.encoding = "UTF-8"
    // JDK 21 warns that release 8 is old; that's the point here.
    options.compilerArgs.addAll(listOf("-Xlint:all", "-Xlint:-options", "-Werror"))
}

tasks.withType<Javadoc>().configureEach {
    (options as StandardJavadocDocletOptions).addStringOption("Xdoclint:none", "-quiet")
}

repositories {
    mavenCentral()
}

dependencies {
    testImplementation(platform("org.junit:junit-bom:5.13.4"))
    testImplementation("org.junit.jupiter:junit-jupiter")
    // Only to read spec/fixtures/cases.json in the tests; the library itself has no dependencies.
    testImplementation("com.google.code.gson:gson:2.13.1")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

tasks.test {
    useJUnitPlatform()
    // The conformance cases the C# and JavaScript clients run too.
    systemProperty("rcinel.fixtures", rootDir.resolve("../spec/fixtures").canonicalPath)
    inputs.dir("../spec/fixtures")
}

publishing {
    publications {
        create<MavenPublication>("maven") {
            artifactId = "rcinel-translate"
            from(components["java"])
            pom {
                name.set("RcinelTranslate")
                description.set("Loads a translation set for a language and translates with Rcinel.translate(\"UUIDv7\", \"English text\").")
                url.set("https://github.com/CodeIsNotEvil/RcinelTranslate")
                licenses {
                    license {
                        name.set("MIT")
                        url.set("https://opensource.org/licenses/MIT")
                    }
                }
                developers {
                    developer {
                        id.set("CodeIsNotEvil")
                        name.set("CodeIsNotEvil")
                    }
                }
                scm {
                    url.set("https://github.com/CodeIsNotEvil/RcinelTranslate")
                }
            }
        }
    }
}
