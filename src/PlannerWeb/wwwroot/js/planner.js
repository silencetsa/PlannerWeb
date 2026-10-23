'use strict';
(() => {
  const planner = document.querySelector('.planner');
  if (!planner) return;
  const token = document.querySelector('#csrf input').value;
  const editor = document.querySelector('#editor');
  const form = document.querySelector('#editor-form');
  const fields = document.querySelector('#editor-fields');
  const menu = document.querySelector('#task-menu');
  let submitAction, contextTask, suppressUntil = 0, pendingWrites = Promise.resolve();
  const money = n => new Intl.NumberFormat('ru-RU', { minimumFractionDigits: n % 1 === 0 ? 0 : 2, maximumFractionDigits: 2 }).format(n) + ' ₽';
  const notify = text => { const s = document.querySelector('#status'); s.textContent = text; s.hidden = false; };
  async function command(data) {
    const response = await fetch('/Planner/Command', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token }, body: JSON.stringify(data) });
    if (response.redirected) { location.assign(response.url); throw new Error('Войдите в аккаунт'); }
    if (!response.ok) { const error = await response.json().catch(() => null); throw new Error(error?.error || 'Не удалось сохранить. Попробуйте ещё раз.'); }
    return response.json();
  }
  async function change(data) { await pendingWrites; await command(data); location.reload(); }
  function reset(title, template, action, button = 'Сохранить') {
    fields.replaceChildren(); document.querySelector('#editor-title').textContent = title;
    document.querySelector('#editor-error').hidden = true;
    document.querySelector('#editor-submit').textContent = button;
    if (template) fields.append(document.querySelector(`#${template}-fields`).content.cloneNode(true));
    submitAction = action; menu.hidden = true;
    if (!editor.open) editor.showModal();
    requestAnimationFrame(() => fields.querySelector('input:not([type=radio]),textarea,select')?.focus());
  }
  function fill(values) { for (const [key, value] of Object.entries(values)) { const input = fields.querySelector(`[name="${key}"]`); if (!input) continue; if (input.type === 'checkbox') input.checked = value === true || value === 'true'; else input.value = value ?? ''; } }
  function values() {
    const data = Object.fromEntries(new FormData(form));
    for (const name of ['important', 'urgent']) data[name] = form.elements[name]?.checked ?? false;
    for (const name of ['repeat', 'part', 'scope']) if (data[name] !== undefined) data[name] = Number(data[name]);
    return data;
  }
  form.addEventListener('submit', async event => {
    event.preventDefault(); const button = document.querySelector('#editor-submit'); button.disabled = true;
    try { await submitAction(values()); }
    catch (e) { const error = document.querySelector('#editor-error'); error.textContent = e.message; error.hidden = false; }
    finally { button.disabled = false; }
  });
  document.querySelector('[data-close]').addEventListener('click', () => editor.close());
  editor.addEventListener('click', event => { if (event.target === editor && (event.clientX < editor.getBoundingClientRect().left || event.clientX > editor.getBoundingClientRect().right)) editor.close(); });
  function taskDialog(source, edit = false) {
    const data = source.dataset;
    reset(edit ? 'Редактировать задачу' : 'Новая задача', 'task', v => change({ ...v, action: 'task.save', id: edit ? data.task : null, date: data.date || null, part: Number(data.part ?? 2) }));
    fill({ text: edit ? source.querySelector('.description').textContent : '', important: data.important, urgent: data.urgent, repeat: data.repeat ?? 0 });
  }
  function taskPreview(task) { const card = document.createElement('div'); card.className = `task-confirm kind-${[...task.classList].find(c => c.startsWith('kind-')).slice(5)}`; card.textContent = task.querySelector('.description').textContent; fields.prepend(card); }
  function scope(task) { if (task.dataset.series === 'true') fields.append(document.querySelector('#scope-fields').content.cloneNode(true)); }
  function moveTask(task, date, part, manual = false) {
    if (manual || task.dataset.series === 'true') {
      reset('Перенести задачу', manual ? 'move' : null, v => change({ action: 'task.move', id: task.dataset.task, date: manual ? v.date || null : date || null, part: manual ? v.part : part, scope: v.scope ?? 0 }), 'Перенести');
      taskPreview(task); scope(task);
      if (manual) fill({ date: task.dataset.date, part: task.dataset.part });
    } else change({ action: 'task.move', id: task.dataset.task, date: date || null, part, scope: 0 }).catch(e => notify(e.message));
  }
  function deleteTask(task) {
    reset('Удалить задачу?', null, v => change({ action: 'task.delete', id: task.dataset.task, scope: v.scope ?? 0 }), 'Удалить');
    taskPreview(task); scope(task);
  }
  function paymentDialog(row = null) {
    reset(row ? 'Редактировать платёж' : 'Новый платёж', 'payment', v => change({ ...v, action: 'payment.save', id: row?.dataset.payment ?? null }));
    fill(row ? { text: row.dataset.name, amount: row.dataset.amount, date: row.dataset.start, end: row.dataset.end, repeat: row.dataset.repeat } : { date: planner.dataset.today, repeat: 0 });
  }
  function categoryDialog(row = null) {
    reset(row ? 'Редактировать категорию' : 'Новая категория', 'category', v => change({ ...v, action: 'category.save', id: row?.dataset.category ?? null }));
    if (row) fill({ text: row.dataset.name, amount: row.dataset.limit });
  }
  document.addEventListener('click', event => {
    const target = event.target.closest('button,[data-week-url]');
    if (!target) { menu.hidden = true; return; }
    if (target.dataset.task) { if (Date.now() >= suppressUntil) change({ action: 'task.toggle', id: target.dataset.task }).catch(e => notify(e.message)); return; }
    if (target.dataset.command) {
      const data = { ...target.dataset, action: target.dataset.command };
      if (data.mood !== undefined) data.mood = Number(data.mood);
      change(data).catch(e => notify(e.message)); return;
    }
    if (target.dataset.open === 'task') taskDialog(target);
    if (target.dataset.open === 'payment') paymentDialog();
    if (target.dataset.open === 'category') categoryDialog();
    if (target.dataset.open === 'habit') reset('Новая привычка', 'habit', v => change({ ...v, action: 'habit.add' }), 'Создать');
    if (target.dataset.finishHabit) {
      reset('Завершить привычку?', null, () => change({ action: 'habit.finish', id: target.dataset.finishHabit }), 'Завершить');
      const text = document.createElement('p'); text.className = 'scope-message'; text.textContent = `«${target.dataset.name}» будет завершена. Вернуть её в активные нельзя; прошлые отметки сохранятся.`; fields.append(text);
    }
    if (target.dataset.editPayment) paymentDialog(target.closest('[data-payment]'));
    if (target.dataset.editCategory) categoryDialog(target.closest('[data-category]'));
    if (target.dataset.context) { if (target.dataset.context === 'edit') taskDialog(contextTask, true); else if (target.dataset.context === 'move') moveTask(contextTask, null, null, true); else deleteTask(contextTask); menu.hidden = true; }
    if (target.dataset.weekUrl && !event.target.closest('button,a')) location.assign(target.dataset.weekUrl);
  });
  function showContext(task, x, y) {
    contextTask = task; menu.hidden = false;
    const zoom = Number(getComputedStyle(document.body).zoom) || 1;
    menu.style.left = `${Math.min(x, innerWidth - 210) / zoom}px`; menu.style.top = `${Math.min(y, innerHeight - 140) / zoom}px`;
    menu.querySelector('button').focus();
  }
  document.addEventListener('contextmenu', event => { const task = event.target.closest('[data-task]'); if (task) { event.preventDefault(); showContext(task, event.clientX, event.clientY); } });
  document.addEventListener('keydown', event => {
    const task = event.target.closest('[data-task]');
    if (task && event.key === 'F2') { event.preventDefault(); taskDialog(task, true); }
    if (task && event.key === 'Delete') { event.preventDefault(); deleteTask(task); }
    if (task && (event.key === 'ContextMenu' || event.shiftKey && event.key === 'F10')) { event.preventDefault(); const r = task.getBoundingClientRect(); showContext(task, r.left, r.bottom); }
    if (event.key === 'Escape') menu.hidden = true;
  });
  const noteButton = document.querySelector('#toggle-notes');
  function toggleNotes(show) { if (!noteButton) return; document.querySelector('#matrix-content').hidden = show; document.querySelector('#notes-content').hidden = !show; noteButton.textContent = show ? 'ЗАМЕТКИ' : 'МАТРИЦА'; }
  toggleNotes(sessionStorage.getItem(`notes-${planner.dataset.screen}`) === 'true');
  noteButton?.addEventListener('click', () => { const show = document.querySelector('#notes-content').hidden; toggleNotes(show); sessionStorage.setItem(`notes-${planner.dataset.screen}`, show); });
  const saves = new Map();
  function queueSave(input) {
    clearTimeout(saves.get(input));
    const data = { action: input.dataset.autosave, key: input.dataset.key, text: input.value };
    pendingWrites = pendingWrites.catch(() => {}).then(() => command(data));
    pendingWrites.then(() => { input.dataset.saved = data.text; }, e => notify(e.message));
  }
  for (const input of document.querySelectorAll('[data-autosave]')) {
    input.dataset.saved = input.value;
    input.addEventListener('input', () => { clearTimeout(saves.get(input)); saves.set(input, setTimeout(() => queueSave(input), 450)); });
    input.addEventListener('blur', () => { if (input.value !== input.dataset.saved) queueSave(input); });
  }
  document.addEventListener('click', event => {
    const link = event.target.closest('a[href]');
    if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
    const dirty = [...document.querySelectorAll('[data-autosave]')].filter(i => i.value !== i.dataset.saved);
    if (dirty.length) { event.preventDefault(); dirty.forEach(queueSave); pendingWrites.then(() => location.assign(link.href)).catch(() => {}); }
  });
  document.querySelector('.date-picker')?.addEventListener('change', event => { const url = new URL(location); url.searchParams.set('date', event.target.value); location.assign(url); });
  document.querySelector('#year-mode')?.addEventListener('change', event => { const url = new URL(location); url.searchParams.set('yearMode', event.target.value); location.assign(url); });
  document.querySelector('#notifications-enabled')?.addEventListener('change', event => command({ action: 'notifications', important: event.target.checked }).catch(e => notify(e.message)));
  const budget = document.querySelector('#budget-data');
  function paintBudget(data) {
    const b = data.budget, categories = data.categories;
    document.querySelector('#mandatory-amount').textContent = money(b.mandatoryPayments);
    document.querySelector('#budget-total').textContent = money(b.total);
    document.querySelector('#budget-remaining').textContent = money(b.remaining);
    document.querySelector('#budget-warning').textContent = b.remaining < 0 ? 'План превышает текущий доход. Уменьшите лимиты или увеличьте доход.' : '';
    const percent = amount => new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(b.income > 0 ? amount / b.income * 100 : 0) + '%';
    for (const row of document.querySelectorAll('[data-category]')) row.querySelector('.category-percent').textContent = percent(Number(row.dataset.limit));
    const slices = [{ name: 'Платежи', color: '#C780E8', limit: b.mandatoryPayments }, ...categories, { name: 'Остаток', color: '#E8E3E0', limit: Math.max(0, b.remaining) }];
    let cursor = 0; const stops = [];
    for (const s of slices) if (s.limit > 0 && b.income > 0 && b.remaining >= 0) { const next = cursor + s.limit / b.income * 360; stops.push(`${s.color} ${cursor}deg ${next}deg`); cursor = next; }
    document.querySelector('#budget-pie').style.background = stops.length ? `conic-gradient(${stops.join(',')})` : '#E8E3E0';
    const legend = document.querySelector('#budget-legend'); legend.replaceChildren();
    for (const s of slices) { const item = document.createElement('span'), dot = document.createElement('i'); dot.className = 'dot filled'; dot.style.setProperty('--color', s.color); item.append(dot, document.createTextNode(s.name + ' · ' + percent(s.limit))); legend.append(item); }
  }
  if (budget) {
    paintBudget({ budget: { income: Number(budget.dataset.income), mandatoryPayments: Number(budget.dataset.mandatory), total: Number(budget.dataset.total), remaining: Number(budget.dataset.remaining) }, categories: [...budget.children].map(c => ({ name: c.dataset.name, color: c.dataset.color, limit: Number(c.dataset.limit) })) });
    const input = document.querySelector('#income'); let saved = input.value;
    const save = async () => { if (input.value === saved) return; const error = document.querySelector('#income-error'); try { const result = await command({ action: 'income', amount: input.value }); saved = input.value; error.hidden = true; paintBudget(result); } catch (e) { error.textContent = e.message; error.hidden = false; } };
    input.addEventListener('blur', save); input.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); save(); } });
  }
  // Measure the actual wrapped cards, donating only empty sections to occupied ones.
  function layoutDays() {
    for (const day of document.querySelectorAll('.day-sections')) {
      const sections = [...day.children]; const height = day.clientHeight; const equal = height / 3;
      const occupied = sections.map(s => !!s.querySelector('.task,.payment'));
      const desired = sections.map((s, i) => occupied[i] ? Math.max(equal, [...s.querySelector('.section-content').children].reduce((n, c) => n + (c.matches('.add-task') ? 0 : c.offsetHeight + 4), 9)) : 28);
      const excess = desired.reduce((sum, d, i) => sum + (occupied[i] ? d - equal : 0), 0);
      const donorCount = occupied.filter(x => !x).length;
      const available = donorCount * Math.max(0, equal - 28);
      sections.forEach((s, i) => { let size = equal; if (excess > 0 && available > 0) size = occupied[i] ? equal + (desired[i] - equal) * Math.min(1, available / excess) : equal - Math.min(excess, available) / donorCount; s.style.flex = `0 0 ${Math.max(28, size)}px`; });
    }
  }
  new ResizeObserver(layoutDays).observe(planner); document.fonts.ready.then(layoutDays); layoutDays();
  // Pointer drag uses the complete card and the initial grab point; never synthesizes user input.
  let drag = null;
  document.addEventListener('dragstart', e => { if (e.target.closest('[data-task]')) e.preventDefault(); });
  document.addEventListener('pointerdown', e => {
    const task = e.target.closest('[data-task]'); if (!task || e.button !== 0 || editor.open) return;
    const rect = task.getBoundingClientRect();
    drag = { task, startX: e.clientX, startY: e.clientY, grabX: e.clientX - rect.left, grabY: e.clientY - rect.top, rect, pointer: e.pointerId, target: null, snapped: false, blocked: null };
    task.setPointerCapture(e.pointerId);
  });
  document.addEventListener('pointermove', e => {
    if (!drag || e.pointerId !== drag.pointer) return;
    if (!drag.ghost && Math.hypot(e.clientX - drag.startX, e.clientY - drag.startY) < 6) return;
    e.preventDefault();
    const zoom = Number(getComputedStyle(document.body).zoom) || 1;
    if (!drag.ghost) { drag.ghost = drag.task.cloneNode(true); drag.ghost.removeAttribute('data-task'); drag.ghost.classList.add('drag-preview'); drag.ghost.style.width = `${drag.rect.width / zoom}px`; drag.ghost.style.height = `${drag.rect.height / zoom}px`; document.body.append(drag.ghost); drag.task.classList.add('dragging'); menu.hidden = true; }
    let target = document.elementFromPoint(e.clientX, e.clientY)?.closest('[data-drop]');
    const content = target?.querySelector('.section-content,.backlog-tasks,.month-day-tasks') ?? target;
    const r = content?.getBoundingClientRect();
    const last = content ? [...content.querySelectorAll('.task,.payment')].filter(c => c !== drag.task).at(-1) : null;
    const anchorY = last ? last.getBoundingClientRect().bottom + 4 : r?.top;
    if (drag.snapped && (target !== drag.target || Math.abs(e.clientX - drag.snapX) > 60 || Math.abs(e.clientY - drag.anchorY) > 20)) { drag.blocked = drag.target; drag.snapped = false; }
    if (drag.blocked && (target !== drag.blocked || Math.abs(e.clientY - anchorY) > 20)) drag.blocked = null;
    if (target && target !== drag.blocked && r && Math.abs(e.clientY - anchorY) <= 20 && Math.abs(e.clientX - (r.left + r.width / 2)) <= r.width / 2) { if (!drag.snapped) drag.snapX = e.clientX; drag.snapped = true; drag.anchorY = anchorY; }
    if (drag.target !== target) { drag.target?.classList.remove('drop-target'); target?.classList.add('drop-target'); }
    drag.target = target;
    drag.ghost.style.left = `${(drag.snapped && r ? r.left : e.clientX - drag.grabX) / zoom}px`; drag.ghost.style.top = `${(drag.snapped ? drag.anchorY : e.clientY - drag.grabY) / zoom}px`;
  }, { passive: false });
  function endDrag(e, cancel = false) {
    if (!drag) return; const current = drag; drag = null;
    current.task.classList.remove('dragging'); current.ghost?.remove(); current.target?.classList.remove('drop-target');
    if (!current.ghost) return;
    suppressUntil = Date.now() + 250;
    if (!cancel && current.target) {
      const date = current.target.dataset.date || null;
      const part = current.target.dataset.part === undefined ? (current.task.dataset.date ? Number(current.task.dataset.part) : 2) : Number(current.target.dataset.part);
      if ((current.task.dataset.date || null) !== date || Number(current.task.dataset.part) !== part) moveTask(current.task, date, part);
    }
  }
  document.addEventListener('pointerup', endDrag); document.addEventListener('pointercancel', e => endDrag(e, true));
  document.addEventListener('keydown', e => { if (e.key === 'Escape') endDrag(e, true); });
  window.addEventListener('blur', e => endDrag(e, true));
  async function pulse() {
    if (document.hidden || editor.open || drag || document.activeElement?.matches('input,textarea')) return;
    try {
      const r = await fetch('/Planner/Pulse', { method: 'POST', headers: { 'X-CSRF-TOKEN': token } });
      if (!r.ok || r.redirected) return; const data = await r.json();
      if (data.today !== planner.dataset.today) { if (planner.dataset.screen === 'day') location.assign('/?screen=day'); else location.reload(); return; }
      if (data.messages.length) { const status = document.querySelector('#status'); status.replaceChildren(); status.hidden = false;
        for (const m of data.messages) { const link = document.createElement('a'); link.href = `/?screen=${m.screen}`; link.textContent = m.body; status.append(link); }
        const close = document.createElement('button'); close.textContent = '×'; close.setAttribute('aria-label', 'Закрыть уведомление'); close.onclick = () => { status.hidden = true; }; status.append(close);
      }
    } catch { /* Next tick retries after a network interruption. */ }
  }
  setInterval(pulse, 60000); pulse();
})();
