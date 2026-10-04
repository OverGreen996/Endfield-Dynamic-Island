import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
export const root=fileURLToPath(new URL('../',import.meta.url));
export const loadPolicy=()=>JSON.parse(readFileSync(resolve(root,'policy.json'),'utf8'));
export const dbPath=resolve(root,'data','usage.sqlite');
