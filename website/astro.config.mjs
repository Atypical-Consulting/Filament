// @ts-check
import { defineConfig } from 'astro/config';
import tailwindcss from '@tailwindcss/vite';

// A fully static marketing/evidence site. No SSR adapter — `astro build` emits a
// self-contained `dist/` that any static host (or `python3 -m http.server`) can serve.
//
// The site is published as a GitHub Pages PROJECT page, so it is served from a repo
// sub-path, not a domain root: https://atypical-consulting.github.io/Filament/.
// `site` makes canonical/og URLs absolute; `base` is what rewrites every emitted asset
// and link under /Filament/. Any root-absolute reference written by hand (href="/x")
// bypasses `base` and 404s in production — derive those from `import.meta.env.BASE_URL`
// instead (see Layout.astro's favicon).
export default defineConfig({
  site: 'https://atypical-consulting.github.io',
  base: '/Filament',
  vite: {
    plugins: [tailwindcss()],
  },
});
