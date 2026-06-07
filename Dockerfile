# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore (clean restore inside Linux container, ignores Windows NuGet cache)
COPY McpServer/McpServer.csproj McpServer/
RUN dotnet restore McpServer/McpServer.csproj

# Copy project files only (not the solution root)
COPY McpServer/ McpServer/
RUN dotnet publish McpServer/McpServer.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Don't run as root
RUN useradd -m appuser
USER appuser

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "McpServer.dll"]