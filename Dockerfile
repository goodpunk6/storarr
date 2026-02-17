# Build frontend
FROM node:20-alpine AS frontend-build
WORKDIR /app
COPY src/Storarr.Frontend/package*.json ./
RUN npm install
COPY src/Storarr.Frontend ./
RUN npm run build

# Build backend
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS backend-build
WORKDIR /app
COPY src/Storarr/Storarr.csproj ./Storarr/
COPY Storarr.sln ./
RUN dotnet restore Storarr/Storarr.csproj
COPY src/Storarr ./Storarr/
COPY --from=frontend-build /app/dist ./Storarr/wwwroot
WORKDIR /app/Storarr
RUN dotnet publish -c Release -o out

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR /app

# Create non-root user (Debian-based)
RUN useradd -u 1000 -g 1000 -m -s /bin/bash storarr || true

# Create directories
RUN mkdir -p /config /data && \
    chown -R 1000:1000 /app /config /data

COPY --from=backend-build --chown=1000:1000 /app/Storarr/out ./

ENV ASPNETCORE_URLS=http://+:8686
ENV DataDirectory=/data

EXPOSE 8686

USER 1000

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8686/api/v1/dashboard || exit 1

ENTRYPOINT ["dotnet", "Storarr.dll"]
