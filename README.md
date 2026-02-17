# Storarr - Tiered Media Storage Manager

A containerized web application that manages automatic transitions between **symlinks** (streaming via NZB-Dav) and **actual .mkv files** (local cache) for Jellyfin media libraries.

## Features

- **Automatic state transitions**: Converts symlinks to MKV files for frequently watched content
- **Smart caching**: Keeps recently watched content as local files, streams the rest
- **Webhook support**: Integrates with Jellyseerr, Sonarr, and Radarr for real-time updates
- **Real-time updates**: SignalR-powered dashboard updates
- **arr-style UI**: Familiar interface for users of the *arr ecosystem

## The Stack

```
Jellyseerr → Sonarr/Radarr → NZB-Dav (priority 1) → symlink → streams from usenet
                                  ↓ (priority 49/50)
                         qBittorrent / SABnzbd → actual .mkv download
```

## Quick Start

### Using Docker Compose

```bash
# Clone the repository
git clone https://github.com/yourname/storarr.git
cd storarr

# Build and run
docker-compose up -d

# Access the UI at http://localhost:8686
```

### Manual Build

```bash
# Build frontend
cd src/Storarr.Frontend
npm ci
npm run build

# Build backend
cd ../Storarr
dotnet publish -c Release

# Run
dotnet bin/Release/net5.0/Storarr.dll
```

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DataDirectory` | `./data` | Directory for SQLite database |
| `TZ` | `UTC` | Timezone |
| `PUID` | `1000` | User ID for file permissions |
| `PGID` | `1000` | Group ID for file permissions |

### Web UI Configuration

Access the Settings page in the UI to configure:

- **Jellyfin**: URL and API key for watch status tracking
- **Jellyseerr**: URL and API key for request management
- **Sonarr**: URL and API key for TV show downloads
- **Radarr**: URL and API key for movie downloads
- **Transition Thresholds**: Days before each transition type

## How It Works

### Symlink → MKV Transition

1. Media is tracked as a symlink (streaming from NZB-Dav)
2. After being unwatched for X days, Storarr triggers a download
3. The symlink is deleted, and Sonarr/Radarr downloads the actual MKV file
4. The file is now available locally for faster playback

### MKV → Symlink Transition

1. Media is tracked as a local MKV file
2. After being inactive for Y days, Storarr restores the symlink
3. The MKV file is deleted
4. A new Jellyseerr request triggers NZB-Dav to create a new symlink

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v1/dashboard` | Dashboard statistics |
| GET | `/api/v1/media` | List all tracked media |
| GET | `/api/v1/media/{id}` | Get single media item |
| POST | `/api/v1/media/{id}/force-download` | Trigger MKV download |
| POST | `/api/v1/media/{id}/force-symlink` | Restore symlink |
| GET | `/api/v1/config` | Get configuration |
| PUT | `/api/v1/config` | Update configuration |
| POST | `/api/v1/config/test` | Test service connections |
| GET | `/api/v1/queue` | Active downloads |
| GET | `/api/v1/activity` | Activity log |
| POST | `/api/v1/webhooks/jellyseerr` | Jellyseerr webhook |
| POST | `/api/v1/webhooks/sonarr` | Sonarr webhook |
| POST | `/api/v1/webhooks/radarr` | Radarr webhook |

## Webhooks

Configure webhooks in your services to point to Storarr:

- **Jellyseerr**: `http://storarr:8686/api/v1/webhooks/jellyseerr`
- **Sonarr**: `http://storarr:8686/api/v1/webhooks/sonarr`
- **Radarr**: `http://storarr:8686/api/v1/webhooks/radarr`

## Development

### Prerequisites

- .NET 5.0 SDK
- Node.js 20+
- SQLite

### Running in Development

```bash
# Terminal 1: Backend
cd src/Storarr
dotnet run

# Terminal 2: Frontend
cd src/Storarr.Frontend
npm run dev
```

The API runs on `http://localhost:8686` and the React dev server on `http://localhost:3000`.

## License

MIT License - see LICENSE file for details.

## Acknowledgments

- Inspired by the *arr ecosystem (Sonarr, Radarr, Lidarr, etc.)
- Built with ASP.NET Core and React
- UI inspired by the arr-style interface design
