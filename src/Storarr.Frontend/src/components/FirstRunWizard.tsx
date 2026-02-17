import { useState } from 'react'
import { ArrowRight, Shield, Eye, Zap, Check } from 'lucide-react'
import { completeFirstRun } from '../api/client'
import { LibraryMode } from '../stores/appStore'

interface FirstRunWizardProps {
  onComplete: () => void
}

const LIBRARY_MODES = [
  {
    id: 'NewContentOnly' as LibraryMode,
    title: 'New Content Only',
    icon: Shield,
    description: 'Only track content added after Storarr is installed',
    details: 'Safest option. Your existing library will not be modified. Storarr will only manage new content as it\'s added.',
    recommended: true,
  },
  {
    id: 'TrackExisting' as LibraryMode,
    title: 'Track Existing',
    icon: Eye,
    description: 'Scan existing library but don\'t auto-transition',
    details: 'Storarr will catalog your existing media and track watch history, but won\'t automatically convert files. You can manually trigger transitions.',
    recommended: false,
  },
  {
    id: 'FullAutomation' as LibraryMode,
    title: 'Full Automation',
    icon: Zap,
    description: 'Scan and auto-transition existing content',
    details: 'Storarr will scan your existing library and automatically convert files based on watch history. Use with caution - this will modify your existing library.',
    recommended: false,
    warning: true,
  },
]

export default function FirstRunWizard({ onComplete }: FirstRunWizardProps) {
  const [step, setStep] = useState(1)
  const [libraryMode, setLibraryMode] = useState<LibraryMode>('NewContentOnly')
  const [loading, setLoading] = useState(false)
  const [config, setConfig] = useState({
    jellyfinUrl: '',
    jellyfinApiKey: '',
    jellyseerrUrl: '',
    jellyseerrApiKey: '',
    sonarrUrl: '',
    sonarrApiKey: '',
    radarrUrl: '',
    radarrApiKey: '',
    mediaLibraryPath: '/media',
  })

  const updateConfig = (field: string, value: string) => {
    setConfig(prev => ({ ...prev, [field]: value }))
  }

  const handleComplete = async () => {
    setLoading(true)
    try {
      await completeFirstRun({
        libraryMode,
        ...config,
      })
      onComplete()
    } catch (error) {
      console.error('Failed to complete first run:', error)
      alert('Failed to save configuration')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="fixed inset-0 bg-black/80 flex items-center justify-center z-50 p-4">
      <div className="bg-arr-card rounded-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="p-6 border-b border-arr-primary">
          <h1 className="text-2xl font-bold">Welcome to Storarr</h1>
          <p className="text-arr-muted mt-1">Let's get you set up</p>
        </div>

        {/* Progress */}
        <div className="px-6 py-4 border-b border-arr-primary">
          <div className="flex items-center gap-2">
            {[1, 2, 3].map((s) => (
              <div key={s} className="flex items-center">
                <div className={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-medium ${
                  s <= step ? 'bg-arr-accent text-white' : 'bg-arr-bg text-arr-muted'
                }`}>
                  {s}
                </div>
                {s < 3 && <div className={`w-12 h-1 ${s < step ? 'bg-arr-accent' : 'bg-arr-bg'}`} />}
              </div>
            ))}
          </div>
          <div className="flex justify-between mt-2 text-xs text-arr-muted">
            <span>Library Mode</span>
            <span>Connections</span>
            <span>Paths</span>
          </div>
        </div>

        {/* Content */}
        <div className="p-6">
          {/* Step 1: Library Mode */}
          {step === 1 && (
            <div className="space-y-4">
              <div>
                <h2 className="text-lg font-semibold mb-2">How should Storarr handle your library?</h2>
                <p className="text-arr-muted text-sm">
                  This determines how Storarr interacts with your existing media collection.
                </p>
              </div>

              <div className="space-y-3">
                {LIBRARY_MODES.map((mode) => (
                  <button
                    key={mode.id}
                    onClick={() => setLibraryMode(mode.id)}
                    className={`w-full p-4 rounded-lg border-2 text-left transition-all ${
                      libraryMode === mode.id
                        ? 'border-arr-accent bg-arr-accent/10'
                        : 'border-arr-primary hover:border-arr-accent/50'
                    }`}
                  >
                    <div className="flex items-start gap-3">
                      <mode.icon className={`w-6 h-6 mt-0.5 ${
                        libraryMode === mode.id ? 'text-arr-accent' : 'text-arr-muted'
                      }`} />
                      <div className="flex-1">
                        <div className="flex items-center gap-2">
                          <span className="font-medium">{mode.title}</span>
                          {mode.recommended && (
                            <span className="text-xs bg-arr-success/20 text-arr-success px-2 py-0.5 rounded">
                              Recommended
                            </span>
                          )}
                          {mode.warning && (
                            <span className="text-xs bg-arr-danger/20 text-arr-danger px-2 py-0.5 rounded">
                              Caution
                            </span>
                          )}
                        </div>
                        <p className="text-arr-muted text-sm mt-1">{mode.description}</p>
                        <p className="text-arr-muted text-xs mt-2">{mode.details}</p>
                      </div>
                      {libraryMode === mode.id && (
                        <Check className="w-5 h-5 text-arr-accent" />
                      )}
                    </div>
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* Step 2: Connections */}
          {step === 2 && (
            <div className="space-y-6">
              <div>
                <h2 className="text-lg font-semibold mb-2">Connect your services</h2>
                <p className="text-arr-muted text-sm">
                  You can configure these later in Settings. Only Jellyfin is required for watch tracking.
                </p>
              </div>

              {/* Jellyfin */}
              <div className="bg-arr-bg rounded-lg p-4">
                <h3 className="font-medium mb-3 flex items-center gap-2">
                  <span className="w-2 h-2 bg-arr-accent rounded-full" />
                  Jellyfin (Required)
                </h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <input
                    type="text"
                    placeholder="http://jellyfin:8096"
                    value={config.jellyfinUrl}
                    onChange={(e) => updateConfig('jellyfinUrl', e.target.value)}
                    className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-arr-accent"
                  />
                  <input
                    type="password"
                    placeholder="API Key"
                    value={config.jellyfinApiKey}
                    onChange={(e) => updateConfig('jellyfinApiKey', e.target.value)}
                    className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-arr-accent"
                  />
                </div>
              </div>

              {/* Jellyseerr */}
              <div className="bg-arr-bg rounded-lg p-4">
                <h3 className="font-medium mb-3">Jellyseerr</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <input
                    type="text"
                    placeholder="http://jellyseerr:5055"
                    value={config.jellyseerrUrl}
                    onChange={(e) => updateConfig('jellyseerrUrl', e.target.value)}
                    className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-arr-accent"
                  />
                  <input
                    type="password"
                    placeholder="API Key"
                    value={config.jellyseerrApiKey}
                    onChange={(e) => updateConfig('jellyseerrApiKey', e.target.value)}
                    className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-arr-accent"
                  />
                </div>
              </div>

              {/* Sonarr */}
              <div className="bg-arr-bg rounded-lg p-4">
                <h3 className="font-medium mb-3">Sonarr (TV Shows)</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <input
                    type="text"
                    placeholder="http://sonarr:8989"
                    value={config.sonarrUrl}
                    onChange={(e) => updateConfig('sonarrUrl', e.target.value)}
                    className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-arr-accent"
                  />
                  <input
                    type="password"
                    placeholder="API Key"
                    value={config.sonarrApiKey}
                    onChange={(e) => updateConfig('sonarrApiKey', e.target.value)}
                    className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-arr-accent"
                  />
                </div>
              </div>

              {/* Radarr */}
              <div className="bg-arr-bg rounded-lg p-4">
                <h3 className="font-medium mb-3">Radarr (Movies)</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <input
                    type="text"
                    placeholder="http://radarr:7878"
                    value={config.radarrUrl}
                    onChange={(e) => updateConfig('radarrUrl', e.target.value)}
                    className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-arr-accent"
                  />
                  <input
                    type="password"
                    placeholder="API Key"
                    value={config.radarrApiKey}
                    onChange={(e) => updateConfig('radarrApiKey', e.target.value)}
                    className="bg-arr-card border border-arr-primary rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-arr-accent"
                  />
                </div>
              </div>
            </div>
          )}

          {/* Step 3: Paths */}
          {step === 3 && (
            <div className="space-y-6">
              <div>
                <h2 className="text-lg font-semibold mb-2">Media Library Path</h2>
                <p className="text-arr-muted text-sm">
                  The path where your media library is mounted inside the container.
                </p>
              </div>

              <div>
                <label className="block text-sm text-arr-muted mb-2">Media Library Path</label>
                <input
                  type="text"
                  value={config.mediaLibraryPath}
                  onChange={(e) => updateConfig('mediaLibraryPath', e.target.value)}
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
                />
              </div>

              <div className="bg-arr-bg rounded-lg p-4">
                <h3 className="font-medium mb-2">Summary</h3>
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-arr-muted">Library Mode:</span>
                    <span className="font-medium">{LIBRARY_MODES.find(m => m.id === libraryMode)?.title}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-arr-muted">Media Path:</span>
                    <span className="font-medium">{config.mediaLibraryPath}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-arr-muted">Jellyfin:</span>
                    <span className="font-medium">{config.jellyfinUrl || 'Not configured'}</span>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="p-6 border-t border-arr-primary flex justify-between">
          <button
            onClick={() => setStep(step - 1)}
            disabled={step === 1}
            className="px-4 py-2 text-arr-muted hover:text-white disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Back
          </button>

          {step < 3 ? (
            <button
              onClick={() => setStep(step + 1)}
              className="flex items-center gap-2 px-6 py-2 bg-arr-accent hover:bg-arr-accent/80 rounded-lg transition-colors"
            >
              Next
              <ArrowRight size={18} />
            </button>
          ) : (
            <button
              onClick={handleComplete}
              disabled={loading}
              className="flex items-center gap-2 px-6 py-2 bg-arr-success hover:bg-arr-success/80 rounded-lg transition-colors disabled:opacity-50"
            >
              {loading ? 'Saving...' : 'Complete Setup'}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
