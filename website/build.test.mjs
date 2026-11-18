import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
const release = JSON.parse(await readFile(new URL('release.json', import.meta.url)));
test('both languages contain the correct release and direct download link', async()=>{
  for(const [file,lang] of [['dist/index.html','ja'],['dist/en/index.html','en']]){
    const html=await readFile(new URL(file,import.meta.url),'utf8');
    assert.ok(html.includes(`<html lang="${lang}">`));
    assert.ok(html.includes(`href="${release.download}"`));
    assert.ok(html.includes(`v${release.version}`));
    assert.ok(html.includes(release.sha256));
    for(const match of html.matchAll(/(?:src|href)="(\/[^"#]+\.(?:png|webp|ico|css|js))"/g))await access(new URL('dist'+match[1],import.meta.url));
    assert.equal((html.match(/<h1 /g)||[]).length,1);
    assert.ok(!html.includes('undefined'));
  }
});
test('release links are scoped to the public Locaptio repository',()=>{
  assert.equal(release.download, `${release.repository}/releases/download/v${release.version}/Locaptio-Setup-${release.version}-x64.exe`);
  assert.equal(release.release, `${release.repository}/releases/tag/v${release.version}`);
  assert.match(release.sha256,/^[a-f0-9]{64}$/);
});

test('task examples and every feature category are readable without opening a feature tab', async()=>{
  const { useCases } = await import('./use-cases.mjs');
  for(const [lang,file] of [['ja','dist/index.html'],['en','dist/en/index.html']]){
    const html=await readFile(new URL(file,import.meta.url),'utf8');
    assert.equal((html.match(/<article class="use-case">/g)||[]).length,6);
    assert.equal((html.match(/<section class="feature-group"/g)||[]).length,4);
    for(const example of useCases[lang].items) assert.ok(html.includes(example.title));
    assert.ok(html.includes('id="engines-title"'));
    assert.ok(!html.includes('role="tabpanel" id="panel-'));
    for(const match of html.matchAll(/href="#(detail-[^"]+)"/g)) assert.ok(html.includes(`id="${match[1]}"`));
  }
});

test('first-visit flow and every new tool have an illustration, icon and in-app location', async()=>{
  const { whatsNew } = await import('./whats-new.mjs');
  for(const [lang,file] of [['ja','dist/index.html'],['en','dist/en/index.html']]){
    const html=await readFile(new URL(file,import.meta.url),'utf8');
    const cards=[...html.matchAll(/<article class="new-card" id="(new-[a-z]+)">([\s\S]*?)<\/article>/g)];
    assert.equal(cards.length,8);
    for(const [,id,card] of cards){
      assert.match(card,/<img src="\/assets\/screens\/[a-z-]+\.webp" width="\d+" height="\d+" loading="lazy" decoding="async" alt="[^"]{10,}">/);
      assert.match(card,/class="new-where"/);
      assert.ok(html.includes(`href="#${id}"`));
    }
    assert.equal((html.match(/<ol class="flow-steps">[\s\S]*?<\/ol>/)[0].match(/<li>/g)||[]).length,3);
    assert.ok(html.includes(whatsNew[lang].flowLabel) && html.includes('href="#new"'));
  }
});

test('every explanation section shows a real screenshot with alt text and a caption', async()=>{
  for(const file of ['dist/index.html','dist/en/index.html']){
    const html=await readFile(new URL(file,import.meta.url),'utf8');
    const figures=[...html.matchAll(/<figure class="shot ([a-z-]+)"><div class="shot-frame"><span class="shot-label">[^<]+<\/span><img src="(\/assets\/screens\/[a-z-]+\.webp)"[^>]* alt="([^"]{10,})"><\/div><figcaption>([^<]{10,})<\/figcaption><\/figure>/g)];
    const count=kind=>figures.filter(f=>f[1]===kind).length;
    assert.deepEqual([count('use-case-shot'),count('group-shot'),count('new-shot')],[6,4,8]);
    // Screen recording: four steps, each a real screenshot with descriptive alt text.
    const steps=[...html.matchAll(/<li class="rec-step"><figure class="shot rec-shot"><div class="shot-frame"><span class="shot-label">[^<]+<\/span><img src="(\/assets\/screens\/[a-z-]+\.webp)"[^>]* alt="([^"]{10,})">/g)];
    assert.equal(steps.length,4);
    for(const [,src] of steps) await access(new URL('dist'+src,import.meta.url));
    assert.ok(html.includes('href="#recording"') && html.includes('id="recording"'));
    for(const [, , src] of figures) await access(new URL('dist'+src,import.meta.url));
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
  for(const [lang,file] of [['ja','dist/index.html'],['en','dist/en/index.html']]){
    const html=await readFile(new URL(file,import.meta.url),'utf8'), items=updates[lang].items;
    assert.equal(items[0][0],release.version);
    const versions=items.map(i=>i[0].split('.').map(Number)), sorted=[...versions].sort((a,b)=>b[2]-a[2]);
    assert.deepEqual(versions,sorted);
    assert.ok(html.includes('id="updates"') && html.includes('href="#updates"'));
    for(const [version,title] of items) assert.ok(html.includes(`v${version}`) && html.includes(title));
  }
});
