import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// No GitHub Pages o site é servido em /messaging-notifications/.
export default defineConfig({
  plugins: [react()],
  base: process.env.GITHUB_PAGES ? '/messaging-notifications/' : '/',
  build: { outDir: 'dist' }
});
