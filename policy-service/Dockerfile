FROM maven:3.9-eclipse-temurin-21 AS build
WORKDIR /workspace
COPY policy-service/pom.xml policy-service/pom.xml
COPY policy-service/src/ policy-service/src/
COPY src/main/ src/main/
COPY third_party/ third_party/
RUN mvn -B -f policy-service/pom.xml verify

# PatchScope uses the javac tree APIs at runtime to analyze Java source. Keep
# the compiler modules available in the production image.
FROM eclipse-temurin:21-jdk
WORKDIR /app
COPY --from=build /workspace/policy-service/target/policy-service-0.1.0.jar app.jar
USER 10001:10001
ENTRYPOINT ["java", "-jar", "/app/app.jar"]
