const puppeteer = require('C:\\GSDDashboard\\node_modules\\puppeteer');
const fs = require('node:fs');
const base = process.env.PULSE_VERIFY_BASE || 'https://d2jn94qg-5000.euw.devtunnels.ms';
const receipt = process.env.PULSE_VERIFY_RECEIPT || __dirname + '\\pulse-browser-acceptance.json';
const evidence = { base, startedUtc: new Date().toISOString(), manualActionsSent: 0 };
function save() { fs.writeFileSync(receipt, JSON.stringify(evidence, null, 2)); }

(async () => {
  const browser = await puppeteer.launch({ headless: true });
  try {
    const page = await browser.newPage();
    await page.setExtraHTTPHeaders({ 'X-Tunnel-Skip-AntiPhishing-Page': '1' });
    const errors = [];
    const forbidden = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => {
      if (request.url().includes('ssr7tm2l-8000') ||
          Object.keys(request.headers()).some(key => key.toLowerCase() === 'x-api-key')) {
        forbidden.push('Direct kiosk request or legacy header');
      }
      if (/\/api\/kiosk\/manual-(checkin|logout)/.test(request.url())) {
        forbidden.push('Unexpected manual submit');
      }
    });
    await page.goto(base + '/login', { waitUntil: 'networkidle2', timeout: 60000 });
    const anonymous = await page.evaluate(async () => {
      const results = {};
      for (const route of ['attendance', 'agents']) {
        results[route] = (await fetch('/api/kiosk/' + route)).status;
      }
      return results;
    });
    evidence.anonymous = anonymous;
    save();
    if (anonymous.attendance !== 401 || anonymous.agents !== 401) throw new Error('Anonymous proxy check failed');
    const login = await page.evaluate(async (kid) => {
      const response = await fetch('/api/auth/login', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ kid }),
      });
      const body = await response.json();
      return { status: response.status, role: body.role || null };
    }, process.env.PULSE_VERIFY_KID || 'S69307');
    evidence.login = login;
    evidence.cookies = (await page.cookies()).filter(cookie =>
      cookie.name === 'gsd_auth' || cookie.name === 'pulse_stage_auth')
      .map(cookie => ({ name: cookie.name, domain: cookie.domain, path: cookie.path,
        secure: cookie.secure, httpOnly: cookie.httpOnly, sameSite: cookie.sameSite }));
    save();
    if (login.status !== 200) throw new Error('Approved operator sign-in failed: ' + login.status);
    await page.goto(base + '/wic-attendance', { waitUntil: 'networkidle2', timeout: 60000 });
    const feeds = {};
    evidence.feeds = feeds;
    for (const route of ['attendance', 'agents']) {
      feeds[route] = await page.evaluate(async (route) => {
        const response = await fetch('/api/kiosk/' + route);
        const text = await response.text();
        let body;
        try { body = JSON.parse(text); } catch {}
        return {
          status: response.status, contentType: response.headers.get('content-type'),
          shape: body === undefined ? 'non-json' : Array.isArray(body) ? 'array' : body === null ? 'null' : typeof body,
          array: Array.isArray(body), rows: Array.isArray(body) ? body.length : null,
          errorKind: body && !Array.isArray(body) ? Object.keys(body).filter(k => ['error','detail'].includes(k)) : [],
          errorCategory: body && typeof body.error === 'string' ?
            (/Authentication required/.test(body.error) ? 'authentication-required' :
              /^Forbidden$/.test(body.error) ? 'forbidden' :
              /not configured/.test(body.error) ? 'not-configured' :
              /timed out/.test(body.error) ? 'upstream-timeout' :
              /unavailable/.test(body.error) ? 'upstream-unavailable' :
              /Invalid kiosk/.test(body.error) ? 'invalid-upstream-response' : 'other') : null,
        };
      }, route);
      save();
      console.log('Proxy check ' + route + ':', JSON.stringify(feeds[route]));
    }
    evidence.session = await page.evaluate(async () => {
      const response = await fetch('/api/auth/me');
      const body = await response.json();
      return { status: response.status, role: body.role || null };
    });
    save();
    console.log('Individual proxy checks:', JSON.stringify(feeds));
    for (const value of Object.values(feeds)) {
      if (value.status !== 200 || !value.array) throw new Error('Signed-in proxy feed failed');
    }
    const body = await page.$eval('body', element => element.innerText);
    if (/Overview failed to load|A rendering error occurred|Invalid kiosk agent list response/.test(body)) {
      throw new Error('WIC rendering failed');
    }
    const buttons = await page.$$('button');
    let opened = false;
    for (const button of buttons) {
      const text = await button.evaluate(element => element.innerText.trim());
      if (/^(Manual Check-in|Manual check-in|Manueller Check-in|Manuelle Anmeldung|Manuell anmelden)$/i.test(text)) {
        await button.click();
        opened = true;
        break;
      }
    }
    if (!opened) {
      const labels = await page.$$eval('button', elements => elements.map(e => e.innerText.trim()).filter(Boolean));
      console.log('Button labels for diagnosis:', JSON.stringify(labels));
      throw new Error('Manual modal opener not located');
    }
    await page.waitForFunction(() => [...document.querySelectorAll('select')].some(select =>
      [...select.options].filter(option => /^\d+$/.test(option.value)).length > 0), { timeout: 30000 });
    const options = await page.$$eval('select', selects => Math.max(0, ...selects.map(select =>
      [...select.options].filter(option => /^\d+$/.test(option.value)).length)));
    if (options !== feeds.agents.rows) throw new Error('Manual agent option count differs from feed');
    const modalText = await page.$eval('body', element => element.innerText);
    if (modalText.includes('Invalid kiosk agent list response')) throw new Error('Modal validator rejected feed');
    if (errors.length || forbidden.length) throw new Error('Browser error or forbidden request');
    Object.assign(evidence, { result: 'PASSED', manualAgentOptions: options,
      renderErrors: errors.length, directKioskRequests: forbidden.length });
    save();
    console.log(JSON.stringify({ tunnel: 'PASSED', anonymous, feeds, manualAgentOptions: options,
      renderErrors: errors.length, directKioskRequests: forbidden.length, manualActionsSent: 0 }));
    await page.evaluate(() => fetch('/api/auth/logout', { method: 'POST' }));
  } finally {
    await browser.close();
  }
})().catch(error => {
  evidence.result = 'FAILED';
  evidence.failure = error.message;
  save();
  console.error('Acceptance failed:', error.message);
  process.exitCode = 1;
});
