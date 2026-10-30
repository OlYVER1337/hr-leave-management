import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '');
  return {
    plugins: [react()],
    server: {
      host: '0.0.0.0',
      port: 5173,
      strictPort: true,
      proxy: {
        '/hr-api': {
          target: env.VITE_DEV_HR_TARGET || 'http://localhost:8081',
          changeOrigin: true,
          rewrite: (path) => path.replace(/^\/hr-api/, ''),
        },
        '/leave-api': {
          target: env.VITE_DEV_LEAVE_TARGET || 'http://localhost:8082',
          changeOrigin: true,
          rewrite: (path) => path.replace(/^\/leave-api/, ''),
        },
      },
    },
  };
});
