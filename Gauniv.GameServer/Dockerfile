# Build stage
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

# Restore only the game server project first
COPY Gauniv.GameServer/Gauniv.GameServer.csproj Gauniv.GameServer/
RUN dotnet restore Gauniv.GameServer/Gauniv.GameServer.csproj

# Copy the remaining sources and publish
COPY Gauniv.GameServer/ Gauniv.GameServer/
WORKDIR /src/Gauniv.GameServer
RUN dotnet publish Gauniv.GameServer.csproj -c Release -o /app/publish --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/nightly/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 7777
ENV GAMESERVER_PORT=7777
ENTRYPOINT ["dotnet", "Gauniv.GameServer.dll"]
