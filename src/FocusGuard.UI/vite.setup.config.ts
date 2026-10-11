import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The installer page: one HTML, one script, one stylesheet, embedded in FocusGuard.Setup.exe and served from memory.
// Fonts are served by the Setup from its own embedded copies, so no public folder is copied.
export default defineConfig({
  plugins: [react()],
  base: '/',
  publicDir: false,
  build: {
    target: 'chrome111',
    outDir: 'dist-setup',
    emptyOutDir: true,
    sourcemap: false,
    modulePreload: false,
    cssCodeSplit: false,
    rolldownOptions: {
      input: 'setup.html',
      output: { entryFileNames: 'setup.js', codeSplitting: false, assetFileNames: 'setup.[ext]' },
    },
  },
});
