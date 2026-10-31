// The editor's browser side: fitting translated text into screenshot boxes, drawing new boxes, and
// small helpers. Plain script, no build step; Blazor calls the functions on window.rcinel.
(() => {
  'use strict';

  const MIN_FONT = 6;

  // --- Fitting text into regions -------------------------------------------------------------

  // The largest font size (px) at which `text` fits into `box`, found by bisection on a hidden
  // measuring copy so the visible box doesn't flicker. Measured half a pixel narrower than the box:
  // a word that fits by 0.3 px in the copy may still wrap in the box.
  function fittingSize(box, text, measure) {
    const rect = box.getBoundingClientRect();
    const width = rect.width - 0.5;
    const height = rect.height;
    measure.textContent = text;
    measure.style.width = `${width}px`;
    let low = MIN_FONT;
    // A box drawn around one line of text is a bit taller than the letters: start at 80% of it.
    let high = Math.max(MIN_FONT, height * 0.8);
    // Height from the fractional rectangle; width as "doesn't overflow its own box", because
    // scrollWidth is rounded to whole pixels and would never equal a fractional width.
    const fits = (size) => {
      measure.style.fontSize = `${size}px`;
      return measure.getBoundingClientRect().height <= height && measure.scrollWidth <= measure.clientWidth;
    };
    if (fits(high)) {
      return high;
    }
    for (let i = 0; i < 12; i++) {
      const mid = (low + high) / 2;
      if (fits(mid)) {
        low = mid;
      } else {
        high = mid;
      }
    }
    return low;
  }

  let measure;
  function measurer() {
    if (!measure) {
      measure = document.createElement('span');
      measure.className = 'region-measure';
      measure.setAttribute('aria-hidden', 'true');
      document.body.appendChild(measure);
    }
    return measure;
  }

  // The source text decides the size: it's what the designer fitted, so the original font was
  // about that big. The translation is shown at that size; if it needs a smaller one, the box gets
  // the "overflow" class (a red outline) and a tooltip with how much smaller.
  function fit(box) {
    const span = box.querySelector('span');
    if (!span || box.getBoundingClientRect().height === 0) {
      return;
    }
    const m = measurer();
    m.style.fontFamily = getComputedStyle(box).fontFamily;
    const source = box.dataset.source || span.textContent;
    // The measured size of the original text when there is one (see sampleColors), otherwise the
    // largest size at which the source text fits the box.
    const shot = box.closest('.shot');
    const measured = Number(box.dataset.font || 0) * (shot ? shot.getBoundingClientRect().height : 0);
    const original = measured > 0 ? Math.min(measured, fittingSize(box, source, m) * 1.05) : fittingSize(box, source, m);
    const needed = fittingSize(box, span.textContent, m);
    const size = Math.min(original, needed);
    span.style.fontSize = `${size}px`;
    const tooLong = needed < original * 0.9 && span.textContent.trim() !== '';
    box.classList.toggle('overflow', tooLong);
    if (tooLong) {
      box.dataset.fitNote = `Needs a ${Math.round((1 - needed / original) * 100)}% smaller font than the original`;
      box.setAttribute('title', `${box.dataset.fitNote}. Source: ${source}`);
    } else {
      delete box.dataset.fitNote;
      box.setAttribute('title', source);
    }
  }

  let scheduled = false;
  function fitAll() {
    if (scheduled) {
      return;
    }
    scheduled = true;
    requestAnimationFrame(() => {
      scheduled = false;
      document.querySelectorAll('[data-fit]').forEach(fit);
    });
  }

  // Blazor patches the DOM whenever a translation is typed, so watch for any change instead of
  // asking every component to call in.
  new MutationObserver(fitAll).observe(document.documentElement, { subtree: true, childList: true, characterData: true });
  const resizes = new ResizeObserver(fitAll);
  window.addEventListener('load', fitAll);
  document.addEventListener('load', (e) => {
    if (e.target.tagName === 'IMG') {
      fitAll();
    }
  }, true);
  new MutationObserver(() => document.querySelectorAll('.shot:not([data-observed])').forEach((shot) => {
    shot.dataset.observed = '1';
    resizes.observe(shot);
  })).observe(document.documentElement, { subtree: true, childList: true });

  // --- Colours ---------------------------------------------------------------------------------

  const hex = (r, g, b) => `#${[r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('')}`;
  const distance = (a, b) => Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1]) + Math.abs(a[2] - b[2]);

  // The most common colour among `pixels`, after rounding to 16 levels so anti-aliasing noise
  // doesn't split one colour into many.
  function dominant(pixels) {
    const counts = new Map();
    let best = null;
    let bestCount = 0;
    for (const p of pixels) {
      const key = ((p[0] >> 4) << 8) | ((p[1] >> 4) << 4) | (p[2] >> 4);
      const entry = counts.get(key) || { n: 0, sum: [0, 0, 0] };
      entry.n++;
      entry.sum[0] += p[0];
      entry.sum[1] += p[1];
      entry.sum[2] += p[2];
      counts.set(key, entry);
      if (entry.n > bestCount) {
        bestCount = entry.n;
        best = entry;
      }
    }
    return best ? best.sum.map((v) => Math.round(v / best.n)) : [255, 255, 255];
  }

  // Background: the dominant colour along the edge of the box. Text: the dominant colour of the
  // pixels inside that differ clearly from it. That reproduces a button or label closely enough
  // for the preview to read like the real screen.
  function sampleColors(img, x, y, w, h) {
    const canvas = document.createElement('canvas');
    canvas.width = img.naturalWidth;
    canvas.height = img.naturalHeight;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(img, 0, 0);
    const px = Math.max(0, Math.round(x * img.naturalWidth));
    const py = Math.max(0, Math.round(y * img.naturalHeight));
    const pw = Math.max(1, Math.min(img.naturalWidth - px, Math.round(w * img.naturalWidth)));
    const ph = Math.max(1, Math.min(img.naturalHeight - py, Math.round(h * img.naturalHeight)));
    const data = ctx.getImageData(px, py, pw, ph).data;
    const at = (cx, cy) => {
      const i = (cy * pw + cx) * 4;
      return [data[i], data[i + 1], data[i + 2]];
    };
    const edge = [];
    for (let cx = 0; cx < pw; cx++) {
      edge.push(at(cx, 0), at(cx, ph - 1));
    }
    for (let cy = 0; cy < ph; cy++) {
      edge.push(at(0, cy), at(pw - 1, cy));
    }
    const background = dominant(edge);
    const inside = [];
    for (let cy = 1; cy < ph - 1; cy++) {
      for (let cx = 1; cx < pw - 1; cx++) {
        const p = at(cx, cy);
        if (distance(p, background) > 120) {
          inside.push(p);
        }
      }
    }
    // The text's font size: the tallest run of rows that contain text pixels is one line of
    // glyphs, roughly 0.75 of the font size (cap height plus descenders). Several runs mean
    // several lines; the tallest is the best guess for one.
    // The alignment: where the text pixels sit between the box's left and right edges.
    let fontSize = 0;
    let align = 'center';
    if (inside.length > 0) {
      let run = 0;
      let tallest = 0;
      let minX = pw;
      let maxX = 0;
      for (let cy = 1; cy < ph - 1; cy++) {
        let hit = false;
        for (let cx = 1; cx < pw - 1; cx++) {
          if (distance(at(cx, cy), background) > 120) {
            hit = true;
            minX = Math.min(minX, cx);
            maxX = Math.max(maxX, cx);
          }
        }
        run = hit ? run + 1 : 0;
        tallest = Math.max(tallest, run);
      }
      fontSize = Math.min(tallest / 0.75, ph) / img.naturalHeight;
      const left = minX;
      const right = pw - 1 - maxX;
      if (Math.abs(left - right) > pw * 0.15) {
        align = left < right ? 'left' : 'right';
      }
    }
    let text;
    if (inside.length > 0) {
      text = dominant(inside);
    } else {
      // No clear text pixels: pick black or white, whichever reads better on the background.
      const light = background[0] * 0.299 + background[1] * 0.587 + background[2] * 0.114 > 140;
      text = light ? [0, 0, 0] : [255, 255, 255];
    }
    return { background: hex(...background), color: hex(...text), fontSize, align };
  }

  // --- Drawing regions -------------------------------------------------------------------------

  const drawing = new WeakMap();

  function attachDrawing(shot, dotnet) {
    if (!shot || drawing.has(shot)) {
      return;
    }
    const state = { enabled: false, start: null, box: null, dotnet };
    drawing.set(shot, state);

    const point = (e) => {
      const rect = shot.getBoundingClientRect();
      return {
        x: Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width)),
        y: Math.min(1, Math.max(0, (e.clientY - rect.top) / rect.height)),
      };
    };

    shot.addEventListener('pointerdown', (e) => {
      if (!state.enabled || e.button !== 0) {
        return;
      }
      e.preventDefault();
      shot.setPointerCapture(e.pointerId);
      state.start = point(e);
      state.box = document.createElement('div');
      state.box.className = 'region drawing-box';
      shot.appendChild(state.box);
    });

    shot.addEventListener('pointermove', (e) => {
      if (!state.start) {
        return;
      }
      const p = point(e);
      const x = Math.min(p.x, state.start.x);
      const y = Math.min(p.y, state.start.y);
      Object.assign(state.box.style, {
        left: `${x * 100}%`,
        top: `${y * 100}%`,
        width: `${Math.abs(p.x - state.start.x) * 100}%`,
        height: `${Math.abs(p.y - state.start.y) * 100}%`,
      });
    });

    const finish = (e) => {
      if (!state.start) {
        return;
      }
      const p = point(e);
      const x = Math.min(p.x, state.start.x);
      const y = Math.min(p.y, state.start.y);
      const w = Math.abs(p.x - state.start.x);
      const h = Math.abs(p.y - state.start.y);
      state.start = null;
      // Leave the box visible until the string is picked; Blazor's next render replaces it.
      if (w < 0.005 || h < 0.005) {
        state.box.remove();
        return;
      }
      let colors = { background: '#ffffff', color: '#000000', fontSize: 0, align: 'center' };
      const img = shot.querySelector('img');
      try {
        colors = sampleColors(img, x, y, w, h);
      } catch {
        // A tainted canvas can't be read; keep the defaults, they can be changed by hand.
      }
      state.dotnet.invokeMethodAsync('RegionDrawn', x, y, w, h, colors.background, colors.color, colors.fontSize, colors.align);
    };
    shot.addEventListener('pointerup', finish);
    shot.addEventListener('pointercancel', () => {
      state.start = null;
      state.box?.remove();
    });
  }

  function setDrawing(shot, enabled) {
    const state = drawing.get(shot);
    if (state) {
      state.enabled = enabled;
      if (!enabled) {
        shot.querySelectorAll('.drawing-box').forEach((b) => b.remove());
      }
    }
  }

  window.rcinel = {
    attachDrawing,
    setDrawing,
    sampleColors,
    fitAll,
    copy: (text) => navigator.clipboard?.writeText(text),
    focus: (id) => document.getElementById(id)?.focus(),
  };
})();
