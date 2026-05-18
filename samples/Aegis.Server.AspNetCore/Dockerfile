# syntax=docker/dockerfile:1
# Build context must be the repository root (so we can ProjectReference ../../src/*).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore project graph
COPY ["src/Aegis/Aegis.csproj", "src/Aegis/"]
COPY ["src/Aegis.Server/Aegis.Server.csproj", "src/Aegis.Server/"]
COPY ["samples/Aegis.Server.AspNetCore/Aegis.Server.AspNetCore.csproj", "samples/Aegis.Server.AspNetCore/"]
RUN dotnet restore "samples/Aegis.Server.AspNetCore/Aegis.Server.AspNetCore.csproj"

# Copy sources and publish
COPY src/ src/
COPY samples/Aegis.Server.AspNetCore/ samples/Aegis.Server.AspNetCore/
RUN dotnet publish "samples/Aegis.Server.AspNetCore/Aegis.Server.AspNetCore.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /data /app/logs

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "Aegis.Server.AspNetCore.dll"]
