import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
const release = JSON.parse(await readFile(new URL('release.json', import.meta.url)));
const pagesFor=lang=>lang==='ja'?{home:'dist/index.html',guide:'dist/guide/index.html',updates:'dist/updates/index.html'}:{home:'dist/en/index.html',guide:'dist/en/guide/index.html',updates:'dist/en/updates/index.html'};
const read=file=>readFile(new URL(file,import.meta.url),'utf8');
test('both languages contain the correct release and direct download link', async()=>{
  for(const lang of ['ja','en']){
    const html=await read(pagesFor(lang).home);
    assert.ok(html.includes(`<html lang="${lang}">`));
    assert.ok(html.includes(`href="${release.download}"`));
    assert.ok(html.includes(`v${release.version}`));
    assert.ok(html.includes(release.sha256));
  }
});
test('every page has one h1, working assets and in-page links, and links to the other pages', async()=>{
  for(const lang of ['ja','en']) for(const [page,file] of Object.entries(pagesFor(lang))){
    const html=await read(file), base=lang==='ja'?'':'/en';
    for(const match of html.matchAll(/(?:src|href)="(\/[^"#]+\.(?:png|webp|ico|css|js))"/g))await access(new URL('dist'+match[1],import.meta.url));
    assert.equal((html.match(/<h1[ >]/g)||[]).length,1,file);
    assert.ok(!html.includes('undefined'),file);
    for(const match of html.matchAll(/href="#([^"]+)"/g)) assert.ok(match[1]==='main'||html.includes(`id="${match[1]}"`),`${file}: #${match[1]}`);
    assert.ok(html.includes(`href="${base}/guide"`) && html.includes(`href="${base}/updates"`),file);
    const path=(base||'')+(page==='home'?(lang==='ja'?'/':''):'/'+page);
    assert.ok(html.includes(`<link rel="canonical" href="https://locaptio.vercel.app${path}">`),file+' canonical');
  }
});
test('release links are scoped to the public Locaptio repository',()=>{
  assert.equal(release.download, `${release.repository}/releases/download/v${release.version}/Locaptio-Setup-${release.version}-x64.exe`);
  assert.equal(release.release, `${release.repository}/releases/tag/v${release.version}`);
  assert.match(release.sha256,/^[a-f0-9]{64}$/);
});

test('the top page explains four languages and live summary with real screenshots', async()=>{
  const { spotlight } = await import('./spotlight.mjs');
  for(const lang of ['ja','en']){
    const html=await read(pagesFor(lang).home);
    for(const f of spotlight[lang]){
      const startAt=html.indexOf(`<section class="spotlight section" id="${f.id}"`);
      const section=startAt<0?null:html.slice(startAt,html.indexOf('</section>',startAt));
      assert.ok(section,f.id); assert.ok(html.includes(`href="${lang==='ja'?'/':'/en'}#${f.id}"`));
      assert.ok(section.includes(`/assets/screens/${f.main[0]}.webp`) && f.more.every(m=>section.includes(`/assets/screens/${m[0]}.webp`)));
      assert.ok(section.includes(f.title));
    }
  }
});

test('task examples are on the top page; engines and every feature category are in the guide', async()=>{
  const { useCases } = await import('./use-cases.mjs');
  for(const lang of ['ja','en']){
    const home=await read(pagesFor(lang).home), guide=await read(pagesFor(lang).guide);
    assert.equal((home.match(/<article class="use-case">/g)||[]).length,6);
    for(const example of useCases[lang].items) assert.ok(home.includes(example.title));
    assert.equal((guide.match(/<section class="feature-group"/g)||[]).length,4);
    assert.ok(guide.includes('id="engines-title"') && !home.includes('id="engines-title"'));
    assert.ok(!guide.includes('role="tabpanel" id="panel-'));
  }
});

test('first-visit flow is on the top page; every tool card is in the guide with an image and in-app location', async()=>{
  const { whatsNew } = await import('./whats-new.mjs');
  for(const lang of ['ja','en']){
    const home=await read(pagesFor(lang).home), guide=await read(pagesFor(lang).guide);
    const cards=[...guide.matchAll(/<article class="new-card" id="(new-[a-z]+)">([\s\S]*?)<\/article>/g)];
    assert.equal(cards.length,8);
    for(const [,id,card] of cards){
      assert.match(card,/<img src="\/assets\/screens\/[a-z-]+\.webp" width="\d+" height="\d+" loading="lazy" decoding="async" alt="[^"]{10,}">/);
      assert.match(card,/class="new-where"/);
      assert.ok(guide.includes(`href="#${id}"`));
    }
    assert.equal((home.match(/<ol class="flow-steps">[\s\S]*?<\/ol>/)[0].match(/<li>/g)||[]).length,3);
    assert.ok(home.includes(whatsNew[lang].flowLabel));
  }
});

test('every explanation section shows a real screenshot with alt text and a caption', async()=>{
  const figuresIn=html=>[...html.matchAll(/<figure class="shot ([a-z- ]+?)"><div class="shot-frame"><span class="shot-label">[^<]+<\/span><img src="(\/assets\/screens\/[a-z-]+\.webp)"[^>]* alt="([^"]{10,})"><\/div><figcaption>([^<]{6,})<\/figcaption><\/figure>/g)];
  for(const lang of ['ja','en']){
    const home=figuresIn(await read(pagesFor(lang).home)), guideHtml=await read(pagesFor(lang).guide), guide=figuresIn(guideHtml);
    const count=(list,kind)=>list.filter(f=>f[1].split(' ')[0]===kind).length;
    assert.deepEqual([count(home,'use-case-shot'),count(home,'spotlight-shot'),count(home,'spotlight-extra')],[6,2,3]);
    assert.deepEqual([count(guide,'group-shot'),count(guide,'new-shot')],[4,8]);
    const steps=[...guideHtml.matchAll(/<li class="rec-step"><figure class="shot rec-shot"><div class="shot-frame"><span class="shot-label">[^<]+<\/span><img src="(\/assets\/screens\/[a-z-]+\.webp)"[^>]* alt="([^"]{10,})">/g)];
    assert.equal(steps.length,4);
    for(const [,src] of steps) await access(new URL('dist'+src,import.meta.url));
    for(const [, , src] of [...home,...guide]) await access(new URL('dist'+src,import.meta.url));
  }
});

test('sharing metadata uses a readable landscape image for each language', async()=>{
  for(const [lang,file] of [['ja','dist/index.html'],['en','dist/en/index.html']]){
    const html=await readFile(new URL(file,import.meta.url),'utf8');
    const image=`https://locaptio.vercel.app/assets/share-${lang}-v2.png`;
    assert.ok(html.includes(`<meta property="og:image" content="${image}">`));
    assert.ok(html.includes(`<meta name="twitter:image" content="${image}">`));
    assert.ok(html.includes('<meta name="twitter:card" content="summary_large_image">'));
    assert.ok(html.includes('<meta property="og:image:alt"'));
    const png=await readFile(new URL(`public/assets/share-${lang}-v2.png`,import.meta.url));
    assert.equal(png.readUInt32BE(16),1200); assert.equal(png.readUInt32BE(20),630);
  }
});

test('search title and description repeat the hero text, and Locaptio icons are declared', async()=>{
  const {languages}=await import('./content.mjs');
  for(const [lang,file] of [['ja','dist/index.html'],['en','dist/en/index.html']]){
    const html=await readFile(new URL(file,import.meta.url),'utf8'), c=languages[lang];
    const plain=c.intro.replace(/\*\*/g,'').replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/'/g,'&#39;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
    assert.ok(html.includes(`<title>Locaptio — ${c.eyebrow}</title>`));
    assert.ok(html.includes(`<p class="eyebrow">${c.eyebrow}</p>`));
    assert.ok(html.includes(`<meta name="description" content="${plain}">`));
    for(const tag of ['<link rel="icon" href="/favicon.ico" sizes="48x48">','<link rel="icon" href="/icon-192.png" type="image/png" sizes="192x192">','<link rel="apple-touch-icon" href="/apple-touch-icon.png">']) assert.ok(html.includes(tag),tag);
  }
  for(const [name,size] of [['icon-192.png',192],['icon-512.png',512],['apple-touch-icon.png',180]]){
    const png=await readFile(new URL('public/'+name,import.meta.url));
    assert.equal(png.readUInt32BE(16),size); assert.equal(png.readUInt32BE(20),size);
  }
});

test('major updates list names every big release, newest first, starting with the current version', async()=>{
  const {updates}=await import('./updates.mjs');
  const release=JSON.parse(await readFile(new URL('release.json',import.meta.url),'utf8'));
  for(const lang of ['ja','en']){
    const html=await read(pagesFor(lang).updates), items=updates[lang].items;
    assert.equal(items[0][0],release.version);
    const versions=items.map(i=>i[0].split('.').map(Number)), sorted=[...versions].sort((a,b)=>b[2]-a[2]);
    assert.deepEqual(versions,sorted);
    assert.ok(html.includes('id="updates"') && !(await read(pagesFor(lang).home)).includes('id="updates"'));
    for(const [version,title] of items) assert.ok(html.includes(`v${version}`) && html.includes(title));
  }
});
