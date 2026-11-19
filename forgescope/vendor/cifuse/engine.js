import {rules} from './rules.js';
export const MAX_BYTES = 5 * 1024 * 1024;
const bytes = value => new TextEncoder().encode(value).length;
export function redact(value) {
  return String(value)
    .replace(/\x1b\[[0-?]*[ -/]*[@-~]/g, '')
    .replace(/\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16})\b/g, '[REDACTED]')
    .replace(/\b(Bearer\s+)[A-Za-z0-9._~+\/-]+/gi, '$1[REDACTED]')
    .replace(/\b((?:[A-Z_]*(?:TOKEN|PASSWORD|SECRET|API_KEY)[A-Z_]*)\s*[:=]\s*)(?:"[^"\n]*"|'[^'\n]*'|[^\s,;]+)/gi, '$1[REDACTED]')
    .replace(/(https?:\/\/)[^\s/@]+:[^\s/@]+@/g, '$1[REDACTED]@')
    .replace(/([?&](?:token|key|secret|password)=)[^&\s]+/gi, '$1[REDACTED]');
}
function clean(line) {
  return redact(line).replace(/^\d{4}-\d\d-\d\d[T ][\d:.]+Z?\s*/, '').trim();
}
export function signature(ruleId, line) {
  return ruleId + ':' + clean(line).replace(/\b\d+(?:\.\d+)?(?:ms|s|m)?\b/g, '#').replace(/\s+/g, ' ').toLowerCase();
}
export function analyze(sources, {lang='en', baseline}={}) {
  if (!Array.isArray(sources) || sources.some(s=>typeof s.name!=='string'||typeof s.text!=='string')) throw new TypeError('Expected an array of {name, text} logs');
  if (sources.length>100 || sources.reduce((sum,s)=>sum+bytes(s.text)+bytes(s.name),0)>MAX_BYTES) throw new RangeError('Log limit: 100 files / 5 MiB total');
  const locale=lang==='pt-BR'?1:0, groups=new Map(); let lines=0;
  for (const source of sources) {
    const all=source.text.split(/\r?\n/); lines+=all.length;
    for (let i=0;i<all.length;i++) {
      const line=clean(all[i]);
      const rule=rules.find(r=>r.match.test(line));
      if (!rule) continue;
      const key=signature(rule.id,line);
      let item=groups.get(key);
      if (!item) {
        item={signature:key,rule:rule.id,title:rule.title[locale],hint:rule.hint[locale],evidence:line.slice(0,1200),count:0,locations:[]};
        groups.set(key,item);
      }
      item.count++;
      if(item.locations.length<10) item.locations.push({file:redact(source.name),line:i+1,context:all.slice(Math.max(0,i-1),i+2).map(x=>redact(x).slice(0,1200))});
    }
  }
  if(baseline && (baseline.schemaVersion!==1||!Array.isArray(baseline.findings)||baseline.findings.some(f=>typeof f.signature!=='string'))) throw new TypeError('Invalid CIFuse v1 baseline report');
  const previous=new Set(baseline?.findings.map(f=>f.signature)??[]);
  const findings=[...groups.values()].map(f=>({...f,status:baseline?(previous.has(f.signature)?'existing':'new'):'uncompared'}));
  const current=new Set(findings.map(f=>f.signature));
  return {schemaVersion:1,lang:locale?'pt-BR':'en',summary:{files:sources.length,lines,signals:findings.reduce((n,f)=>n+f.count,0),groups:findings.length,new:baseline?findings.filter(f=>f.status==='new').length:null,resolved:baseline?[...previous].filter(s=>!current.has(s)).length:null},findings};
}
const escapeMd = s=>String(s).replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c])).replace(/([\\`*_{}\[\]()#+!|])/g,'\\$1').replace(/[\r\n]/g,' ');
export function markdown(report) {
  const pt=report.lang==='pt-BR';
  const rows=[`# CIFuse — ${pt?'relatório de falhas':'failure report'}`, '', `${report.summary.groups} ${pt?'grupos':'groups'} · ${report.summary.signals} ${pt?'sinais':'signals'} · ${report.summary.lines} ${pt?'linhas':'lines'}`, '', pt?'Correspondências de padrões; revise as evidências. A ausência de sinais não comprova sucesso.':'Pattern matches; review the evidence. No signals does not prove a successful build.', ''];
  if(report.context)rows.push(`## ${pt?'Contexto do projeto':'Project context'}`, '', escapeMd(JSON.stringify(report.context)), '', escapeMd(report.investigation.explanation), '');
  if(report.validation)rows.push(`## ${pt?'Validação':'Validation'}`, '', escapeMd(JSON.stringify(report.validation)), '');
  if(report.repair)rows.push(`## ${pt?'Correção':'Repair'}`, '', escapeMd(JSON.stringify(report.repair)), '');
  for(const f of report.findings) {
    if(f.role)rows.push(escapeMd(f.role),'');
    for(const l of f.codeLocations||[])rows.push(`- ${pt?'Código':'Code'}: ${escapeMd(l.path)}:${l.line} (${l.verified?(pt?'verificado':'verified'):(pt?'não verificado':'unverified')})${l.url?' '+l.url:''}`, ...l.snippet.map(s=>`    ${s.line}: ${escapeMd(s.text)}`));
    for(const d of f.codeDiagnostics||[])rows.push(escapeMd(`${d.path}:${d.line}: ${d.specifier} — ${d.status}; ${d.candidates.join(', ')}`), '');
    for(const check of f.diagnosis?.checks.slice(1)||[])rows.push(escapeMd(check),'');
    rows.push(`## ${escapeMd(f.title)} (${f.count})`, '', escapeMd(f.evidence), '', escapeMd(f.hint), '', ...f.locations.map(l=>`- ${escapeMd(l.file)}:${l.line}`), '');
  }
  return rows.join('\n');
}
