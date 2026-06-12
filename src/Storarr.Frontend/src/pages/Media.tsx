import { useState } from 'react'
import { Search } from 'lucide-react'
import CatalogView from '../components/CatalogView'

export default function Media() {
  const [search, setSearch] = useState('')
  const [stateFilter, setStateFilter] = useState<string>('')
  const [typeFilter, setTypeFilter] = useState<string>('')

  return (
    <div className="space-y-6">
      <h2 className="text-2xl font-bold">Media</h2>

      {/* Filters */}
      <div className="bg-arr-card rounded-lg p-4 flex flex-wrap gap-4">
        <div className="relative flex-1 min-w-[200px]">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 text-arr-muted" size={20} />
          <input
            type="text"
            placeholder="Search media..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full bg-arr-bg border border-arr-primary rounded-lg pl-10 pr-4 py-2 focus:outline-none focus:border-arr-accent"
          />
        </div>
        <select
          value={stateFilter}
          onChange={(e) => setStateFilter(e.target.value)}
          className="bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
        >
          <option value="">All States</option>
          <option value="Symlink">Symlink</option>
          <option value="Mkv">MKV</option>
          <option value="Downloading">Downloading</option>
          <option value="PendingSymlink">Pending Symlink</option>
          <option value="Error">Error</option>
        </select>
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value)}
          className="bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
        >
          <option value="">All Types</option>
          <option value="Movie">Movies</option>
          <option value="Series">Series</option>
          <option value="Anime">Anime</option>
        </select>
      </div>

      <CatalogView filters={{ search, stateFilter, typeFilter }} />
    </div>
  )
}
