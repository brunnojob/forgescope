FROM maven:3.9-eclipse-temurin-21 AS build
WORKDIR /workspace
COPY policy-service/pom.xml policy-service/pom.xml
COPY policy-service/src/ policy-service/src/
COPY src/main/ src/main/
COPY third_party/ third_party/
RUN mvn -B -f policy-service/pom.xml verify

FROM eclipse-temurin:21-jre
WORKDIR /app
COPY --from=build /workspace/policy-service/target/policy-service-0.1.0.jar app.jar
USER 10001:10001
ENTRYPOINT ["java", "-jar", "/app/app.jar"]
