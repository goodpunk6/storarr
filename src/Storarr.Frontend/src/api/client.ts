import axios from 'axios'

const api = axios.create({
  baseURL: '/api/v1',
  headers: {
    'Content-Type': 'application/json',
  },
})

// Dashboard
// Global response interceptor: log API errors centrally
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error.response?.status
    const url = error.config?.url ?? 'unknown'
    console.error(`API error ${status} on ${url}:`, error.message)
    return Promise.reject(error)
  }
)

export const getDashboard = () => api.get('/dashboard')

// Media
export const getMedia = (params?: {
  state?: string
  type?: string
  search?: string
  excluded?: boolean
  page?: number
  pageSize?: number
}) => api.get('/media', { params })

export const getMediaItem = (id: number) => api.get(`/media/${id}`)
export const createMedia = (data: any) => api.post('/media', data)
export const forceDownload = (id: number) => api.post(`/media/${id}/force-download`)
export const forceSymlink = (id: number) => api.post(`/media/${id}/force-symlink`)
export const toggleExcluded = (id: number) => api.post(`/media/${id}/toggle-excluded`)
export const setExcluded = (id: number, isExcluded: boolean) => api.put(`/media/${id}/excluded`, { isExcluded })
export const deleteMedia = (id: number) => api.delete(`/media/${id}`)

// Config
export const getConfig = () => api.get('/config')
export const updateConfig = (data: any) => api.put('/config', data)
export const testConnections = () => api.post('/config/test')
export const completeFirstRun = (data: {
  libraryMode: string
  jellyfinUrl?: string
  jellyfinApiKey?: string
  jellyseerrUrl?: string
  jellyseerrApiKey?: string
  sonarrUrl?: string
  sonarrApiKey?: string
  radarrUrl?: string
  radarrApiKey?: string
  mediaLibraryPath?: string
}) => api.post('/config/firstrun', data)

// Queue
export const getQueue = () => api.get('/queue')
export const getDownloadClientQueues = () => api.get('/queue/clients')

// Activity
export const getActivity = (params?: {
  mediaItemId?: number
  page?: number
  pageSize?: number
}) => api.get('/activity', { params })

// Transitions
export const processTransitions = () => api.post('/transitions/process')

// Exclusions
export const getExclusions = (params?: {
  type?: string
  search?: string
}) => api.get('/exclusions', { params })

export const getExclusion = (id: number) => api.get(`/exclusions/${id}`)

export const createExclusion = (data: {
  title: string
  type: string
  tmdbId?: number
  tvdbId?: number
  sonarrId?: number
  radarrId?: number
  reason?: string
}) => api.post('/exclusions', data)

export const deleteExclusion = (id: number) => api.delete(`/exclusions/${id}`)

export const checkExclusion = (params: {
  sonarrId?: number
  radarrId?: number
  tmdbId?: number
  tvdbId?: number
}) => api.get('/exclusions/check', { params })

export const excludeByArrId = (data: {
  sonarrId?: number
  radarrId?: number
  tmdbId?: number
  tvdbId?: number
  title?: string
  type?: string
  reason?: string
}) => api.post('/exclusions/by-arr-id', data)

export default api
