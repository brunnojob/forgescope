import fs from 'node:fs';
import {analyzeDiff} from './vendor/patchscope/engine.js';
import {analyze as maintenance} from './vendor/reporelief/analyze.js';
import {analyze as ci} from './vendor/cifuse/engine.js';
import {analyze as rescue} from './vendor/devrescue/engine.js';
const input = JSON.parse(fs.readFileSync(0, 'utf8'));
const result = {};
const inventory=new Set(Object.keys(input.files));
for (const path of Object.keys(input.files)) {
  const parts=path.split('/');parts.pop();
  while(parts.length){inventory.add(parts.join('/'));parts.pop();}
}
for (const [name, run] of Object.entries({
  patchscope: () => analyzeDiff(input.diff),
  reporelief: () => maintenance(input.files, [...new Set([...(input.changed || []),...(result.patchscope?.report?.files || []).map(f=>f.path)])], [...inventory]),
  cifuse: () => ci(input.logs || []),
  devrescue: () => {
    const reports = (input.logs || []).map(log => ({name: log.name, report: rescue(log.text)}));
    return {findings: reports.flatMap(({name,report}) => report.findings.map(f => ({...f, log:name})))};
  }
})) {
  if(name==='patchscope' && !input.diff) {result[name]={status:'skipped',reason:'No diff supplied'};continue;}
  try { result[name] = {status:'ok', report:run()}; }
  catch (e) { result[name] = {status:'error', error:String(e.message)}; }
}
console.log(JSON.stringify(result));
