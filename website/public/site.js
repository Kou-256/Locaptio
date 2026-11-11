const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const reveal = element => {
  if (!element?.animate || reducedMotion.matches) return;
  element.getAnimations().forEach(animation => animation.cancel());
  element.animate([{opacity:0,transform:'translateY(6px)'},{opacity:1,transform:'translateY(0)'}], {duration:220,easing:'cubic-bezier(.2,.8,.2,1)'});
};
reducedMotion.addEventListener('change',event=>{if(event.matches)document.getAnimations().forEach(animation=>animation.cancel());});
const copy = document.querySelector('#copy-checksum');
copy?.addEventListener('click', async () => {
  const status = document.querySelector('#copy-status');
  try { await navigator.clipboard.writeText(copy.dataset.copy); status.textContent = copy.dataset.success; }
  catch { status.textContent = copy.dataset.failed; }
});
for (const group of document.querySelectorAll('[data-tabs]')) {
  const tabs = [...group.querySelectorAll('[role=tab]')];
  const activate = tab => {
    for (const item of tabs) {
      const selected = item === tab;
      const changed = item.getAttribute('aria-selected') !== String(selected);
      item.setAttribute('aria-selected', String(selected));
      item.tabIndex = selected ? 0 : -1;
      const panel = document.getElementById(item.getAttribute('aria-controls'));
      panel.hidden = !selected;
      if (selected && changed) reveal(panel);
    }
  };
  for (const [index,tab] of tabs.entries()) {
    tab.addEventListener('click', () => activate(tab));
    tab.addEventListener('keydown', event => {
      let next;
      if (event.key === 'ArrowRight') next = tabs[(index+1)%tabs.length];
      if (event.key === 'ArrowLeft') next = tabs[(index+tabs.length-1)%tabs.length];
      if (event.key === 'Home') next = tabs[0];
      if (event.key === 'End') next = tabs.at(-1);
      if (next) { event.preventDefault(); activate(next); next.focus(); }
    });
  }
}
const menuToggle=document.querySelector('.menu-toggle');
const menu=document.querySelector('#navigation');
menuToggle?.addEventListener('click',()=>{
  const open=menuToggle.getAttribute('aria-expanded')!=='true';
  menuToggle.setAttribute('aria-expanded',String(open)); menu.classList.toggle('is-open',open); if(open)reveal(menu);
});
menu?.addEventListener('click',event=>{if(event.target.closest('a')){menu.classList.remove('is-open');menuToggle.setAttribute('aria-expanded','false');}});
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&menu?.classList.contains('is-open')){menu.classList.remove('is-open');menuToggle.setAttribute('aria-expanded','false');menuToggle.focus();}});

for(const detail of document.querySelectorAll('.faq details'))detail.addEventListener('toggle',()=>{if(detail.open)reveal(detail.querySelector('p'));});
