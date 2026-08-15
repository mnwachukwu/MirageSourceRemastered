// Check that the seed counts quoted in the README match what is actually in data/.
//
//     node tools/check-seed-counts.mjs
//
// Node rather than C#, for the same reason as check-doc-links.mjs beside it: every GitHub-hosted
// runner ships Node already, so this costs nothing, while a .NET file-based program would need a
// setup-dotnet step to check one sentence. No dependencies, no package.json.
//
// WHY THIS EXISTS. The README's seed-data callout is the only place the size of the shipped world is
// written down in prose, and the site is written FROM this README by its own stated rule — so a wrong
// number here publishes in two places. It has gone stale twice: once when the armory grew to 558 items
// and the bestiary to 124, and again when the friendly NPCs took that to 174 and added conversations,
// quests and shops that the sentence did not mention at all.
//
// Nothing else would catch it. The counts are prose; no test reads them, nothing compiles them, and
// the person who adds 50 NPCs is the least likely to reread a paragraph in the getting-started
// section. That is exactly the shape of rot check-doc-links.mjs was written for, applied to numbers.
//
// AN EMPTY data/ IS A PASS, not a failure — the same rule SeedIntegrityTests uses. data/ is the
// shipped default configuration and is deliberately allowed to be empty; a fresh clone that has not
// populated it must not go red, or this check is the first thing anybody deletes.

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const dataDir = join(repoRoot, 'server', 'src', 'Mirage.Server.Host', 'data');
const readmePath = join(repoRoot, 'README.md');

// The words the README uses -> the folder they describe. A count is only checked when the README
// actually mentions it, so adding a collection to data/ never fails this on its own.
const COLLECTIONS = {
  classes: 'classes',
  items: 'items',
  spells: 'spells',
  npcs: 'npcs',
  conversations: 'conversations',
  quests: 'quests',
  shops: 'shops',
};

const readme = readFileSync(readmePath, 'utf8');

// The seed-data callout, found by its bolded label rather than by line number.
const callout = readme.split('\n').find(l => l.includes('**Seed data:**'));
if (!callout) {
  console.error('FAILED: no "**Seed data:**" line in README.md — this check is anchored to it.');
  process.exit(1);
}

if (!existsSync(dataDir)) {
  console.log(`No data/ at ${dataDir} — nothing to check.`);
  process.exit(0);
}

const count = folder => {
  const dir = join(dataDir, folder);
  if (!existsSync(dir)) return 0;
  return readdirSync(dir).filter(f => f.endsWith('.json')).length;
};

const total = Object.values(COLLECTIONS).reduce((n, f) => n + count(f), 0);
if (total === 0) {
  console.log('data/ is empty — the shipped default configuration is allowed to be, so nothing to check.');
  process.exit(0);
}

// "558 items", "10 classes", and also the spelled-out forms the README has used before ("ten classes").
const WORDS = { one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10 };

const problems = [];
for (const [word, folder] of Object.entries(COLLECTIONS)) {
  const match = callout.match(new RegExp(`([\\d,]+|[A-Za-z]+)\\s+${word}\\b`, 'i'));
  if (!match) continue;   // the README does not quote this one

  const raw = match[1];
  const quoted = /^[\d,]+$/.test(raw) ? Number(raw.replace(/,/g, '')) : WORDS[raw.toLowerCase()];
  if (quoted === undefined) continue;   // an adjective, not a number ("partial data/ folder")

  const actual = count(folder);
  if (quoted !== actual) problems.push(`  README says ${raw} ${word}; data/${folder} holds ${actual}`);
}

if (problems.length > 0) {
  console.error('FAILED — the README\'s seed counts disagree with data/:\n');
  console.error(problems.join('\n'));
  console.error('\nUpdate the "**Seed data:**" line in README.md. The site quotes these numbers from it.');
  process.exit(1);
}

console.log(`Seed counts match the README (${total} records across ${Object.keys(COLLECTIONS).length} collections).`);
