// The listing build renders this file with Scriban too, so it must never contain two opening braces in a row.
// All data comes from index.html (data-listing-url).
const listingUrl = document.body.dataset.listingUrl;

document.querySelectorAll('[data-action="add"]').forEach(button => button.addEventListener('click', () => {
  window.location.assign(`vcc://vpm/addRepo?url=${encodeURIComponent(listingUrl)}`);
}));

document.querySelectorAll('[data-action="copy"]').forEach(button => {
  const label = button.querySelector('[data-label]');
  const text = label.textContent;
  button.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(listingUrl);
      label.textContent = 'copied';
    } catch {
      // Clipboard API blocked: select the URL so Ctrl+C works.
      getSelection().selectAllChildren(document.getElementById('listingUrl'));
      label.textContent = 'press ctrl+c';
    }
    setTimeout(() => { label.textContent = text; }, 1600);
  });
});

// Pivot: mark the section in view.
const pivotLinks = [...document.querySelectorAll('.pivot-items a')];
const sectionObserver = new IntersectionObserver(entries => {
  for (const entry of entries) {
    if (entry.isIntersecting) pivotLinks.forEach(a => a.setAttribute('aria-current', a.hash === `#${entry.target.id}`));
  }
}, { rootMargin: '-45% 0px -50% 0px' });
document.querySelectorAll('#start, main section').forEach(s => sectionObserver.observe(s));

// Tile groups swing in (turnstile) the first time they scroll into view.
const groupObserver = new IntersectionObserver(entries => {
  for (const entry of entries) {
    if (!entry.isIntersecting) continue;
    entry.target.classList.add('in');
    groupObserver.unobserve(entry.target);
  }
}, { threshold: .15 });
document.querySelectorAll('.group').forEach(g => groupObserver.observe(g));

const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;

// Start-screen parallax: the backdrop scrolls at a fraction of the page speed, capped to the 480px it has spare.
if (!reduceMotion) {
  let frame = 0;
  addEventListener('scroll', () => {
    if (frame) return;
    frame = requestAnimationFrame(() => {
      frame = 0;
      document.documentElement.style.setProperty('--py', `${Math.max(-460, -scrollY * .12).toFixed(1)}px`);
    });
  }, { passive: true });
}

// Metro tilt: a pressed tile leans away from the pointer.
if (!reduceMotion) {
  document.querySelectorAll('.tile').forEach(tile => {
    tile.addEventListener('pointerdown', e => {
      const r = tile.getBoundingClientRect();
      const x = (e.clientX - r.left) / r.width - .5;
      const y = (e.clientY - r.top) / r.height - .5;
      tile.style.transform = `perspective(700px) rotateX(${(-y * 14).toFixed(1)}deg) rotateY(${(x * 14).toFixed(1)}deg) scale(.97)`;
    });
    const reset = () => { tile.style.transform = ''; };
    ['pointerup', 'pointerleave', 'pointercancel'].forEach(type => tile.addEventListener(type, reset));
  });
}
