const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const puppeteer = require("C:\\GSDDashboard\\node_modules\\puppeteer");
const legacy = process.argv.includes("--legacy-baseline");
const root = process.env.PULSE_STATIC_ROOT || (legacy ? "C:\\ProgramData\\PulseRollback\\20261005-101137\\wwwroot"
  : "C:\\GSDDashboard_secure\\Backend\\wwwroot");
const baseline = legacy || process.argv.includes("--baseline");
const generic = process.env.PULSE_GENERIC_SHIFT_API === "1";

(async () => {
  const browser = await puppeteer.launch({ headless: true });
  try {
    const page = await browser.newPage();
    await page.setViewport({ width: 1280, height: 720 });
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    const today = new Date().toISOString().slice(0, 10);
    let row = {
      id: 101, employeeId: "TEST-AGENT", fullName: "Synthetic agent", primaryRole: "Voice",
      shiftDate: today, shiftType: "SL", shiftStart: null, shiftEnd: null,
      patternStart: "09:00", patternEnd: "17:00", effectiveTask: "Voice",
    };
    const writes = [];
    let rejectValidation = false;
    let rejectSave = false;
    await page.setRequestInterception(true);
    page.on("request", async request => {
      const url = new URL(request.url());
      if (url.hostname !== "pulse-offline.invalid") {
        await request.abort(); return;
      }
      if (url.pathname.startsWith("/api/")) {
        let data = [];
        if (url.pathname === "/api/auth/me") data = { kid: "TEST-RTM", name: "Test operator", role: "RTM" };
        if (url.pathname === "/api/shifts") data = [row];
        if (url.pathname === "/api/shifts/coverage") data = { slots: [] };
        if (url.pathname === "/api/shifts/validate") data = { valid: true, violations: [] };
        if (url.pathname === "/api/shifts/validate" && rejectValidation) {
          await request.respond({ status: 503, contentType: "application/json", body: '{"error":"Synthetic validation failure"}' });
          return;
        }
        if ((url.pathname === "/api/shifts/101/manual" || (legacy || generic) && url.pathname === "/api/shifts/101") &&
            request.method() === "PATCH") {
          if (rejectSave) {
            await request.respond({ status: 503, contentType: "application/json", body: '{"error":"Synthetic save failure"}' });
            return;
          }
          const body = JSON.parse(request.postData());
          writes.push(body); row = { ...row, ...body }; data = row;
        }
        await request.respond({ status: 200, contentType: "application/json", body: JSON.stringify(data) });
        return;
      }
      const file = url.pathname.startsWith("/assets/")
        ? path.join(root, ...url.pathname.split("/").filter(Boolean))
        : path.join(root, "index.html");
      if (!fs.existsSync(file)) { await request.respond({ status: 404, body: "" }); return; }
      const type = file.endsWith(".js") ? "application/javascript" : file.endsWith(".css") ? "text/css" : "text/html";
      await request.respond({ status: 200, contentType: type, body: fs.readFileSync(file) });
    });
    await page.goto("http://pulse-offline.invalid/shifts", { waitUntil: "networkidle0" });
    try { await page.waitForSelector("#shifts-scroll-bot tbody tr"); }
    catch (error) {
      console.error(JSON.stringify({ errors, url: page.url(), fixtureText: await page.evaluate(() => document.body.innerText.slice(0, 350)) }));
      throw error;
    }
    const clickText = async (selector, text) => {
      const handle = await page.evaluateHandle((selector, text) =>
        [...document.querySelectorAll(selector)].find(element => element.textContent.trim() === text ||
          element.firstChild?.textContent.trim() === text),
      selector, text);
      assert.ok(handle.asElement(), `Missing ${text}`);
      await handle.asElement().click();
      await handle.dispose();
    };
    await clickText("#shifts-scroll-bot tbody td > div.font-mono", "SL");
    await clickText("#shifts-scroll-bot div.font-mono", "WORKING");
    if (baseline) {
      const geometry = await page.evaluate(() => {
        const save = [...document.querySelectorAll("button")].find(x => x.textContent.trim() === "Save");
        const r = save.getBoundingClientRect();
        return { pendingSaveVisible: r.top >= 0 && r.bottom <= innerHeight,
          saveTop: r.top, saveBottom: r.bottom, viewport: innerHeight,
          hitTest: document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2) === save };
      });
      console.log(JSON.stringify({ baseline: geometry, writesBeforeTimeConfirmation: writes.length }));
      if (legacy) {
        await clickText("button", "Save");
        await page.waitForFunction(() => [...document.querySelectorAll("#shifts-scroll-bot tbody td > div.font-mono")]
          .some(x => x.textContent.includes("WORKING")));
        assert.equal(writes.length, 1);
        assert.equal(writes[0].shiftType, "WORKING");
        console.log("Legacy workaround confirmed: scrolling time confirmation into view and saving writes WORKING.");
      }
      return;
    }
    await page.waitForSelector('[role="dialog"][aria-label="Shift time"]');
    assert.equal(await page.$eval('[aria-label="Shift time"] button[type="submit"]', button => {
      const r = button.getBoundingClientRect();
      return r.top >= 0 && r.bottom <= innerHeight && document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2) === button;
    }), true, "Time confirmation must be visible and clickable");
    await clickText('[role="dialog"] button', "Save");
    await page.waitForFunction(() => !document.querySelector('[aria-label="Shift time"]'));
    assert.deepEqual(writes.pop(), { shiftType: "WORKING", shiftStart: "09:00", shiftEnd: "17:00" });
    for (const type of ["WIC_DUTY", "AL", "HALF_AL", "SL", "UL", "OL", "TRAINING", "OFF", "OFF_WEEKEND", "PH", "LPH", "CD", "CO", "RESIGNED"]) {
      const current = row.shiftType.replace("_", " ");
      await clickText("#shifts-scroll-bot tbody td > div.font-mono", current);
      await clickText("#shifts-scroll-bot div.font-mono", type.replace("_", " "));
      const override = await page.$('[role="dialog"][aria-label="Override"]');
      if (override) await clickText('[aria-label="Override"] button', "Override");
      if (["WORKING", "WIC_DUTY"].includes(type)) await clickText('[aria-label="Shift time"] button', "Save");
      await page.waitForFunction(expected => [...document.querySelectorAll("#shifts-scroll-bot tbody td > div.font-mono")]
        .some(x => x.textContent.includes(expected)), {}, type.replace("_", " "));
      assert.equal(writes.at(-1).shiftType, type);
    }
    const beforeCancel = writes.length;
    await clickText("#shifts-scroll-bot tbody td > div.font-mono", "RESIGNED");
    await clickText("#shifts-scroll-bot div.font-mono", "WORKING");
    await clickText('[aria-label="Shift time"] button', "Cancel");
    assert.equal(writes.length, beforeCancel);
    await clickText("#shifts-scroll-bot tbody td > div.font-mono", "RESIGNED");
    await clickText("#shifts-scroll-bot div.font-mono", "WORKING");
    rejectValidation = true;
    await clickText('[aria-label="Shift time"] button', "Save");
    await page.waitForSelector('[aria-label="Shift time"] [role="alert"]');
    assert.equal(writes.length, beforeCancel, "Validation failures must not silently save");
    rejectValidation = false;
    rejectSave = true;
    await clickText('[aria-label="Shift time"] button', "Save");
    await page.waitForFunction(() => document.querySelector('[aria-label="Shift time"] [role="alert"]')?.textContent === "Synthetic save failure");
    assert.equal(writes.length, beforeCancel);
    rejectSave = false;
    await clickText('[aria-label="Shift time"] button', "Save");
    await page.waitForFunction(() => !document.querySelector('[aria-label="Shift time"]'));
    assert.equal(writes.at(-1).shiftType, "WORKING");
    page.once("dialog", dialog => dialog.accept());
    await clickText("#shifts-scroll-bot tbody td > div.font-mono", "WORKING");
    const deleted = page.waitForResponse(response => response.url().endsWith(generic ? "/api/shifts/101" : "/api/shifts/101/manual") &&
      response.request().method() === "PATCH");
    await clickText("#shifts-scroll-bot button", "Delete");
    await deleted;
    await page.waitForFunction(() => [...document.querySelectorAll("#shifts-scroll-bot tbody td > div.font-mono")]
      .some(x => x.textContent.trim() === "—"));
    assert.equal(writes.at(-1).shiftType, "EMPTY");
    assert.deepEqual(errors, []);
    console.log("Offline Shifts manual-cell status/time confirmation: PASSED; no real API requests.");
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
