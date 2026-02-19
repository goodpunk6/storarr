import { useEffect, useState } from 'react'
import { Save, RefreshCw, Check, X, Info, Plus, Trash2, Play } from 'lucide-react'
import { getConfig, updateConfig, testConnections, processTransitions } from '../api/client'
import { Config, TimeUnit, DownloadClientType } from '../stores/appStore'

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

// Sentinel value the backend sends for any configured API key / password.
// Send it back unchanged to indicate 'do not modify this secret'.
const MASKED_SENTINEL = '__MASKED__'

const TIME_UNITS: TimeUnit[] = ['Minutes', 'Hours', 'Days', 'Weeks', 'Months']
const DOWNLOAD_CLIENT_TYPES: DownloadClientType[] = ['QBittorrent', 'Transmission', 'Sabnzbd']

export default function Settings() {
  const [config, setConfig] = useState<Config | null>(null)
  const [downloadClients, setDownloadClients] = useState<DownloadClientConfig[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [processing, setProcessing] = useState(false)
  const [testResults, setTestResults] = useState<ConnectionTest[]>([])

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
          mediaLibraryPath: apiConfig.mediaLibraryPath,
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
        jellyfinUrl: config.jellyfinUrl,
        jellyfinApiKey: config.jellyfinApiKey,
        jellyseerrUrl: config.jellyseerrUrl,
        jellyseerrApiKey: config.jellyseerrApiKey,
        sonarrUrl: config.sonarrUrl,
        sonarrApiKey: config.sonarrApiKey,
        radarrUrl: config.radarrUrl,
        radarrApiKey: config.radarrApiKey,
      }

      // Map download clients back to fixed slots
      downloadClients.forEach((client, index) => {
        if (index === 0) {
          apiData.downloadClient1Enabled = client.enabled
          apiData.downloadClient1Type = client.type
          apiData.downloadClient1Url = client.url
          apiData.downloadClient1Username = client.username
          apiData.downloadClient1Password = client.password
          apiData.downloadClient1ApiKey = client.apiKey
        } else if (index === 1) {
          apiData.downloadClient2Enabled = client.enabled
          apiData.downloadClient2Type = client.type
          apiData.downloadClient2Url = client.url
          apiData.downloadClient2Username = client.username
          apiData.downloadClient2Password = client.password
          apiData.downloadClient2ApiKey = client.apiKey
        } else if (index === 2) {
          apiData.downloadClient3Enabled = client.enabled
          apiData.downloadClient3Type = client.type
          apiData.downloadClient3Url = client.url
          apiData.downloadClient3ApiKey = client.apiKey
        }
      })

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
            </div>
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
        </div>
      )}
    </div>
  )
}
