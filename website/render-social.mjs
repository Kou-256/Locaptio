// Optional asset-authoring script. The site build uses the committed PNGs.
// Set LOCAPTIO_PLAYWRIGHT_MODULE to an existing Playwright installation if needed.
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { languages } from './content.mjs';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LOCAPTIO_PLAYWRIGHT_MODULE || 'playwright');
process.chdir(fileURLToPath(new URL('.', import.meta.url)));
const asset = async file => 'data:image/png;base64,' + (await readFile('public/assets/'+file)).toString('base64');
const logo = await asset('locaptio.png'), waves = await asset('waves.png');
const browser = await chromium.launch({headless:true,channel:'chrome'});
try {
 for(const lang of ['ja','en']) {
  const c=languages[lang], ja=lang==='ja';
  const page = await browser.newPage({viewport:{width:1200,height:630},deviceScaleFactor:1});
  await page.setContent(`<!doctype html><html lang="${lang}"><head><meta charset="utf-8"><style>
  *{box-sizing:border-box}html,body{width:1200px;height:630px;margin:0}body{background:#fff;color:#18373b;font-family:"Segoe UI","Yu Gothic UI",Meiryo,sans-serif;border-top:8px solid #167b77;padding:42px 52px 36px}header{display:flex;align-items:center;justify-content:space-between}.brand{display:flex;align-items:center;gap:16px;font-size:42px;font-weight:600}.brand img{width:66px;height:66px}.platform{font-size:21px;color:#167b77;font-weight:600}.main{display:grid;grid-template-columns:610px 446px;gap:38px;margin-top:36px;align-items:center;height:348px}h1{font-size:${ja?44:47}px;line-height:1.5;font-weight:700;letter-spacing:-.02em;margin:0}.features{font-size:25px;line-height:1.7;color:#167b77;font-weight:700;margin:22px 0 10px}.inputs{font-size:22px;line-height:1.6;color:#50676a;margin:0;max-width:600px}.preview{height:348px;border:1px solid #dce6e3;border-radius:16px;background:#f0f6f4 url('${waves}') center/cover;padding:20px;display:flex;flex-direction:column;gap:16px}.preview-header{align-self:flex-start;padding:8px 12px;border-radius:8px;background:rgba(255,255,255,.95);font-size:18px;font-weight:600}.captions{display:flex;flex-direction:column;gap:12px;margin-top:8px}.bubble{background:rgba(30,34,39,.95);border:1px solid #858585;border-radius:10px;padding:12px 14px;color:#fff;font-size:24px;line-height:1.5;font-weight:600}.source{color:#7dd3fc;font-size:22px}.sample{align-self:flex-end;margin-top:auto;background:rgba(255,255,255,.95);padding:3px 9px;border-radius:5px;font-size:14px;color:#50676a}footer{border-top:1px solid #dce6e3;margin-top:30px;padding-top:20px;display:flex;justify-content:space-between;align-items:center;color:#50676a;font-size:21px}footer strong{color:#18373b;font-weight:600}
  </style></head><body><header><div class="brand"><img src="${logo}" alt="">Locaptio</div><div class="platform">${ja?'Windows 11 / ローカル処理':'Windows 11 / Local processing'}</div></header><main class="main"><div><h1>${c.headline[0]}<br>${c.headline[1]}</h1><p class="features">${ja?'字幕表示・録音・テキスト保存':'Captions · Recording · Text export'}</p><p class="inputs">${ja?'PC音声・マイク・音声ファイルに対応':'PC audio, microphone and audio files'}</p></div><div class="preview"><div class="preview-header">${c.demoSource}</div><div class="captions"><div class="bubble source" lang="en">${c.demoEn}</div><div class="bubble" lang="ja">${c.demoTranslation}</div></div><div class="sample">${c.demoNote}</div></div></main><footer><strong>${ja?'無料・MIT License':'Free · MIT License'}</strong><span>locaptio.vercel.app</span></footer></body></html>`);
  await page.evaluate(()=>document.fonts.ready);
  const clipped = await page.evaluate(()=>[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>1200||e.getBoundingClientRect().bottom>630).map(e=>e.tagName));
  if(clipped.length) throw new Error('Social image overflow: '+clipped.join(','));
  await page.screenshot({path:`public/assets/share-${lang}-v2.png`});
  console.log(`Rendered ${lang}: 1200 x 630`); await page.close();
 }
} finally { await browser.close(); }
