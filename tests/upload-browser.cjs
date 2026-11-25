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
    await input.setInputFiles('web/frontend-example.zip');
    await status.getByText('Scanned frontend-example.zip · 3 source files · 9 findings', {exact:true}).waitFor();
    await page.getByRole('button',{name:'Coverage',exact:true}).click();
    await page.getByRole('heading',{name:'Scan coverage'}).waitFor();
    await page.getByRole('button',{name:'Dependencies',exact:true}).click();
    await page.getByRole('heading',{name:'Dependency usage'}).waitFor();
    await page.getByRole('button',{name:'Findings',exact:true}).click();
    await page.locator('.row').filter({hasText:'Legacy jQuery size()'}).click();
    await page.getByRole('button',{name:'Explain and preview fix'}).click();
    await page.locator('.diff ins').filter({hasText:'.length'}).waitFor({timeout:110000});
    const revised = require('node:child_process').execFileSync('python3',['-c',[
      'import io,sys,zipfile',
      'out=io.BytesIO()',
      'with zipfile.ZipFile("web/frontend-example.zip") as src, zipfile.ZipFile(out,"w") as dst:',
      ' for name in src.namelist(): dst.writestr(name,src.read(name).replace(b".size()", b".length") if name.endswith(".js") else src.read(name))',
      'sys.stdout.buffer.write(out.getvalue())'
    ].join('\n')]);
    await page.getByLabel('Compare revised ZIP').setInputFiles({name:'revised.zip',mimeType:'application/zip',buffer:revised});
    await page.getByRole('button',{name:'Resolved · 1',exact:true}).click();
    await page.locator('.report-line').filter({hasText:'Resolved · Legacy jQuery size()'}).waitFor();
    await page.getByRole('button',{name:'Still present · 8',exact:true}).waitFor();
  } finally { await browser.close(); }
})().catch(error=>{ console.error(error); process.exitCode=1; });
