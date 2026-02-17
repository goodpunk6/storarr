import { useEffect, useState } from 'react'
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom'
import Layout from './components/Layout'
import Dashboard from './pages/Dashboard'
import Media from './pages/Media'
import MediaDetail from './pages/MediaDetail'
import Queue from './pages/Queue'
import Activity from './pages/Activity'
import Settings from './pages/Settings'
import FirstRunWizard from './components/FirstRunWizard'
import { getConfig } from './api/client'
import { useAppStore } from './stores/appStore'

function App() {
  const [loading, setLoading] = useState(true)
  const { showFirstRun, setConfig, setShowFirstRun } = useAppStore()

  useEffect(() => {
    const fetchConfig = async () => {
      try {
        const response = await getConfig()
        const apiConfig = response.data
        
        const config = {
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
          },
          downloadClient2: {
            enabled: apiConfig.downloadClient2Enabled,
            type: apiConfig.downloadClient2Type,
            url: apiConfig.downloadClient2Url,
            username: apiConfig.downloadClient2Username,
            password: apiConfig.downloadClient2Password,
          },
          downloadClient3: {
            enabled: apiConfig.downloadClient3Enabled,
            type: apiConfig.downloadClient3Type,
            url: apiConfig.downloadClient3Url,
            apiKey: apiConfig.downloadClient3ApiKey,
          },
        }
        
        setConfig(config)
        
        if (!apiConfig.firstRunComplete) {
          setShowFirstRun(true)
        }
      } catch (error) {
        console.error('Failed to fetch config:', error)
      } finally {
        setLoading(false)
      }
    }

    fetchConfig()
  }, [setConfig, setShowFirstRun])

  const handleFirstRunComplete = async () => {
    setShowFirstRun(false)
    // Refetch config to get updated state
    try {
      const response = await getConfig()
      const apiConfig = response.data
      const config = {
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
        },
        downloadClient2: {
          enabled: apiConfig.downloadClient2Enabled,
          type: apiConfig.downloadClient2Type,
          url: apiConfig.downloadClient2Url,
          username: apiConfig.downloadClient2Username,
          password: apiConfig.downloadClient2Password,
        },
        downloadClient3: {
          enabled: apiConfig.downloadClient3Enabled,
          type: apiConfig.downloadClient3Type,
          url: apiConfig.downloadClient3Url,
          apiKey: apiConfig.downloadClient3ApiKey,
        },
      }
      setConfig(config)
    } catch (error) {
      console.error('Failed to refetch config:', error)
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-screen bg-arr-bg">
        <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-arr-accent"></div>
      </div>
    )
  }

  return (
    <>
      {showFirstRun && <FirstRunWizard onComplete={handleFirstRunComplete} />}
      <Router>
        <Layout>
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/media" element={<Media />} />
            <Route path="/media/:id" element={<MediaDetail />} />
            <Route path="/queue" element={<Queue />} />
            <Route path="/activity" element={<Activity />} />
            <Route path="/settings" element={<Settings />} />
          </Routes>
        </Layout>
      </Router>
    </>
  )
}

export default App
