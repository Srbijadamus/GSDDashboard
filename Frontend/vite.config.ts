import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    // Output directly to the .NET backend's static-file root so the backend
    // always serves the latest bundle without a separate copy step.
    outDir: '../Backend/wwwroot',
    emptyOutDir: true,
  },
  server: {
    // Relative /api calls (see src/api/client.ts) need somewhere to go in dev,
    // since Vite serves the frontend on :5173 while the backend is on :5000.
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
})
