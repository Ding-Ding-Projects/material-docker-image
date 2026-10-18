import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.dirname(here);
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const files=fs.readdirSync(path.join(root,'docs/features')).filter(f=>f.endsWith('.md')).sort().map(f=>'docs/features/'+f);
for(const f of ['README.md','ROADMAP.md','HANDOFF.md'])if(fs.existsSync(path.join(root,f)))files.push(f);
function wikiFiles(directory){if(!fs.existsSync(path.join(root,directory)))return;for(const entry of fs.readdirSync(path.join(root,directory),{withFileTypes:true})){const name=directory+'/'+entry.name;if(entry.isDirectory())wikiFiles(name);else if(name.endsWith('.md'))files.push(name);}}
wikiFiles('wiki');
const id=f=>'doc-'+f.toLowerCase().replace(/[^a-z0-9]+/g,'-');
const target=(href,source)=>{if(/^https:\/\//.test(href))return href;const file=path.posix.normalize(path.posix.join(path.posix.dirname(source),href.split('#')[0]));return files.includes(file)?'#'+id(file):null;};
function inline(s,source){return esc(s).replace(/`([^`]+)`/g,'<code>$1</code>').replace(/\*\*([^*]+)\*\*/g,'<strong>$1</strong>').replace(/\[([^\]]+)\]\(([^)]+)\)/g,(_,label,href)=>{const to=target(href,source);return to?'<a href="'+esc(to)+'">'+label+'</a>':label+' <span class="muted">(document unavailable)</span>';});}
function markdown(s,source){let code=false,buf=[],out=[];for(const line of s.split(/\r?\n/)){if(line.startsWith('```')){if(code){out.push('<pre><code>'+esc(buf.join('\n'))+'</code></pre>');buf=[];}code=!code;continue;}if(code){buf.push(line);continue;}if(!line.trim())continue;let m=line.match(/^(#{1,6}) (.+)/);if(m){const n=Math.min(6,m[1].length+2);out.push('<h'+n+'>'+inline(m[2],source)+'</h'+n+'>');}else if((m=line.match(/^- \[([ x])\] (.+)/)))out.push('<p class="source-item">'+(m[1]==='x'?'Completed: ':'Pending: ')+inline(m[2],source)+'</p>');else if((m=line.match(/^- (.+)/)))out.push('<p class="source-item">• '+inline(m[1],source)+'</p>');else out.push('<p>'+inline(line,source)+'</p>');}if(buf.length)out.push('<pre><code>'+esc(buf.join('\n'))+'</code></pre>');return out.join('\n');}
const entries=files.map(f=>({path:f,sha256:crypto.createHash('sha256').update(fs.readFileSync(path.join(root,f))).digest('hex')}));
const articles=files.map(f=>{const s=fs.readFileSync(path.join(root,f),'utf8');const title=s.match(/^# (.+)/m)?.[1]||f;return '<article id="'+id(f)+'" class="article" data-search="'+esc((title+' '+s).toLowerCase())+'"><h3>'+esc(title)+'</h3><p class="source-label">Complete source: <a href="https://github.com/Ding-Ding-Projects/material-docker-image/blob/main/'+f+'">'+esc(f)+'</a></p>'+markdown(s,f)+'</article>';}).join('\n');
const nav=files.map(f=>'<a href="#'+id(f)+'">'+esc(f.replace('docs/features/',''))+'</a>').join('');
const template=fs.readFileSync(path.join(here,'template.html'),'utf8');
fs.writeFileSync(path.join(here,'index.html'),template.replace('<!--ARTICLE_LINKS-->',nav).replace('<!--ARTICLES-->',articles));
fs.writeFileSync(path.join(here,'content-manifest.json'),JSON.stringify({generatedAt:new Date().toISOString(),meaning:'Documentation generation time, not application release provenance',sources:entries},null,2)+'\n');
console.log('Embedded '+files.length+' complete Markdown sources.');
