const { chromium } = require('playwright-core');
const assert = require('node:assert/strict');

(async () => {
  const browser = await chromium.launch({channel:'chrome', headless:true});
  try {
    const page = await browser.newPage({viewport:{width:390,height:844}, isMobile:true, hasTouch:true});
    await page.goto(process.env.SITE, {timeout:90000});
    const input = page.getByLabel('Upload ZIP', {exact:true});
    const [chooser] = await Promise.all([page.waitForEvent('filechooser'), input.tap()]);
    await chooser.setFiles('web/RetailDesk.zip');
    const status = page.getByRole('status');
    await status.getByText('Scanned RetailDesk.zip · 31 source files', {exact:false}).waitFor();
    const response = page.waitForResponse(r=>r.url().endsWith('/api/scans') && r.request().method()==='POST');
    await page.getByRole('button',{name:'Scan sample ZIP'}).tap();
    assert.equal((await response).status(),200);
    await page.waitForFunction(() => document.querySelector('[role=status]')?.textContent.startsWith('Scanned RetailDesk.zip'));
    const count = await page.locator('.row').count();
    assert.ok(count > 0);
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
      'with zipfile.ZipFile("web/RetailDesk.zip") as src, zipfile.ZipFile(out,"w",compression=zipfile.ZIP_DEFLATED) as dst:',
      ' for name in src.namelist(): dst.writestr(name,src.read(name).replace(b".size()", b".length") if name.endswith("/RetailDesk.Web/Scripts/stock.js") else src.read(name))',
      'sys.stdout.buffer.write(out.getvalue())'
    ].join('\n')]);
    await page.getByLabel('Compare revised ZIP').setInputFiles({name:'revised.zip',mimeType:'application/zip',buffer:revised});
    await page.getByRole('button',{name:'Resolved · 1',exact:true}).click();
    await page.locator('.report-line').filter({hasText:'Resolved · Legacy jQuery size()'}).waitFor();
    await page.getByRole('button',{name:'Still present · '+(count-1),exact:true}).waitFor();
  } finally { await browser.close(); }
})().catch(error=>{ console.error(error); process.exitCode=1; });
