// The page the phone opens (http://<PC>:8765/): newest line on top; the translation large and what was said small,
// or (not translating) what was said, large.
namespace TranslatorPc.Core;

public static class OthersPage
{
    public const string Html = """
<!doctype html>
<html lang="ja">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>相手の声</title>
<style>
  :root { color-scheme: dark; }
  body { margin: 0; background: #10141c; color: #eef1f6; font-family: system-ui, sans-serif; }
  header { position: sticky; top: 0; background: #10141c; padding: 12px 16px; border-bottom: 1px solid #2a3140;
           display: flex; justify-content: space-between; align-items: center; font-size: 15px; }
  #state { font-size: 13px; color: #8a93a6; }
  #state.ok { color: #6fd08c; }
  main { padding: 8px 16px 40px; }
  .line { padding: 12px 0; border-bottom: 1px solid #222a37; }
  .ja { font-size: 22px; line-height: 1.45; }
  .orig { margin-top: 4px; font-size: 14px; color: #9aa3b5; }
  .meta { font-size: 12px; color: #6c7588; margin-right: 6px; }
  .empty { color: #6c7588; padding: 24px 0; }
</style>
</head>
<body>
<header><span>相手の声</span><span id="state">つないでいます…</span></header>
<main id="list"><div class="empty">ほかの人が話すと、ここに出ます。</div></main>
<script>
  let after = 0;
  const list = document.getElementById('list');
  const state = document.getElementById('state');
  function add(l) {
    const empty = list.querySelector('.empty');
    if (empty) empty.remove();
    const div = document.createElement('div');
    div.className = 'line';
    const ja = document.createElement('div');
    ja.className = 'ja';
    ja.textContent = l.translation || l.original;
    const orig = document.createElement('div');
    orig.className = 'orig';
    const meta = document.createElement('span');
    meta.className = 'meta';
    meta.textContent = l.time + ' ' + l.lang;
    orig.append(meta, document.createTextNode(l.translation ? l.original : ''));
    div.append(ja, orig);
    list.prepend(div);
    while (list.children.length > 50) list.lastChild.remove();
  }
  async function poll() {
    try {
      const r = await fetch('/lines?after=' + after, { cache: 'no-store' });
      const data = await r.json();
      for (const l of data.lines) { add(l); after = Math.max(after, l.id); }
      state.textContent = 'つながっています';
      state.className = 'ok';
    } catch (e) {
      state.textContent = 'PC につながりません';
      state.className = '';
    }
    setTimeout(poll, 1000);
  }
  poll();
</script>
</body>
</html>
""";
}
