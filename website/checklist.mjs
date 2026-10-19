import fs from 'node:fs';
import path from 'node:path';

export const statusLabels = {
  unverified: {en: 'Unverified', zh: '尚未驗證'},
  planned: {en: 'Planned', zh: '計劃中'},
  implemented: {en: 'Implemented, verification pending', zh: '已實作，驗證待完成'},
  verified: {en: 'Fully verified', zh: '完整驗證'},
};
const milestones = ['feature-checklist', 'container-health-checks', 'container-device-mappings', 'container-gpu-access'];
const workflows = ['structured-execution', 'build-workspace', 'run-editor', 'native-canvas', 'resource-management', 'restart-restoration', 'package-release', 'installed-setup', 'native-captures', 'documentation-companions'].map(id => 'workflow-' + id);
const escape = text => String(text).replace(/[&<>"']/g, c => ({'&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;'}[c]));
const localized = (value, tag = 'span', attributes = '') => `<${tag} ${attributes} data-en="${escape(value.en)}" data-zh="${escape(value.zh)}">${escape(value.en)}</${tag}>`;
const fail = message => { throw new Error('Feature inventory: ' + message); };

export function validateInventory(inventory, requiredIds) {
  if (inventory.schemaVersion !== 1 || !Array.isArray(inventory.categories) || !Array.isArray(inventory.entries)) fail('unsupported schema');
  if (requiredIds.length !== 109 || new Set(requiredIds).size !== 109) fail('required baseline must contain 109 unique IDs');
  const bilingual = value => {
    if (!value || typeof value.en !== 'string' || typeof value.zh !== 'string' || !value.en.trim() || !value.zh.trim()) fail('English and Cantonese text are required');
  };
  const categories = new Set();
  for (const category of inventory.categories) {
    if (!/^[a-z][a-z0-9-]*$/.test(category.id) || categories.has(category.id)) fail('duplicate or invalid category');
    categories.add(category.id); bilingual(category.name);
  }
  const expected = new Map([...requiredIds.map(id => [id, 'required']), ...workflows.map(id => [id, 'workflow']), ...milestones.map(id => [id, 'milestone'])]);
  const seen = new Set();
  for (const row of inventory.entries) {
    if (seen.has(row.id)) fail('duplicate ID: ' + row.id);
    if (expected.get(row.id) !== row.kind) fail('unknown ID or incorrect kind: ' + row.id);
    seen.add(row.id);
    if (!categories.has(row.category)) fail('unknown category: ' + row.id);
    if (!Object.hasOwn(statusLabels, row.status)) fail('unknown status: ' + row.id);
    for (const key of ['name', 'summary', 'remaining']) bilingual(row[key]);
    if (!Array.isArray(row.evidence) || !row.evidence.length) fail('missing evidence references: ' + row.id);
    const kinds = new Set();
    for (const evidence of row.evidence) {
      if (!['source', 'checks', 'interaction', 'capture'].includes(evidence.kind)) fail('unknown evidence kind');
      bilingual(evidence.label);
      let url;
      try { url = new URL(evidence.url); } catch { fail('invalid public evidence URL'); }
      // Restrict links to this public project. No arbitrary hosts, credentials or query secrets.
      const repository = '/Ding-Ding-Projects/material-docker-image/';
      const githubPath = url.hostname === 'github.com' && url.pathname.startsWith(repository) && /^(blob|commit|actions\/runs|releases\/tag|issues|discussions)\//.test(url.pathname.slice(repository.length));
      const pagesRoot = new URL('https://ding-ding-projects.github.io/material-docker-image/');
      const pagesPath = url.hostname === pagesRoot.hostname && url.pathname.startsWith(pagesRoot.pathname);
      if (url.protocol !== 'https:' || url.username || url.password || url.port || url.search || /%|\\/.test(evidence.url) || !(githubPath || pagesPath)) fail('non-public or out-of-project evidence URL');
      kinds.add(evidence.kind);
      if (evidence.kind === 'source' && ['scope', 'sourceCommit', 'artifactSha256', 'recordedAt', 'result'].some(key => Object.hasOwn(evidence, key))) fail('source reference cannot carry verification metadata');
      if (evidence.kind !== 'source') {
        if (!/^[a-f0-9]{40}$/.test(evidence.sourceCommit || '') || !/^[a-f0-9]{64}$/.test(evidence.artifactSha256 || '')) fail('evidence must bind exact source and artifact');
        if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z$/.test(evidence.recordedAt || '') || !Number.isFinite(Date.parse(evidence.recordedAt))) fail('evidence requires a verified UTC timestamp');
        bilingual(evidence.scope);
        if (evidence.result !== 'passed') fail('verification evidence must have passed');
        if (evidence.kind === 'capture' && !/\.(png|webp)$/.test(url.pathname)) fail('capture must link directly to an image');
        if (!url.pathname.startsWith(repository + 'blob/' + evidence.sourceCommit + '/evidence/')) fail('verification evidence must link to an immutable project evidence file');
      }
    }
    if (row.status === 'verified') {
      if (!['source', 'checks', 'interaction', 'capture'].every(kind => kinds.has(kind))) fail('full verification needs source, checks, interaction and capture evidence');
      const bound = row.evidence.filter(e => e.kind !== 'source');
      if (new Set(bound.map(e => e.sourceCommit)).size !== 1 || new Set(bound.map(e => e.artifactSha256)).size !== 1) fail('full verification evidence must describe the same candidate');
      if (row.remaining.en !== 'None.' || row.remaining.zh !== '無。') fail('fully verified rows cannot retain open actions');
    } else if (row.remaining.en === 'None.' || row.remaining.zh === '無。') fail('incomplete rows require remaining actions');
  }
  for (const id of expected.keys()) if (!seen.has(id)) fail('missing ID: ' + id);
  return inventory;
}

export function loadInventory(directory) {
  return validateInventory(JSON.parse(fs.readFileSync(path.join(directory, 'feature-inventory.json'), 'utf8')), JSON.parse(fs.readFileSync(path.join(directory, 'required-feature-ids.json'), 'utf8')));
}

export function inventorySummary(inventory) {
  const counts = Object.fromEntries(Object.keys(statusLabels).map(status => [status, inventory.entries.filter(row => row.status === status).length]));
  return {total: inventory.entries.length, required: inventory.entries.filter(row => row.kind === 'required').length, workflows: workflows.length, milestones: milestones.length, counts};
}

export function renderChecklist(inventory) {
  const summary = inventorySummary(inventory);
  const binding = evidence => evidence.kind !== 'source' && evidence.scope
    ? localized(evidence.scope, 'p') + `<code>${escape(evidence.sourceCommit)}</code><br><code>${escape(evidence.artifactSha256)}</code><p>${escape(evidence.recordedAt)}</p>`
    : '';
  const options = (values) => values.map(([id, label]) => localized(label, 'option', `value="${escape(id)}"`)).join('');
  const allCategories = {en:'All categories', zh:'全部分類'}, allStatuses = {en:'All statuses', zh:'全部狀態'};
  const rows = inventory.entries.map(row => `<details class="feature-row" data-feature-id="${row.id}" data-category="${row.category}" data-status="${row.status}"><summary>${localized(row.name, 'span', 'class="feature-name"')}${localized(statusLabels[row.status], 'span', `class="feature-status status-${row.status}"`)}<code>${row.id}</code></summary><div class="feature-detail">${localized(row.summary, 'p')}${localized({en:'Remaining actions',zh:'待辦事項'}, 'h3')}${localized(row.remaining, 'p')}${localized({en:'Sources and bounded evidence',zh:'來源與有限範圍證據'}, 'h3')}<ul>${row.evidence.map(e => `<li><a href="${escape(e.url)}">${localized(e.label)}</a> ${localized({en:e.kind === 'source' ? 'Source reference only, not full verification.' : e.kind + ' evidence', zh:e.kind === 'source' ? '只係來源參考，唔代表完整驗證。' : '驗證證據'})}${binding(e)}</li>`).join('')}</ul></div></details>`).join('\n');
  return `<section id="feature-completion" class="panel" aria-labelledby="feature-heading">${localized({en:'Feature completion',zh:'功能完成狀態'},'h2','id="feature-heading"')}${localized({en:'Maintainer-owned status, backed by the checked-in inventory. Implemented does not mean fully verified. This is separate from your preparation checklist below.',zh:'狀態由維護者管理，以來源清單為準。已實作唔等於完整驗證，同下面你自己嘅準備清單分開。'},'p')}<p class="feature-overview">${localized({en:`${summary.total} entries: ${summary.required} required capabilities, ${summary.workflows} product workflows and ${summary.milestones} milestones. ${summary.counts.verified} fully verified.`,zh:`共 ${summary.total} 項：${summary.required} 項必需功能、${summary.workflows} 項產品流程同 ${summary.milestones} 項里程碑。${summary.counts.verified} 項完整驗證。`})}</p><div class="feature-filters"><label>${localized({en:'Category',zh:'分類'})}<select id="feature-category">${options([['all',allCategories],...inventory.categories.map(c=>[c.id,c.name])])}</select></label><label>${localized({en:'Status',zh:'狀態'})}<select id="feature-status">${options([['all',allStatuses],...Object.entries(statusLabels)])}</select></label><button id="feature-clear" type="button" data-en="Reset feature filters" data-zh="重設功能篩選">Reset feature filters</button></div><p id="feature-results" role="status" aria-live="polite"></p><p id="feature-empty" hidden data-en="No entries match these filters. Reset the filters to see all entries." data-zh="冇項目符合篩選，重設後可查看全部項目。">No entries match these filters. Reset the filters to see all entries.</p><div id="feature-rows">${rows}</div><p><a href="#doc-docs-features-feature-checklist-md" data-en="How this checklist is maintained" data-zh="清單維護方式">How this checklist is maintained</a> · <a href="feature-inventory.json" data-en="Source inventory (JSON)" data-zh="來源清單（JSON）">Source inventory (JSON)</a></p></section>`;
}

export function renderInventoryMarkdown(inventory) {
  const summary = inventorySummary(inventory);
  return `<!-- Generated from website/feature-inventory.json by website/render.mjs. -->\n\n${summary.total} entries retain ${summary.required} required identifiers, ${summary.workflows} product workflows and ${summary.milestones} milestones. Fully verified: ${summary.counts.verified}. Implemented with verification pending: ${summary.counts.implemented}. Planned: ${summary.counts.planned}. Unverified: ${summary.counts.unverified}.\n\n` + inventory.categories.map(category => `### ${category.name.en}\n\n| Identifier | Capability | Status | Remaining actions |\n| --- | --- | --- | --- |\n` + inventory.entries.filter(row => row.category === category.id).map(row => `| \`${row.id}\` | ${row.name.en} | ${statusLabels[row.status].en} | ${row.remaining.en} |`).join('\n')).join('\n\n');
}
