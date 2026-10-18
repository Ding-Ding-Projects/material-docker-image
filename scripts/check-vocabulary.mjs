import {existsSync,readFileSync} from 'node:fs';
import {createHash,timingSafeEqual} from 'node:crypto';
import {homedir} from 'node:os';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const directory=path.join(homedir(),'.agents','skills','agent-global-memory','scripts');
const source=path.join(directory,'vocabulary-dictionary.json');
const lock=path.join(homedir(),'.agents','vocabulary-locks','material-docker-image.lock');
try {
  if(!existsSync(source)){console.log('SKIP: private instruction source unavailable.');process.exit(0);}
  const {vocabularySemanticProjection}=await import(pathToFileURL(path.join(directory,'private-emission-guard.mjs')).href);
  const actual=createHash('sha256').update('material-docker-image-vocabulary-currency-v1\n').update(vocabularySemanticProjection(readFileSync(source,'utf8'))).digest();
  const expected=readFileSync(lock,'utf8').trim();
  if(!/^[0-9a-f]{64}$/.test(expected)||!timingSafeEqual(actual,Buffer.from(expected,'hex')))throw new Error('Stale currency lock.');
  console.log('PASS: instruction currency verified. Conversation use is not proven.');
}catch{console.error('FAIL: private instruction currency lock missing or stale.');process.exitCode=1;}
