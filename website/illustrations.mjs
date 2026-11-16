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
    </g>`, label, '0 0 560 150'),

  autosave: label => svg(`
    ${win(20, 18, 150, 108)}${bar(32, 46, 110)}${bar(32, 60, 86)}${bar(32, 74, 118)}${bar(32, 88, 70, 'il-text il-faint')}
    <path class="il-warn" d="M120 96l12-18 6 10 10-14"/>
    <circle class="il-accent-fill" cx="178" cy="94" r="34"/>
    <path class="il-on-accent" d="M163 94a15 15 0 1 0 5-11"/><path class="il-on-accent" d="M161 78v8h8"/>
    <path class="il-on-accent" d="M178 86v9l6 4"/>`, label),

  edit: label => svg(`
    ${win(16, 16, 208, 118)}
    ${bar(30, 44, 64)}<rect class="il-strike-box" x="98" y="40" width="50" height="14" rx="4"/>${bar(102, 44, 42, 'il-text il-faint')}<path class="il-strike" d="M100 47h46"/>${bar(152, 44, 54)}
    <rect class="il-accent-soft" x="98" y="60" width="54" height="16" rx="4"/>${bar(103, 65, 44, 'il-accent-bar')}
    ${bar(30, 90, 170)}${bar(30, 104, 130, 'il-text il-faint')}
    <path class="il-pen" d="M164 96l26-26 10 10-26 26-14 4z"/><path class="il-line" d="M184 76l10 10"/>
    <g transform="translate(30 60)"><circle class="il-chip" cx="24" cy="8" r="13"/><path class="il-accent-stroke" d="M18 6a7 7 0 1 1 2 6"/><path class="il-accent-stroke" d="M16 1v6h6"/></g>`, label),

  retranscribe: label => svg(`
    <rect class="il-frame" x="14" y="20" width="98" height="110" rx="8"/><rect class="il-frame" x="128" y="20" width="98" height="110" rx="8"/>
    <rect class="il-tag" x="24" y="30" width="40" height="14" rx="7"/><rect class="il-accent-fill" x="138" y="30" width="58" height="14" rx="7"/>
    ${bar(24, 56, 76)}${bar(24, 70, 52, 'il-text il-faint')}<rect class="il-warn-soft" x="22" y="82" width="80" height="12" rx="3"/>${bar(26, 85, 60, 'il-text il-faint')}${bar(24, 104, 64)}
    ${bar(138, 56, 76)}${bar(138, 70, 52)}<rect class="il-accent-soft" x="136" y="82" width="80" height="12" rx="3"/>${bar(140, 85, 70, 'il-accent-bar')}${bar(138, 104, 64)}
    <circle class="il-chip" cx="120" cy="75" r="14"/><path class="il-accent-stroke" d="M113 72a7 7 0 0 1 13-2m1 8a7 7 0 0 1-13 2"/><path class="il-accent-stroke" d="M127 64v6h-6m-8 16v-6h6"/>`, label),

  dictionary: label => svg(`
    <path class="il-frame" d="M30 24h70a12 12 0 0 1 12 12v90H42a12 12 0 0 1-12-12z"/><path class="il-line" d="M30 114a12 12 0 0 1 12-12h70"/>
    <rect class="il-accent-fill" x="44" y="40" width="54" height="10" rx="5"/>${bar(44, 58, 42)}${bar(44, 72, 50, 'il-text il-faint')}${bar(44, 86, 34, 'il-text il-faint')}
    <rect class="il-warn-soft" x="128" y="34" width="90" height="26" rx="13"/><text class="il-label il-label-warn" x="173" y="51">ロカプション</text>
    <path class="il-arrow" d="M173 66v18m-7-7 7 7 7-7"/>
    <rect class="il-accent-fill" x="128" y="92" width="90" height="26" rx="13"/><text class="il-label il-label-on" x="173" y="109">Locaptio</text>`, label),

  sync: label => svg(`
    <rect class="il-soft" x="14" y="18" width="212" height="60" rx="8"/>
    <path class="il-wave-strong" d="M24 48h6m4-14v28m6-20v12m6-22v32m6-18v4m6-16v28m6-12v-4m6 18v-30m6 10v10m6-24v36m6-26v16m6-10v4m6-14v24m6-30v36m6-20v4m6-12v20m6-26v32m6-18v4m6-10v16m6-22v28m6-12v-4"/>
    <path class="il-playhead" d="M108 12v126"/><circle class="il-accent-fill" cx="108" cy="12" r="5"/>
    <rect class="il-tag" x="16" y="96" width="38" height="20" rx="5"/><rect class="il-tag" x="58" y="96" width="44" height="20" rx="5"/>
    <rect class="il-accent-fill" x="106" y="96" width="34" height="20" rx="5"/><rect class="il-tag" x="144" y="96" width="46" height="20" rx="5"/><rect class="il-tag" x="194" y="96" width="30" height="20" rx="5"/>
    <path class="il-line il-dashed" d="M16 88h38m4 0h44m4 0h34m4 0h46m4 0h30"/>`, label),

  subtitles: label => svg(`
    <rect class="il-frame" x="14" y="16" width="160" height="100" rx="8"/><rect class="il-soft" x="22" y="24" width="144" height="84" rx="4"/>
    <path class="il-mountain" d="M22 96l36-30 24 18 22-14 40 30v8H22z"/><circle class="il-sun" cx="136" cy="44" r="9"/>
    <rect class="il-caption" x="44" y="86" width="100" height="16" rx="3"/>${bar(52, 91, 84, 'il-caption-text')}
    <path class="il-line" d="M14 128h160"/><circle class="il-accent-fill" cx="64" cy="128" r="5"/>
    <g transform="translate(176 32)"><rect class="il-accent-fill" x="0" y="0" width="52" height="28" rx="6"/><text class="il-label il-label-on" x="26" y="19">SRT</text>
    <rect class="il-tag-strong" x="0" y="40" width="52" height="28" rx="6"/><text class="il-label il-label-accent" x="26" y="59">VTT</text></g>`, label),

  bookmarks: label => svg(`
    <path class="il-line il-thick" d="M18 96h204"/>
    ${[30, 54, 70, 104, 118, 150, 176, 200].map((x, i) => `<path class="il-wave" d="M${x} ${96 - (i % 3 + 1) * 7}v${(i % 3 + 1) * 14}"/>`).join('')}
    <g transform="translate(62 30)"><path class="il-star" d="M12 0l3.7 7.6 8.3 1.2-6 5.9 1.4 8.3L12 19l-7.4 3.9L6 14.7 0 8.8l8.3-1.2z"/><path class="il-line" d="M12 28v32"/></g>
    <g transform="translate(150 22)"><rect class="il-accent-fill" x="0" y="0" width="58" height="22" rx="11"/><text class="il-label il-label-on" x="29" y="15">後で確認</text><path class="il-line" d="M29 26v46"/></g>
    <g transform="translate(26 112)"><rect class="il-chip-strong" x="0" y="0" width="86" height="24" rx="12"/><path class="il-star-small" d="M14 6l1.9 3.8 4.1.6-3 2.9.7 4.1L14 15.4l-3.7 2 .7-4.1-3-2.9 4.1-.6z"/><text class="il-label il-label-dark" x="54" y="16">00:12:40</text></g>`, label),

  summary: label => svg(`
    <rect class="il-frame" x="16" y="14" width="132" height="122" rx="8"/>
    <rect class="il-accent-fill" x="28" y="26" width="40" height="8" rx="4"/>
    <circle class="il-dot-accent" cx="32" cy="48" r="3"/>${bar(40, 45, 76)}<circle class="il-dot-accent" cx="32" cy="62" r="3"/>${bar(40, 59, 58)}
    <rect class="il-accent-fill" x="28" y="76" width="54" height="8" rx="4"/>
    <rect class="il-check" x="28" y="92" width="10" height="10" rx="2"/><path class="il-accent-stroke" d="M30 97l2.5 2.5 4-5"/>${bar(44, 94, 66)}
    <rect class="il-check" x="28" y="110" width="10" height="10" rx="2"/>${bar(44, 112, 80)}
    <rect class="il-chip-strong" x="118" y="90" width="44" height="16" rx="8"/><text class="il-label il-label-dark il-small" x="140" y="101">▶ 12:40</text>
    <path class="il-link" d="M162 98c18 0 18-46 34-46"/>
    <rect class="il-frame" x="176" y="30" width="54" height="44" rx="6"/><rect class="il-accent-soft" x="182" y="44" width="42" height="12" rx="3"/>${bar(186, 47, 30, 'il-accent-bar')}${bar(184, 62, 36, 'il-text il-faint')}
    <path class="il-sparkle" d="M210 102l3 8 8 3-8 3-3 8-3-8-8-3 8-3z"/>`, label)
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
