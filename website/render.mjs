import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.dirname(here);
const sourceText=file=>fs.readFileSync(file,'utf8').replace(/\r\n/g,'\n');
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const files=fs.readdirSync(path.join(root,'docs/features')).filter(f=>f.endsWith('.md')).sort().map(f=>'docs/features/'+f);
for(const f of ['README.md','ROADMAP.md','HANDOFF.md'])if(fs.existsSync(path.join(root,f)))files.push(f);
function wikiFiles(directory){if(!fs.existsSync(path.join(root,directory)))return;for(const entry of fs.readdirSync(path.join(root,directory),{withFileTypes:true})){const name=directory+'/'+entry.name;if(entry.isDirectory())wikiFiles(name);else if(name.endsWith('.md'))files.push(name);}}
wikiFiles('wiki');
const id=f=>'doc-'+f.toLowerCase().replace(/[^a-z0-9]+/g,'-');
const target=(href,source)=>{if(/^https:\/\//.test(href))return href;const file=path.posix.normalize(path.posix.join(path.posix.dirname(source),href.split('#')[0]));return files.includes(file)?'#'+id(file):null;};
const imageAssets=new Map();
function localImage(href,alt,source){
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
function markdown(s,source){let code=false,buf=[],out=[];for(const line of s.split(/\r?\n/)){if(line.startsWith('```')){if(code){out.push('<pre><code>'+esc(buf.join('\n'))+'</code></pre>');buf=[];}code=!code;continue;}if(code){buf.push(line);continue;}if(!line.trim())continue;let m=line.match(/^(#{1,6}) (.+)/);if(m){const n=Math.min(6,m[1].length+2);out.push('<h'+n+'>'+inline(m[2],source)+'</h'+n+'>');}else if((m=line.match(/^- \[([ x])\] (.+)/)))out.push('<p class="source-item">'+(m[1]==='x'?'Completed: ':'Pending: ')+inline(m[2],source)+'</p>');else if((m=line.match(/^- (.+)/)))out.push('<p class="source-item">• '+inline(m[1],source)+'</p>');else out.push('<p>'+inline(line,source)+'</p>');}if(buf.length)out.push('<pre><code>'+esc(buf.join('\n'))+'</code></pre>');return out.join('\n');}
const entries=files.map(f=>({path:f,sha256:crypto.createHash('sha256').update(sourceText(path.join(root,f))).digest('hex')}));
const articles=files.map(f=>{const s=sourceText(path.join(root,f));const title=s.match(/^# (.+)/m)?.[1]||f;return '<article id="'+id(f)+'" class="article" data-search="'+esc((title+' '+s).toLowerCase())+'"><h3>'+esc(title)+'</h3><p class="source-label">Complete source: <a href="https://github.com/Ding-Ding-Projects/material-docker-image/blob/main/'+f+'">'+esc(f)+'</a></p>'+markdown(s,f)+'</article>';}).join('\n');
const nav=files.map(f=>'<a href="#'+id(f)+'">'+esc(f.replace('docs/features/',''))+'</a>').join('');
const template=sourceText(path.join(here,'template.html'));
if(process.argv.includes('--check')){
  console.log('Validated '+files.length+' complete Markdown sources and '+imageAssets.size+' evidence PNGs; no output written.');
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
  fs.writeFileSync(path.join(here,'index.html'),template.replace('<!--ARTICLE_LINKS-->',nav).replace('<!--ARTICLES-->',articles));
  fs.writeFileSync(path.join(here,'content-manifest.json'),JSON.stringify({generatedAt:new Date().toISOString(),meaning:'Documentation generation time, not application release provenance',sources:entries,images:[...imageAssets.values()].map(({bytes,...entry})=>entry)},null,2)+'\n');
  console.log('Embedded '+files.length+' complete Markdown sources and '+imageAssets.size+' evidence PNGs.');
}
