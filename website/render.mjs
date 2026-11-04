import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {loadInventory, renderChecklist, renderInventoryMarkdown, inventorySummary} from './checklist.mjs';
const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.dirname(here);
const sourceText=file=>fs.readFileSync(file,'utf8').replace(/\r\n/g,'\n');
const inventory=loadInventory(here);
const inventoryFile=path.join(root,'docs/features/capability-inventory.md');
const existingInventory=sourceText(inventoryFile);
const inventoryDocument=existingInventory.replace(/(## Required-feature inventory\n)[\s\S]*?(\n## Known limits)/,'$1\n'+renderInventoryMarkdown(inventory)+'\n\n$2');
if(inventoryDocument===existingInventory&&!existingInventory.includes('<!-- Generated from website/feature-inventory.json'))throw new Error('Required-feature inventory section is missing');
if(process.argv.includes('--stamp')){
  // Stamping reads the latest commit that touched any rendered source; it is the only step that runs git.
  const sources=['docs','wiki','README.md','ROADMAP.md','HANDOFF.md','website/template.html','website/feature-inventory.json','website/render.mjs','website/checklist.mjs','website/checklist.css','website/checklist-client.js','website/download-client.js','website/search-client.js'];
  const git=args=>execFileSync('git',args,{cwd:root,encoding:'utf8'}).trim();
  const committed=git(['log','-1','--format=%cI','--',...sources]);
  const version=fs.readFileSync(path.join(root,'src/MaterialDockerImage/MaterialDockerImage.csproj'),'utf8').match(/<Version>(\d+\.\d+\.\d+)<\/Version>/)?.[1];
  if(!committed||!version)throw new Error('Cannot stamp: no commit or project version found');
  const updatedAt=new Date(committed).toISOString().replace(/\.\d+Z$/,'Z');
  fs.writeFileSync(path.join(here,'provenance.json'),JSON.stringify({version,updatedAt},null,2)+'\n');
  console.log('Stamped documentation source version '+version+', updated '+updatedAt+'.');
  process.exit(0);
}
const offlineIndex=process.argv.indexOf('--offline');
const check=process.argv.includes('--check');
if(check&&inventoryDocument!==existingInventory)throw new Error('Generated capability inventory is stale; run node website/render.mjs');
if(!check&&inventoryDocument!==existingInventory)fs.writeFileSync(inventoryFile,inventoryDocument);
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const files=fs.readdirSync(path.join(root,'docs/features')).filter(f=>f.endsWith('.md')).sort().map(f=>'docs/features/'+f);
for(const f of ['README.md','ROADMAP.md','HANDOFF.md'])if(fs.existsSync(path.join(root,f)))files.push(f);
function wikiFiles(directory){if(!fs.existsSync(path.join(root,directory)))return;for(const entry of fs.readdirSync(path.join(root,directory),{withFileTypes:true})){const name=directory+'/'+entry.name;if(entry.isDirectory())wikiFiles(name);else if(name.endsWith('.md'))files.push(name);}}
wikiFiles('wiki');
const id=f=>'doc-'+f.toLowerCase().replace(/[^a-z0-9]+/g,'-');
const target=(href,source)=>{if(/^https:\/\//.test(href))return href;const file=path.posix.normalize(path.posix.join(path.posix.dirname(source),href.split('#')[0]));return files.includes(file)?'#'+id(file):null;};
const imageAssets=new Map();
function localImage(href,alt,source){
  // Articles may use a source-relative path (../../evidence/...) so they also render on the forge;
  // it is resolved to its repository-root evidence path before the same strict validation.
  if(href.startsWith('../')){const resolved=path.posix.normalize(path.posix.join(path.posix.dirname(source),href));if(resolved.startsWith('evidence/'))href=resolved;}
  // Image paths are repository-root evidence paths, never URLs or relative traversal.
  if(!/^evidence\/(?:[A-Za-z0-9_-]+\/)*[A-Za-z0-9_.-]+\.png$/i.test(href)||href.split('/').some(p=>p==='.'||p==='..'))throw new Error('Unsupported local image path in '+source+': '+href);
  const input=path.join(root,...href.split('/'));
  if(!fs.existsSync(input))throw new Error('Referenced evidence PNG is missing: '+href+' ('+source+')');
  const realRoot=fs.realpathSync(root),realInput=fs.realpathSync(input);
  if(path.relative(realRoot,realInput)!==href.split('/').join(path.sep))throw new Error('Evidence image resolves through a redirected path: '+href);
  let cursor=root;for(const segment of href.split('/')){cursor=path.join(cursor,segment);if(fs.lstatSync(cursor).isSymbolicLink())throw new Error('Linked evidence paths are not supported: '+href);}
  if(!fs.statSync(realInput).isFile())throw new Error('Evidence PNG is not a regular file: '+href);
  const bytes=fs.readFileSync(realInput);
  if(bytes.length<33||!bytes.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]))||bytes.toString('ascii',12,16)!=='IHDR')throw new Error('Referenced evidence file is not a PNG: '+href);
  const width=bytes.readUInt32BE(16),height=bytes.readUInt32BE(20);
  if(!width||!height)throw new Error('Evidence PNG has invalid dimensions: '+href);
  const sha256=crypto.createHash('sha256').update(bytes).digest('hex');
  const destination='assets/'+sha256+'.png';
  imageAssets.set(destination,{path:href,destination,sha256,width,height,bytes});
  return '<img class="evidence-image" src="'+destination+'" alt="'+esc(alt)+'" width="'+width+'" height="'+height+'" loading="lazy" decoding="async">';
}
function inline(s,source){
  // Tokenize images and code before text markup so generated attributes stay untouched.
  const pattern=/`([^`]+)`|!\[([^\]]*)\]\(([^)]+)\)/g;let output='',at=0;
  const prose=text=>esc(text).replace(/\*\*([^*]+)\*\*/g,'<strong>$1</strong>').replace(/\[([^\]]+)\]\(([^)]+)\)/g,(_,label,href)=>{const to=target(href,source);return to?'<a href="'+esc(to)+'">'+label+'</a>':label+' <span class="muted">(document unavailable)</span>';});
  for(const match of s.matchAll(pattern)){output+=prose(s.slice(at,match.index));output+=match[1]!==undefined?'<code>'+esc(match[1])+'</code>':localImage(match[3],match[2],source);at=match.index+match[0].length;}return output+prose(s.slice(at));
}
function markdown(s,source){let code=false,buf=[],out=[];const used=new Map();const anchor=text=>{const base=id(source)+'-'+(text.toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,60)||'section');const n=(used.get(base)||0)+1;used.set(base,n);return n===1?base:base+'-'+n;};for(const line of s.split(/\r?\n/)){if(line.startsWith('```')){if(code){out.push('<pre><code>'+esc(buf.join('\n'))+'</code></pre>');buf=[];}code=!code;continue;}if(code){buf.push(line);continue;}if(!line.trim())continue;let m=line.match(/^(#{1,6}) (.+)/);if(m){const n=Math.min(6,m[1].length+2);out.push('<h'+n+' id="'+anchor(m[2])+'">'+inline(m[2],source)+'</h'+n+'>');}else if((m=line.match(/^- \[([ x])\] (.+)/)))out.push('<p class="source-item">'+(m[1]==='x'?'Completed: ':'Pending: ')+inline(m[2],source)+'</p>');else if((m=line.match(/^- (.+)/)))out.push('<p class="source-item">• '+inline(m[1],source)+'</p>');else out.push('<p>'+inline(line,source)+'</p>');}if(buf.length)out.push('<pre><code>'+esc(buf.join('\n'))+'</code></pre>');return out.join('\n');}
const entries=files.map(f=>({path:f,sha256:crypto.createHash('sha256').update(sourceText(path.join(root,f))).digest('hex')}));
const provenance=JSON.parse(fs.readFileSync(path.join(here,'provenance.json'),'utf8'));
if(!/^\d+\.\d+\.\d+$/.test(provenance.version)||!/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$/.test(provenance.updatedAt)||Number.isNaN(Date.parse(provenance.updatedAt)))throw new Error('website/provenance.json needs a semantic version and a UTC timestamp; run node website/render.mjs --stamp');
const staticAssets=['favicon.svg','provenance.json','social-preview.png'].map(file=>({path:file,sha256:crypto.createHash('sha256').update(file.endsWith('.png')?fs.readFileSync(path.join(here,file)):sourceText(path.join(here,file))).digest('hex')}));
const SITE='https://ding-ding-projects.github.io/material-docker-image/';
const socialMeta=['<meta property="og:type" content="website">','<meta property="og:site_name" content="Material Docker Image">','<meta property="og:title" content="Material Docker Image: practical guides">','<meta property="og:description" content="Build, run, canvas and diagnostics guides for Material Docker Image, a native Windows workspace.">','<meta property="og:url" content="'+SITE+'">','<meta property="og:image" content="'+SITE+'social-preview.png">','<meta property="og:image:type" content="image/png">','<meta property="og:image:width" content="1200">','<meta property="og:image:height" content="630">','<meta property="og:image:alt" content="The Material Docker Image build workspace, captured from the built application.">','<meta name="twitter:card" content="summary_large_image">','<meta name="twitter:title" content="Material Docker Image: practical guides">','<meta name="twitter:description" content="Build, run, canvas and diagnostics guides for Material Docker Image, a native Windows workspace.">','<meta name="twitter:image" content="'+SITE+'social-preview.png">','<meta name="twitter:image:alt" content="The Material Docker Image build workspace, captured from the built application.">'].join('');
const fill=(text,token,value)=>text.split(token).join(value);
function assemble(offline){
  let html=template.replace('<!--ARTICLE_LINKS-->',()=>nav).replace('<!--ARTICLES-->',()=>articles).replace('<!--FEATURE_CHECKLIST-->',()=>renderChecklist(inventory)).replace('/* FEATURE_CHECKLIST_CSS */',()=>sourceText(path.join(here,'checklist.css'))).replace('/* FEATURE_CHECKLIST_CLIENT */',()=>sourceText(path.join(here,'checklist-client.js'))).replace('/* DOWNLOAD_CLIENT */',()=>sourceText(path.join(here,'download-client.js'))).replace('/* SEARCH_CLIENT */',()=>sourceText(path.join(here,'search-client.js')));
  html=html.replace('<!--SOCIAL_META-->',()=>offline?'':socialMeta);
  html=fill(html,'{{DOC_VERSION}}',provenance.version);html=fill(html,'{{UPDATED_AT}}',provenance.updatedAt);html=fill(html,'{{OFFLINE}}',String(offline));
  if(offline)html=html.replace('<title>','<title>[Offline] ');
  return html;
}
const articles=files.map(f=>{const s=sourceText(path.join(root,f));const title=s.match(/^# (.+)/m)?.[1]||f;return '<article id="'+id(f)+'" class="article"><h3>'+esc(title)+'</h3><p class="source-label">Complete source: <a href="https://github.com/Ding-Ding-Projects/material-docker-image/blob/main/'+f+'">'+esc(f)+'</a></p>'+markdown(s,f)+'</article>';}).join('\n');
const nav=files.map(f=>'<a href="#'+id(f)+'">'+esc(f.replace('docs/features/',''))+'</a>').join('');
const template=sourceText(path.join(here,'template.html'));
const output=assemble(false);
const manifest=JSON.stringify({meaning:'Deterministic source inventory, not application release provenance',features:{...inventorySummary(inventory),sha256:crypto.createHash('sha256').update(sourceText(path.join(here,'feature-inventory.json'))).digest('hex')},sources:entries,staticAssets,images:[...imageAssets.values()].map(({bytes,...entry})=>entry)},null,2)+'\n';
if(offlineIndex>=0){
  // Offline package: the same embedded corpus with no remote asset, no release lookup and no social metadata.
  const requested=process.argv[offlineIndex+1];
  if(!requested)throw new Error('--offline needs an output directory under out/docs');
  const directory=path.resolve(requested),allowed=path.join(root,'out','docs')+path.sep;
  if(!(directory+path.sep).startsWith(allowed)||directory+path.sep===allowed)throw new Error('The offline directory must be a subdirectory of out/docs');
  fs.rmSync(directory,{recursive:true,force:true});fs.mkdirSync(path.join(directory,'assets'),{recursive:true});
  fs.writeFileSync(path.join(directory,'index.html'),assemble(true));
  fs.writeFileSync(path.join(directory,'content-manifest.json'),manifest);
  for(const file of ['favicon.svg','feature-inventory.json'])fs.copyFileSync(path.join(here,file),path.join(directory,file));
  for(const image of imageAssets.values())fs.writeFileSync(path.join(directory,image.destination),image.bytes);
  console.log('Wrote offline documentation site with '+files.length+' sources and '+imageAssets.size+' images to '+path.relative(root,directory)+'.');
  process.exit(0);
}
if(check){
  for(const [file,expected] of [['index.html',output],['content-manifest.json',manifest]])if(sourceText(path.join(here,file))!==expected)throw new Error('Generated '+file+' is stale; run node website/render.mjs');
  for(const image of imageAssets.values())if(!fs.existsSync(path.join(here,image.destination))||!fs.readFileSync(path.join(here,image.destination)).equals(image.bytes))throw new Error('Generated evidence image is missing or differs from source');
  console.log('Validated deterministic output, '+inventory.entries.length+' feature entries, '+files.length+' complete Markdown sources and '+imageAssets.size+' evidence PNGs; no output written.');
}else{
  if(imageAssets.size){
    const assets=path.join(here,'assets');
    if(fs.existsSync(assets)&&(!fs.lstatSync(assets).isDirectory()||fs.lstatSync(assets).isSymbolicLink()||fs.realpathSync(assets)!==path.join(fs.realpathSync(here),'assets')))throw new Error('Asset destination must be a local regular directory');
    fs.mkdirSync(assets,{recursive:true});
    for(const image of imageAssets.values()){
      const destination=path.join(here,image.destination);
      if(fs.existsSync(destination)){
        if(!fs.lstatSync(destination).isFile()||fs.lstatSync(destination).isSymbolicLink())throw new Error('Asset destination is not a regular file');
        if(!fs.readFileSync(destination).equals(image.bytes))throw new Error('Existing content-addressed PNG differs from source');
      }else fs.writeFileSync(destination,image.bytes,{flag:'wx'});
      if(!fs.readFileSync(destination).equals(image.bytes))throw new Error('Evidence PNG copy verification failed');
    }
  }
  fs.writeFileSync(path.join(here,'index.html'),output);
  fs.writeFileSync(path.join(here,'content-manifest.json'),manifest);
  console.log('Embedded '+files.length+' complete Markdown sources and '+imageAssets.size+' evidence PNGs.');
}
