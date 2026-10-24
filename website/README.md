# Locaptio official website / 公式サイト

Live: https://locaptio.vercel.app · English: https://locaptio.vercel.app/en

Japanese: `/` · English: `/en`

This dependency-free static site is deployed on Vercel from the `website` directory of `Kou-256/Locaptio`. The Windows application remains in the repository root. No audio is captured or processed by the website. There are no analytics, cookies or external font requests.

```sh
cd website
npm ci
npm run build
npm test
npm run dev
```

Local preview: `http://127.0.0.1:4173`. Vercel project settings: Framework **Other**, Root Directory **website**, Build Command **npm run build**, Output Directory **dist**. Git integration deploys changes from `main`.

## Releasing / 配布版の更新

1. Build and test the Windows app; create a portable ZIP with `scripts/build-portable.ps1`.
2. Publish the tested ZIP to GitHub Releases with the stable asset name **Locaptio-win-x64.zip**, plus **SHA256SUMS.txt**. The public download URL intentionally uses `releases/latest/download/Locaptio-win-x64.zip` so the button continues to work for later versions.
3. Update `release.json` with the matching version, tag URL and SHA-256; run the site build/tests and commit. Keep the site metadata and Release asset in sync.

日本語・英語の内容は `build.mjs`、デザインは `public/style.css`、配布版の情報は `release.json` にあります。`dist` と `.vercel` はコミットしません。Windows用ZIPとモデルをVercelへアップロードせず、GitHub Releasesから配布します。

Artwork: existing Locaptio icon supplied by the project owner; original abstract wallpaper generated for the website. Product preview is an HTML illustration, labeled as such, rather than an application screenshot.
