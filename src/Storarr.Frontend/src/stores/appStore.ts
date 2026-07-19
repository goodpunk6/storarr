import { create } from 'zustand'

export interface MediaItem {
  id: number
  title: string
  type: 'Movie' | 'Series' | 'Anime'
  currentState: 'Symlink' | 'Mkv' | 'Downloading' | 'PendingSymlink' | 'Error'
  filePath: string
  lastWatchedAt?: string
  seasonNumber?: number
  episodeNumber?: number
  fileSize?: number
  daysUntilTransition?: number
  isExcluded: boolean
  disableAutoToMkv: boolean
  disableAutoToSymlink: boolean
  createdAt?: string
  stateChangedAt?: string
  sonarrId?: number
  radarrId?: number
  tmdbId?: number
  tvdbId?: number
}

export interface QueueItem {
  downloadId: string
  title: string
  status: string
  size: number
  sizeLeft: number
  progress: number
  errorMessage?: string
  source: string
  mediaItemId: number
}

export interface Transition {
  mediaItemId: number
  title: string
  currentState: string
  targetState: string
  daysUntilTransition: number
  transitionDate?: string
}

export interface ActivityLog {
  id: number
  mediaItemId: number
  mediaTitle?: string
  action: string
  fromState: string
  toState: string
  details?: string
  timestamp: string
}

export interface ExcludedItem {
  id: number
  title: string
  type: 'Movie' | 'Series' | 'Anime'
  tmdbId?: number
  tvdbId?: number
  sonarrId?: number
  radarrId?: number
  reason?: string
  createdAt: string
  removedMediaCount?: number
}

export interface CatalogGroupDto {
  title: string
  type: 'Movie' | 'Series' | 'Anime'
  sonarrId?: number
  radarrId?: number
  tmdbId?: number
  tvdbId?: number
  posterUrl?: string
  totalEpisodes: number
  trackedEpisodes: number
  totalSizeBytes: number
  formattedSize: string
  stateBreakdown: Record<string, number>
  isExcluded: boolean
  episodes: CatalogEpisodeDto[]
}

export interface CatalogEpisodeDto {
  mediaItemId?: number
  seasonNumber?: number
  episodeNumber?: number
  title: string
  currentState: string
  fileSize?: number
  isExcluded: boolean
  filePath?: string
}

export interface EnsureTrackedRequestDto {
  sonarrId?: number
  radarrId?: number
  type: 'Movie' | 'Series' | 'Anime'
  title: string
  seasonNumber?: number
  episodeNumber?: number
  tmdbId?: number
  filePath: string
}

export interface EnsureTrackedResponseDto {
  mediaItemId: number
  created: boolean
}

export type TimeUnit = 'Minutes' | 'Hours' | 'Days' | 'Weeks' | 'Months'
export type LibraryMode = 'NewContentOnly' | 'TrackExisting' | 'FullAutomation'
export type DownloadClientType = 'QBittorrent' | 'Transmission' | 'Sabnzbd'
export type DownloadOrder = 'StrmFirst' | 'MkvFirst'

export interface DownloadClientConfig {
  enabled: boolean
  type: DownloadClientType
  url?: string
  username?: string
  password?: string
  apiKey?: string
}

export interface Config {
  firstRunComplete: boolean
  libraryMode: LibraryMode
  symlinkToMkvValue: number
  symlinkToMkvUnit: TimeUnit
  mkvToSymlinkValue: number
  mkvToSymlinkUnit: TimeUnit
  preferredDownloadOrder: DownloadOrder
  mediaLibraryPath: string
  // Multi-drive storage
  multiDriveEnabled: boolean
  symlinkStoragePath?: string
  mkvStoragePath?: string
  sonarrSymlinkRootFolder?: string
  sonarrMkvRootFolder?: string
  radarrSymlinkRootFolder?: string
  radarrMkvRootFolder?: string
  // Services
  jellyfinUrl?: string
  jellyfinApiKey?: string
  jellyseerrUrl?: string
  jellyseerrApiKey?: string
  sonarrUrl?: string
  sonarrApiKey?: string
  radarrUrl?: string
  radarrApiKey?: string
  downloadClient1: DownloadClientConfig
  downloadClient2: DownloadClientConfig
  downloadClient3: DownloadClientConfig
  sonarrSymlinkDownloadClientId?: number
  radarrSymlinkDownloadClientId?: number
  sonarrMkvDownloadClientId?: number
  radarrMkvDownloadClientId?: number
  // STRM Refresh Schedule
  strmRefreshEnabled: boolean
  strmRefreshHour: number
  strmRefreshMinute: number
  strmRefreshDayOfWeek: string
  strmRefreshInterval: string
  strmRefreshLastRun?: string
  strmRefreshNextRun?: string
}

interface AppState {
  // Dashboard
  symlinkCount: number
  mkvCount: number
  downloadingCount: number
  pendingSymlinkCount: number
  errorCount: number
  upcomingTransitions: Transition[]
  setDashboardData: (data: {
    symlinkCount: number
    mkvCount: number
    downloadingCount: number
    pendingSymlinkCount: number
    errorCount: number
    upcomingTransitions: Transition[]
  }) => void

  // Media
  mediaItems: MediaItem[]
  setMediaItems: (items: MediaItem[]) => void

  // Queue
  queueItems: QueueItem[]
  setQueueItems: (items: QueueItem[]) => void

  // Activity
  activityLogs: ActivityLog[]
  setActivityLogs: (logs: ActivityLog[]) => void

  // Config
  config: Config | null
  setConfig: (config: Config) => void

  // First Run
  showFirstRun: boolean
  setShowFirstRun: (show: boolean) => void

  // SignalR
  lastMediaUpdate: number
  setLastMediaUpdate: (ts: number) => void
}

export const useAppStore = create<AppState>((set) => ({
  // Dashboard
  symlinkCount: 0,
  mkvCount: 0,
  downloadingCount: 0,
  pendingSymlinkCount: 0,
  errorCount: 0,
  upcomingTransitions: [],
  setDashboardData: (data) => set(data),

  // Media
  mediaItems: [],
  setMediaItems: (items) => set({ mediaItems: items }),

  // Queue
  queueItems: [],
  setQueueItems: (items) => set({ queueItems: items }),

  // Activity
  activityLogs: [],
  setActivityLogs: (logs) => set({ activityLogs: logs }),

  // Config
  config: null,
  setConfig: (config) => set({ config, showFirstRun: !config.firstRunComplete }),

  // First Run
  showFirstRun: false,
  setShowFirstRun: (show) => set({ showFirstRun: show }),

  // SignalR
  lastMediaUpdate: 0,
  setLastMediaUpdate: (ts) => set({ lastMediaUpdate: ts }),
}))
