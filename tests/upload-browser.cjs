const { chromium } = require('playwright-core');
const assert = require('node:assert/strict');

(async () => {
  const browser = await chromium.launch({channel:'chrome', headless:true});
  try {
    const page = await browser.newPage({viewport:{width:390,height:844}, isMobile:true, hasTouch:true});
    await page.goto(process.env.SITE, {timeout:90000});
    const input = page.getByLabel('Upload ZIP', {exact:true});
    const [chooser] = await Promise.all([page.waitForEvent('filechooser'), input.tap()]);
    await chooser.setFiles('web/storefront.zip');
    const status = page.getByRole('status');
    await status.getByText('Scanned storefront.zip · 4 source files · 6 findings', {exact:true}).waitFor();
    const response = page.waitForResponse(r=>r.url().endsWith('/api/scans') && r.request().method()==='POST');
    await page.getByRole('button',{name:'Scan sample ZIP'}).tap();
    assert.equal((await response).status(),200);
    await page.waitForFunction(() => document.querySelector('[role=status]')?.textContent.startsWith('Scanned storefront.zip'));
    assert.equal(await page.locator('.row').count(),6);
  } finally { await browser.close(); }
})().catch(error=>{ console.error(error); process.exitCode=1; });
