// Generates docs/assets/banner.svg. Text becomes outlines, so the banner looks
// the same everywhere: GitHub serves README images without web fonts.
//
//   npm i fontkit            (outside the project, on NODE_PATH)
//   node docs/assets/build-banner.cjs [path/to/CascadiaMono.ttf]
const fs = require('fs');
const path = require('path');
const fontkit = require('fontkit');

const FONT = process.argv[2] || 'C:/Windows/Fonts/CascadiaMono.ttf';
const base = fontkit.openSync(FONT);
const regular = base.getVariation({ wght: 400 });
const bold = base.getVariation({ wght: 700 });

const C = {
  bg: '#0d1117',
  border: '#263040',
  grid: '#1a222d',
  text: '#e6edf3',
  muted: '#7d8a99',
  accent: '#f0a3a8',
};

const defs = new Map();
function glyph(font, key, ch) {
  const id = `${key}${ch.codePointAt(0).toString(36)}`;
  if (!defs.has(id)) {
    const g = font.layout(ch).glyphs[0];
    defs.set(id, `<path id="${id}" d="${g.path.toSVG()}"/>`);
  }
  return id;
}

function line(segments, x, y, size, { font = regular, key = 'r' } = {}) {
  const scale = size / base.unitsPerEm;
  const advance = regular.layout('0').glyphs[0].advanceWidth;
  let col = 0;
  let out = '';
  for (const [text, fill] of segments) {
    let uses = '';
    for (const ch of text) {
      if (ch !== ' ') uses += `<use href="#${glyph(font, key, ch)}" x="${col * advance}"/>`;
      col++;
    }
    out += `<g fill="${fill}">${uses}</g>`;
  }
  return {
    markup: `<g transform="translate(${x} ${y}) scale(${scale.toFixed(5)} ${(-scale).toFixed(5)})">${out}</g>`,
    end: x + col * advance * scale,
    charWidth: advance * scale,
  };
}

const W = 1200;
const H = 420;

// Prompt line: typed out character by character.
const PROMPT = 'hh=# ';
const QUERY = 'EXPLAIN ANALYZE SELECT * FROM vacancies WHERE fits_resume;';
const q = line([[PROMPT, C.accent], [QUERY, C.text]], 48, 62, 22);
const total = PROMPT.length + QUERY.length;
const widths = [];
for (let i = PROMPT.length; i <= total; i++) widths.push(((i * q.charWidth) + 2).toFixed(1));
const TYPE_END = 1.7;

// The pipeline, as a query plan: each row fades in after the query has "run".
const PLAN_X = 650;
const rows = [
  [[['QUERY PLAN', C.muted]], 136],
  [[['Collect', C.accent], ['  public hh.ru pages', C.text]], 186],
  [[['├─ ', C.muted], ['Filter', C.accent], ['    search rules, LLM score', C.text]], 223],
  [[['├─ ', C.muted], ['Generate', C.accent], ['  cover letter, answers', C.text]], 260],
  [[['├─ ', C.muted], ['Review', C.accent], ['    web page, Telegram bot', C.text]], 297],
  [[['└─ ', C.muted], ['Apply', C.accent], ['     queue, in your browser', C.text]], 334],
  [[['Daily limit: ', C.muted], ['on', C.text], ['   Captcha: ', C.muted], ['pause', C.accent]], 378],
];
let plan = '';
let lastEnd = 0;
rows.forEach(([segments, y], i) => {
  const l = line(segments, PLAN_X, y, 20);
  lastEnd = l.end;
  plan += `<g class="row" style="animation-delay:${(TYPE_END + 0.2 + i * 0.22).toFixed(2)}s">${l.markup}</g>`;
});
const cursorDelay = (TYPE_END + 0.2 + rows.length * 0.22).toFixed(2);

const title = line([['hh-autoapplier', C.text]], 48, 232, 68, { font: bold, key: 'b' });
const sub = line([['Semi-automated job search', C.muted]], 48, 288, 28);
const stack = line([['TypeScript · ASP.NET Core · PostgreSQL', C.muted]], 48, 330, 20);

const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}" role="img" aria-labelledby="t">
<title id="t">hh-autoapplier: semi-automated job search</title>
<style>
.row{animation:in .45s ease-out both}
.cur{animation:cur 1.1s steps(1) ${cursorDelay}s infinite both}
@keyframes in{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:none}}
@keyframes cur{0%{opacity:0}50%{opacity:1}100%{opacity:0}}
@media (prefers-reduced-motion:reduce){.row,.cur{animation:none}}
</style>
<defs>
<pattern id="dots" width="24" height="24" patternUnits="userSpaceOnUse"><circle cx="2" cy="2" r="1.2" fill="${C.grid}"/></pattern>
<clipPath id="type"><rect x="48" y="30" height="46" width="${q.end - 48 + 4}"><animate attributeName="width" values="${widths.join(';')}" dur="${TYPE_END}s" calcMode="discrete" fill="freeze"/></rect></clipPath>
__DEFS__
</defs>
<rect x=".5" y=".5" width="${W - 1}" height="${H - 1}" rx="14" fill="${C.bg}" stroke="${C.border}"/>
<rect x="1" y="1" width="${W - 2}" height="${H - 2}" rx="14" fill="url(#dots)"/>
<g clip-path="url(#type)">${q.markup}</g>
<path d="M48 92H${W - 48}" stroke="${C.border}"/>
${title.markup}
${sub.markup}
${stack.markup}
${plan}
<rect class="cur" x="${(lastEnd + 8).toFixed(1)}" y="362" width="11" height="20" fill="${C.accent}"/>
</svg>
`.replace('__DEFS__', [...defs.values()].join(''));

const out = path.join(__dirname, 'banner.svg');
fs.writeFileSync(out, svg);
console.log(`${out}: ${(svg.length / 1024).toFixed(1)} KB, ${defs.size} glyphs`);
