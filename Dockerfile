FROM eclipse-temurin:21-jdk-jammy AS java
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
COPY --from=java /opt/java/openjdk /opt/java/openjdk
ENV JAVA_HOME=/opt/java/openjdk
ENV PATH="/opt/java/openjdk/bin:${PATH}"
WORKDIR /src
COPY . .
RUN dotnet publish dotnet/ForgeScope.Hub -c Release -o /out/hub --nologo -m:1 \
    && dotnet publish dotnet/ForgeScope.Worker -c Release -o /out/worker --nologo -m:1 \
    && java tools/Build.java

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
COPY --from=java /opt/java/openjdk /opt/java/openjdk
ENV JAVA_HOME=/opt/java/openjdk
ENV PATH="/opt/java/openjdk/bin:${PATH}"
ENV ASPNETCORE_URLS=http://+:8080
ENV FORGESCOPE_JAR=/app/analyzer/forgescope.jar
ENV FORGESCOPE_WORKER=/app/worker/ForgeScope.Worker.dll
WORKDIR /app
COPY --from=build /out/hub ./hub
COPY --from=build /out/worker ./worker
COPY --from=build /src/build/forgescope.jar ./analyzer/forgescope.jar
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/hub/ForgeScope.Hub.dll"]
