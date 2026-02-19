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
      // Record timestamp so subscribers can react without clearing cached data
      useAppStore.getState().setLastMediaUpdate(Date.now())
    })

    let stopped = false

    connection.start()
      .then(() => console.log('SignalR connected'))
      .catch((err) => console.error('SignalR connection error:', err))

    return () => {
      stopped = true
      connection.stop().catch(() => {
        // Ignore stop errors on cleanup
      })
    }
  }, [])
}
