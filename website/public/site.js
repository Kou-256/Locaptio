const copy = document.querySelector('#copy-checksum');
copy?.addEventListener('click', async () => {
  const status = document.querySelector('#copy-status');
  try { await navigator.clipboard.writeText(copy.dataset.copy); status.textContent = copy.dataset.success; }
  catch { status.textContent = copy.dataset.failed; }
});
