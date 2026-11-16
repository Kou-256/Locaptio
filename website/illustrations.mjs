// Inline SVG illustrations. Colors come from classes styled in public/style.css
// (.il-*), so every drawing follows the site's single teal accent and neutral roles.
// Each drawing explains one feature at a glance; the text beside it carries the detail.
const svg = (body, label, viewBox = '0 0 240 150') =>
  `<svg class="illustration" viewBox="${viewBox}" role="img" aria-label="${label}" xmlns="http://www.w3.org/2000/svg">${body}</svg>`;
const win = (x, y, w, h) => `<rect class="il-frame" x="${x}" y="${y}" width="${w}" height="${h}" rx="8"/><path class="il-line" d="M${x} ${y + 16}h${w}"/><circle class="il-dot" cx="${x + 10}" cy="${y + 8}" r="2.5"/><circle class="il-dot" cx="${x + 19}" cy="${y + 8}" r="2.5"/>`;
const bar = (x, y, w, cls = 'il-text') => `<rect class="${cls}" x="${x}" y="${y}" width="${w}" height="6" rx="3"/>`;

export const illustrations = {
  // How Locaptio works: sound from the PC → captions → files you keep.
  flow: label => svg(`
    <g transform="translate(6 34)">
      <rect class="il-frame" x="0" y="0" width="128" height="86" rx="9"/><rect class="il-soft" x="8" y="8" width="112" height="62" rx="5"/>
      <path class="il-accent-fill" d="M54 26v26l22-13z"/><path class="il-line" d="M48 86l-6 14h44l-6-14"/><path class="il-line" d="M34 100h60"/>
      <path class="il-wave" d="M142 30c8 8 8 26 0 34M154 20c14 14 14 40 0 54M166 10c20 20 20 54 0 74"/>
    </g>
    <path class="il-arrow" d="M196 77h32m-8-8 8 8-8 8"/>
    <g transform="translate(236 26)">
      ${win(0, 0, 150, 104)}
      <rect class="il-caption" x="12" y="62" width="126" height="32" rx="6"/>${bar(22, 70, 98, 'il-caption-text')}${bar(34, 82, 74, 'il-caption-text')}
      ${bar(12, 26, 118)}${bar(12, 38, 92)}${bar(12, 50, 106, 'il-text il-faint')}
    </g>
    <path class="il-arrow" d="M394 77h32m-8-8 8 8-8 8"/>
    <g transform="translate(434 20)">
      <path class="il-frame" d="M0 10a6 6 0 016-6h34l10 10v56a6 6 0 01-6 6H6a6 6 0 01-6-6z"/><path class="il-wave" d="M10 48v-8m8 14V34m8 16v-12m8 8v-4"/>
      <path class="il-frame" d="M30 36a6 6 0 016-6h34l10 10v56a6 6 0 01-6 6H36a6 6 0 01-6-6z"/>${bar(40, 50, 30)}${bar(40, 62, 34)}${bar(40, 74, 24)}
      <rect class="il-accent-fill" x="60" y="86" width="48" height="26" rx="13"/><path class="il-on-accent" d="M72 99l5 5 10-10"/>
    </g>`, label, '0 0 560 150')
};

// Small stroke icons for buttons and headings, matching build.mjs.
export const iconPaths = {
  restore: '<path d="M4 12a8 8 0 1 0 2.4-5.7"/><path d="M4 4v4h4"/><path d="M12 8v4l3 2"/>',
  edit: '<path d="M4 20l4-1L19 8l-3-3L5 16z"/><path d="M14 7l3 3"/>',
  retranscribe: '<path d="M4 12a8 8 0 0 1 14-5.3M20 12a8 8 0 0 1-14 5.3"/><path d="M18 3v4h-4M6 21v-4h4"/>',
  dictionary: '<path d="M5 4h11a3 3 0 0 1 3 3v13H8a3 3 0 0 1-3-3z"/><path d="M5 17a3 3 0 0 1 3-3h11M9 8h6"/>',
  sync: '<circle cx="12" cy="13" r="8"/><path d="M12 9v4l3 2M9 2h6"/>',
  subtitles: '<rect x="2" y="4" width="20" height="16" rx="3"/><path d="M6 15h6m3 0h3M6 11h3m3 0h6"/>',
  bookmark: '<path d="M12 3l2.6 5.4 5.9.9-4.3 4.1 1 5.9L12 16.5l-5.2 2.8 1-5.9L3.5 9.3l5.9-.9z"/>',
  summary: '<path d="M8 6h12M8 12h12M8 18h8"/><path d="M3.5 6h.01M3.5 12h.01M3.5 18h.01"/>',
  sound: '<path d="M4 9v6h4l5 4V5L8 9z"/><path d="M16 9a4 4 0 0 1 0 6M19 6a8 8 0 0 1 0 12"/>',
  file: '<path d="M6 2h9l5 5v15H6z"/><path d="M14 2v6h6M9 13h7M9 17h5"/>'
};
