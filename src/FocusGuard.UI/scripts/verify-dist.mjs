import { readFile, readdir } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve('dist');
const files = await readdir(root);
if (files.sort().join(',') !== 'chunks,fonts,host.css,host.js,index.html') throw new Error('Unexpected production UI assets');
const html = await readFile(resolve(root, 'index.html'), 'utf8');
const script = await readFile(resolve(root, 'host.js'), 'utf8');
const css = await readFile(resolve(root, 'host.css'), 'utf8');
const directives = ["default-src 'self'", "script-src 'self'", "style-src 'self'", "img-src 'self'", "font-src 'self'", "connect-src 'none'", "frame-src 'none'", "object-src 'none'", "base-uri 'none'", "form-action 'none'", "worker-src 'none'", "media-src 'none'"];
const policy = /http-equiv="Content-Security-Policy" content="([^"]+)"/.exec(html)?.[1];
const chunks = await readdir(resolve(root, 'chunks'));
if (chunks.sort().join(',') !== 'CompactFocus.js,DashboardStart.js,PreferencesPanel.js,ProfileEditor.js,ScheduleEditor.js,SessionEndExperience.js,SessionHistoryDetail.js,StrictReviewPanel.js,WorkflowViews.js,count-label.js,focus-time.js,strict.js') throw new Error('Unexpected local UI chunks');
const chunkScripts = await Promise.all(chunks.map(file => readFile(resolve(root, 'chunks', file), 'utf8')));
if (!policy || policy.split(';').map(value => value.trim()).filter(Boolean).sort().join(';') !== directives.sort().join(';')
  || !html.includes('/host.js') || !html.includes('/host.css')
  || /localhost|127\.0\.0\.1|\/src\/|unsafe-inline|unsafe-eval/.test(html)
  || /vite\/client|testTransport|mockBridge|demoSession/.test(script)
  || chunkScripts.some(chunk => /localhost|127\.0\.0\.1|vite\/client|testTransport|mockBridge|demoSession/.test(chunk))) throw new Error('Unsafe/dev UI artifact');
const fonts = (await readdir(resolve(root, 'fonts'))).sort();
if (fonts.join(',') !== 'Geist-Regular.ttf,Geist-SemiBold.ttf,OFL-Geist.txt') throw new Error('Font/license assets missing');

function tokens(selector) {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const blocks = [...css.matchAll(new RegExp(`${escaped}\\{([^}]*)\\}`, 'g'))].map(match => match[1]);
  if (blocks.length === 0) throw new Error(`Missing theme token block ${selector}`);
  return blocks.join(';');
}

function token(block, name) {
  const values = [...block.matchAll(new RegExp(`(?:^|;)--${name}:([^;]+)`, 'g'))];
  const value = values.at(-1)?.[1];
  if (!value) throw new Error(`Missing CSS token --${name}`);
  return value.trim();
}

function luminance(hex) {
  if (!/^#(?:[\da-f]{3}|[\da-f]{6})$/i.test(hex)) throw new Error(`Expected a solid hex color, got ${hex}`);
  const expanded = hex.length === 4 ? `#${[...hex.slice(1)].map(value => value + value).join('')}` : hex;
  const channels = expanded.slice(1).match(/../g).map(value => parseInt(value, 16) / 255);
  const linear = channels.map(value => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4);
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
}

function contrast(first, second) {
  const a = luminance(first);
  const b = luminance(second);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

const themes = [
  { name: 'dark', selector: ':root', line: 'control-line' },
  { name: 'light', selector: ':root[data-theme=light]', line: 'control-line' },
];
const contrastMinima = [];
const textContrastMinima = [];
for (const theme of themes) {
  const block = tokens(theme.selector);
  const line = token(block, theme.line);
  const ratios = [];
  for (const background of ['bg', 'surface', 'raised']) {
    const ratio = contrast(line, token(block, background));
    if (ratio < 3) throw new Error(`${theme.name} control border contrast ${ratio.toFixed(2)}:1 against --${background} is below 3:1`);
    ratios.push(ratio);
  }
  contrastMinima.push(`${theme.name} ${Math.min(...ratios).toFixed(2)}:1`);

  const textRatios = [];
  for (const foreground of ['text', 'secondary', 'muted', 'accent', 'warning']) {
    for (const background of ['bg', 'surface', 'raised']) {
      const ratio = contrast(token(block, foreground), token(block, background));
      if (ratio < 4.5) throw new Error(`${theme.name} --${foreground} text contrast ${ratio.toFixed(2)}:1 against --${background} is below 4.5:1`);
      textRatios.push(ratio);
    }
  }
  const actionLabel = contrast(token(block, 'accent-text'), token(block, 'accent'));
  if (actionLabel < 4.5) throw new Error(`${theme.name} primary-action label contrast ${actionLabel.toFixed(2)}:1 is below 4.5:1`);
  textRatios.push(actionLabel);
  textContrastMinima.push(`${theme.name} ${Math.min(...textRatios).toFixed(2)}:1`);
}

if (!/@media\s*\(\s*(?:max-width:\s*1450px|width\s*<=\s*1450px)\s*\)\s*\{[^}]*\.rule-editors\s*\{\s*grid-template-columns:\s*1fr\s*\}/.test(css)) throw new Error('Profile application and website editors must stack through 1450px.');
const profileColorSwatch = /\.profile-color-swatch\{([^}]*)\}/.exec(css)?.[1] ?? '';
if (!/\.color-choice\{[^}]*width:44px[^}]*height:44px/.test(css)
  || !/width:24px/.test(profileColorSwatch)
  || !/height:24px/.test(profileColorSwatch)
  || !/border-radius:50%/.test(profileColorSwatch)
  || !/@media\s*\(\s*forced-colors:\s*active\s*\)\s*\{[^}]*\.profile-colors\s*\.color-choice\s*\{[^}]*border:\s*1px solid canvastext/i.test(css)
  || !/\.profile-color-check\{[^}]*width:12px[^}]*height:12px/.test(css)) {
  throw new Error('Profile color controls must keep a 44px target, compact swatch and forced-colors boundary.');
}
const compactProfileRail = /@media\s*\(\s*(?:max-width:\s*1300px|width\s*<=\s*1300px)\s*\)\s*\{[^@]*\.profile-list-heading\s*\{([^}]*)\}[^@]*\.profile-list-heading button\s*\{([^}]*)\}/.exec(css);
if (!compactProfileRail || !/align-items:\s*stretch/.test(compactProfileRail[1])
  || !/flex-direction:\s*column/.test(compactProfileRail[1])
  || !/width:\s*100%/.test(compactProfileRail[2])
  || !/white-space:\s*nowrap/.test(compactProfileRail[2])) {
  throw new Error('Profile refresh action must remain distinct, stacked and readable at compact desktop widths.');
}
const stickyNavigationRule = /\.app-shell\s*>\s*\.sidebar\s*\{([^}]*)\}/.exec(css)?.[1] ?? '';
const desktopStickyNavigation = /@media\s*\(\s*(?:min-width:\s*1001px|width\s*>=\s*1001px)\s*\)\s*\{[^}]*\.app-shell\s*>\s*\.sidebar\s*\{([^}]*)\}\s*\}/.exec(css)?.[1] ?? '';
const desktopStickyProfiles = /@media\s*\(\s*(?:min-width:\s*801px|width\s*>=\s*801px)\s*\)\s*\{[^}]*\.profiles-layout\s*>\s*\.profiles-rail\s*\{([^}]*)\}\s*\}/.exec(css)?.[1] ?? '';
if (!/position:\s*sticky/.test(stickyNavigationRule) || !/top:\s*0/.test(stickyNavigationRule)
  || !/align-self:\s*start/.test(stickyNavigationRule) || !/z-index:\s*1/.test(stickyNavigationRule)
  || !/height:\s*100vh/.test(desktopStickyNavigation) || !/overflow-y:\s*auto/.test(desktopStickyNavigation)
  || !/position:\s*sticky/.test(desktopStickyProfiles) || !/top:\s*24px/.test(desktopStickyProfiles)
  || !/align-self:\s*start/.test(desktopStickyProfiles) || !/z-index:\s*1/.test(desktopStickyProfiles)
  || !/max-height:\s*calc\(100vh - 48px\)/.test(desktopStickyProfiles)
  || !/overflow-y:\s*auto/.test(desktopStickyProfiles) || !/scrollbar-gutter:\s*stable/.test(desktopStickyProfiles)) {
  throw new Error('Long profile rule pages must keep desktop navigation and profile selection reachable.');
}
if (!/input:not\(\[type=checkbox\]\):not\(\[type=radio\]\),select,textarea\{border-color:var\(--control-line\)\}/.test(css)
  || !/@media\s*\(\s*forced-colors:\s*active\s*\)\{[^}]*border-color:\s*CanvasText/i.test(css)) {
  throw new Error('Accessible control-border or forced-colors rules are missing');
}
if (!/@media\s*\(\s*forced-colors:\s*active\s*\)\{[^}]*\.session-overview-active>\.facts>\.fact\{[^}]*border:\s*1px solid canvastext/i.test(css)) {
  throw new Error('Active session health cards must keep a visible border in forced-colors mode.');
}
if (!css.includes('html[data-reduce-motion=true] *')
  || !/@media\s*\(\s*prefers-reduced-motion:\s*reduce\s*\)/i.test(css)
  || !css.includes('transition:none!important') || !css.includes('animation:none!important')
  || !css.includes('scroll-behavior:auto!important')) {
  throw new Error('Stored and system reduced-motion settings must disable transition, animation and smooth scrolling.');
}
if (!/\.session-end-dialog\{margin:auto\}/.test(css) || !/\.strict-review-dialog\{margin:auto\}/.test(css)
  || !/@media\s*\(\s*prefers-reduced-motion:\s*reduce\s*\)\s*\{\.strict-review-dialog\[open\],\.strict-review-dialog\[open\]::backdrop\{animation:none\}/.test(css)
  || !css.includes('html[data-reduce-motion=true] .strict-review-dialog[open]::backdrop{animation:none}')
  || !/@media\s*\(\s*prefers-reduced-transparency:\s*reduce\s*\)\s*\{\.strict-review-dialog\[open\]::backdrop\{backdrop-filter:none/.test(css)) {
  throw new Error('The strict review dialog must stay centered despite the CSS reset and its fade/blur must honor reduced-motion and reduced-transparency.');
}
const catalogDisclosure = /\.catalog-disclosure-summary\{([^}]*)\}/.exec(css)?.[1] ?? '';
const catalogSearch = /\.catalog-search-label input\{([^}]*)\}/.exec(css)?.[1] ?? '';
if (!/display:flex/.test(catalogDisclosure) || !/min-height:68px/.test(catalogDisclosure)
  || !/cursor:pointer/.test(catalogDisclosure)
  || !/min-height:44px/.test(catalogSearch)
  || !/\.catalog-disclosure-summary:focus-visible\{[^}]*outline:2px solid var\(--accent\)/.test(css)
  || !/@media\s*\(\s*forced-colors:\s*active\s*\)\s*\{\.applications-picker\{border:1px solid canvastext/i.test(css)) {
  throw new Error('The collapsed application catalog must keep its target, visible focus, search target and forced-colors boundary in the shipped CSS.');
}
console.log(`Editable control borders meet 3:1 contrast: ${contrastMinima.join(', ')}`);
console.log(`Enabled semantic text and primary labels meet 4.5:1 contrast: ${textContrastMinima.join(', ')}`);
if (!/@media\s*\(\s*width\s*<=\s*1000px\s*\)\s*\{[^@]*\.navigation\{display:none\}[^@]*\.mobile-navigation-label\{[^}]*display:block\}[^@]*\.mobile-navigation\{[^}]*display:block/.test(css)) throw new Error('Windows up to1000px must expose the native section selector instead of the tab row.');
const minimumWindowRules = [
  '.app-shell{display:block}.sidebar{',
  '@media (width>=1001px){.app-shell>.sidebar{height:100vh;overflow-y:auto}}',
  'grid-template-columns:auto auto max-content minmax(0,1fr)',
  '@media (width<=520px){.sidebar{grid-template-rows:auto auto;',
  '@media (width<=1300px){.start-heading{flex-direction:row;align-items:center}',
  '@media (width<=600px){.start-heading{flex-direction:column;align-items:stretch}',
  '@media (height<=560px){.main{padding-top:12px;padding-bottom:12px}',
  '.start-panel{padding:18px}',
];
if (minimumWindowRules.some(rule => !css.includes(rule))) {
  throw new Error('Minimum-window mobile navigation and visible session-action layout rules are missing.');
}
if (!css.includes('.session-overview-active{grid-template-columns:minmax(0,1fr) minmax(220px,.38fr)}')
  || !/@media\s*\(\s*(?:max-width:\s*1000px|width\s*<=\s*1000px)\s*\)\{[^}]*\.session-overview-active(?:,\.session-overview-active>\.facts)?\{grid-template-columns:minmax\(0,1fr\)\}/.test(css)) {
  throw new Error('Active session channel health must sit beside the session on desktop and stack at narrower widths.');
}
const forcedSkipFocus = /@media\s*\(\s*forced-colors:\s*active\s*\)\s*\{[^}]*\.skip-link:focus-visible\s*\{([^}]*)\}/i.exec(css)?.[1] ?? '';
if (!/\.app-shell\s*>\s*\.skip-link:focus\s*~\s*\.main\s*\{\s*padding-top:\s*100px\s*;?\s*\}/.test(css)
  || !/@media\s*\(\s*(?:max-width:\s*1000px|width\s*<=\s*1000px)\s*\)\s*\{[^}]*\.app-shell\s*>\s*\.skip-link:focus\s*~\s*\.main\s*\{\s*padding-top:\s*84px\s*;?\s*\}/.test(css)
  || !/background:\s*highlight/i.test(forcedSkipFocus)
  || !/color:\s*highlighttext/i.test(forcedSkipFocus)
  || !/outline-color:\s*highlight/i.test(forcedSkipFocus)) {
  throw new Error('The visible keyboard skip link must clear the page heading at desktop and compact widths and remain visible in forced colors.');
}
console.log('Minimum-window navigation and session-action layout contract verified');
console.log('Active session channel health layout contract verified');
console.log('Keyboard skip-link spacing and forced-colors contract verified');
console.log('Stored/system reduced-motion contract verified');
console.log('Strict review dialog centering and motion contract verified');
console.log('Profile catalog disclosure and accessibility styling verified');
console.log('Local production UI assets and CSP verified');

