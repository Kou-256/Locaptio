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
    for(const match of html.matchAll(/(?:src|href)="(\/[^"#]+\.(?:png|ico|css|js))"/g))await access(new URL('dist'+match[1],import.meta.url));
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
      assert.match(card,/<svg class="illustration"[^>]*role="img" aria-label="[^"]+"/);
      assert.match(card,/class="new-where"/);
      assert.ok(html.includes(`href="#${id}"`));
    }
    assert.equal((html.match(/<ol class="flow-steps">[\s\S]*?<\/ol>/)[0].match(/<li>/g)||[]).length,3);
    assert.ok(html.includes(whatsNew[lang].flowLabel) && html.includes('href="#new"'));
  }
});
