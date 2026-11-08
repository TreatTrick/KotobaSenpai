# Product Website Implementation Plan

**Goal:** Build and publish an animated, bilingual KotobaSenpai product website with real demonstrations and the existing Clutch dragon maid.

**Architecture:** Prerender two SvelteKit pages into a static GitHub Pages site under `/KotobaSenpai/`. Shared Svelte components own the narrative, media playback, and mascot. Keep deployment independent from the disabled desktop CI workflow.

**Tech stack:** Svelte 5, SvelteKit 3 static adapter, TypeScript, Vite 8, pnpm, GSAP, GitHub Actions and Pages.

## 1. Assets and foundation

Create `website/package.json`, `vite.config.ts`, locale routes, product/design notes, and media preparation tooling. Convert existing GIFs to optimized videos with still posters; optimize existing screenshots. Copy the Clutch mascot atlases, player, and behavior tests; adapt only project names and English comments.

## 2. Product narrative and motion

Create `website/src/lib/Landing.svelte`, `content.ts`, `DemoPlayer.svelte`, and `src/app.css`. Implement prominent real download links, annotated-reading demonstrations, a six-language gallery, provider/cost explanation, onboarding, and accessible FAQ. Apply restrained GSAP scroll motion and preserve usable static HTML.

## 3. Verify the visitor experience

Run `pnpm test`, `pnpm check`, and `pnpm build`. Preview desktop and mobile layouts; verify navigation, locale switching, videos, reduced motion, mascot exit intent/drag/download celebration, and installer links. Check for missing assets and horizontal overflow.

## 4. Publish

Create `.github/workflows/website.yml` to check, build, and deploy the static output. Configure this repository's Pages source as GitHub Actions. Commit and push the website, follow its deployment to completion, and verify the public English and Chinese pages and asset URLs.
