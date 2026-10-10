import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';
import ts from 'typescript';

async function dashboard(logout) {
  const events = [];
  const state = [];
  let stateIndex = 0;
  class ApiError extends Error { constructor(status) { super('Request failed'); this.status = status; } }
  const jsx = (type, props) => ({ type, props });
  const context = vm.createContext({ console });
  const dependencies = {
    react: {
      useState: (initial) => {
        const index = stateIndex++;
        if (!(index in state)) state[index] = initial;
        return [state[index], (value) => { state[index] = value; }];
      },
      useRef: () => ({ current: null }),
      useEffect: () => {},
    },
    'react/jsx-runtime': { jsx, jsxs: jsx },
    'react-router-dom': {
      Link: 'a', NavLink: 'a', useLocation: () => ({ pathname: '/doctor' }),
      useNavigate: () => (path) => events.push(['navigate', path]),
    },
    '../auth/useAuth': { useAuth: () => ({
      user: { fullName: 'Test Doctor', role: 'Doctor', roomNumber: 'R-1' },
      signOut: () => events.push(['clear-session']),
    }) },
    '../auth/roleRoutes': { roleRoutes: { Doctor: '/doctor', Receptionist: '/reception', Admin: '/admin' } },
    '../api/auth': { logout: () => logout(ApiError) },
    '../api/client': { ApiError },
    '../components/ui/BackLink': { BackLink: 'a' },
    '../components/ui/Icon': { Icon: 'svg' },
    '../assets/swiftcare-logo.png': { default: 'logo.png' },
  };
  const source = await readFile(new URL('../src/dashboards/DashboardShell.tsx', import.meta.url), 'utf8');
  const output = ts.transpileModule(source, { compilerOptions: {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext, jsx: ts.JsxEmit.ReactJSX,
  } }).outputText;
  const module = new vm.SourceTextModule(output, { context });
  await module.link((specifier) => {
    const exports = dependencies[specifier];
    assert.ok(exports, `Unexpected import: ${specifier}`);
    return new vm.SyntheticModule(Object.keys(exports), function () {
      for (const [name, value] of Object.entries(exports)) this.setExport(name, value);
    }, { context });
  });
  await module.evaluate();
  function render() {
    stateIndex = 0;
    return module.namespace.DashboardShell({ sectionLabel: 'Dashboard' });
  }
  function find(node, predicate) {
    if (!node || typeof node !== 'object') return null;
    if (predicate(node)) return node;
    const children = Array.isArray(node) ? node : Object.values(node);
    for (const child of children) {
      const result = find(child, predicate);
      if (result) return result;
    }
    return null;
  }
  return { events, render, find };
}

test('sign out retains the token until durable server confirmation', async () => {
  let resolve;
  const pending = new Promise((completed) => { resolve = completed; });
  const app = await dashboard(() => pending);
  const button = app.find(app.render(), (node) => node.type === 'button' && node.props.disabled === false);
  assert.ok(button);
  const action = button.props.onClick();
  assert.deepEqual(app.events, []);
  assert.equal(app.find(app.render(), (node) => node.type === 'button' && node.props.disabled === true).props.disabled, true);
  resolve();
  await action;
  assert.deepEqual(app.events, [['clear-session'], ['navigate', '/login']]);
});

test('an unavailable server preserves the session and offers retry', async () => {
  const app = await dashboard((ApiError) => Promise.reject(new ApiError(503)));
  const button = app.find(app.render(), (node) => node.type === 'button' && node.props.disabled === false);
  await button.props.onClick();
  assert.deepEqual(app.events, []);
  const alert = app.find(app.render(), (node) => node.props?.role === 'alert');
  assert.match(alert.props.children, /could not be confirmed/);
  assert.ok(app.find(app.render(), (node) => node.type === 'button' && node.props.disabled === false));
});

test('a token already rejected by the server can be cleared locally', async () => {
  const app = await dashboard((ApiError) => Promise.reject(new ApiError(401)));
  await app.find(app.render(), (node) => node.type === 'button' && node.props.disabled === false).props.onClick();
  assert.deepEqual(app.events, [['clear-session'], ['navigate', '/login']]);
});
