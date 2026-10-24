import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
const release = JSON.parse(await readFile(new URL('release.json', import.meta.url)));
test('both languages contain the correct release and direct ZIP link', async()=>{
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
  assert.match(release.download,/^https:\/\/github.com\/Kou-256\/Locaptio\/releases\/latest\/download\/Locaptio-win-x64\.zip$/);
  assert.equal(release.release, `${release.repository}/releases/tag/v${release.version}`);
  assert.match(release.sha256,/^[a-f0-9]{64}$/);
});
