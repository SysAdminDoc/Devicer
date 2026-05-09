# Devicer — Logo Prompts (v0.1.0 drafts)

Five-prompt logo flow per the global branding rule. Each prompt assumes ChatGPT / DALL-E or equivalent. Pick one and iterate; the winner becomes `branding/logo.png` (square) and `branding/banner.png` (wide).

**Brand traits**
- Domain: power-user Android phone management on Windows
- Tone: technical, confident, slightly dangerous (you're flashing firmware — there's risk)
- Palette: Catppuccin Mocha base — Mauve `#cba6f7`, Sapphire `#74c7ec`, Base `#1e1e2e`
- Avoid: anything that reads as a generic "phone repair shop" logo

---

## 1. Minimal — Geometric monoline

```
A minimal monoline logo for an app called "Devicer". Single-weight outline,
geometric, no fill. Composition: stylized smartphone silhouette interlocked
with a USB-A connector tip, suggesting hardwired control. Two colors max:
Catppuccin Mauve (#cba6f7) outline, transparent background. No text in the
mark. Clean, balanced, square 1:1 composition.

Final output: 512x512 PNG, RGBA, true transparent background, alpha channel
enabled, no checkerboard, no solid background, no watermark, no text, only
the main icon visible.
```

## 2. App — Rounded-square iOS-style icon

```
A modern rounded-square app icon for "Devicer", a Windows tool for managing
rooted Android phones. Background: subtle Catppuccin Mocha gradient from
deep navy (#1e1e2e) to indigo (#313244). Foreground: a phone outline with
a small unlocked-padlock charm dangling from a USB cable, rendered in
Sapphire (#74c7ec) and Mauve (#cba6f7). Soft inner glow. No text.

Final output: 1024x1024 PNG, RGBA, true transparent background outside the
rounded-square shape itself. Real alpha channel, no checkerboard, no
external solid background. Only the rounded-square icon visible.
```

## 3. Wordmark — Technical mono typeface

```
A wordmark logo for "DEVICER" set in a precise technical mono typeface
(JetBrains Mono or similar). All-caps, generous tracking. The letter "I"
is replaced by a vertical USB-A connector glyph in Sapphire (#74c7ec);
the rest of the wordmark is in soft white (#cdd6f4). No tagline, no
underline, no decorative flourishes. Wide composition for use as a banner.

Final output: 1600x400 PNG, RGBA, true transparent background, alpha = 0
outside the wordmark glyphs. No checkerboard, no solid background, only
the wordmark visible.
```

## 4. Emblem — Circular badge

```
A circular badge emblem for "Devicer". Outer ring: thin Mauve (#cba6f7)
border with small tick marks at quarter points (suggests a tool dial).
Center: stylized phone silhouette overlaid with crossed wrench and key,
all in Sapphire (#74c7ec) on a dark Base (#1e1e2e) circular fill. Above
the center, a small "v0.1" version stamp would fit but leave that out for
the master logo. Strong, official, slightly industrial.

Final output: 1024x1024 PNG, RGBA, true transparent background outside the
circular badge edge. Real alpha channel, no checkerboard, no rectangular
backdrop, only the circular emblem visible.
```

## 5. Abstract — Data-stream motif

```
An abstract logo mark for "Devicer" representing the flow of firmware from
PC to phone. Composition: a stylized data-stream of small geometric particles
flowing diagonally from upper-left to lower-right, condensing into a phone
silhouette at the bottom-right. Particles transition in color from Sapphire
(#74c7ec) at the source to Mauve (#cba6f7) at the destination. Minimal,
modern, suggestive of motion. No text in the mark.

Final output: 1024x1024 PNG, RGBA, true transparent background, alpha = 0
outside the particle flow and phone silhouette. No checkerboard, no solid
backdrop, only the mark visible.
```

---

## Reusable transparency clause (append to any of the above if regenerating)

```
Background/output requirements: The final image must be a true transparent
PNG in RGBA format with a real alpha channel. Everything outside the main
icon/logo must be fully transparent, alpha = 0. Do not render a checkerboard
pattern. Do not render a white, gray, black, colored, or textured background.
Do not simulate transparency. Only the main icon/logo should contain visible
pixels. If the generated image includes a checkerboard or any visible
background, remove it with image processing and export a corrected
transparent PNG artifact.
```

## Integration checklist (when a winner is chosen)

- [ ] Save as `branding/logo.png` (square 1024x1024) and `branding/banner.png` (wide 1600x400 or similar)
- [ ] Verify RGBA in an image-info tool (`file branding/logo.png` should report `8-bit/color RGBA`)
- [ ] Update README banner reference at the top
- [ ] If app icon: export ICO multi-resolution (16/32/48/64/128/256) for the WPF assembly
- [ ] Commit + push (once the repo has a GH remote)
