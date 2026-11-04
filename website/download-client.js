/* Latest-release download panel. Resolves the release at view time; nothing about a version is baked into the page. */
(() => {
  'use strict';
  const REPOSITORY = 'Ding-Ding-Projects/material-docker-image';
  const API = 'https://api.github.com/repos/' + REPOSITORY + '/releases/latest';
  const RELEASES_PAGE = 'https://github.com/' + REPOSITORY + '/releases';
  const DOWNLOAD_PREFIX = 'https://github.com/' + REPOSITORY + '/releases/download/';
  const PRIMARY = {setup: 'Setup.exe', companion: 'MaterialDockerImageBuild.exe', docs: 'MaterialDockerImageDocs.zip'};
  const TIMEOUT_MS = 10000;

  function formatSize(bytes) {
    if (!Number.isFinite(bytes) || bytes < 0) return 'unknown size';
    const units = ['B', 'KB', 'MB', 'GB'];
    let value = bytes, unit = 0;
    while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
    return (unit === 0 ? String(value) : value.toFixed(value >= 100 ? 0 : 1)) + ' ' + units[unit];
  }

  // Throws on anything that is not a well-formed release of this repository, so a malformed reply never renders as a version.
  function parseRelease(data) {
    if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('malformed');
    const tag = data.tag_name;
    if (typeof tag !== 'string' || !/^[0-9A-Za-z._-]{1,64}$/.test(tag)) throw new Error('malformed');
    const published = Date.parse(data.published_at);
    if (!Number.isFinite(published)) throw new Error('malformed');
    if (!Array.isArray(data.assets)) throw new Error('malformed');
    const assets = [];
    for (const asset of data.assets.slice(0, 100)) {
      if (!asset || typeof asset.name !== 'string' || !/^[A-Za-z0-9._-]{1,200}$/.test(asset.name)) continue;
      if (typeof asset.browser_download_url !== 'string' || !asset.browser_download_url.startsWith(DOWNLOAD_PREFIX)) continue;
      if (!Number.isFinite(asset.size) || asset.size < 0) continue;
      assets.push({name: asset.name, size: asset.size, url: asset.browser_download_url});
    }
    return {tag, version: tag.replace(/^v/, ''), publishedAt: new Date(published).toISOString(), assets, page: RELEASES_PAGE + '/tag/' + encodeURIComponent(tag)};
  }

  // The failure kinds drive honest wording; none of them invents a version.
  function classifyStatus(status, rateLimitRemaining) {
    if (status === 429 || (status === 403 && rateLimitRemaining === '0')) return 'rate-limited';
    if (status === 404) return 'none';
    return 'unavailable';
  }

  async function loadRelease(fetchFn, options) {
    const controller = typeof AbortController === 'function' ? new AbortController() : null;
    const timer = controller && typeof setTimeout === 'function' ? setTimeout(() => controller.abort(), (options && options.timeoutMs) || TIMEOUT_MS) : null;
    try {
      let response;
      try {
        response = await fetchFn(API, {headers: {Accept: 'application/vnd.github+json'}, signal: controller ? controller.signal : undefined, cache: 'no-store', credentials: 'omit'});
      } catch {
        return {kind: 'offline'};
      }
      if (!response.ok) return {kind: classifyStatus(response.status, response.headers && response.headers.get ? response.headers.get('x-ratelimit-remaining') : null)};
      try {
        return {kind: 'ready', release: parseRelease(await response.json())};
      } catch {
        return {kind: 'unavailable'};
      }
    } finally {
      if (timer) clearTimeout(timer);
    }
  }

  const MESSAGES = {
    loading: ['Looking up the latest release…', '正在查詢最新發行版本…'],
    offline: ['The release list could not be reached (offline or blocked). Use the releases page instead.', '連接唔到發行清單（離線或被封鎖），請改用發行頁面。'],
    'rate-limited': ['GitHub is rate limiting release lookups right now. Use the releases page, or try again later.', 'GitHub 暫時限制查詢次數，請使用發行頁面或稍後再試。'],
    none: ['No release is marked as the latest yet. Use the releases page to see what exists.', '暫時未有標記為最新嘅發行版本，請到發行頁面查看。'],
    unavailable: ['The release information could not be read. Use the releases page instead.', '讀取唔到發行資料，請改用發行頁面。'],
    offlinePackage: ['This is the offline documentation package, so the latest release is not looked up. Open the releases page when you are online.', '呢個係離線文件包，唔會查詢最新發行版本。上網後請開啟發行頁面。']
  };

  function wire() {
    const panel = document.getElementById('download-panel');
    if (!panel) return;
    const el = id => document.getElementById(id);
    let outcome = {kind: 'loading'};
    const offline = panel.dataset.offline === 'true';
    const translate = (pair) => {
      const language = el('language') ? el('language').value : 'en';
      return language === 'zh' ? pair[1] : language === 'both' ? pair[0] + ' / ' + pair[1] : pair[0];
    };
    function setLink(id, asset, label) {
      const link = el(id);
      if (asset) { link.href = asset.url; link.removeAttribute('aria-disabled'); link.textContent = label + ' · ' + formatSize(asset.size); link.hidden = false; }
      else { link.removeAttribute('href'); link.setAttribute('aria-disabled', 'true'); link.textContent = label + ' · ' + translate(['not attached to this release', '呢個發行版本未附上']); link.hidden = outcome.kind !== 'ready'; }
    }
    function render() {
      const status = el('download-status');
      const ready = outcome.kind === 'ready';
      el('download-details').hidden = !ready;
      el('download-buttons').hidden = !ready;
      status.textContent = ready ? translate(['Latest release loaded from GitHub just now.', '剛從 GitHub 載入最新發行版本。']) : translate(MESSAGES[outcome.kind] || MESSAGES.unavailable);
      el('download-fallback').hidden = false;
      if (!ready) return;
      const release = outcome.release, byName = new Map(release.assets.map(a => [a.name, a]));
      el('download-version').textContent = release.version;
      el('download-date').textContent = release.publishedAt.replace('T', ' ').replace(/\.\d+Z$/, 'Z');
      el('download-release-link').href = release.page;
      setLink('download-setup', byName.get(PRIMARY.setup), translate(['Download installer (Setup.exe)', '下載安裝程式（Setup.exe）']));
      setLink('download-companion', byName.get(PRIMARY.companion), translate(['Download build companion', '下載建置助手']));
      setLink('download-docs', byName.get(PRIMARY.docs), translate(['Download offline documentation', '下載離線文件']));
      const list = el('download-assets');
      list.textContent = '';
      for (const asset of release.assets) {
        const item = document.createElement('li');
        const link = document.createElement('a');
        link.href = asset.url; link.textContent = asset.name;
        item.append(link, ' · ' + formatSize(asset.size));
        list.append(item);
      }
    }
    if (el('language')) el('language').addEventListener('change', render);
    render();
    if (offline) { outcome = {kind: 'offlinePackage'}; render(); return; }
    if (typeof fetch !== 'function') { outcome = {kind: 'offline'}; render(); return; }
    loadRelease(fetch.bind(globalThis)).then(result => { outcome = result; render(); });
  }

  globalThis.MDIDownload = {formatSize, parseRelease, classifyStatus, loadRelease, MESSAGES, API, RELEASES_PAGE, PRIMARY};
  if (typeof document !== 'undefined' && document.getElementById) wire();
})();
