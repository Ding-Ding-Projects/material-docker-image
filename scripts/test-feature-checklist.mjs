import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import os from 'node:os';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {loadInventory, validateInventory, renderChecklist, renderInventoryMarkdown, inventorySummary} from '../website/checklist.mjs';

const root=path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const site=path.join(root,'website');
const inventory=loadInventory(site);
const required=JSON.parse(fs.readFileSync(path.join(site,'required-feature-ids.json'),'utf8'));
let passed=0;
const test=(name,body)=>{body();passed++;console.log('PASS '+name);};
const reject=(change,pattern)=>{const candidate=structuredClone(inventory);change(candidate);assert.throws(()=>validateInventory(candidate,required),pattern);};

test('All 109 baseline identifiers, 10 existing workflows and five milestones are retained',()=>{
  assert.equal(inventory.entries.length,124);
  assert.equal(inventorySummary(inventory).required,109);
  assert.equal(inventorySummary(inventory).counts.verified,0);
  assert.ok(inventory.entries.filter(r=>r.kind==='required').every(r=>r.status!=='verified'));
});
test('Every omitted entry is rejected independently',()=>{
  for(let index=0;index<inventory.entries.length;index++)reject(v=>v.entries.splice(index,1),/missing ID/);
});
test('Duplicates, renamed identifiers, categories and unsupported statuses are rejected',()=>{
  reject(v=>v.entries.push(v.entries[0]),/duplicate ID/);
  reject(v=>v.entries[0].id='invented',/unknown ID/);
  reject(v=>v.entries[0].category='invented',/unknown category/);
  reject(v=>v.entries[0].status='complete',/unknown status/);
  reject(v=>v.categories.push(v.categories[0]),/duplicate or invalid category/);
});
test('Both languages and remaining actions are mandatory',()=>{
  for(const field of ['name','summary','remaining'])reject(v=>v.entries[0][field].zh='',/English and Cantonese/);
  reject(v=>v.entries[0].remaining={en:'None.',zh:'無。'},/remaining actions/);
});
test('Source references cannot establish full verification',()=>{
  reject(v=>v.entries[0].status='verified',/full verification needs/);
  reject(v=>v.entries[0].evidence=[],/missing evidence/);
  reject(v=>v.entries[0].evidence[0].kind='checks',/exact source and artifact/);
});

test('Source references reject verification metadata instead of rendering unvalidated bindings',()=>{
  for(const field of ['scope','sourceCommit','artifactSha256','recordedAt','result']) {
    reject(v=>v.entries[0].evidence[0][field]=field==='scope'?{en:'Reference scope',zh:'參考範圍'}:'<img src=x onerror=alert(1)>',/source reference.*verification metadata/);
  }
});

test('Evidence rendering escapes binding metadata even when called without validation',()=>{
  const candidate=structuredClone(inventory),evidence=candidate.entries[0].evidence[0];
  evidence.kind='checks';evidence.scope={en:'Renderer boundary fixture',zh:'顯示邊界測試'};
  for(const field of ['sourceCommit','artifactSha256','recordedAt'])evidence[field]='<img src=x onerror=alert(1)>';
  const rendered=renderChecklist(candidate);
  assert.doesNotMatch(rendered,/<img src=x/);
  assert.equal((rendered.match(/&lt;img src=x onerror=alert\(1\)&gt;/g)||[]).length,3);
});
test('Private, credential-bearing, unrelated and unsafe evidence links are rejected',()=>{
  for(const url of ['http://localhost/proof','file:///C:/proof','javascript:alert(1)','https://example.com/proof','https://github.com/another/project/blob/main/proof','https://user:pass@github.com/Ding-Ding-Projects/material-docker-image/blob/main/proof','https://github.com/Ding-Ding-Projects/material-docker-image/blob/main/proof?token=secret','https://github.com/Ding-Ding-Projects/material-docker-image/blob/main/%2e%2e/proof','https://ding-ding-projects.github.io/other-project/'])reject(v=>v.entries[0].evidence[0].url=url,/public.*URL/);
});
test('Complete evidence schema accepts one bound candidate and rejects each missing proof',()=>{
  const candidate=structuredClone(inventory),row=candidate.entries[0],sourceCommit='a'.repeat(40),artifactSha256='b'.repeat(64);
  row.status='verified';row.remaining={en:'None.',zh:'無。'};
  for(const kind of ['checks','interaction','capture'])row.evidence.push({kind,label:{en:'Recorded proof',zh:'已記錄證據'},url:`https://github.com/Ding-Ding-Projects/material-docker-image/blob/${sourceCommit}/evidence/${kind}.${kind==='capture'?'png':'json'}`,sourceCommit,artifactSha256,recordedAt:'2026-10-18T12:00:00Z',result:'passed',scope:{en:'Synthetic schema fixture, never published as real evidence.',zh:'只供結構測試，唔會當成實際證據發佈。'}});
  validateInventory(candidate,required);
  for(const kind of ['source','checks','interaction','capture']){const changed=structuredClone(candidate);changed.entries[0].evidence=changed.entries[0].evidence.filter(e=>e.kind!==kind);assert.throws(()=>validateInventory(changed,required),/full verification needs/);}
  const mismatched=structuredClone(candidate);mismatched.entries[0].evidence[1].artifactSha256='c'.repeat(64);assert.throws(()=>validateInventory(mismatched,required),/same candidate/);
  const stale=structuredClone(candidate);stale.entries[0].evidence[1].url=stale.entries[0].evidence[1].url.replace(sourceCommit,'main');assert.throws(()=>validateInventory(stale,required),/immutable/);
});
test('Rendered entries use native disclosure controls and never editable completion controls',()=>{
  const html=renderChecklist(inventory);
  assert.equal((html.match(/<details class="feature-row"/g)||[]).length,124);
  assert.equal((html.match(/data-feature-id=/g)||[]).length,124);
  assert.doesNotMatch(html,/<input|contenteditable/);
  assert.match(html,/role="status" aria-live="polite"/);
  assert.match(html,/尚未驗證/);
  const hostile=structuredClone(inventory);hostile.entries[0].name.en='<script>alert(1)</script>';
  assert.doesNotMatch(renderChecklist(hostile),/<script>alert/);
  assert.match(renderChecklist(hostile),/&lt;script&gt;/);
  assert.equal(renderChecklist(inventory),renderChecklist(inventory));
  assert.equal(renderInventoryMarkdown(inventory),renderInventoryMarkdown(inventory));
});
test('Real filter controller combines selectors, reports empty results and resets without storage writes',()=>{
  const elements=new Map();
  for(const id of ['feature-category','feature-status','feature-results','feature-empty','feature-clear','language'])elements.set(id,{value:id==='language'?'en':'all',listeners:{},addEventListener(event,fn){this.listeners[event]=fn;},focus(){this.focused=true;}});
  const rows=inventory.entries.map(r=>({dataset:{category:r.category,status:r.status},hidden:false}));
  const context={document:{getElementById:id=>elements.get(id),querySelectorAll:()=>rows}};
  vm.runInNewContext(fs.readFileSync(path.join(site,'checklist-client.js'),'utf8'),context);
  const event=(id,value)=>{const el=elements.get(id);el.value=value;el.listeners.change();};
  assert.match(elements.get('feature-results').textContent,/124 of 124/);
  event('feature-category','milestones');assert.equal(rows.filter(r=>!r.hidden).length,5);
  event('feature-status','implemented');assert.equal(rows.filter(r=>!r.hidden).length,5);
  event('language','zh');assert.match(elements.get('feature-results').textContent,/顯示 124 項中嘅 5 項/);
  event('language','both');assert.match(elements.get('feature-results').textContent,/5 of 124.*顯示/);
  event('feature-status','verified');assert.equal(rows.filter(r=>!r.hidden).length,0);assert.equal(elements.get('feature-empty').hidden,false);
  elements.get('feature-clear').listeners.click();assert.equal(rows.filter(r=>!r.hidden).length,124);assert.equal(elements.get('feature-category').focused,true);
  assert.equal(context.localStorage,undefined);
});
test('Existing preparation storage and deterministic embedded documentation remain intact',()=>{
  const html=fs.readFileSync(path.join(site,'index.html'),'utf8');
  for(const id of ['check-engine','check-source','check-storage','check-result'])assert.match(html,new RegExp('id="'+id+'"'));
  assert.match(html,/localStorage\.getItem\('mdi-guide-v1'\)/);
  execFileSync(process.execPath,['website/render.mjs','--check'],{cwd:root,stdio:'pipe'});
});
test('Actual renderer rejects stale HTML, summaries, manifests and evidence bytes without repairing them',()=>{
  const temporaryRoot=path.resolve(os.tmpdir());
  const fixture=fs.mkdtempSync(path.join(temporaryRoot,'mdi-checklist-'));
  try {
    for(const directory of ['website','docs','evidence','wiki'])if(fs.existsSync(path.join(root,directory)))fs.cpSync(path.join(root,directory),path.join(fixture,directory),{recursive:true});
    for(const file of ['README.md','ROADMAP.md','HANDOFF.md'])fs.copyFileSync(path.join(root,file),path.join(fixture,file));
    const check=()=>execFileSync(process.execPath,['website/render.mjs','--check'],{cwd:fixture,stdio:'pipe'});
    check();
    for(const file of ['website/index.html','website/content-manifest.json','docs/features/capability-inventory.md']) {
      const target=path.join(fixture,file),original=fs.readFileSync(target);
      const changed=file.includes('capability-inventory')?Buffer.from(original.toString().replace('124 entries retain','125 entries retain')):Buffer.concat([original,Buffer.from('\nstale')]);
      fs.writeFileSync(target,changed);assert.throws(check,/stale/);assert.deepEqual(fs.readFileSync(target),changed);fs.writeFileSync(target,original);check();
    }
    const manifest=JSON.parse(fs.readFileSync(path.join(fixture,'website/content-manifest.json'),'utf8'));
    const image=path.join(fixture,'website',manifest.images[0].destination),original=fs.readFileSync(image);
    fs.writeFileSync(image,Buffer.from('invalid'));assert.throws(check,/differs from source/);fs.writeFileSync(image,original);check();
  } finally {
    if(path.dirname(fixture)!==temporaryRoot||!path.basename(fixture).startsWith('mdi-checklist-'))throw new Error('Unexpected fixture cleanup path');
    fs.rmSync(fixture,{recursive:true});
  }
});
// ---- Site delivery: download panel, link previews, provenance, site-wide search, offline package ----
const pageHtml=fs.readFileSync(path.join(site,'index.html'),'utf8');
const loadClient=(file,context={})=>{vm.runInNewContext(fs.readFileSync(path.join(site,file),'utf8'),context);return context;};
const J=value=>JSON.parse(JSON.stringify(value));
const pending=[];
const testAsync=(name,body)=>pending.push(body().then(()=>{passed++;console.log('PASS '+name);}));
const RELEASE_URL='https://github.com/Ding-Ding-Projects/material-docker-image/releases/download/v0.9.1/';
const sampleRelease=()=>({tag_name:'v0.9.1',published_at:'2026-11-03T12:30:00Z',assets:[
  {name:'Setup.exe',size:75*1024*1024,browser_download_url:RELEASE_URL+'Setup.exe'},
  {name:'MaterialDockerImageBuild.exe',size:12345678,browser_download_url:RELEASE_URL+'MaterialDockerImageBuild.exe'},
  {name:'evil.exe',size:10,browser_download_url:'https://example.com/evil.exe'},
  {name:'RELEASES',size:512,browser_download_url:RELEASE_URL+'RELEASES'}]});
const fakeResponse=(status,body,headers={})=>({ok:status>=200&&status<300,status,headers:{get:name=>headers[name.toLowerCase()]??null},json:async()=>{if(body instanceof Error)throw body;return body;}});

test('Download panel parses a real release shape and drops assets that are not this repository\'s downloads',()=>{
  const {MDIDownload}=loadClient('download-client.js');
  const parsed=MDIDownload.parseRelease(sampleRelease());
  assert.equal(parsed.version,'0.9.1');assert.equal(parsed.publishedAt,'2026-11-03T12:30:00.000Z');
  assert.deepEqual(J(parsed.assets.map(a=>a.name)),['Setup.exe','MaterialDockerImageBuild.exe','RELEASES']);
  assert.equal(MDIDownload.formatSize(75*1024*1024),'75.0 MB');assert.equal(MDIDownload.formatSize(512),'512 B');assert.equal(MDIDownload.formatSize(-1),'unknown size');
  for(const bad of [null,[],{},{tag_name:'v1'},{tag_name:'<b>',published_at:'2026-01-01T00:00:00Z',assets:[]},{tag_name:'v1',published_at:'not a date',assets:[]},{tag_name:'v1',published_at:'2026-01-01T00:00:00Z',assets:'x'}])assert.throws(()=>MDIDownload.parseRelease(bad),/malformed/);
});
testAsync('Download panel reports every failure honestly and never invents a version',async()=>{
  const {MDIDownload}=loadClient('download-client.js');
  const run=fetchFn=>MDIDownload.loadRelease(fetchFn);
  const ready=await run(async()=>fakeResponse(200,sampleRelease()));assert.equal(ready.kind,'ready');assert.equal(ready.release.version,'0.9.1');
  assert.deepEqual(J(await run(async()=>fakeResponse(403,{},{'x-ratelimit-remaining':'0'}))),{kind:'rate-limited'});
  assert.deepEqual(J(await run(async()=>fakeResponse(429,{}))),{kind:'rate-limited'});
  assert.deepEqual(J(await run(async()=>fakeResponse(403,{}))),{kind:'unavailable'});
  assert.deepEqual(J(await run(async()=>fakeResponse(404,{}))),{kind:'none'});
  assert.deepEqual(J(await run(async()=>fakeResponse(500,{}))),{kind:'unavailable'});
  assert.deepEqual(J(await run(async()=>{throw new TypeError('Failed to fetch');})),{kind:'offline'});
  assert.deepEqual(J(await run(async()=>fakeResponse(200,new SyntaxError('bad json')))),{kind:'unavailable'});
  assert.deepEqual(J(await run(async()=>fakeResponse(200,{tag_name:'v1'}))),{kind:'unavailable'});
  for(const kind of ['loading','offline','rate-limited','none','unavailable','offlinePackage']){assert.ok(MDIDownload.MESSAGES[kind].every(s=>s.length>10),kind);assert.doesNotMatch(MDIDownload.MESSAGES[kind].join(' '),/\d+\.\d+\.\d+/);}
});
test('Home page carries the download panel with a releases-page fallback and no baked-in release version',()=>{
  for(const id of ['download-panel','download-status','download-setup','download-companion','download-docs','download-assets','download-fallback'])assert.match(pageHtml,new RegExp('id="'+id+'"'));
  assert.match(pageHtml,/id="download-status" role="status" aria-live="polite"/);
  assert.ok(pageHtml.includes('id="download-fallback" href="https://github.com/Ding-Ding-Projects/material-docker-image/releases"'));
  assert.ok(pageHtml.includes("https://api.github.com/repos/' + REPOSITORY + '/releases/latest")&&pageHtml.includes("REPOSITORY = 'Ding-Ding-Projects/material-docker-image'"));
  assert.doesNotMatch(pageHtml,/releases\/download\/v?\d/);
  assert.match(pageHtml,/data-offline="false"/);
});
test('Link previews: Open Graph and Twitter metadata point at a real 1200x630 PNG listed in the manifest',()=>{
  const pagesUrl='https://ding-ding-projects.github.io/material-docker-image/';
  for(const meta of ['property="og:title"','property="og:description"','property="og:type" content="website"','property="og:url" content="'+pagesUrl+'"','property="og:image" content="'+pagesUrl+'social-preview.png"','property="og:image:width" content="1200"','property="og:image:height" content="630"','property="og:image:alt"','name="twitter:card" content="summary_large_image"','name="twitter:image" content="'+pagesUrl+'social-preview.png"'])assert.ok(pageHtml.includes(meta),meta);
  const png=fs.readFileSync(path.join(site,'social-preview.png'));
  assert.ok(png.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10])));
  assert.equal(png.readUInt32BE(16),1200);assert.equal(png.readUInt32BE(20),630);assert.ok(png.length>20000&&png.length<5*1024*1024);
  const manifest=JSON.parse(fs.readFileSync(path.join(site,'content-manifest.json'),'utf8'));
  const entry=manifest.staticAssets.find(a=>a.path==='social-preview.png');
  assert.ok(entry);assert.equal(entry.sha256,crypto.createHash('sha256').update(png).digest('hex'));
  assert.ok(fs.existsSync(path.join(root,'scripts/make-social-preview.mjs')));
});
test('Front provenance shows the source version and commit-derived update time, both bound by the manifest',()=>{
  const provenance=JSON.parse(fs.readFileSync(path.join(site,'provenance.json'),'utf8'));
  assert.match(provenance.version,/^\d+\.\d+\.\d+$/);assert.match(provenance.updatedAt,/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$/);assert.ok(!Number.isNaN(Date.parse(provenance.updatedAt)));
  assert.match(fs.readFileSync(path.join(root,'src/MaterialDockerImage/MaterialDockerImage.csproj'),'utf8'),new RegExp('<Version>'+provenance.version.replace(/\./g,'\\.')+'</Version>'));
  assert.ok(pageHtml.includes('Documentation source version '+provenance.version+' · updated '+provenance.updatedAt));
  assert.ok(pageHtml.includes('文件來源版本 '+provenance.version+' · 更新時間 '+provenance.updatedAt));
  assert.doesNotMatch(pageHtml,/\{\{[A-Z_]+\}\}/);
  const manifest=JSON.parse(fs.readFileSync(path.join(site,'content-manifest.json'),'utf8'));
  assert.ok(manifest.staticAssets.some(a=>a.path==='provenance.json'));
});
test('Search core is plain-text by default, escapes literals and validates regular expressions',()=>{
  const {MDISearch}=loadClient('search-client.js');
  const plain=MDISearch.buildMatcher('a.b(c)',{flags:'i'});
  assert.ok(plain.ok&&plain.expression.test('xx A.B(C) yy')&&!plain.expression.test('axb(c)'));
  assert.ok(MDISearch.buildMatcher('',{}).empty);
  const regex=MDISearch.buildMatcher('v\\d+\\.\\d+',{regex:true,flags:'i'});assert.ok(regex.ok&&regex.expression.test('V1.20'));
  assert.equal(MDISearch.buildMatcher('(',{regex:true}).error,'invalid');assert.match(MDISearch.buildMatcher('(',{regex:true}).detail,/\S/);
  assert.equal(MDISearch.buildMatcher('(',{regex:false}).ok,true);
  assert.equal(MDISearch.buildMatcher('x'.repeat(201),{}).error,'too-long');
  for(const hostile of ['(a+)+$','(a*)*b','(\\w+\\s?)+x','(.*a){2}*'])assert.equal(MDISearch.buildMatcher(hostile,{regex:true}).error,'nested-quantifier',hostile);
  assert.equal(MDISearch.buildMatcher('x',{regex:true,flags:'gzi'}).flags,'i');
  assert.equal(MDISearch.buildMatcher('(a+)+$',{regex:false}).ok,true);
});
test('Search execution is bounded by result cap, time budget and per-section text limit',()=>{
  const {MDISearch}=loadClient('search-client.js');
  const sections=Array.from({length:500},(_,i)=>({id:'s'+i,article:'Article',heading:'Heading '+i,text:'needle '+i}));
  const matcher=MDISearch.buildMatcher('needle',{});
  const capped=MDISearch.runSearch(sections,matcher);assert.equal(capped.results.length,200);assert.equal(capped.capped,true);assert.equal(capped.results[0].id,'s0');
  assert.equal(MDISearch.runSearch(sections,matcher,{maxResults:3}).results.length,3);
  const late=MDISearch.runSearch(sections,matcher,{budgetMs:-1});assert.equal(late.timedOut,true);assert.equal(late.results.length,0);
  const long=[{id:'long',article:'A',heading:'H',text:'x'.repeat(25000)+' needle'}];
  const truncated=MDISearch.runSearch(long,matcher);assert.equal(truncated.results.length,0);assert.equal(truncated.truncatedSections,1);
  const hit=MDISearch.runSearch([{id:'h',article:'A',heading:'H',text:'before '.repeat(30)+'NEEDLE'+' after'.repeat(30)}],MDISearch.buildMatcher('needle',{flags:'i'}));
  assert.equal(hit.results[0].snippet.match,'NEEDLE');assert.ok(hit.results[0].snippet.before.startsWith('…'));
  assert.deepEqual(J(MDISearch.runSearch(sections,{ok:false,error:'invalid'}).results),[]);
});
test('Search sections follow article headings so results link to anchors that exist',()=>{
  const {MDISearch}=loadClient('search-client.js');
  const child=(tagName,textContent,id='')=>({tagName,textContent,id});
  const root={querySelectorAll:()=>[{id:'doc-a',querySelector:()=>child('H3','Article A'),children:[child('H3','Article A'),child('P','intro'),child('H4','Part one','doc-a-part-one'),child('P','one body'),child('H4','Part two','doc-a-part-two'),child('P','two body')]}]};
  const sections=MDISearch.collectSections(root);
  assert.deepEqual(J(sections.map(s=>s.id)),['doc-a','doc-a-part-one','doc-a-part-two']);
  assert.ok(sections[0].text.includes('intro')&&!sections[0].text.includes('one body'));
  assert.ok(sections[2].text.includes('two body'));
  const ids=[...pageHtml.matchAll(/ id="([^"]+)"/g)].map(m=>m[1]);
  assert.equal(new Set(ids).size,ids.length,'every id on the page is unique');
  const headings=[...pageHtml.matchAll(/<h[3-8] id="(doc-[^"]+)">/g)].length;
  assert.ok(headings>100,'embedded article headings carry anchors ('+headings+')');
});
test('Search markup has a plain-text field, regex toggle, flags, adjacent builder, validation message and both languages',()=>{
  for(const id of ['search','regex-toggle','regex-panel','regex-mode','flag-i','flag-m','flag-s','flag-u','regex-pattern','regex-error','regex-tokens','regex-library','search-status','search-results','clear-search'])assert.match(pageHtml,new RegExp('id="'+id+'"'));
  assert.match(pageHtml,/<input id="regex-mode" type="checkbox">/);assert.doesNotMatch(pageHtml,/<input id="regex-mode" type="checkbox" checked/);
  assert.match(pageHtml,/id="regex-error" role="alert"/);assert.match(pageHtml,/id="search-status" role="status" aria-live="polite"/);
  assert.match(pageHtml,/not RegexBuddy parity/);assert.match(pageHtml,/data-zh="搜尋全部文件"/);
  assert.doesNotMatch(pageHtml,/data-search=/,'the duplicated lowercase article copy is gone');
});
test('Offline documentation package renders without remote assets or release lookup and keeps every internal link working from file://',()=>{
  const offlineDir=path.join(root,'out','docs','offline-check');
  try {
    execFileSync(process.execPath,['website/render.mjs','--offline','out/docs/offline-check'],{cwd:root,stdio:'pipe'});
    const html=fs.readFileSync(path.join(offlineDir,'index.html'),'utf8');
    assert.match(html,/data-offline="true"/);assert.doesNotMatch(html,/property="og:|name="twitter:/);
    assert.doesNotMatch(html,/(?:src|srcset|poster)\s*=\s*["']https?:/i);assert.doesNotMatch(html,/<link[^>]+href\s*=\s*["']https?:/i);assert.doesNotMatch(html,/url\(\s*["']?https?:/i);assert.doesNotMatch(html,/@import\s+(?:url\(|["'])/i);assert.doesNotMatch(html,/<script[^>]+src=/i);
    const ids=new Set([...html.matchAll(/ id="([^"]+)"/g)].map(m=>m[1]));
    const anchors=[...html.matchAll(/href="#([^"]+)"/g)].map(m=>m[1]);assert.ok(anchors.length>50);
    for(const anchor of anchors)assert.ok(ids.has(anchor),'internal link target exists: '+anchor);
    const relative=[...html.matchAll(/(?:src|href)="((?!https?:|#|data:|mailto:)[^"]+)"/g)].map(m=>m[1]);
    assert.ok(relative.includes('favicon.svg')&&relative.some(r=>r.startsWith('assets/')));
    for(const file of relative)assert.ok(fs.existsSync(path.join(offlineDir,file)),'offline file exists: '+file);
    assert.ok(fs.existsSync(path.join(offlineDir,'content-manifest.json')));
    const online=[...pageHtml.matchAll(/(?:src|href)="((?!https?:|#|data:|mailto:)[^"]+)"/g)].map(m=>m[1]);
    for(const file of online)assert.ok(fs.existsSync(path.join(site,file)),'deployed file exists: '+file);
    assert.equal(html.includes('doc-readme-md'),true);
    assert.throws(()=>execFileSync(process.execPath,['website/render.mjs','--offline','website/escape'],{cwd:root,stdio:'pipe'}),/out\/docs/);
    assert.throws(()=>execFileSync(process.execPath,['website/render.mjs','--offline'],{cwd:root,stdio:'pipe'}),/needs an output directory/);
  } finally {
    if(path.basename(offlineDir)==='offline-check'&&fs.existsSync(offlineDir))fs.rmSync(offlineDir,{recursive:true});
  }
});
test('Release workflow and entrypoints ship and read back the offline documentation asset',()=>{
  const workflow=fs.readFileSync(path.join(root,'.github/workflows/release.yml'),'utf8');
  assert.match(workflow,/MaterialDockerImageDocs\.zip','docs\.json'/);assert.match(workflow,/docsReceipt\.source -ne \$env:GITHUB_SHA/);
  assert.match(fs.readFileSync(path.join(root,'build-installer.bat'),'utf8'),/package-docs\.ps1/);
  const script=fs.readFileSync(path.join(root,'build/package-docs.ps1'),'utf8');
  for(const needle of ['out/docs/MaterialDockerImageDocs.zip','--offline out/docs/site','remote asset','Assert-SourceBinding','out/receipts/docs.json'])assert.ok(script.includes(needle),needle);
});
await Promise.all(pending);
console.log(`Feature checklist: ${passed} checks passed, including 124 independent missing-entry regressions. Browser interaction and pixels require separate verification.`);
