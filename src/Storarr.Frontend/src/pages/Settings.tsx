import { useEffect, useState } from 'react'
import { Save, RefreshCw, Check, X, Info, Plus, Trash2, Play, Clock } from 'lucide-react'
import { getConfig, updateConfig, testConnections, processTransitions, getDownloadClients } from '../api/client'
import { Config, TimeUnit, DownloadClientType, DownloadOrder } from '../stores/appStore'

interface ConnectionTest {
  service: string
  success: boolean
  error?: string
  version?: string
}

interface DownloadClientConfig {
  enabled: boolean
  type: DownloadClientType
  url: string
  username?: string
  password?: string
  apiKey?: string
}

const TIME_UNITS: TimeUnit[] = ['Minutes', 'Hours', 'Days', 'Weeks', 'Months']
const DOWNLOAD_CLIENT_TYPES: DownloadClientType[] = ['QBittorrent', 'Transmission', 'Sabnzbd']
const DAYS_OF_WEEK = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const STRM_REFRESH_INTERVALS = ['Daily', 'Weekly', 'Monthly', 'Yearly']

export default function Settings() {
  const [config, setConfig] = useState<Config | null>(null)
  const [downloadClients, setDownloadClients] = useState<DownloadClientConfig[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [processing, setProcessing] = useState(false)
  const [testResults, setTestResults] = useState<ConnectionTest[]>([])
  const [arrDownloadClients, setArrDownloadClients] = useState<{
    sonarrClients: Array<{ id: number; name: string; implementation: string; enable: boolean }>
    radarrClients: Array<{ id: number; name: string; implementation: string; enable: boolean }>
  }>({ sonarrClients: [], radarrClients: [] })

  useEffect(() => {
    const fetchConfig = async () => {
      try {
        const response = await getConfig()
        const apiConfig = response.data

        const newConfig: Config = {
          firstRunComplete: apiConfig.firstRunComplete,
          libraryMode: apiConfig.libraryMode,
          symlinkToMkvValue: apiConfig.symlinkToMkvValue,
          symlinkToMkvUnit: apiConfig.symlinkToMkvUnit,
          mkvToSymlinkValue: apiConfig.mkvToSymlinkValue,
          mkvToSymlinkUnit: apiConfig.mkvToSymlinkUnit,
          preferredDownloadOrder: apiConfig.preferredDownloadOrder,
          mediaLibraryPath: apiConfig.mediaLibraryPath,
          // Multi-drive
          multiDriveEnabled: apiConfig.multiDriveEnabled || false,
          symlinkStoragePath: apiConfig.symlinkStoragePath,
          mkvStoragePath: apiConfig.mkvStoragePath,
          sonarrSymlinkRootFolder: apiConfig.sonarrSymlinkRootFolder,
          sonarrMkvRootFolder: apiConfig.sonarrMkvRootFolder,
          radarrSymlinkRootFolder: apiConfig.radarrSymlinkRootFolder,
          radarrMkvRootFolder: apiConfig.radarrMkvRootFolder,
          // Services
          jellyfinUrl: apiConfig.jellyfinUrl,
          jellyfinApiKey: apiConfig.jellyfinApiKey,
          jellyseerrUrl: apiConfig.jellyseerrUrl,
          jellyseerrApiKey: apiConfig.jellyseerrApiKey,
          sonarrUrl: apiConfig.sonarrUrl,
          sonarrApiKey: apiConfig.sonarrApiKey,
          radarrUrl: apiConfig.radarrUrl,
          radarrApiKey: apiConfig.radarrApiKey,
          downloadClient1: {
            enabled: apiConfig.downloadClient1Enabled,
            type: apiConfig.downloadClient1Type,
            url: apiConfig.downloadClient1Url,
            username: apiConfig.downloadClient1Username,
            password: apiConfig.downloadClient1Password,
            apiKey: apiConfig.downloadClient1ApiKey,
          },
          downloadClient2: {
            enabled: apiConfig.downloadClient2Enabled,
            type: apiConfig.downloadClient2Type,
            url: apiConfig.downloadClient2Url,
            username: apiConfig.downloadClient2Username,
            password: apiConfig.downloadClient2Password,
            apiKey: apiConfig.downloadClient2ApiKey,
          },
          downloadClient3: {
            enabled: apiConfig.downloadClient3Enabled,
            type: apiConfig.downloadClient3Type,
            url: apiConfig.downloadClient3Url,
            apiKey: apiConfig.downloadClient3ApiKey,
          },
          // STRM Refresh Schedule
          strmRefreshEnabled: apiConfig.strmRefreshEnabled ?? true,
          strmRefreshHour: apiConfig.strmRefreshHour ?? 4,
          strmRefreshMinute: apiConfig.strmRefreshMinute ?? 0,
          strmRefreshDayOfWeek: apiConfig.strmRefreshDayOfWeek ?? 'Monday',
          strmRefreshInterval: apiConfig.strmRefreshInterval ?? 'Weekly',
          strmRefreshLastRun: apiConfig.strmRefreshLastRun,
          strmRefreshNextRun: apiConfig.strmRefreshNextRun,
          sonarrSymlinkDownloadClientId: apiConfig.sonarrSymlinkDownloadClientId,
          radarrSymlinkDownloadClientId: apiConfig.radarrSymlinkDownloadClientId,
          sonarrMkvDownloadClientId: apiConfig.sonarrMkvDownloadClientId,
          radarrMkvDownloadClientId: apiConfig.radarrMkvDownloadClientId,
        }
        setConfig(newConfig)

        // Build download clients list from config
        const clients: DownloadClientConfig[] = []
        if (apiConfig.downloadClient1Enabled || apiConfig.downloadClient1Url) {
          clients.push({
            enabled: apiConfig.downloadClient1Enabled,
            type: apiConfig.downloadClient1Type,
            url: apiConfig.downloadClient1Url || '',
            username: apiConfig.downloadClient1Username,
            password: apiConfig.downloadClient1Password,
            apiKey: apiConfig.downloadClient1ApiKey,
          })
        }
        if (apiConfig.downloadClient2Enabled || apiConfig.downloadClient2Url) {
          clients.push({
            enabled: apiConfig.downloadClient2Enabled,
            type: apiConfig.downloadClient2Type,
            url: apiConfig.downloadClient2Url || '',
            username: apiConfig.downloadClient2Username,
            password: apiConfig.downloadClient2Password,
            apiKey: apiConfig.downloadClient2ApiKey,
          })
        }
        if (apiConfig.downloadClient3Enabled || apiConfig.downloadClient3Url) {
          clients.push({
            enabled: apiConfig.downloadClient3Enabled,
            type: apiConfig.downloadClient3Type,
            url: apiConfig.downloadClient3Url || '',
            apiKey: apiConfig.downloadClient3ApiKey,
          })
        }
        if (clients.length === 0) {
          clients.push({ enabled: false, type: 'QBittorrent', url: '' })
        }
        setDownloadClients(clients)

        try {
          const clientsResponse = await getDownloadClients()
          setArrDownloadClients(clientsResponse.data)
        } catch {
          // Silently fail - dropdowns will just show "None (use Jellyseerr)"
        }
      } catch (error) {
        console.error('Failed to fetch config:', error)
      } finally {
        setLoading(false)
      }
    }

    fetchConfig()
  }, [])

  const handleSave = async () => {
    if (!config || saving) return
    setSaving(true)
    try {
      const apiData: any = {
        libraryMode: config.libraryMode,
        symlinkToMkvValue: config.symlinkToMkvValue,
        symlinkToMkvUnit: config.symlinkToMkvUnit,
        mkvToSymlinkValue: config.mkvToSymlinkValue,
        mkvToSymlinkUnit: config.mkvToSymlinkUnit,
        mediaLibraryPath: config.mediaLibraryPath,
        // Multi-drive
        multiDriveEnabled: config.multiDriveEnabled,
        symlinkStoragePath: config.symlinkStoragePath || '',
        mkvStoragePath: config.mkvStoragePath || '',
        sonarrSymlinkRootFolder: config.sonarrSymlinkRootFolder || '',
        sonarrMkvRootFolder: config.sonarrMkvRootFolder || '',
        radarrSymlinkRootFolder: config.radarrSymlinkRootFolder || '',
        radarrMkvRootFolder: config.radarrMkvRootFolder || '',
        // Services
        jellyfinUrl: config.jellyfinUrl,
        jellyfinApiKey: config.jellyfinApiKey,
        jellyseerrUrl: config.jellyseerrUrl,
        jellyseerrApiKey: config.jellyseerrApiKey,
        sonarrUrl: config.sonarrUrl,
        sonarrApiKey: config.sonarrApiKey,
        radarrUrl: config.radarrUrl,
        radarrApiKey: config.radarrApiKey,
        // STRM Refresh Schedule
        strmRefreshEnabled: config.strmRefreshEnabled,
        strmRefreshHour: config.strmRefreshHour,
        strmRefreshMinute: config.strmRefreshMinute,
        strmRefreshDayOfWeek: config.strmRefreshDayOfWeek,
        strmRefreshInterval: config.strmRefreshInterval,
        sonarrSymlinkDownloadClientId: config.sonarrSymlinkDownloadClientId ?? 0,
        radarrSymlinkDownloadClientId: config.radarrSymlinkDownloadClientId ?? 0,
        sonarrMkvDownloadClientId: config.sonarrMkvDownloadClientId ?? 0,
        radarrMkvDownloadClientId: config.radarrMkvDownloadClientId ?? 0,
      }

      // Map download clients back to fixed slots, clear unused slots
      // Client 1
      if (downloadClients.length > 0) {
        const client = downloadClients[0]
        apiData.downloadClient1Enabled = client.enabled
        apiData.downloadClient1Type = client.type
        apiData.downloadClient1Url = client.url || ''
        apiData.downloadClient1Username = client.username || ''
        apiData.downloadClient1Password = client.password || ''
        apiData.downloadClient1ApiKey = client.apiKey || ''
      } else {
        apiData.downloadClient1Enabled = false
        apiData.downloadClient1Url = ''
        apiData.downloadClient1Username = ''
        apiData.downloadClient1Password = ''
        apiData.downloadClient1ApiKey = ''
      }

      // Client 2
      if (downloadClients.length > 1) {
        const client = downloadClients[1]
        apiData.downloadClient2Enabled = client.enabled
        apiData.downloadClient2Type = client.type
        apiData.downloadClient2Url = client.url || ''
        apiData.downloadClient2Username = client.username || ''
        apiData.downloadClient2Password = client.password || ''
        apiData.downloadClient2ApiKey = client.apiKey || ''
      } else {
        apiData.downloadClient2Enabled = false
        apiData.downloadClient2Url = ''
        apiData.downloadClient2Username = ''
        apiData.downloadClient2Password = ''
        apiData.downloadClient2ApiKey = ''
      }

      // Client 3
      if (downloadClients.length > 2) {
        const client = downloadClients[2]
        apiData.downloadClient3Enabled = client.enabled
        apiData.downloadClient3Type = client.type
        apiData.downloadClient3Url = client.url || ''
        apiData.downloadClient3ApiKey = client.apiKey || ''
      } else {
        apiData.downloadClient3Enabled = false
        apiData.downloadClient3Url = ''
        apiData.downloadClient3ApiKey = ''
      }

      await updateConfig(apiData)
      // Config saved successfully
    } catch (error) {
      console.error('Failed to save config:', error)
      console.error('Failed to save configuration')
    } finally {
      setSaving(false)
    }
  }

  const handleTest = async () => {
    setTesting(true)
    setTestResults([])
    try {
      const response = await testConnections()
      setTestResults(response.data.results)
    } catch (error) {
      console.error('Failed to test connections:', error)
    } finally {
      setTesting(false)
    }
  }

  const handleProcessTransitions = async () => {
    setProcessing(true)
    try {
      await processTransitions()
      // Transition processing triggered
    } catch (error) {
      console.error('Failed to process transitions:', error)
      console.error('Failed to process transitions')
    } finally {
      setProcessing(false)
    }
  }

  const updateConfigField = (field: string, value: any) => {
    setConfig(prev => prev ? { ...prev, [field]: value } : null)
  }

  const addDownloadClient = () => {
    if (downloadClients.length < 3) {
      setDownloadClients([...downloadClients, { enabled: true, type: 'QBittorrent', url: '' }])
    }
  }

  const removeDownloadClient = (index: number) => {
    setDownloadClients(downloadClients.filter((_, i) => i !== index))
  }

  const updateDownloadClient = (index: number, field: string, value: any) => {
    setDownloadClients(prev => {
      const updated = [...prev]
      updated[index] = { ...updated[index], [field]: value }
      return updated
    })
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-arr-accent"></div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold">Settings</h2>
        <div className="flex gap-2">
          <button
            onClick={handleProcessTransitions}
            disabled={processing}
            className="flex items-center gap-2 px-4 py-2 bg-green-600 hover:bg-green-700 rounded-lg transition-colors disabled:opacity-50"
            title="Process transitions now"
          >
            <Play size={20} className={processing ? 'animate-pulse' : ''} />
            Process Now
          </button>
          <button
            onClick={handleTest}
            disabled={testing}
            className="flex items-center gap-2 px-4 py-2 bg-arr-card hover:bg-arr-primary rounded-lg transition-colors disabled:opacity-50"
          >
            <RefreshCw size={20} className={testing ? 'animate-spin' : ''} />
            Test Connections
          </button>
          <button
            onClick={handleSave}
            disabled={saving}
            className="flex items-center gap-2 px-4 py-2 bg-arr-accent hover:bg-arr-accent/80 rounded-lg transition-colors disabled:opacity-50"
          >
            <Save size={20} />
            Save
          </button>
        </div>
      </div>

      {/* Connection Test Results */}
      {testResults.length > 0 && (
        <div className="bg-arr-card rounded-lg p-6">
          <h3 className="text-lg font-semibold mb-4">Connection Test Results</h3>
          <div className="space-y-2">
            {testResults.map((result) => (
              <div key={result.service} className="flex items-center gap-2">
                {result.success ? (
                  <Check className="text-arr-success" size={20} />
                ) : (
                  <X className="text-arr-danger" size={20} />
                )}
                <span>{result.service}</span>
                {result.success && (
                  <span className="text-arr-muted text-sm">({result.version || 'Connected'})</span>
                )}
                {result.error && (
                  <span className="text-arr-danger text-sm">- {result.error}</span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {config && (
        <div className="space-y-6">
          {/* Transition Thresholds */}
          <div className="bg-arr-card rounded-lg p-6">
            <h3 className="text-lg font-semibold mb-4">Transition Thresholds</h3>
            <div className="space-y-6">
              <div>
                <label className="block text-sm text-arr-muted mb-2">
                  Unwatched period before MKV download
                </label>
                <div className="flex gap-2">
                  <input
                    type="number"
                    min="1"
                    value={config.symlinkToMkvValue}
                    onChange={(e) => updateConfigField('symlinkToMkvValue', parseInt(e.target.value) || 1)}
                    className="w-24 bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                  />
                  <select
                    value={config.symlinkToMkvUnit}
                    onChange={(e) => updateConfigField('symlinkToMkvUnit', e.target.value as TimeUnit)}
                    className="flex-1 bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                  >
                    {TIME_UNITS.map(unit => (
                      <option key={unit} value={unit}>{unit}</option>
                    ))}
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-sm text-arr-muted mb-2">
                  Inactive period before symlink restore
                </label>
                <div className="flex gap-2">
                  <input
                    type="number"
                    min="1"
                    value={config.mkvToSymlinkValue}
                    onChange={(e) => updateConfigField('mkvToSymlinkValue', parseInt(e.target.value) || 1)}
                    className="w-24 bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                  />
                  <select
                    value={config.mkvToSymlinkUnit}
                    onChange={(e) => updateConfigField('mkvToSymlinkUnit', e.target.value as TimeUnit)}
                    className="flex-1 bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                  >
                    {TIME_UNITS.map(unit => (
                      <option key={unit} value={unit}>{unit}</option>
                    ))}
                  </select>
                </div>
              </div>
            </div>
          </div>

          {/* Download Order Preference */}
          <div className="bg-arr-card rounded-lg p-6">
            <h3 className="text-lg font-semibold mb-2">Download Order</h3>
            <p className="text-sm text-arr-muted mb-4">
              Whether newly-added content is fetched as a streaming symlink or materialized as a local MKV first.
              Applies to items within 30 minutes of being added (one-shot).
            </p>
            <select
              value={config.preferredDownloadOrder}
              onChange={(e) => updateConfigField('preferredDownloadOrder', e.target.value as DownloadOrder)}
              className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
            >
              <option value="StrmFirst">STRM first (default)</option>
              <option value="MkvFirst">MKV first</option>
            </select>
          </div>

          {/* STRM Refresh Schedule */}
          <div className="bg-arr-card rounded-lg p-6">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center gap-3">
                <Clock size={24} className="text-arr-accent" />
                <div>
                  <h3 className="text-lg font-semibold">STRM Refresh Schedule</h3>
                  <p className="text-sm text-arr-muted">Automatically check and refresh expired STRM files</p>
                </div>
              </div>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={config.strmRefreshEnabled ?? true}
                  onChange={(e) => updateConfigField('strmRefreshEnabled', e.target.checked)}
                  className="w-4 h-4 rounded border-arr-primary"
                />
                <span className="text-sm">Enabled</span>
              </label>
            </div>

            {config.strmRefreshEnabled !== false && (
              <div className="space-y-4">
                <div className="flex items-start gap-2 p-3 bg-arr-bg rounded-lg">
                  <Info size={20} className="text-arr-accent mt-0.5 flex-shrink-0" />
                  <p className="text-sm text-arr-muted">
                    STRM files contain URLs to streaming sources that can expire over time.
                    This scheduled task checks each STRM file's URL and deletes expired ones
                    so the *arr stack can re-download fresh copies.
                  </p>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div>
                    <label className="block text-sm text-arr-muted mb-2">Interval</label>
                    <select
                      value={config.strmRefreshInterval || 'Weekly'}
                      onChange={(e) => updateConfigField('strmRefreshInterval', e.target.value)}
                      className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                    >
                      {STRM_REFRESH_INTERVALS.map(interval => (
                        <option key={interval} value={interval}>{interval}</option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="block text-sm text-arr-muted mb-2">Day of Week</label>
                    <select
                      value={config.strmRefreshDayOfWeek || 'Monday'}
                      onChange={(e) => updateConfigField('strmRefreshDayOfWeek', e.target.value)}
                      className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                      disabled={config.strmRefreshInterval === 'Daily'}
                    >
                      {DAYS_OF_WEEK.map(day => (
                        <option key={day} value={day}>{day}</option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="block text-sm text-arr-muted mb-2">Time</label>
                    <div className="flex gap-2">
                      <input
                        type="number"
                        min="0"
                        max="23"
                        value={config.strmRefreshHour ?? 4}
                        onChange={(e) => updateConfigField('strmRefreshHour', parseInt(e.target.value) || 0)}
                        className="w-20 bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                        placeholder="Hour"
                      />
                      <span className="text-arr-muted self-center">:</span>
                      <input
                        type="number"
                        min="0"
                        max="59"
                        value={config.strmRefreshMinute ?? 0}
                        onChange={(e) => updateConfigField('strmRefreshMinute', parseInt(e.target.value) || 0)}
                        className="w-20 bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                        placeholder="Min"
                      />
                    </div>
                    <p className="text-xs text-arr-muted mt-1">24-hour format (UTC)</p>
                  </div>
                </div>

                {(config.strmRefreshLastRun || config.strmRefreshNextRun) && (
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-4 border-t border-arr-primary">
                    <div>
                      <span className="text-sm text-arr-muted">Last Run:</span>
                      <p className="text-sm font-medium">
                        {config.strmRefreshLastRun
                          ? new Date(config.strmRefreshLastRun).toLocaleString()
                          : 'Never'}
                      </p>
                    </div>
                    <div>
                      <span className="text-sm text-arr-muted">Next Run:</span>
                      <p className="text-sm font-medium">
                        {config.strmRefreshNextRun
                          ? new Date(config.strmRefreshNextRun).toLocaleString()
                          : 'Calculating...'}
                      </p>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Media Library */}
          <div className="bg-arr-card rounded-lg p-6">
            <h3 className="text-lg font-semibold mb-4">Media Library</h3>
            <div>
              <label className="block text-sm text-arr-muted mb-2">Media Library Path</label>
              <input
                type="text"
                value={config.mediaLibraryPath}
                onChange={(e) => updateConfigField('mediaLibraryPath', e.target.value)}
                className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
              />
              {config.multiDriveEnabled && (
                <p className="text-sm text-arr-muted mt-1">
                  This path is used as fallback when multi-drive is enabled but tier paths are not set.
                </p>
              )}
            </div>
          </div>

          {/* Multi-Drive Storage */}
          <div className="bg-arr-card rounded-lg p-6">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-semibold">Multi-Drive Storage</h3>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={config.multiDriveEnabled || false}
                  onChange={(e) => updateConfigField('multiDriveEnabled', e.target.checked)}
                  className="w-4 h-4 rounded border-arr-primary"
                />
                <span className="text-sm">Enable Multi-Drive</span>
              </label>
            </div>

            {config.multiDriveEnabled && (
              <div className="space-y-6">
                <div className="flex items-start gap-2 p-3 bg-arr-bg rounded-lg">
                  <Info size={20} className="text-arr-accent mt-0.5 flex-shrink-0" />
                  <p className="text-sm text-arr-muted">
                    When multi-drive is enabled, symlinks (.strm files) and MKVs are stored on separate paths.
                    This allows you to keep frequently accessed content on fast storage (SSD) and older content on bulk storage (HDD).
                  </p>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm text-arr-muted mb-2">Symlink Storage Path (Fast/SSD)</label>
                    <input
                      type="text"
                      value={config.symlinkStoragePath || ''}
                      onChange={(e) => updateConfigField('symlinkStoragePath', e.target.value)}
                      placeholder="/mnt/ssd/media/symlinks"
                      className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                    />
                    <p className="text-xs text-arr-muted mt-1">Where .strm files are stored</p>
                  </div>
                  <div>
                    <label className="block text-sm text-arr-muted mb-2">MKV Storage Path (Bulk/HDD)</label>
                    <input
                      type="text"
                      value={config.mkvStoragePath || ''}
                      onChange={(e) => updateConfigField('mkvStoragePath', e.target.value)}
                      placeholder="/mnt/hdd/media/mkv"
                      className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                    />
                    <p className="text-xs text-arr-muted mt-1">Where .mkv files are stored</p>
                  </div>
                </div>

                <div className="border-t border-arr-primary pt-4 mt-4">
                  <h4 className="text-md font-semibold mb-3">Sonarr Root Folders</h4>
                  <p className="text-sm text-arr-muted mb-3">
                    These are the root folders configured in Sonarr for each storage tier.
                    Storarr will update the series root folder when transitioning between tiers.
                  </p>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm text-arr-muted mb-2">Symlink Root Folder</label>
                      <input
                        type="text"
                        value={config.sonarrSymlinkRootFolder || ''}
                        onChange={(e) => updateConfigField('sonarrSymlinkRootFolder', e.target.value)}
                        placeholder="/data/symlinks/tv"
                        className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                      />
                    </div>
                    <div>
                      <label className="block text-sm text-arr-muted mb-2">MKV Root Folder</label>
                      <input
                        type="text"
                        value={config.sonarrMkvRootFolder || ''}
                        onChange={(e) => updateConfigField('sonarrMkvRootFolder', e.target.value)}
                        placeholder="/data/mkv/tv"
                        className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                      />
                    </div>
                  </div>
                </div>

                <div className="border-t border-arr-primary pt-4">
                  <h4 className="text-md font-semibold mb-3">Radarr Root Folders</h4>
                  <p className="text-sm text-arr-muted mb-3">
                    These are the root folders configured in Radarr for each storage tier.
                    Storarr will update the movie root folder when transitioning between tiers.
                  </p>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm text-arr-muted mb-2">Symlink Root Folder</label>
                      <input
                        type="text"
                        value={config.radarrSymlinkRootFolder || ''}
                        onChange={(e) => updateConfigField('radarrSymlinkRootFolder', e.target.value)}
                        placeholder="/data/symlinks/movies"
                        className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                      />
                    </div>
                    <div>
                      <label className="block text-sm text-arr-muted mb-2">MKV Root Folder</label>
                      <input
                        type="text"
                        value={config.radarrMkvRootFolder || ''}
                        onChange={(e) => updateConfigField('radarrMkvRootFolder', e.target.value)}
                        placeholder="/data/mkv/movies"
                        className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                      />
                    </div>
                  </div>
                </div>
              </div>
            )}
          </div>

          {/* Jellyfin */}
          <div className="bg-arr-card rounded-lg p-6">
            <h3 className="text-lg font-semibold mb-4">Jellyfin</h3>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm text-arr-muted mb-2">URL</label>
                <input
                  type="text"
                  value={config.jellyfinUrl || ''}
                  onChange={(e) => updateConfigField('jellyfinUrl', e.target.value)}
                  placeholder="http://jellyfin:8096"
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>
              <div>
                <label className="block text-sm text-arr-muted mb-2">API Key</label>
                <input
                  type="password"
                  value={config.jellyfinApiKey || ''}
                  onChange={(e) => updateConfigField('jellyfinApiKey', e.target.value)}
                  placeholder="Enter API key"
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>
            </div>
          </div>

          {/* Jellyseerr */}
          <div className="bg-arr-card rounded-lg p-6">
            <h3 className="text-lg font-semibold mb-4">Jellyseerr</h3>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm text-arr-muted mb-2">URL</label>
                <input
                  type="text"
                  value={config.jellyseerrUrl || ''}
                  onChange={(e) => updateConfigField('jellyseerrUrl', e.target.value)}
                  placeholder="http://jellyseerr:5055"
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>
              <div>
                <label className="block text-sm text-arr-muted mb-2">API Key</label>
                <input
                  type="password"
                  value={config.jellyseerrApiKey || ''}
                  onChange={(e) => updateConfigField('jellyseerrApiKey', e.target.value)}
                  placeholder="Enter API key"
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>
            </div>
          </div>

          {/* Sonarr */}
          <div className="bg-arr-card rounded-lg p-6">
            <h3 className="text-lg font-semibold mb-4">Sonarr (TV Shows & Anime)</h3>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm text-arr-muted mb-2">URL</label>
                <input
                  type="text"
                  value={config.sonarrUrl || ''}
                  onChange={(e) => updateConfigField('sonarrUrl', e.target.value)}
                  placeholder="http://sonarr:8989"
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>
              <div>
                <label className="block text-sm text-arr-muted mb-2">API Key</label>
                <input
                  type="password"
                  value={config.sonarrApiKey || ''}
                  onChange={(e) => updateConfigField('sonarrApiKey', e.target.value)}
                  placeholder="Enter API key"
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>
            </div>
          </div>

          {/* Radarr */}
          <div className="bg-arr-card rounded-lg p-6">
            <h3 className="text-lg font-semibold mb-4">Radarr (Movies)</h3>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm text-arr-muted mb-2">URL</label>
                <input
                  type="text"
                  value={config.radarrUrl || ''}
                  onChange={(e) => updateConfigField('radarrUrl', e.target.value)}
                  placeholder="http://radarr:7878"
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>
              <div>
                <label className="block text-sm text-arr-muted mb-2">API Key</label>
                <input
                  type="password"
                  value={config.radarrApiKey || ''}
                  onChange={(e) => updateConfigField('radarrApiKey', e.target.value)}
                  placeholder="Enter API key"
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>
            </div>
          </div>

          {/* Download Clients */}
          <div className="bg-arr-card rounded-lg p-6">
            <div className="flex items-start gap-2 mb-4">
              <Info size={20} className="text-arr-accent mt-0.5" />
              <p className="text-sm text-arr-muted">
                Download clients are configured for <strong>queue monitoring only</strong>.
                Storarr reads the queue status to track download progress.
              </p>
            </div>

            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-semibold">Download Clients</h3>
              {downloadClients.length < 3 && (
                <button
                  onClick={addDownloadClient}
                  className="flex items-center gap-1 px-3 py-1 bg-arr-accent hover:bg-arr-accent/80 rounded-lg text-sm transition-colors"
                >
                  <Plus size={16} />
                  Add Client
                </button>
              )}
            </div>

            <div className="space-y-4">
              {downloadClients.map((client, index) => (
                <div key={index} className="bg-arr-bg rounded-lg p-4">
                  <div className="flex items-center gap-3 mb-3">
                    <input
                      type="checkbox"
                      checked={client.enabled}
                      onChange={(e) => updateDownloadClient(index, 'enabled', e.target.checked)}
                      className="w-4 h-4 rounded border-arr-primary"
                    />
                    <select
                      value={client.type}
                      onChange={(e) => updateDownloadClient(index, 'type', e.target.value)}
                      className="bg-arr-card border border-arr-primary rounded-lg px-3 py-1 text-sm"
                    >
                      {DOWNLOAD_CLIENT_TYPES.map(type => (
                        <option key={type} value={type}>{type}</option>
                      ))}
                    </select>
                    {downloadClients.length > 1 && (
                      <button
                        onClick={() => removeDownloadClient(index)}
                        className="ml-auto p-1 text-arr-danger hover:bg-arr-danger/20 rounded"
                        title="Remove client"
                      >
                        <Trash2 size={16} />
                      </button>
                    )}
                  </div>
                  {client.enabled && (
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                      <input
                        type="text"
                        placeholder="http://localhost:8080"
                        value={client.url || ''}
                        onChange={(e) => updateDownloadClient(index, 'url', e.target.value)}
                        className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm"
                      />
                      {client.type === 'Sabnzbd' ? (
                        <input
                          type="password"
                          placeholder="API Key"
                          value={client.apiKey || ''}
                          onChange={(e) => updateDownloadClient(index, 'apiKey', e.target.value)}
                          className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm md:col-span-2"
                        />
                      ) : (
                        <>
                          <input
                            type="text"
                            placeholder="Username"
                            value={client.username || ''}
                            onChange={(e) => updateDownloadClient(index, 'username', e.target.value)}
                            className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm"
                          />
                          <input
                            type="password"
                            placeholder="Password"
                            value={client.password || ''}
                            onChange={(e) => updateDownloadClient(index, 'password', e.target.value)}
                            className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm"
                          />
                        </>
                      )}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>

          {/* Symlink Download Client */}
          <div className="bg-arr-card rounded-lg p-6">
            <div className="flex items-start gap-2 mb-4">
              <Info size={20} className="text-arr-accent mt-0.5" />
              <p className="text-sm text-arr-muted">
                When converting MKV to symlink, Storarr can bypass Jellyseerr and directly grab releases from
                Sonarr/Radarr using a specific download client (e.g. NZBdav for .strm delivery).
                If not configured, Jellyseerr is used as fallback.
              </p>
            </div>
            <h3 className="text-lg font-semibold mb-4">Symlink Download Client</h3>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm text-arr-muted mb-2">Sonarr Symlink Client</label>
                <select
                  value={config.sonarrSymlinkDownloadClientId ?? 0}
                  onChange={(e) => updateConfigField('sonarrSymlinkDownloadClientId', parseInt(e.target.value) || undefined)}
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                >
                  <option value={0}>None (use Jellyseerr)</option>
                  {arrDownloadClients.sonarrClients.map(client => (
                    <option key={client.id} value={client.id}>
                      {client.name} ({client.implementation}) {!client.enable ? '- Disabled' : ''}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-sm text-arr-muted mb-2">Radarr Symlink Client</label>
                <select
                  value={config.radarrSymlinkDownloadClientId ?? 0}
                  onChange={(e) => updateConfigField('radarrSymlinkDownloadClientId', parseInt(e.target.value) || undefined)}
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                >
                  <option value={0}>None (use Jellyseerr)</option>
                  {arrDownloadClients.radarrClients.map(client => (
                    <option key={client.id} value={client.id}>
                      {client.name} ({client.implementation}) {!client.enable ? '- Disabled' : ''}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          </div>

          {/* MKV Download Client */}
          <div className="bg-arr-card rounded-lg p-6">
            <div className="flex items-start gap-2 mb-4">
              <Info size={20} className="text-arr-accent mt-0.5" />
              <p className="text-sm text-arr-muted">
                When converting symlink to MKV, Storarr will search for releases and grab them using a specific
                download client (e.g. SABnzbd or qBittorrent for full MKV downloads). If not configured, the Arr's
                default client selection is used.
              </p>
            </div>
            <h3 className="text-lg font-semibold mb-4">MKV Download Client</h3>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm text-arr-muted mb-2">Sonarr MKV Client</label>
                <select
                  value={config.sonarrMkvDownloadClientId ?? 0}
                  onChange={(e) => updateConfigField('sonarrMkvDownloadClientId', parseInt(e.target.value) || undefined)}
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                >
                  <option value={0}>None (use Arr default)</option>
                  {arrDownloadClients.sonarrClients.map(client => (
                    <option key={client.id} value={client.id}>
                      {client.name} ({client.implementation}) {!client.enable ? '- Disabled' : ''}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-sm text-arr-muted mb-2">Radarr MKV Client</label>
                <select
                  value={config.radarrMkvDownloadClientId ?? 0}
                  onChange={(e) => updateConfigField('radarrMkvDownloadClientId', parseInt(e.target.value) || undefined)}
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                >
                  <option value={0}>None (use Arr default)</option>
                  {arrDownloadClients.radarrClients.map(client => (
                    <option key={client.id} value={client.id}>
                      {client.name} ({client.implementation}) {!client.enable ? '- Disabled' : ''}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
