# Locaptio website design

## Direction approved 2026-11-12

- Product: local Windows captions, transcription and recordings for people following speech on their PC.
- Character: calm, legible, precise.
- World: captions and a small audio recorder; white with the existing muted teal.
- Moments: an interactive caption appearance preview and a genuine settings screenshot.
- Signature: a consistent line-wave icon, used sparingly.

## Audit and changes

The original site already had a coherent light palette and split hero. This was a polish and content expansion, not a new visual identity. The owner-supplied icon and teal were preserved.

1. Spacing: a four-pixel rhythm, consistent section gaps, matching controls and mobile stacking.
2. Color: one neutral role system with one teal accent. Preserve the original Windows-style wallpaper in the caption stage at the owner's request, with light surfaces behind controls for legibility.
3. Icons: one stroke family, no decorative emoji. The actual app screenshot keeps its original UI.
4. Copy: a concrete headline and detailed feature descriptions instead of three short claims. Explain setup, optional models, device support and known limits.
5. Type: use 600-weight headings and selective bold emphasis for important features and instructions, reduced negative tracking, separated body and supporting text, and removed forced no-wrap text.
6. Layout: retain hero, features, setup and distribution order; expand features with four keyboard-accessible categories and add settings/FAQ content.
7. Details: preserve a single direct ZIP action, accessible focus, checksums, language links and native details controls. Mobile navigation has expanded state and Escape dismissal.
8. Visuals: caption demo is labeled as an illustration, not an actual live recognizer. The settings image is real, captured from the v1.0.38 release smoke test.
9. Motion: brief, one-time hero entry and user-triggered tab/menu/FAQ reveals, plus subtle button feedback. Reduced-motion disables movement. No looping or scroll-driven animation.

## References and attribution

- Unslop by Damir Sokolovsky (@heydamir): https://github.com/heydamir/unslop, CC BY 4.0 https://creativecommons.org/licenses/by/4.0/. Used for the audit and polish process; adaptations are described above.
- Astryx tokens: https://astryx.atmeta.com/docs/tokens
- Astryx design conventions: https://github.com/meta/astryx/wiki/Design-Conventions

Astryx informs semantic color roles, spacing, typography, state consistency and accessible controls. Its React packages and templates are not installed: the existing site is a static Node-generated site with no runtime framework. The Astryx CLI skill was not available locally; its official documentation was consulted rather than claiming a component-library integration.

## Validation

Build and static tests: `npm run build`, `npm test`. Browser walkthrough: caption-mode tabs, feature tabs, language switch, mobile menu and checksum copy. Japanese desktop 1440x1000 and mobile 390x844, plus English route. No missing images, horizontal page overflow or browser console errors in the checked states. Browser plugin was tagged during the task; subsequent interactive validation used its in-app browser through CUA. Initial baseline used an isolated Playwright headless browser before that browser became available.

Download metadata stays in `release.json` and is changed only after the corresponding ZIP and checksum have been verified on GitHub Releases.

Owner copy preference (2026-11-12): use explicit feature descriptions, not poetic slogans. The hero names Japanese transcription and English-to-Japanese translation; section headings name their functions.

Owner refinement (2026-11-12): remove design-reference links from the public footer; retain attribution in repository documentation. Use a gentler easing curve with 700ms entry, 420ms interaction reveals and 240ms button feedback.
