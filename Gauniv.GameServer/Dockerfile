# Build stage
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

# Copy the entire project structure
COPY Gauniv.GameServer/*.csproj ./Gauniv.GameServer/
RUN dotnet restore Gauniv.GameServer/Gauniv.GameServer.csproj

# Copy all source files
COPY Gauniv.GameServer/ ./Gauniv.GameServer/
WORKDIR /src/Gauniv.GameServer
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/nightly/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 7777
ENV GAMESERVER_PORT=7777
ENTRYPOINT ["dotnet", "Gauniv.GameServer.dll"]
