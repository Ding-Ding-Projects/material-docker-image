import {existsSync,readFileSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {homedir} from 'node:os';
import path from 'node:path';
const source=path.join(homedir(),'.agents','skills','agent-global-memory','scripts','vocabulary-dictionary.json');
if(!existsSync(source)){console.log('UNVERIFIED: private publication dictionary unavailable.');process.exit(2);}
const dictionary=JSON.parse(readFileSync(source,'utf8'));
const terms=dictionary.terms.filter(t=>t.proseOnly).flatMap(t=>t.alias.split(' / ')).filter(t=>t.length>2);
const files=execFileSync('git',['ls-files','-co','--exclude-standard','-z'],{encoding:'utf8'}).split('\0').filter(Boolean);
let count=0;
for(const file of new Set(files)) {
  const bytes=readFileSync(file); if(bytes.includes(0))continue;
  let content=bytes.toString('utf8').replace(/Ding[- ]Ding[- ]Projects/g,'');
  // Published labels can hide inside code spans or hyphenated identifiers.
  // Review distinctive multiword phrases there too, without rewriting any source.
  const identifierText=content.replace(/https?:\/\/[^\s<>"')]+/g,'').replace(/[-_]+/g,' ');
  for(const term of terms.filter(t=>t.includes(' '))) {
    const escaped=term.replace(/[.*+?^$\{\}()|[\]\\]/g,'\\$&');
    if(new RegExp('(?<![A-Za-z0-9])'+escaped+'(?![A-Za-z0-9])','iu').test(identifierText)) { console.error('Publication identifier requires review: '+file);count++;break; }
  }
  // Literal technical spans remain unchanged, never automatically translated.
  content=content.replace(/```[\s\S]*?```/g,'').replace(/`[^`\n]+`/g,'').replace(/https?:\/\/[^\s<>"')]+/g,'');
  for(const term of terms) {
    // Single common words need human context review; this scan enforces distinctive phrases.
    if(!term.includes(' ')&&!term.startsWith('@'))continue;
    const escaped=term.replace(/[.*+?^$\{\}()|[\]\\]/g,'\\$&');
    if(new RegExp('(?<![A-Za-z0-9_])'+escaped+'(?![A-Za-z0-9_])','iu').test(content)) { console.error('Publication phrase requires review: '+file);count++;break; }
  }
}
if(count)process.exitCode=1;
else console.log('PASS: distinctive private phrases absent. Single-word context and pixels require independent review.');
