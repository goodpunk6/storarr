/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        arr: {
          bg: '#1a1a2e',
          card: '#16213e',
          primary: '#0f3460',
          accent: '#e94560',
          text: '#eaeaea',
          muted: '#8892b0',
          success: '#64ffda',
          warning: '#fbbf24',
          danger: '#ef4444',
        }
      }
    },
  },
  plugins: [],
}
