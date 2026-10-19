/* Read-only maintainer status. Visitor preparation state belongs to the guide. */
(() => {
  const category = document.getElementById('feature-category');
  const status = document.getElementById('feature-status');
  const rows = [...document.querySelectorAll('[data-feature-id]')];
  const translate = (en, zh) => {
    const language = document.getElementById('language').value;
    return language === 'zh' ? zh : language === 'both' ? en + ' / ' + zh : en;
  };
  function filterFeatures() {
    let visible = 0;
    for (const row of rows) {
      row.hidden = !((category.value === 'all' || row.dataset.category === category.value) && (status.value === 'all' || row.dataset.status === status.value));
      if (!row.hidden) visible++;
    }
    document.getElementById('feature-results').textContent = translate(`${visible} of ${rows.length} entries shown.`, `顯示 ${rows.length} 項中嘅 ${visible} 項。`);
    document.getElementById('feature-empty').hidden = visible !== 0;
  }
  category.addEventListener('change', filterFeatures);
  status.addEventListener('change', filterFeatures);
  document.getElementById('language').addEventListener('change', filterFeatures);
  document.getElementById('feature-clear').addEventListener('click', () => {
    category.value = 'all'; status.value = 'all'; filterFeatures(); category.focus();
  });
  filterFeatures();
})();
