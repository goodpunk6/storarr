import { ReactNode } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { HardDrive, Film, Download, Clock, Settings, Menu, X } from 'lucide-react'
import { useState } from 'react'
import { useSignalR } from '../hooks/useSignalR'
import logoWithText from '../assets/logo-with-text.svg'
import logoIcon from '../assets/logo-icon.svg'

interface LayoutProps {
  children: ReactNode
}

export default function Layout({ children }: LayoutProps) {
  const location = useLocation()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  useSignalR()

  const navItems = [
    { path: '/', label: 'Dashboard', icon: HardDrive },
    { path: '/media', label: 'Media', icon: Film },
    { path: '/queue', label: 'Queue', icon: Download },
    { path: '/activity', label: 'Activity', icon: Clock },
    { path: '/settings', label: 'Settings', icon: Settings },
  ]

  const isActive = (path: string) => location.pathname === path

  return (
    <div className="min-h-screen bg-arr-bg">
      {/* Header */}
      <header className="bg-arr-card border-b border-arr-primary sticky top-0 z-40">
        <div className="flex items-center justify-between px-4 py-3">
          <div className="flex items-center gap-4">
            <button
              onClick={() => setSidebarOpen(!sidebarOpen)}
              className="lg:hidden p-2 hover:bg-arr-primary rounded-lg"
            >
              {sidebarOpen ? <X size={24} /> : <Menu size={24} />}
            </button>
            <Link to="/" className="flex items-center gap-2">
              <img 
                src={logoWithText} 
                alt="Storarr" 
                className="h-10 w-auto hidden sm:block"
              />
              <img 
                src={logoIcon} 
                alt="Storarr" 
                className="h-8 w-8 sm:hidden"
              />
            </Link>
          </div>
          <div className="text-arr-muted text-sm hidden md:block">
            Tiered Media Storage Manager
          </div>
        </div>
      </header>

      <div className="flex">
        {/* Sidebar */}
        <aside
          className={`
            fixed lg:static inset-y-0 left-0 z-30
            w-64 bg-arr-card border-r border-arr-primary
            transform transition-transform duration-200 ease-in-out
            ${sidebarOpen ? 'translate-x-0' : '-translate-x-full lg:translate-x-0'}
            pt-16 lg:pt-0
          `}
        >
          <nav className="p-4 space-y-2">
            {navItems.map((item) => {
              const Icon = item.icon
              return (
                <Link
                  key={item.path}
                  to={item.path}
                  onClick={() => setSidebarOpen(false)}
                  className={`
                    flex items-center gap-3 px-4 py-3 rounded-lg
                    transition-colors duration-200
                    ${isActive(item.path)
                      ? 'bg-arr-accent text-white'
                      : 'text-arr-muted hover:bg-arr-primary hover:text-arr-text'
                    }
                  `}
                >
                  <Icon size={20} />
                  <span>{item.label}</span>
                </Link>
              )
            })}
          </nav>
        </aside>

        {/* Overlay */}
        {sidebarOpen && (
          <div
            className="fixed inset-0 bg-black/50 z-20 lg:hidden"
            onClick={() => setSidebarOpen(false)}
          />
        )}

        {/* Main content */}
        <main className="flex-1 p-6 min-h-[calc(100vh-60px)]">
          {children}
        </main>
      </div>
    </div>
  )
}
