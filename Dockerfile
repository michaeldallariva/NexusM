# ═══════════════════════════════════════════════════════════
# NexusM - Docker Container (Phase 2 - Linux Deployment)
# ═══════════════════════════════════════════════════════════
# Build:  docker build -t nexusm .
# Run:    docker run -d -p 8501:8501 -v /path/to/music:/music -v nexusm-data:/app/data --name nexusm nexusm
# ═══════════════════════════════════════════════════════════

# ─── Build Stage ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY NexusM.csproj .
RUN dotnet restore

# Copy everything and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ─── Runtime Stage ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install FFmpeg for transcoding support (optional)
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && \
    rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Create data and logs directories
RUN mkdir -p /app/data /app/logs

# Default music mount point
VOLUME ["/music"]
# Persist database and config
VOLUME ["/app/data"]

# Expose default port
EXPOSE 8501

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8501/api/stats || exit 1

# Environment variables (can override config)
ENV ASPNETCORE_URLS=http://+:8501
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "NexusM.dll"]
