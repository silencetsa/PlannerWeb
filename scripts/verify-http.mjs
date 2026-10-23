// Runs only against a disposable local instance. With no URL, launches the published application itself.
import { spawn } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
let processHandle, qaDirectory, log = '', checks = 0;
const base = process.argv[2] || 'http://127.0.0.1:5089';
assert.match(base, /^http:\/\/127\.0\.0\.1:\d+$/);
const check = (value, name) => { assert.ok(value, name); checks++; console.log(`PASS ${name}`); };
class Client {
  cookies = new Map(); token = '';
  async request(url, options = {}) {
    const r = await fetch(base + url, { ...options, redirect: 'manual', headers: { Cookie: [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; '), ...options.headers } });
    for (const cookie of r.headers.getSetCookie()) { const pair = cookie.split(';')[0], index = pair.indexOf('='); this.cookies.set(pair.slice(0, index), pair.slice(index + 1)); }
    return r;
  }
  async page(url = '/') { const r = await this.request(url); const html = await r.text(); const token = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/); if (token) this.token = token[1]; return { r, html }; }
  post(url, values) { return this.request(url, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams({ ...values, __RequestVerificationToken: this.token }) }); }
  command(value, csrf = true) { return this.request('/Planner/Command', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(csrf ? { 'X-CSRF-TOKEN': this.token } : {}) }, body: JSON.stringify(value) }); }
}
try {
  if (!process.argv[2]) {
    qaDirectory = await mkdtemp(path.join(tmpdir(), 'PlannerWeb-http-'));
    const publish = path.join(root, 'artifacts', 'publish');
    processHandle = spawn('dotnet', [path.join(publish, 'PlannerWeb.dll')], { cwd: publish, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: base, Planner__DataDirectory: qaDirectory }, windowsHide: true });
    processHandle.stdout.on('data', b => { log += b; }); processHandle.stderr.on('data', b => { log += b; });
    for (let i = 0; i < 80; i++) { if (await fetch(base + '/health').then(r => r.ok).catch(() => false)) break; await new Promise(r => setTimeout(r, 200)); }
  }
  const a = new Client(), b = new Client(), anonymous = new Client();
  check((await anonymous.request('/')).status === 302, 'anonymous planner access redirects to login');
  check((await anonymous.command({ action: 'income', amount: '1' }, false)).status !== 200, 'anonymous mutations rejected');
  const suffix = Date.now().toString();
  for (const [client, nick] of [[a, `Alice${suffix}`], [b, `Bob${suffix}`]]) {
    await client.page('/Account/Register');
    const r = await client.post('/Account/Register', { Nickname: nick, Email: nick + '@example.test', Password: 'HTTP-test-only-123', ConfirmPassword: 'HTTP-test-only-123' });
    check(r.status === 302 && client.cookies.has('PlannerWeb.Auth'), 'registration creates authenticated session');
  }
  let { html } = await a.page('/'); await b.page('/');
  check((await a.command({ action: 'task.save', text: 'Private <script>alert(1)</script>', important: true, urgent: true, repeat: 1, date: '2026-10-24', part: 0 })).status === 200, 'MVC creates recurring task');
  ({ html } = await a.page('/?date=2026-10-24'));
  const id = html.match(/data-task="([^"]+)"/)[1];
  check(html.includes('&lt;script&gt;') && !html.includes('<script>alert(1)</script>'), 'task content HTML encoded');
  check(!(await b.page('/?date=2026-10-24')).html.includes(id), 'other user cannot read private task');
  check((await b.command({ action: 'task.toggle', id })).status === 400, 'foreign task mutation rejected');
  check((await a.command({ action: 'task.toggle', id }, false)).status === 400, 'missing antiforgery token rejected');
  check((await a.command({ action: 'task.toggle', id })).status === 200, 'owner can complete own task');
  check((await a.command({ action: 'task.save', text: 'bad enum', repeat: 123 })).status === 400, 'invalid repeat rejected by MVC');
  check((await a.command({ action: 'income', amount: '10000,25' })).status === 200, 'decimal comma accepted');
  check((await a.command({ action: 'category.save', text: 'Food', amount: '3000' })).status === 200, 'budget category saved');
  const excess = await a.command({ action: 'category.save', text: 'Too much', amount: '9000' });
  check(excess.status === 400 && (await excess.json()).error === 'не хватает бюджета, уменьши сумму', 'exact budget validation message');
  check((await a.command({ action: 'income', amount: '1000.123' })).status === 400, 'fractional cents rejected');
  for (let i = 0; i < 3; i++) check((await a.command({ action: 'habit.add', text: 'Habit ' + i, color: '#66E37E' })).status === 200, 'habit creation');
  check((await a.command({ action: 'habit.add', text: 'Habit 4', color: '#66E37E' })).status === 400, 'habit limit enforced');
  check((await a.command({ action: 'payment.save', text: 'Internet', amount: '800.50', date: '2026-10-24', repeat: 3 })).status === 200, 'monthly payment created');
  check((await a.command({ action: 'goal', key: 'week:2026-10-19', text: 'Private goal' })).status === 200, 'goal saved');
  check((await a.command({ action: 'note', key: 'month:2026-10', text: 'Private note' })).status === 200, 'note saved');
  for (const screen of ['week', 'month', 'year', 'day', 'habits', 'payments', 'finances']) {
    const page = await a.page('/?screen=' + screen + '&date=2026-10-24');
    check(page.r.status === 200 && page.html.includes(`screen-${screen}`), 'rendered MVC screen ' + screen);
    const other = await b.page('/?screen=' + screen + '&date=2026-10-24');
    check(!other.html.includes('Private goal') && !other.html.includes('Private note') && !other.html.includes('Internet') && !other.html.includes('Food'), 'private data absent in ' + screen);
  }
  check((await a.page('/?date=not-a-date')).r.status === 400, 'invalid date rejected');
  const before = await a.page('/');
  check(before.r.headers.get('cache-control').includes('no-store') && before.r.headers.get('content-security-policy').includes("frame-ancestors 'none'"), 'private response security headers');
  check((await a.post('/Account/Logout', {})).status === 302 && (await a.request('/')).status === 302, 'logout ends session');
  await a.page('/Account/Login');
  const login = await a.post('/Account/Login', { Login: `ALICE${suffix}@EXAMPLE.TEST`, Password: 'HTTP-test-only-123', ReturnUrl: 'https://example.org' });
  check(login.status === 302 && !login.headers.get('location').includes('example.org'), 'email login rejects external return URL');
  check((await a.page('/?screen=finances')).html.includes('Food'), 'data retained after logout and login');
  console.log(`${checks} HTTP checks passed`);
} catch (e) { console.error(log); throw e; }
finally {
  if (processHandle) { const exited = new Promise(r => processHandle.once('exit', r)); processHandle.kill(); await exited; }
  if (qaDirectory && path.basename(qaDirectory).startsWith('PlannerWeb-http-') && path.dirname(qaDirectory) === path.resolve(tmpdir())) await rm(qaDirectory, { recursive: true, force: true });
}
