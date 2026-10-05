const puppeteer = require("C:\\GSDDashboard\\node_modules\\puppeteer");
const fs = require("node:fs");
const assert = require("node:assert/strict");
const base = "https://d2jn94qg-5000.euw.devtunnels.ms";
const receipt = process.env.PULSE_STATIC_RECEIPT;
const evidence = { startedUtc: new Date().toISOString(), writesSent: 0, simulatedWrites: [], result: "RUNNING" };
function save() { fs.writeFileSync(receipt, JSON.stringify(evidence, null, 2)); }

(async () => {
  const browser = await puppeteer.launch({ headless: true });
  try {
    const page = await browser.newPage();
    await page.setExtraHTTPHeaders({ "X-Tunnel-Skip-AntiPhishing-Page": "true" });
    await page.setViewport({ width: 1280, height: 720 });
    const errors = [];
    page.on("pageerror", () => errors.push("render-error"));
    page.on("response", response => {
      if (new URL(response.url()).pathname === "/api/agents") {
        evidence.agentFeedStatus = response.status(); save();
      }
    });
    let rows = [];
    await page.setRequestInterception(true);
    page.on("request", async request => {
      const url = new URL(request.url());
      const method = request.method();
      if (method !== "GET" && method !== "HEAD" && url.pathname !== "/api/auth/login" && url.pathname !== "/api/auth/logout" &&
          url.pathname !== "/api/shifts/validate") {
        if (method === "PATCH" && /^\/api\/shifts\/\d+$/.test(url.pathname)) {
          const body = JSON.parse(request.postData());
          const row = rows.find(row => String(row.id) === url.pathname.split("/").pop());
          evidence.simulatedWrites.push({ status: body.shiftType, timed: !!body.shiftStart && !!body.shiftEnd });
          save();
          await request.respond({ status: 200, contentType: "application/json", body: JSON.stringify({ ...row, ...body }) });
        } else { await request.abort(); }
        return;
      }
      await request.continue();
    });
    await page.goto(base + "/login", { waitUntil: "networkidle2" });
    evidence.login = await page.evaluate(async () => {
      const r = await fetch("/api/auth/login", {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ kid: "S60028" }),
      });
      const b = await r.json(); return { status: r.status, role: b.role };
    });
    save();
    assert.deepEqual(evidence.login, { status: 200, role: "RTM" });
    evidence.cookies = (await page.cookies()).map(cookie => ({ name: cookie.name, secure: cookie.secure, domain: cookie.domain }));
    const clickText = async (selector, text) => {
      const handle = await page.evaluateHandle((selector, text) =>
        [...document.querySelectorAll(selector)].find(element => element.textContent.trim() === text ||
          element.firstChild?.textContent.trim() === text), selector, text);
      assert.ok(handle.asElement(), "Required UI control missing");
      await handle.asElement().click(); await handle.dispose();
    };
    const today = "2026-10-05", tomorrow = "2026-10-06";
    for (const [employeeId, date] of [["9074352", tomorrow], ["9086658", today]]) {
      await page.goto(base + "/shifts", { waitUntil: "networkidle2" });
      evidence.page = await page.evaluate(async () => {
        const r = await fetch('/api/auth/me'); const b = await r.json();
        return { path: location.pathname, sessionStatus: r.status, role: b.role,
          bundle: [...document.scripts].map(s => s.src).filter(s => s.includes('/assets/')),
          searchInput: !!document.querySelector('input[placeholder="Search name or ID..."]'),
          crashed: /Overview failed to load|A rendering error occurred/.test(document.body.innerText) };
      });
      evidence.renderErrors = errors.length; save();
      await page.waitForSelector('input[placeholder="Search name or ID..."]', { timeout: 15000 });
      rows = await page.evaluate(async () => (await fetch("/api/shifts?from=2026-10-05&to=2026-10-11")).json());
      assert.ok(Array.isArray(rows), "Live shift GET did not return rows");
      const row = rows.find(row => row.employeeId === employeeId && row.shiftDate === date);
      assert.ok(row, "Requested shift cell not found");
      const input = await page.$('input[placeholder="Search name or ID..."]');
      await input.type(employeeId);
      await page.waitForFunction(id => document.querySelector("#shifts-scroll-bot tbody tr")?.innerText.includes(id), {}, employeeId);
      const index = date === today ? 0 : 1;
      const cell = await page.$(`#shifts-scroll-bot tbody tr td:nth-child(${index + 5}) div.font-mono`);
      assert.ok(cell, "Requested rendered cell missing");
      await cell.click();
      await clickText("#shifts-scroll-bot td .bg-sunken > div.font-mono", "WORKING");
      const override = await page.$('[aria-label="Override"]');
      if (override) await clickText('[aria-label="Override"] button', "Override");
      await page.waitForSelector('[aria-label="Shift time"]');
      assert.equal(await page.$eval('[aria-label="Shift time"] button[type="submit"]', button => {
        const r = button.getBoundingClientRect();
        return r.top >= 0 && r.bottom <= innerHeight && document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2) === button;
      }), true);
      const before = evidence.simulatedWrites.length;
      const validation = await page.evaluate(async row => {
        const r = await fetch("/api/shifts/validate", {
          method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ shiftId: row.id, shiftType: "WORKING", shiftStart: row.shiftStart || row.patternStart || "08:00",
            shiftEnd: row.shiftEnd || row.patternEnd || "17:00" }),
        });
        const b = await r.json();
        return { status: r.status, valid: b.valid, rules: (b.violations || []).map(v => ({ rule: v.rule, hard: v.isHardBlock })) };
      }, row);
      evidence.targetValidation ||= [];
      evidence.targetValidation.push(validation);
      save();
      assert.equal(validation.status, 200);
      await clickText('[aria-label="Shift time"] button', "Save");
      await page.waitForFunction(() => !document.querySelector('[aria-label="Shift time"]'));
      if (evidence.simulatedWrites.length === before) {
        const warning = await page.$("button");
        const hard = await page.evaluate(() => document.body.innerText.includes("Shift Validation Failed"));
        if (hard) throw new Error("Existing working-time hard block remains for a requested case");
        await clickText("button", "Save Anyway");
      }
      await page.waitForFunction(() => !document.querySelector('[aria-label="Shift time"]'));
      assert.equal(evidence.simulatedWrites.length, before + 1);
      for (const type of (index === 1 ? ["OFF", "AL", "SL"] : [])) {
        evidence.phase = "Checking additional status " + type; save();
        await page.waitForNetworkIdle({ idleTime: 500, timeout: 30000 });
        const targetCell = await page.$(`#shifts-scroll-bot tbody tr td:nth-child(${index + 5}) div.font-mono`);
        await targetCell.click();
        const saved = page.waitForResponse(response =>
          /^\/api\/shifts\/\d+$/.test(new URL(response.url()).pathname) &&
          response.request().method() === "PATCH", { timeout: 30000 });
        await clickText("#shifts-scroll-bot td .bg-sunken > div.font-mono", type);
        const requiredOverride = await page.$('[aria-label="Override"]');
        if (requiredOverride) await clickText('[aria-label="Override"] button', "Override");
        await saved;
        assert.equal(evidence.simulatedWrites.at(-1).status, type);
      }
    }
    await page.goto(base + "/wic-attendance", { waitUntil: "networkidle2" });
    const buttons = await page.$$("button");
    let opened = false;
    for (const button of buttons) {
      const text = await button.evaluate(e => e.innerText.trim());
      if (/^(Manual Check-in|Manueller Check-in|Manuelle Anmeldung|Manuell anmelden)$/i.test(text)) {
        await button.click(); opened = true; break;
      }
    }
    assert.ok(opened, "Manual modal button missing");
    await page.waitForFunction(() => document.querySelector('div[style*="z-index: 1000"] [role="alert"]') ||
      document.querySelector('div[style*="z-index: 1000"] form select')?.options.length > 1, { timeout: 30000 });
    evidence.modal = await page.evaluate(() => ({
      visibleError: !!document.querySelector('div[style*="z-index: 1000"] [role="alert"]'),
      options: (document.querySelector('div[style*="z-index: 1000"] form select')?.options.length || 1) - 1,
      pageCrashed: /Overview failed to load|A rendering error occurred/.test(document.body.innerText),
    }));
    assert.equal(evidence.modal.pageCrashed, false);
    assert.deepEqual(errors, []);
    evidence.renderErrors = errors.length;
    await page.evaluate(() => fetch("/api/auth/logout", { method: "POST" }));
    evidence.result = "PASSED_WITH_INTERCEPTED_WRITES";
    save(); console.log(JSON.stringify(evidence));
  } finally { await browser.close(); }
})().catch(error => {
  evidence.result = "FAILED"; evidence.failure = error.message; save();
  console.error("Static hotfix verification failed: " + error.message); process.exitCode = 1;
});
