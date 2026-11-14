import { mkdir, copyFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const root = new URL('../', import.meta.url);
await mkdir(new URL('wwwroot/vendor/', root), { recursive: true });
for (const [source, name] of [
  ['@xterm/xterm/lib/xterm.js', 'xterm.js'],
  ['@xterm/xterm/css/xterm.css', 'xterm.css'],
  ['@xterm/xterm/LICENSE', 'xterm-LICENSE'],
  ['@xterm/addon-fit/lib/addon-fit.js', 'addon-fit.js'],
  ['@xterm/addon-fit/LICENSE', 'addon-fit-LICENSE'],
]) await copyFile(fileURLToPath(new URL(`node_modules/${source}`, root)), fileURLToPath(new URL(`wwwroot/vendor/${name}`, root)));
console.log('Copied pinned xterm.js assets and licenses for offline, same-origin serving.');
