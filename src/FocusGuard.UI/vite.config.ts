import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: '/',
  build: {
    target: 'chrome111',
    sourcemap: false,
    modulePreload: false,
    cssCodeSplit: false,
    rolldownOptions: {
      output: { entryFileNames: 'host.js', chunkFileNames: 'chunks/[name].js', codeSplitting: true, assetFileNames: 'host.[ext]' },
    },
  },
});
