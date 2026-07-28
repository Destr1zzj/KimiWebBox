namespace KimiWebBox.Quota;

/// <summary>
/// Overlay injected into the hosted kimi web page: a floating chip (bottom-right)
/// that expands into a usage + quota panel. Data flows in via window.KimiQuota.update(json);
/// user intents flow out via chrome.webview.postMessage({type}).
/// </summary>
internal static class OverlayScript
{
    public const string Source = """
(() => {
  if (window.top !== window.self || window.KimiQuota) return;

  const css = `
#kqb-chip{position:fixed;right:14px;bottom:14px;z-index:2147483647;display:flex;align-items:center;gap:6px;padding:6px 11px;border-radius:999px;font:12px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif;background:rgba(255,255,255,.85);color:#1c1c1e;border:1px solid rgba(60,60,67,.12);backdrop-filter:blur(12px) saturate(1.4);-webkit-backdrop-filter:blur(12px) saturate(1.4);box-shadow:0 2px 10px rgba(0,0,0,.10);cursor:pointer;user-select:none;transition:transform .15s,box-shadow .15s,opacity .18s}
#kqb-chip:hover{transform:translateY(-1px);box-shadow:0 4px 16px rgba(0,0,0,.16)}
#kqb-chip b{font-weight:700;font-variant-numeric:tabular-nums}
#kqb-dot{width:7px;height:7px;border-radius:50%;background:#8e8e93;flex:none}
#kqb-panel{position:fixed;right:14px;bottom:54px;z-index:2147483647;width:264px;box-sizing:border-box;border-radius:14px;padding:12px 14px 10px;font:12px/1.55 system-ui,-apple-system,"Segoe UI",sans-serif;background:rgba(255,255,255,.9);color:#1c1c1e;border:1px solid rgba(60,60,67,.12);backdrop-filter:blur(16px) saturate(1.4);-webkit-backdrop-filter:blur(16px) saturate(1.4);box-shadow:0 12px 40px rgba(0,0,0,.18);user-select:none;transform-origin:100% 100%;transition:opacity .18s,transform .18s;max-height:80vh;overflow-y:auto}
#kqb-panel.kqb-hidden,#kqb-chip.kqb-hidden{opacity:0;pointer-events:none;transform:scale(.9)}
#kqb-head{display:flex;justify-content:space-between;align-items:center;font-weight:600;font-size:12.5px;margin-bottom:6px}
#kqb-actions span{cursor:pointer;opacity:.55;padding:0 5px;font-size:14px;line-height:1}#kqb-actions span:hover{opacity:1}
.kqb-sec{padding:6px 0;border-top:1px solid rgba(60,60,67,.10)}
.kqb-sec:first-of-type{border-top:0;padding-top:2px}
.kqb-sec-title{opacity:.55;font-size:11px;margin-bottom:4px}
.kqb-row{display:flex;justify-content:space-between;padding:1px 0}
.kqb-row b{font-variant-numeric:tabular-nums;font-weight:600}
.kqb-dim{opacity:.55}
.kqb-limit{margin:7px 0 2px}
.kqb-lrow{display:flex;justify-content:space-between;margin-bottom:3px}
.kqb-lpct{font-weight:600;font-variant-numeric:tabular-nums}
.kqb-track{height:5px;border-radius:3px;background:rgba(120,120,128,.22);overflow:hidden}
.kqb-fill{height:100%;border-radius:3px;transition:width .5s ease}
.kqb-ldetail,.kqb-lreset{opacity:.55;font-size:11px;margin-top:2px}
.kqb-hint{opacity:.8;padding:4px 0}
.kqb-hint u{cursor:pointer}
.kqb-heatwrap{display:flex;gap:4px}
.kqb-wdays{display:grid;grid-template-rows:repeat(7,9px);gap:3px;font-size:8px;line-height:9px;opacity:.45;text-align:right;width:10px}
.kqb-months{display:grid;grid-auto-flow:column;grid-auto-columns:12px;gap:3px;font-size:8px;line-height:1;opacity:.55;height:9px;margin-bottom:2px;margin-left:14px}
.kqb-heat{display:grid;grid-template-rows:repeat(7,9px);grid-auto-flow:column;grid-auto-columns:9px;gap:3px;justify-content:start}
.kqb-hcell{width:9px;height:9px;border-radius:2px}
.kqb-legend{display:flex;align-items:center;gap:2px;font-size:9px;opacity:.55;justify-content:flex-end;margin-top:3px}
.kqb-legend .kqb-hcell{width:8px;height:8px}
.kqb-mrow{margin-bottom:5px}
.kqb-mhead{display:flex;justify-content:space-between;margin-bottom:2px}
.kqb-mhead span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:150px}
.kqb-mhead b{font-variant-numeric:tabular-nums;font-weight:600}
.kqb-ax{fill:currentColor;opacity:.45;font-size:9px}
.kqb-grid{stroke:currentColor;stroke-opacity:.08;stroke-width:1}
.kqb-peak{fill:#3d7ef0;font-size:9px;font-weight:600}
.kqb-todayv{fill:currentColor;opacity:.75;font-size:9px;font-weight:600}
.kqb-cross{stroke:currentColor;stroke-opacity:.3;stroke-width:1}
.kqb-tip{position:absolute;background:rgba(30,30,34,.95);color:#fff;font-size:10px;padding:2px 6px;border-radius:5px;pointer-events:none;white-space:nowrap;z-index:1}
#kqb-foot{display:flex;justify-content:space-between;align-items:center;opacity:.55;font-size:11px;padding-top:6px;margin-top:4px;border-top:1px solid rgba(60,60,67,.10)}
#kqb-refresh{cursor:pointer;font-size:13px;opacity:.7}#kqb-refresh:hover{opacity:1}
@media (prefers-color-scheme:dark){
#kqb-chip{background:rgba(30,30,34,.85);color:#e5e5ea;border-color:rgba(255,255,255,.14);box-shadow:0 2px 10px rgba(0,0,0,.4)}
#kqb-chip:hover{box-shadow:0 4px 16px rgba(0,0,0,.5)}
#kqb-panel{background:rgba(30,30,34,.9);color:#e5e5ea;border-color:rgba(255,255,255,.14);box-shadow:0 12px 40px rgba(0,0,0,.5)}
.kqb-sec,#kqb-foot{border-color:rgba(255,255,255,.10)}
.kqb-track{background:rgba(120,120,128,.3)}
}`;

  let expanded = false;
  try { expanded = localStorage.getItem('kqb-expanded') === '1'; } catch (e) {}
  let data = null, mounted = false;
  let chipEl, chipText, dotEl, panelEl;

  function fmt(v) { return Math.max(0, Math.round(v || 0)).toLocaleString('en-US'); }
  function barColor(p) { return p >= 50 ? '#22c55e' : p >= 20 ? '#f59e0b' : '#ef4444'; }
  function fmtReset(iso) {
    const d = new Date(iso);
    if (!iso || isNaN(d)) return '';
    const sameDay = d.toDateString() === new Date().toDateString();
    const hm = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
    return (sameDay ? '' : (d.getMonth() + 1) + '/' + d.getDate() + ' ') + hm + ' 重置';
  }
  function h(html) { const t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstElementChild; }
  function post(type) { window.chrome.webview.postMessage({ type }); }
  function dayKey(d) { return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0'); }
  function md(key) { return key.slice(5).replace('-', '/'); }

  function mount() {
    if (mounted || !document.body || !document.head) return;
    mounted = true;
    const style = document.createElement('style');
    style.id = 'kqb-style';
    style.textContent = css;
    document.head.appendChild(style);

    chipEl = h('<div id="kqb-chip"><span id="kqb-dot"></span><span id="kqb-chip-text">Kimi</span></div>');
    chipEl.addEventListener('click', () => setExpanded(true));
    chipText = chipEl.querySelector('#kqb-chip-text');
    dotEl = chipEl.querySelector('#kqb-dot');

    panelEl = h(`<div id="kqb-panel">
      <div id="kqb-head"><span>Kimi 用量</span><span id="kqb-actions"><span id="kqb-gear" title="额度设置">&#9881;</span><span id="kqb-close" title="收起">&times;</span></span></div>
      <div class="kqb-sec">
        <div class="kqb-row"><span>今日</span><b id="kqb-today">&ndash;</b></div>
        <div class="kqb-row"><span>近 7 天</span><b id="kqb-week">&ndash;</b></div>
        <div class="kqb-row"><span>本月</span><b id="kqb-month">&ndash;</b></div>
        <div class="kqb-row kqb-dim"><span>累计</span><b id="kqb-all">&ndash;</b></div>
      </div>
      <div class="kqb-sec" id="kqb-limits"></div>
      <div class="kqb-sec"><div class="kqb-sec-title">模型 · 本月</div><div id="kqb-models"></div></div>
      <div class="kqb-sec"><div class="kqb-sec-title">活动 · 近 17 周</div><div id="kqb-heat"></div></div>
      <div class="kqb-sec"><div class="kqb-sec-title">趋势 · 近 30 天</div><div id="kqb-trend"></div></div>
      <div id="kqb-foot"><span id="kqb-updated"></span><span id="kqb-refresh" title="刷新">&#10227;</span></div>
    </div>`);
    panelEl.querySelector('#kqb-close').addEventListener('click', () => setExpanded(false));
    panelEl.querySelector('#kqb-gear').addEventListener('click', () => post('openSettings'));
    panelEl.querySelector('#kqb-refresh').addEventListener('click', () => post('refresh'));

    document.body.appendChild(chipEl);
    document.body.appendChild(panelEl);
    applyState();
    if (data) render(data);
  }

  function setExpanded(v) {
    expanded = v;
    try { localStorage.setItem('kqb-expanded', v ? '1' : '0'); } catch (e) {}
    applyState();
  }
  function applyState() {
    if (!mounted) return;
    chipEl.classList.toggle('kqb-hidden', expanded);
    panelEl.classList.toggle('kqb-hidden', !expanded);
  }

  function renderModels(box, models) {
    box.innerHTML = '';
    if (!models || !models.length) { box.appendChild(h('<div class="kqb-dim">本月暂无模型用量</div>')); return; }
    const top = models.slice(0, 5);
    const max = top[0].tokens || 1;
    for (const m of top) {
      const name = m.id.replace(/^kimi-code\//, '');
      const share = Math.max(2, Math.round(m.tokens / max * 100));
      const row = h(`<div class="kqb-mrow"><div class="kqb-mhead"><span></span><b></b></div><div class="kqb-track"><div class="kqb-fill" style="background:#8aa8f8"></div></div></div>`);
      const label = row.querySelector('.kqb-mhead span');
      label.textContent = name;
      label.title = m.id + ' · ' + fmt(m.tokens) + ' tokens';
      row.querySelector('.kqb-mhead b').textContent = fmt(m.tokens);
      row.querySelector('.kqb-fill').style.width = share + '%';
      box.appendChild(row);
    }
  }

  const HEAT_COLORS = ['rgba(120,120,128,.18)', '#9ec4ff', '#6ea3f8', '#3d7ef0', '#1f5fd6'];

  function renderHeatmap(box, daily) {
    box.innerHTML = '';
    const map = new Map((daily || []).map(x => [x.date, x.tokens]));
    const weeks = 17;
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const end = new Date(today); end.setDate(end.getDate() + (6 - end.getDay()));
    const start = new Date(end); start.setDate(start.getDate() - (weeks * 7 - 1));
    let max = 1;
    for (const v of map.values()) if (v > max) max = v;

    // columns: weeks x 7 day cells
    const cols = [];
    for (let c = 0; c < weeks; c++) {
      const col = [];
      for (let r = 0; r < 7; r++) {
        const d = new Date(start); d.setDate(d.getDate() + c * 7 + r);
        col.push(d);
      }
      cols.push(col);
    }

    // month labels: mark column when its first day's month differs from previous column
    const months = h('<div class="kqb-months"></div>');
    let prevM = -1;
    for (const col of cols) {
      const m = col[0].getMonth();
      months.appendChild(h(m !== prevM ? '<span>' + (m + 1) + '月</span>' : '<span></span>'));
      prevM = m;
    }
    box.appendChild(months);

    const wrap = h('<div class="kqb-heatwrap"></div>');
    const wdays = h('<div class="kqb-wdays"><span></span><span>一</span><span></span><span>三</span><span></span><span>五</span><span></span></div>');
    wrap.appendChild(wdays);

    const grid = h('<div class="kqb-heat"></div>');
    for (const col of cols) {
      for (const d of col) {
        const key = dayKey(d);
        const v = map.get(key) || 0;
        const lv = v === 0 ? 0 : Math.min(4, 1 + Math.floor((v / max) * 3.999));
        const cell = h('<span class="kqb-hcell"></span>');
        cell.style.background = HEAT_COLORS[lv];
        cell.title = key + ' · ' + fmt(v) + ' tokens';
        grid.appendChild(cell);
      }
    }
    wrap.appendChild(grid);
    box.appendChild(wrap);

    const legend = h('<div class="kqb-legend">少 </div>');
    for (const c of HEAT_COLORS) {
      const cell = h('<span class="kqb-hcell"></span>');
      cell.style.background = c;
      legend.appendChild(cell);
    }
    legend.appendChild(document.createTextNode(' 多'));
    box.appendChild(legend);
  }

  function renderTrend(box, daily, days) {
    box.innerHTML = '';
    const map = new Map((daily || []).map(x => [x.date, x.tokens]));
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const vals = [];
    let max = 0;
    for (let i = days - 1; i >= 0; i--) {
      const d = new Date(today); d.setDate(d.getDate() - i);
      const key = dayKey(d);
      const v = map.get(key) || 0;
      if (v > max) max = v;
      vals.push({ key, v });
    }
    if (max === 0) { box.appendChild(h('<div class="kqb-dim">暂无数据</div>')); return; }

    const w = 236, hgt = 58, top = 13, ih = hgt - top - 13;
    const step = w / (days - 1);
    const px = (i) => i * step;
    const py = (v) => top + ih - (v / max) * ih;
    const coords = vals.map((p, i) => px(i).toFixed(1) + ',' + py(p.v).toFixed(1));

    let pi = 0;
    vals.forEach((p, i) => { if (p.v > vals[pi].v) pi = i; });
    const peak = vals[pi];
    const lastV = vals[vals.length - 1].v;

    const parts = ['<svg width="' + w + '" height="' + hgt + '" viewBox="0 0 ' + w + ' ' + hgt + '" style="display:block">'];
    parts.push('<text x="0" y="9" class="kqb-ax">' + fmt(max) + '</text>');
    parts.push('<line x1="0" y1="' + top + '" x2="' + w + '" y2="' + top + '" class="kqb-grid"/>');
    parts.push('<line x1="0" y1="' + (top + ih) + '" x2="' + w + '" y2="' + (top + ih) + '" class="kqb-grid"/>');
    parts.push('<polygon points="0,' + (top + ih) + ' ' + coords.join(' ') + ' ' + w + ',' + (top + ih) + '" fill="rgba(61,126,240,.15)" stroke="none"/>');
    parts.push('<polyline points="' + coords.join(' ') + '" fill="none" stroke="#3d7ef0" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round"/>');
    parts.push('<circle cx="' + px(pi).toFixed(1) + '" cy="' + py(peak.v).toFixed(1) + '" r="2.5" fill="#3d7ef0"/>');
    const plx = Math.min(Math.max(px(pi) - 42, 0), w - 86);
    parts.push('<text x="' + plx.toFixed(1) + '" y="' + Math.max(9, py(peak.v) - 5).toFixed(1) + '" class="kqb-peak">' + fmt(peak.v) + ' · ' + md(peak.key) + '</text>');
    parts.push('<text x="' + w + '" y="' + Math.max(9, py(lastV) - 4).toFixed(1) + '" class="kqb-todayv" text-anchor="end">' + fmt(lastV) + '</text>');
    parts.push('<text x="0" y="' + (hgt - 2) + '" class="kqb-ax">' + md(vals[0].key) + '</text>');
    parts.push('<text x="' + w + '" y="' + (hgt - 2) + '" class="kqb-ax" text-anchor="end">' + md(vals[vals.length - 1].key) + '</text>');
    parts.push('<line id="kqb-ch" x1="0" y1="' + top + '" x2="0" y2="' + (top + ih) + '" class="kqb-cross" style="display:none"/>');
    parts.push('</svg>');

    const wrap = h('<div style="position:relative">' + parts.join('') + '</div>');
    const tip = h('<div class="kqb-tip" style="display:none"></div>');
    wrap.appendChild(tip);
    wrap.addEventListener('mousemove', (ev) => {
      const r = wrap.getBoundingClientRect();
      const i = Math.max(0, Math.min(days - 1, Math.round((ev.clientX - r.left) / step)));
      const p = vals[i];
      const cross = wrap.querySelector('#kqb-ch');
      cross.style.display = '';
      cross.setAttribute('x1', px(i).toFixed(1));
      cross.setAttribute('x2', px(i).toFixed(1));
      tip.style.display = '';
      tip.style.left = Math.min(Math.max(px(i) - 50, 0), w - 100) + 'px';
      tip.style.top = '0px';
      tip.textContent = md(p.key) + ' · ' + fmt(p.v);
    });
    wrap.addEventListener('mouseleave', () => {
      wrap.querySelector('#kqb-ch').style.display = 'none';
      tip.style.display = 'none';
    });
    box.appendChild(wrap);
  }

  function render(d) {
    data = d;
    if (!mounted) return;
    panelEl.querySelector('#kqb-today').textContent = fmt(d.today);
    panelEl.querySelector('#kqb-week').textContent = fmt(d.week);
    panelEl.querySelector('#kqb-month').textContent = fmt(d.month);
    panelEl.querySelector('#kqb-all').textContent = fmt(d.allTime);
    panelEl.querySelector('#kqb-updated').textContent = d.updatedAt
      ? '更新于 ' + new Date(d.updatedAt).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
      : '';

    const box = panelEl.querySelector('#kqb-limits');
    box.innerHTML = '';
    let sessionPct = null;
    if (d.limitsStatus === 'ok' && d.windows && d.windows.length) {
      for (const w of d.windows) {
        const p = Math.max(0, Math.min(100, w.remainingPercent));
        if (w.kind === 'session') sessionPct = p;
        box.appendChild(h(`<div class="kqb-limit">
          <div class="kqb-lrow"><span></span><span class="kqb-lpct"></span></div>
          <div class="kqb-track"><div class="kqb-fill"></div></div>
        </div>`));
        const row = box.lastElementChild;
        row.querySelector('.kqb-lrow span').textContent = w.label;
        const pct = row.querySelector('.kqb-lpct');
        pct.textContent = Math.round(p) + '%';
        pct.style.color = barColor(p);
        const fill = row.querySelector('.kqb-fill');
        fill.style.width = p + '%';
        fill.style.background = barColor(p);
        if (w.detail) { const x = h('<div class="kqb-ldetail"></div>'); x.textContent = w.detail; row.appendChild(x); }
        const reset = fmtReset(w.resetsAt);
        if (reset) { const x = h('<div class="kqb-lreset"></div>'); x.textContent = reset; row.appendChild(x); }
      }
    } else {
      const hints = {
        notConfigured: '未配置额度凭据', unauthorized: '凭据已失效，请重新设置',
        sourceRateLimited: '接口限流，稍后再试', unavailable: '额度接口暂不可用'
      };
      const row = h('<div class="kqb-hint"><span></span> <u id="kqb-setup">设置</u></div>');
      row.querySelector('span').textContent = hints[d.limitsStatus] || '';
      row.querySelector('#kqb-setup').addEventListener('click', () => post('openSettings'));
      box.appendChild(row);
    }

    renderModels(panelEl.querySelector('#kqb-models'), d.modelsMonth);
    renderHeatmap(panelEl.querySelector('#kqb-heat'), d.daily);
    renderTrend(panelEl.querySelector('#kqb-trend'), d.daily, 30);

    let chip = '今日 <b>' + fmt(d.today) + '</b>';
    if (sessionPct !== null) chip += ' · 5h ' + Math.round(sessionPct) + '%';
    chipText.innerHTML = chip;
    if (dotEl) dotEl.style.background = sessionPct === null ? '#8e8e93' : barColor(sessionPct);
  }

  window.KimiQuota = { update: render };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount);
  else mount();
})();
""";
}
