let session = null;
let view = 'users';
const $ = id => document.getElementById(id);
const message = text => { $('feedback').textContent = text; };
async function request(path, method = 'GET', body, retry = true) {
  const response = await fetch('/api/v1/' + path, { method, credentials: 'omit', headers: { 'Content-Type': 'application/json', ...(session ? { Authorization: 'Bearer ' + session.accessToken } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
  if (response.status === 401 && session && retry) {
    const refresh = await fetch('/api/v1/auth/refresh', { method: 'POST', credentials: 'omit', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: session.refreshToken }) });
    if (refresh.ok) { session = await refresh.json(); return request(path, method, body, false); }
    clearSession();
  }
  if (!response.ok) { const error = await response.json().catch(() => ({})); throw new Error(error.message || (response.status === 401 ? '登录失败，请检查管理账号和密码。' : '请求失败：' + response.status)); }
  return response.status === 204 ? null : response.json();
}
function clearSession() { session = null; $('login').hidden = false; $('console').hidden = true; $('logout').hidden = true; $('rows').replaceChildren(); }
function cell(row, value) { const td = document.createElement('td'); td.textContent = value ?? ''; row.append(td); return td; }
function action(parent, label, run) {
  const button = document.createElement('button'); button.textContent = label;
  button.onclick = async () => { button.disabled = true; try { await run(); await load(); message('操作已完成。'); } catch (e) { message(e.message); } finally { button.disabled = false; } };
  parent.append(button);
}
async function load() {
  const data = await request('admin/' + view);
  $('release-panel').hidden = view !== 'modules';
  const headings = view === 'users' ? ['用户名', '状态', '操作'] : view === 'modules' ? ['模块', '版本', '状态', '操作'] : ['时间', '操作者', '动作', '对象'];
  const hr = document.createElement('tr'); headings.forEach(t => { const th = document.createElement('th'); th.textContent = t; hr.append(th); }); $('head').replaceChildren(hr);
  $('rows').replaceChildren();
  for (const item of data) {
    const row = document.createElement('tr');
    if (view === 'users') {
      cell(row, item.username); cell(row, item.disabled ? '已停用' : '正常'); const td = cell(row, '');
      action(td, item.disabled ? '启用' : '停用', async () => { if (confirm('确认修改这个账号的状态？现有会话会失效。')) await request('admin/users/' + encodeURIComponent(item.id) + '/state', 'POST', { disabled: !item.disabled }); });
      action(td, '撤销会话', () => request('admin/users/' + encodeURIComponent(item.id) + '/revoke-sessions', 'POST'));
    } else if (view === 'modules') {
      cell(row, item.name); cell(row, item.version); cell(row, ({development:'开发中',planned:'规划中',available:'已发布',disabled:'停用'})[item.status] || item.status); const td = cell(row, '');
      action(td, item.status === 'disabled' ? '恢复为开发中' : '停用', () => request('admin/modules/' + encodeURIComponent(item.id) + '/state', 'POST', { status: item.status === 'disabled' ? 'development' : 'disabled' }));
    } else { cell(row, new Date(item.createdAt).toLocaleString()); cell(row, item.actorId); cell(row, item.action); cell(row, item.targetId); }
    $('rows').append(row);
  }
}
$('login-form').onsubmit = async event => {
  event.preventDefault(); const form = event.currentTarget; const button = form.querySelector('button'); button.disabled = true;
  try { const data = new FormData(form); session = await request('auth/admin-login', 'POST', { username: data.get('username'), password: data.get('password') }); form.reset(); $('login').hidden = true; $('console').hidden = false; $('logout').hidden = false; message(''); await load(); }
  catch (e) { clearSession(); message(e.message); } finally { button.disabled = false; }
};
$('logout').onclick = async () => { try { await request('auth/logout', 'POST'); } catch {} finally { clearSession(); message('已退出。'); } };
document.querySelectorAll('[data-view]').forEach(button => { button.onclick = async () => { view = button.dataset.view; document.querySelectorAll('[data-view]').forEach(b => b.classList.toggle('active', b === button)); try { await load(); message(''); } catch (e) { message(e.message); } }; });
$('refresh').onclick = async () => { try { await load(); message('已刷新。'); } catch (e) { message(e.message); } };
$('release-form').onsubmit = async event => {
  event.preventDefault(); const form = event.currentTarget; const button = form.querySelector('button'); button.disabled = true;
  try {
    const input = Object.fromEntries(new FormData(form));
    await request('admin/modules', 'POST', { ...input, icon: '□', status: 'available', runtime: 'content', offline: true, features: ['离线可用'], capabilities: [{ id: 'open', policy: 'free' }], protocolVersion: 1 });
    await load(); message('模块发布信息已保存，客户端刷新目录后可安装。');
  } catch (e) { message(e.message); } finally { button.disabled = false; }
};
