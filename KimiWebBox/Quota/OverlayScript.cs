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
#kqb-dot{width:7px;height:7px;border-radius:50%;background:#8e8e93;flex:none}
#kqb-panel{position:fixed;right:14px;bottom:54px;z-index:2147483647;width:264px;box-sizing:border-box;border-radius:14px;padding:12px 14px 10px;font:12px/1.55 system-ui,-apple-system,"Segoe UI",sans-serif;background:rgba(255,255,255,.9);color:#1c1c1e;border:1px solid rgba(60,60,67,.12);backdrop-filter:blur(16px) saturate(1.4);-webkit-backdrop-filter:blur(16px) saturate(1.4);box-shadow:0 12px 40px rgba(0,0,0,.18);user-select:none;transform-origin:100% 100%;transition:opacity .18s,transform .18s}
#kqb-panel.kqb-hidden,#kqb-chip.kqb-hidden{opacity:0;pointer-events:none;transform:scale(.9)}
#kqb-head{display:flex;justify-content:space-between;align-items:center;font-weight:600;font-size:12.5px;margin-bottom:6px}
#kqb-actions span{cursor:pointer;opacity:.55;padding:0 5px;font-size:14px;line-height:1}#kqb-actions span:hover{opacity:1}
.kqb-sec{padding:6px 0;border-top:1px solid rgba(60,60,67,.10)}
.kqb-sec:first-of-type{border-top:0;padding-top:2px}
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

  function fmt(v) {
    v = Math.max(0, Math.round(v || 0));
    if (v >= 1e6) return (v / 1e6).toFixed(1).replace(/\.0$/, '') + 'M';
    if (v >= 1e3) return (v / 1e3).toFixed(1).replace(/\.0$/, '') + 'k';
    return String(v);
  }
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

    let chip = '今日 ' + fmt(d.today);
    if (sessionPct !== null) chip += ' · 5h ' + Math.round(sessionPct) + '%';
    chipText.textContent = chip;
    if (dotEl) dotEl.style.background = sessionPct === null ? '#8e8e93' : barColor(sessionPct);
  }

  window.KimiQuota = { update: render };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount);
  else mount();
})();
""";
}
