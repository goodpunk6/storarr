# Storarr - Tiered Media Storage Manager

[![Docker](https://img.shields.io/badge/docker-supported-blue)](https://www.docker.com/)
[![.NET](https://img.shields.io/badge/.NET-5.0-512BD4)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-18-61DAFB)](https://reactjs.org/)

A containerized web application that orchestrates automatic transitions between **.strm streaming files** and **actual .mkv files** (local storage) for Jellyfin media libraries.

> **Note:** While originally designed for NZB-Dav, Storarr works with **any WebDAV-based streaming solution** that generates .strm files recognizable by Jellyfin (e.g., rclone mount, plexdrive, cloud-based streaming services). The power of Storarr comes from leveraging your existing *arr stack (Sonarr/Radarr) to handle the actual file management.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Library Modes](#library-modes)
- [How It Works](#how-it-works)
- [API Reference](#api-reference)
- [Download Clients](#download-clients)
- [Webhooks](#webhooks)
- [Development](#development)
- [Troubleshooting](#troubleshooting)
- [Acknowledgments](#acknowledgments)

## Overview

Storarr bridges the gap between streaming and local storage for media libraries. It intelligently manages your media by:

1. **Tracking watch history** via Jellyfin
2. **Converting symlinks to MKV** when content is frequently watched
3. **Restoring symlinks** when content becomes inactive
4. **Integrating seamlessly** with Sonarr, Radarr, and Jellyseerr

### The Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│                      STORARR WORKFLOW                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  NEW CONTENT                                                    │
│      │                                                          │
│      ▼                                                          │
│  ┌─────────┐    Unwatched     ┌─────────┐                      │
│  │ Symlink │ ───────────────▶ │   MKV   │  (Download)          │
│  │ (.strm) │    7+ days       │  File   │                      │
│  └─────────┘                  └─────────┘                      │
│      ▲                             │                           │
│      │                             │ Inactive                  │
│      │        Restore              │ 30+ days                  │
│      └─────────────────────────────┘                           │
│              (Delete & Re-request)                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### WebDAV/STRM Compatibility

Storarr is designed to work with **any streaming solution** that uses `.strm` files:

| Solution | Type | How It Works |
|----------|------|--------------|
| **NZB-Dav** | Usenet WebDAV | Streams directly from usenet providers |
| **rclone mount** | Cloud storage | Mounts Google Drive, Dropbox, etc. as WebDAV |
| **plexdrive** | Google Drive | Optimized Drive mounting for media |
| **Any WebDAV server** | Generic | Any server that serves media via WebDAV |

The key requirement is that your streaming solution generates `.strm` files that Jellyfin can read. When a `.strm` file is played, Jellyfin reads the URL inside and streams from that location.

**How it leverages your arr stack:**

Storarr doesn't download files itself - it orchestrates your existing infrastructure:

1. **Sonarr/Radarr** handle all download management
2. **Jellyfin** tracks watch history
3. **Jellyseerr** manages content requests
4. **Your download clients** (qBittorrent, SABnzbd, etc.) do the actual downloading

Storarr simply tells Sonarr/Radarr when to download or delete files based on watch patterns, making it a lightweight orchestration layer on top of your existing setup.

## Features

### Core Features

- **Automatic State Transitions**: Converts between symlinks and MKV files based on watch history
- **Flexible Time Periods**: Configure thresholds in minutes, hours, days, weeks, or months
- **Watch History Tracking**: Integrates with Jellyfin to track when content was last watched
- **Real-time Updates**: SignalR-powered dashboard with live progress updates
- **Activity Logging**: Complete audit trail of all transitions and changes

### Media Management

- **Individual File Controls**: Exclude specific movies/episodes from auto-transitions
- **Manual Overrides**: Force transitions on demand via UI or API
- **STRM File Support**: Treats .strm files as symlinks for streaming integrations
- **Multi-format Support**: Handles .mkv, .mp4, .avi, .wmv, and .strm files

### Service Integration

- **Jellyfin**: Watch status tracking and library scanning
- **Jellyseerr**: Request management for symlink restoration
- **Sonarr**: TV show and anime download management
- **Radarr**: Movie download management

### Download Client Support

- **qBittorrent**: Full queue monitoring support
- **SABnzbd**: Full queue monitoring support
- **Transmission**: Full queue monitoring support
- **Multiple Instances**: Support for up to 3 download clients simultaneously

### User Interface

- **First-Run Wizard**: Guided setup for new installations
- **arr-style Design**: Familiar interface for users of the *arr ecosystem
- **Responsive Layout**: Works on desktop and mobile devices
- **Real-time Queue Display**: Live download progress from both arr services and download clients

## Quick Start

### Using Docker Compose

1. Copy the example compose file:
```bash
cp docker-compose.example.yml docker-compose.yml
```

2. Edit `docker-compose.yml` and set your media path:
```yaml
volumes:
  - /path/to/your/media:/media:rw  # Change this!
```

3. Build and run:
```bash
docker-compose up -d
```

4. Access the UI at `http://localhost:8687`

5. Complete the first-run wizard to configure your services

### Manual Build

```bash
# Build frontend
cd src/Storarr.Frontend
npm ci
npm run build

# Build backend
cd ../Storarr
dotnet publish -c Release -o out

# Run
dotnet out/Storarr.dll
```

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DataDirectory` | `./data` | Directory for SQLite database |
| `TZ` | `UTC` | Timezone for scheduling |
| `PUID` | `1000` | User ID for file permissions |
| `PGID` | `1000` | Group ID for file permissions |

### Web UI Configuration

Access **Settings** in the UI to configure:

#### Media Services
| Service | Required | Purpose |
|---------|----------|---------|
| Jellyfin | **Yes** | Watch status tracking, library scanning |
| Jellyseerr | No | Symlink restoration requests |
| Sonarr | No | TV show downloads |
| Radarr | No | Movie downloads |

#### Transition Thresholds
| Setting | Default | Description |
|---------|---------|-------------|
| Symlink → MKV | 7 days | Unwatched period before downloading |
| MKV → Symlink | 30 days | Inactive period before restoring symlink |

## Library Modes

Storarr supports three library modes, configurable during first-run setup:

### New Content Only (Recommended)
- **Safest option** - Your existing library is never modified
- Only tracks content added after Storarr is installed
- Best for new installations

### Track Existing
- Scans your existing library and tracks watch history
- **Does not auto-transition** existing content
- Manual transitions available via UI
- Good for understanding usage before automation

### Full Automation (Caution)
- Scans existing library and **automatically transitions** files
- Will modify your existing library based on watch history
- Most hands-off but highest risk
- Recommended only after testing with other modes

## How It Works

### Symlink → MKV Transition

1. Media is tracked as a symlink/streaming file
2. After being unwatched for the configured threshold:
   - File is deleted via Sonarr/Radarr API (updates their internal state)
   - Search is triggered to re-download as MKV
   - Status changes to "Downloading"
3. Once download completes:
   - File is verified as MKV
   - Status changes to "Mkv"
   - Activity log entry is created

### MKV → Symlink Transition

1. Media is tracked as a local MKV file
2. After being inactive for the configured threshold:
   - File is deleted via Sonarr/Radarr API
   - Jellyseerr request is created to re-add as symlink
   - Status changes to "PendingSymlink"
3. Once symlink is created:
   - File is detected as symlink
   - Status changes to "Symlink"
   - Activity log entry is created

### File Exclusion

Individual files can be excluded from auto-transitions:
- Click the pause button on any media item
- Excluded items show "PAUSED" status
- Manual transitions still work for excluded items

## API Reference

### Dashboard

```http
GET /api/v1/dashboard
```
Returns symlink/MKV counts, download status, and upcoming transitions.

### Media

```http
GET /api/v1/media                    # List all media
GET /api/v1/media?state=Symlink      # Filter by state
GET /api/v1/media?type=Movie         # Filter by type
GET /api/v1/media/{id}               # Get single item

POST /api/v1/media/{id}/force-download   # Trigger MKV download
POST /api/v1/media/{id}/force-symlink    # Restore symlink
POST /api/v1/media/{id}/toggle-excluded  # Toggle exclusion
PUT /api/v1/media/{id}/excluded          # Set exclusion status
```

### Configuration

```http
GET /api/v1/config           # Get current configuration
PUT /api/v1/config           # Update configuration
POST /api/v1/config/test     # Test all service connections
```

### Queue

```http
GET /api/v1/queue            # Arr queue (Sonarr/Radarr)
GET /api/v1/queue/clients    # Download client queues
```

### Activity

```http
GET /api/v1/activity         # Activity log
GET /api/v1/activity?mediaItemId=1  # Filter by media
```

### Transitions

```http
POST /api/v1/transitions/process   # Manually trigger transition check
```

### Webhooks

```http
POST /api/v1/webhooks/jellyseerr   # Jellyseerr webhook
POST /api/v1/webhooks/sonarr       # Sonarr webhook
POST /api/v1/webhooks/radarr       # Radarr webhook
```

## Download Clients

Storarr can monitor download queues from multiple clients:

### Supported Clients

| Client | Authentication | Queue Monitoring |
|--------|---------------|------------------|
| qBittorrent | Username/Password | ✅ |
| SABnzbd | API Key | ✅ |
| Transmission | Username/Password | ✅ |

### Configuration

1. Go to **Settings** → **Download Clients**
2. Click **Add Client** to add up to 3 clients
3. Select the client type and enter credentials
4. Click **Test Connections** to verify

### Multiple Instances

You can configure multiple instances of the same client type:
- Two qBittorrent instances for different trackers
- Separate SABnzbd for movies vs TV

## Webhooks

Configure webhooks in your services:

### Jellyseerr
```
URL: http://storarr:8686/api/v1/webhooks/jellyseerr
Events: Request Approved, Request Available
```

### Sonarr
```
URL: http://storarr:8686/api/v1/webhooks/sonarr
Events: Download, Import, Delete
```

### Radarr
```
URL: http://storarr:8686/api/v1/webhooks/radarr
Events: Download, Import, Delete
```

## Development

### Prerequisites

- .NET 5.0 SDK
- Node.js 20+
- SQLite

### Project Structure

```
storarr/
├── src/
│   ├── Storarr/                 # Backend (ASP.NET Core)
│   │   ├── Controllers/         # API endpoints
│   │   ├── Services/            # Business logic
│   │   ├── Models/              # Data models
│   │   ├── BackgroundServices/  # Scheduled tasks
│   │   └── Hubs/                # SignalR hubs
│   └── Storarr.Frontend/        # Frontend (React + Vite)
│       └── src/
│           ├── pages/           # Page components
│           ├── components/      # Reusable components
│           └── api/             # API client
├── Dockerfile
└── docker-compose.example.yml
```

### Running in Development

```bash
# Terminal 1: Backend
cd src/Storarr
dotnet run

# Terminal 2: Frontend
cd src/Storarr.Frontend
npm run dev
```

- API: `http://localhost:8686`
- Frontend: `http://localhost:3000`

### Building for Production

```bash
docker-compose build
```

## Troubleshooting

### Common Issues

**Database errors on startup**
- Ensure the data directory is writable
- Check that no other instance is using the database

**Jellyfin connection fails**
- Verify URL and API key are correct
- Check for trailing whitespace in API key
- Use the "Test Connections" button in Settings

**Transitions not happening**
- Check LibraryMode is set to "FullAutomation"
- Verify threshold settings in Settings
- Check logs for errors: `docker logs storarr`

**Files not being deleted**
- Ensure media mount is read-write (`:rw`)
- Check Sonarr/Radarr have proper permissions
- Verify API connectivity to Arr services

### Debug Logging

Enable debug logging in docker-compose:
```yaml
environment:
  - Logging__LogLevel__Default=Debug
  - Logging__LogLevel__Storarr=Debug
```

### Useful Commands

```bash
# View logs
docker logs storarr -f

# Check recent errors
docker logs storarr 2>&1 | grep -i error

# Restart container
docker restart storarr

# Reset database (WARNING: loses all data)
docker-compose down -v
docker-compose up -d
```

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | .NET 5 / ASP.NET Core |
| Database | SQLite / Entity Framework Core |
| Frontend | React 18 + TypeScript + Vite |
| Styling | Tailwind CSS |
| Real-time | SignalR |
| Container | Docker + docker-compose |

## License

MIT License - see LICENSE file for details.

## Acknowledgments

- **[Claude Code](https://claude.ai/code)** - This application was created with the assistance of Claude Code, Anthropic's AI-powered coding assistant. Claude Code helped with architecture design, code implementation, debugging, and documentation.
- Inspired by the *arr ecosystem (Sonarr, Radarr, Lidarr, etc.)
- UI design inspired by arr-style interfaces
- Built with love for the self-hosted community
