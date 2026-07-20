// @ts-check
import { defineConfig } from 'astro/config';
import tailwindcss from '@tailwindcss/vite';

// A fully static marketing/evidence site. No SSR adapter — `astro build` emits a
// self-contained `dist/` that any static host (or `python3 -m http.server`) can serve.
// `site` + `base` are left at defaults so the build works both at a domain root and,
// if ever moved under a repo path, only `base` needs setting.
export default defineConfig({
  vite: {
    plugins: [tailwindcss()],
  },
});
