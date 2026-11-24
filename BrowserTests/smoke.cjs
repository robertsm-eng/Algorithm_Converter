const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
(async()=>{
  const browser=await chromium.launch({headless:true});
  try {
    const page=await browser.newPage({acceptDownloads:true});
    const errors=[];
    page.on('pageerror',err=>errors.push(err.message));
    await page.goto('http://127.0.0.1:8765/',{waitUntil:'domcontentloaded'});
    const code=page.getByRole('textbox',{name:'VB.NET code',exact:true});
    const pseudo=page.getByRole('textbox',{name:'Editable pseudocode',exact:true});
    await code.waitFor({timeout:120000});
    await code.fill('Dim x As Integer = 5\nIf x >= 3 Then\n    Console.WriteLine("yes")\nElse\n    Console.WriteLine("no")\nEnd If');
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('IF x ≥ 3 THEN'));
    assert.match(await pseudo.inputValue(),/OUTPUT/);
    await page.locator('.flow-viewport svg').waitFor();
    await pseudo.fill('Student-edited pseudocode: < & >');
    const [download]=await Promise.all([page.waitForEvent('download'),page.getByRole('button',{name:'Export Draw.io XML'}).click()]);
    const xml=await fs.readFile(await download.path(),'utf8');
    const data=await page.evaluate(xml=>{const doc=new DOMParser().parseFromString(xml,'application/xml');return {error:!!doc.querySelector('parsererror'),pages:doc.querySelectorAll('diagram').length,vertices:doc.querySelectorAll('mxCell[vertex="1"]').length,edges:doc.querySelectorAll('mxCell[edge="1"]').length,pseudo:doc.querySelector('mxCell[id="pseudocode"]')?.getAttribute('value')};},xml);
    assert.equal(data.error,false);assert.equal(data.pages,2);assert.ok(data.vertices>=4);assert.ok(data.edges>=3);assert.equal(data.pseudo,'Student-edited pseudocode: < & >');
    await code.fill('If Then');
    assert.equal(await page.getByRole('button',{name:'Export Draw.io XML'}).isDisabled(),true);
    page.once('dialog',dialog=>dialog.accept());
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.locator('.notes .error').waitFor();
    assert.deepEqual(errors,[]);
    console.log('Browser smoke test passed: conversion, graph, edited XML, stale source, invalid syntax.');
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
