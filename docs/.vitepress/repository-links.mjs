import { execFileSync } from 'node:child_process';
import { existsSync, statSync } from 'node:fs';
import { resolve, relative, sep } from 'node:path';

// Keep repository-relative links useful on both GitHub and the published site. Validate the
// referenced path on disk before converting it: dead links should still fail the docs build.
export function installRepositoryLinks(md) {
  const root = execFileSync('git', ['rev-parse', '--show-toplevel'], {encoding:'utf8'}).trim();
  const commit = execFileSync('git', ['rev-parse', 'HEAD'], {encoding:'utf8'}).trim();
  md.core.ruler.after('inline', 'royalterminal-source-links', state => {
    for (const block of state.tokens) for (const token of block.children ?? []) {
      if (token.type !== 'link_open') continue;
      const raw = token.attrGet('href') ?? '';
      const match = /^\.\.\/\.\.\/((?:src|samples|scripts|tests|native|\.github)\/[^?#]*)([?#].*)?$/.exec(raw);
      if (!match) continue;
      const file = resolve(root, decodeURIComponent(match[1]));
      const path = relative(root, file);
      if (path.startsWith('..' + sep) || !existsSync(file)) throw new Error(`Missing repository link: ${raw}`);
      const kind = statSync(file).isDirectory() ? 'tree' : 'blob';
      const escaped = path.split(sep).map(encodeURIComponent).join('/');
      token.attrSet('href', `https://github.com/royalapplications/RoyalTerminal/${kind}/${commit}/${escaped}${match[2] ?? ''}`);
    }
  });
}
