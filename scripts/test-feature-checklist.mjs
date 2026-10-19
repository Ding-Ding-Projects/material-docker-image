import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import os from 'node:os';
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

test('All 109 baseline identifiers, 10 existing workflows and four milestones are retained',()=>{
  assert.equal(inventory.entries.length,123);
  assert.equal(inventorySummary(inventory).required,109);
  assert.equal(inventorySummary(inventory).counts.verified,0);
  assert.ok(inventory.entries.filter(r=>r.kind==='required').every(r=>r.status==='unverified'));
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
  assert.equal((html.match(/<details class="feature-row"/g)||[]).length,123);
  assert.equal((html.match(/data-feature-id=/g)||[]).length,123);
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
  assert.match(elements.get('feature-results').textContent,/123 of 123/);
  event('feature-category','milestones');assert.equal(rows.filter(r=>!r.hidden).length,4);
  event('feature-status','planned');assert.equal(rows.filter(r=>!r.hidden).length,3);
  event('language','zh');assert.match(elements.get('feature-results').textContent,/顯示 123 項中嘅 3 項/);
  event('language','both');assert.match(elements.get('feature-results').textContent,/3 of 123.*顯示/);
  event('feature-status','verified');assert.equal(rows.filter(r=>!r.hidden).length,0);assert.equal(elements.get('feature-empty').hidden,false);
  elements.get('feature-clear').listeners.click();assert.equal(rows.filter(r=>!r.hidden).length,123);assert.equal(elements.get('feature-category').focused,true);
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
      const changed=file.includes('capability-inventory')?Buffer.from(original.toString().replace('123 entries retain','124 entries retain')):Buffer.concat([original,Buffer.from('\nstale')]);
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
console.log(`Feature checklist: ${passed} checks passed, including 123 independent missing-entry regressions. Browser interaction and pixels require separate verification.`);
