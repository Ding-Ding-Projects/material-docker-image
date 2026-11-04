/* Site-wide documentation search with an adjacent regular-expression builder. Everything runs locally in the visitor's browser. */
(() => {
  'use strict';
  const MAX_PATTERN = 200;
  const MAX_SECTION_CHARS = 20000;
  const MAX_RESULTS = 200;
  const BUDGET_MS = 250;
  const ALLOWED_FLAGS = 'imsu';
  const clock = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());
  const escapeLiteral = s => s.replace(/[.*+?^${}()|[\]\\\/]/g, '\\$&');
  // A quantified group that itself contains a quantifier is the classic catastrophic-backtracking shape; it is refused up front
  // because a synchronous regular expression cannot be interrupted once it is running.
  const NESTED_QUANTIFIER = /\((?:[^()\\]|\\.)*[+*}](?:[^()\\]|\\.)*\)\s*[+*{]/;

  function buildMatcher(query, options) {
    const opts = options || {};
    if (!query) return {ok: true, empty: true};
    if (query.length > MAX_PATTERN) return {ok: false, error: 'too-long'};
    const flags = [...new Set(String(opts.flags || ''))].filter(f => ALLOWED_FLAGS.includes(f)).join('');
    if (opts.regex && NESTED_QUANTIFIER.test(query)) return {ok: false, error: 'nested-quantifier'};
    try {
      const expression = new RegExp(opts.regex ? query : escapeLiteral(query), flags);
      return {ok: true, expression, flags};
    } catch (error) {
      return {ok: false, error: 'invalid', detail: String(error && error.message || error).slice(0, 160)};
    }
  }

  function snippetAround(text, index, length) {
    const start = Math.max(0, index - 60), end = Math.min(text.length, index + Math.max(length, 1) + 80);
    return {before: (start > 0 ? '…' : '') + text.slice(start, index), match: text.slice(index, index + length), after: text.slice(index + length, end) + (end < text.length ? '…' : '')};
  }

  // Bounded: per-section text cap, result cap and a wall-clock budget checked between sections.
  function runSearch(sections, matcher, limits) {
    const budget = (limits && limits.budgetMs) || BUDGET_MS, cap = (limits && limits.maxResults) || MAX_RESULTS;
    const started = clock();
    const results = [];
    let truncatedSections = 0, timedOut = false, capped = false, scanned = 0;
    if (!matcher.ok || matcher.empty) return {results, scanned, truncatedSections, timedOut, capped};
    for (const section of sections) {
      if (clock() - started > budget) { timedOut = true; break; }
      scanned++;
      let text = section.text;
      if (text.length > MAX_SECTION_CHARS) { text = text.slice(0, MAX_SECTION_CHARS); truncatedSections++; }
      matcher.expression.lastIndex = 0;
      const found = matcher.expression.exec(text);
      if (found) {
        results.push({id: section.id, article: section.article, heading: section.heading, snippet: snippetAround(text, found.index, found[0].length)});
        if (results.length >= cap) { capped = true; break; }
      }
    }
    return {results, scanned, truncatedSections, timedOut, capped};
  }

  const MESSAGES = {
    'too-long': ['The pattern is longer than ' + MAX_PATTERN + ' characters.', '模式超過 ' + MAX_PATTERN + ' 個字元。'],
    'nested-quantifier': ['This pattern repeats a group that already repeats, which can freeze a browser. Rewrite it without nesting.', '呢個模式重複一個本身已重複嘅群組，可能令瀏覽器停頓，請改寫避免巢狀重複。'],
    invalid: ['The pattern is not valid', '模式無效']
  };

  const LIBRARY = [
    ['Semantic version', '語意版本', '\\b\\d+\\.\\d+\\.\\d+\\b'],
    ['SHA-256 digest', 'SHA-256 摘要', '\\b[0-9a-f]{64}\\b'],
    ['Port number', '連接埠', '\\b\\d{2,5}:\\d{2,5}\\b'],
    ['Image reference', '映像參照', '[a-z0-9][a-z0-9._/-]*:[A-Za-z0-9_.-]+'],
    ['Heading line', '標題行', '^#{1,3} .+$']
  ];
  const TOKENS = [['.', '.'], ['\\d', '\\d'], ['\\w', '\\w'], ['\\s', '\\s'], ['^', '^'], ['$', '$'], ['\\b', '\\b'], ['[ ]', '[]'], ['( )', '()'], ['(?: )', '(?:)'], ['|', '|'], ['*', '*'], ['+', '+'], ['?', '?'], ['{n,m}', '{1,3}'], ['(?= )', '(?=)'], ['(?<= )', '(?<=)']];

  function collectSections(root) {
    const sections = [];
    for (const article of root.querySelectorAll('.article')) {
      const title = (article.querySelector('h3') || {}).textContent || article.id;
      let current = {id: article.id, article: title, heading: title, text: ''};
      for (const child of article.children) {
        if (/^H[3-8]$/.test(child.tagName) && child.id) {
          sections.push(current);
          current = {id: child.id, article: title, heading: child.textContent, text: child.textContent + '\n'};
        } else current.text += child.textContent + '\n';
      }
      sections.push(current);
    }
    return sections;
  }

  function wire() {
    const $ = id => document.getElementById(id);
    const input = $('search'), pattern = $('regex-pattern'), mode = $('regex-mode');
    if (!input || !pattern || !mode) return;
    let sections = null, synchronizing = false;
    const language = () => ($('language') ? $('language').value : 'en');
    const tr = pair => language() === 'zh' ? pair[1] : language() === 'both' ? pair[0] + ' / ' + pair[1] : pair[0];
    const flagsFromControls = () => ['i', 'm', 's', 'u'].filter(f => $('flag-' + f).checked).join('');

    function render() {
      if (!sections) sections = collectSections(document);
      const status = $('search-status'), list = $('search-results'), error = $('regex-error');
      list.textContent = '';
      error.textContent = '';
      input.removeAttribute('aria-invalid'); pattern.removeAttribute('aria-invalid');
      const query = input.value;
      const matcher = buildMatcher(query, {regex: mode.checked, flags: flagsFromControls()});
      if (matcher.empty) { status.textContent = tr(['Type to search all ' + $('articles').querySelectorAll('.article').length + ' embedded documents. Plain text by default.', '輸入文字搜尋全部 ' + $('articles').querySelectorAll('.article').length + ' 份內嵌文件。預設為純文字。']); return; }
      if (!matcher.ok) {
        const message = tr(MESSAGES[matcher.error]) + (matcher.detail ? ': ' + matcher.detail : '');
        error.textContent = message; status.textContent = message;
        input.setAttribute('aria-invalid', 'true'); pattern.setAttribute('aria-invalid', 'true');
        return;
      }
      const outcome = runSearch(sections, matcher);
      for (const hit of outcome.results) {
        const item = document.createElement('li');
        const link = document.createElement('a');
        link.href = '#' + hit.id;
        link.textContent = hit.article + (hit.heading !== hit.article ? ' › ' + hit.heading : '');
        const snippet = document.createElement('span');
        snippet.className = 'muted';
        const mark = document.createElement('mark');
        mark.textContent = hit.snippet.match;
        snippet.append(' ', hit.snippet.before, mark, hit.snippet.after);
        item.append(link, document.createElement('br'), snippet);
        list.append(item);
      }
      let line = tr([outcome.results.length + ' matching sections.', '找到 ' + outcome.results.length + ' 個相符段落。']);
      if (outcome.capped) line += ' ' + tr(['Showing the first ' + MAX_RESULTS + '.', '只顯示首 ' + MAX_RESULTS + ' 個。']);
      if (outcome.timedOut) line += ' ' + tr(['Stopped early after ' + BUDGET_MS + ' ms; narrow the search.', '超過 ' + BUDGET_MS + ' 毫秒已提前停止，請收窄搜尋。']);
      if (outcome.truncatedSections) line += ' ' + tr([outcome.truncatedSections + ' long sections were searched only in their first ' + MAX_SECTION_CHARS + ' characters.', outcome.truncatedSections + ' 個長段落只搜尋首 ' + MAX_SECTION_CHARS + ' 個字元。']);
      status.textContent = line + ' ' + tr(['Search stays in this browser.', '搜尋只喺瀏覽器內進行。']);
    }

    function sync(from) {
      if (synchronizing) return;
      synchronizing = true;
      if (from === 'search') pattern.value = input.value; else input.value = pattern.value;
      synchronizing = false;
      render();
    }
    input.addEventListener('input', () => sync('search'));
    pattern.addEventListener('input', () => sync('pattern'));
    mode.addEventListener('change', () => { $('regex-panel').classList.toggle('regex-active', mode.checked); render(); });
    for (const f of ['i', 'm', 's', 'u']) $('flag-' + f).addEventListener('change', render);

    const toggle = $('regex-toggle');
    function setPanel(open) { $('regex-panel').hidden = !open; toggle.setAttribute('aria-expanded', String(open)); if (open) pattern.focus(); }
    toggle.addEventListener('click', () => setPanel($('regex-panel').hidden));
    document.addEventListener('keydown', event => {
      if (event.altKey && (event.key === 'r' || event.key === 'R')) { event.preventDefault(); setPanel($('regex-panel').hidden); }
    });
    $('clear-search').addEventListener('click', () => { input.value = ''; pattern.value = ''; render(); input.focus(); });

    const tokens = $('regex-tokens');
    for (const [label, insert] of TOKENS) {
      const button = document.createElement('button');
      button.type = 'button'; button.textContent = label; button.title = insert;
      button.addEventListener('click', () => {
        const start = pattern.selectionStart == null ? pattern.value.length : pattern.selectionStart, end = pattern.selectionEnd == null ? start : pattern.selectionEnd;
        pattern.value = pattern.value.slice(0, start) + insert + pattern.value.slice(end);
        const caret = start + (insert.length > 1 && /[\])]$/.test(insert) ? insert.length - 1 : insert.length);
        pattern.focus(); pattern.setSelectionRange(caret, caret);
        mode.checked = true; $('regex-panel').classList.add('regex-active');
        sync('pattern');
      });
      tokens.append(button);
    }
    const library = $('regex-library');
    for (const [en, zh, value] of LIBRARY) { const option = document.createElement('option'); option.value = value; option.textContent = en + ' / ' + zh; library.append(option); }
    library.addEventListener('change', () => { if (!library.value) return; pattern.value = library.value; mode.checked = true; library.value = ''; sync('pattern'); });

    if ($('language')) $('language').addEventListener('change', render);
    globalThis.MDISearch = Object.assign(globalThis.MDISearch || {}, {refresh: render});
    render();
  }

  globalThis.MDISearch = {buildMatcher, runSearch, snippetAround, collectSections, escapeLiteral, LIMITS: {MAX_PATTERN, MAX_SECTION_CHARS, MAX_RESULTS, BUDGET_MS}};
  if (typeof document !== 'undefined' && document.getElementById) wire();
})();
