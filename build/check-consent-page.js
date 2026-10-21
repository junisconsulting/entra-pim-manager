// Checks site/consent/consent.js against the redirects Entra sends after the admin-consent link:
// which block shows, and that nothing but GUIDs and AADSTS numbers from the URL reaches the page.
// Runs in CI and before every Pages deployment; locally: node build/check-consent-page.js
const fs = require('fs');
const vm = require('vm');
const assert = require('assert');

const src = fs.readFileSync(process.argv[2] || 'site/consent/consent.js', 'utf8');
const html = fs.readFileSync('site/consent/index.html', 'utf8');
const ids = ['granted', 'tenant-line', 'tenant', 'failed', 'cancelled', 'refused', 'code-line', 'code', 'none'];

// consent.js throws on an id the page lacks, and nothing after it shows.
ids.forEach(i => assert.ok(html.includes(`id="${i}"`), `index.html has no id="${i}"`));

function run(search) {
  const els = {};
  ids.forEach(i => (els[i] = { hidden: true, textContent: '' }));
  vm.runInNewContext(src, { window: { location: { search } }, document: { getElementById: i => els[i] }, URLSearchParams });
  const visible = ids.filter(i => !els[i].hidden && !['tenant', 'code'].includes(i));
  return { visible, els };
}

const tenant = '00000000-0000-4000-8000-000000000000';
let r = run(`?admin_consent=True&tenant=${tenant}`);
assert.deepStrictEqual(r.visible, ['granted', 'tenant-line']);
assert.strictEqual(r.els.tenant.textContent, tenant);

// Only a GUID is shown as the tenant.
r = run('?admin_consent=True&tenant=<b>evil</b>');
assert.deepStrictEqual(r.visible, ['granted']);
assert.strictEqual(r.els.tenant.textContent, '');

// Cancel: Entra still sends admin_consent=True next to the error.
const declined = encodeURIComponent('AADSTS65004: User declined to consent to access the app.\r\nTrace ID: 0b1c2d3e-0000-4000-8000-000000000001\r\nCorrelation ID: 0b1c2d3e-0000-4000-8000-000000000002\r\nTimestamp: 2026-10-17');
r = run(`?error=access_denied&error_description=${declined}`);
assert.deepStrictEqual(r.visible, ['failed', 'cancelled']);
r = run(`?admin_consent=True&error=consent_required&error_description=${declined}`);
assert.deepStrictEqual(r.visible, ['failed', 'cancelled']);

r = run(`?error=access_denied&error_description=${encodeURIComponent('AADSTS90094: The grant requires admin permission.')}`);
assert.deepStrictEqual(r.visible, ['failed', 'refused', 'code-line']);
assert.strictEqual(r.els.code.textContent, 'AADSTS90094');

// Free text from a crafted link never reaches the page.
r = run(`?error=x&error_description=${encodeURIComponent('Call support at +49 123 now. Trace ID: not-a-guid-at-all-not-a-guid-at-all-xx')}`);
assert.deepStrictEqual(r.visible, ['failed', 'refused']);
assert.ok(ids.every(i => !r.els[i].textContent.includes('Call support')));

r = run('');
assert.deepStrictEqual(r.visible, ['none']);

console.log('consent.js: all checks passed');
