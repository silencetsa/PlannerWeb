FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json ./
COPY src/Planer.Core/Planer.Core.csproj src/Planer.Core/
COPY src/Planer.Data/Planer.Data.csproj src/Planer.Data/
COPY src/PlannerWeb/PlannerWeb.csproj src/PlannerWeb/
RUN dotnet restore src/PlannerWeb/PlannerWeb.csproj
COPY src/ src/
RUN dotnet publish src/PlannerWeb/PlannerWeb.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends gosu && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/ ./
COPY scripts/docker-entrypoint.sh /usr/local/bin/planner-entrypoint
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Planner__DataDirectory=/data \
    Planner__ExternalHttps=true \
    Planner__TimeZone=Asia/Yekaterinburg
RUN mkdir -p /data && chown -R app:app /data /app && chmod +x /usr/local/bin/planner-entrypoint
EXPOSE 8080
ENTRYPOINT ["planner-entrypoint"]
CMD ["dotnet", "PlannerWeb.dll"]

