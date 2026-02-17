import { useEffect } from 'react'
import * as signalR from '@microsoft/signalr'
import { useAppStore } from '../stores/appStore'

export function useSignalR() {
  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/notifications')
      .withAutomaticReconnect()
      .build()

    connection.on('MediaUpdated', (mediaId: number, newState: string) => {
      console.log(`Media ${mediaId} updated to ${newState}`)
      // Trigger a refresh of relevant data
      useAppStore.getState().setMediaItems([]) // Clear cache to force refetch
    })

    connection.start()
      .then(() => console.log('SignalR connected'))
      .catch((err) => console.error('SignalR connection error:', err))

    return () => {
      connection.stop()
    }
  }, [])
}
