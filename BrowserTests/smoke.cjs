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
    // Regression: exercise the actual published WebAssembly build, not just the console library.
    await code.fill("Dim InputPath As String = \"Scores.csv\"\nDim OutputPath As String = \"Passed.csv\"\n\nUsing Reader As New StreamReader(InputPath)\n    Using Writer As New StreamWriter(OutputPath)\n\n        While Not Reader.EndOfStream\n            Dim Line As String = Reader.ReadLine()\n            Dim Fields() As String = Line.Split(\",\")\n\n            Dim Name As String = Fields(0)\n            Dim Score As Integer = CInt(Fields(1))\n\n            If Score >= 50 Then\n                Writer.WriteLine(Name)\n            End If\n        End While\n\n    End Using\nEnd Using\n\nConsole.WriteLine(\"Processing complete\")");
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('Processing complete'),{},{timeout:30000});
    const csvPseudo=await pseudo.inputValue();
    console.log('Published CSV pseudocode:\n'+csvPseudo);
    assert.ok(!csvPseudo.includes('UNSUPPORTED VB.NET'), 'Published build must expand nested USING blocks');
    for(const expected of ['USING Reader ← OPENFILE(InputPath, READ)','USING Writer ← OPENFILE(OutputPath, WRITE)','WHILE NOT Reader.EndOfStream','DECLARE Fields : ARRAY OF STRING ← Line.Split(",")','Fields[0]','Fields[1]','IF Score ≥ 50 THEN','Writer.WriteLine(Name)']) assert.ok(csvPseudo.includes(expected), 'Missing CSV pseudocode: '+expected);
    const chart=page.locator('.flow-viewport svg');
    await chart.waitFor();
    const labels=await chart.locator('text').allTextContents();
    for(const expected of ['Writer.Dispose()','Reader.Dispose()','Writer.WriteLine(Name)']) assert.ok(labels.some(label=>label.includes(expected)), 'Missing CSV graph label: '+expected);
    console.log('PASS: published browser nested CSV USING regression');
    await code.fill('Try\n    Number = CInt(Console.ReadLine())\n    Console.WriteLine(Number)\nCatch ex As FormatException\n    Console.WriteLine("Invalid number")\nFinally\n    Console.WriteLine("Finished")\nEnd Try\nConsole.WriteLine("After try")');
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ENDTRY'),{},{timeout:30000});
    const tryPseudo=await pseudo.inputValue();
    assert.ok(!tryPseudo.includes('UNSUPPORTED VB.NET'));
    for(const expected of ['TRY','CATCH ex : FormatException','FINALLY','OUTPUT "Finished"','ENDTRY']) assert.ok(tryPseudo.includes(expected),'Missing TRY pseudocode: '+expected);
    const tryLabels=await chart.locator('text > title').allTextContents();
    for(const expected of ['Exception raised?','Exception is FormatException?','FINALLY (normal completion)','FINALLY (exception pending)','EXCEPTION PROPAGATES']) assert.ok(tryLabels.includes(expected),'Missing TRY graph label: '+expected);
    console.log('PASS: published browser TRY/CATCH/FINALLY regression');
    // Verify named, independent event handlers in the actual published application.
    await code.fill(`Public Class Form1
    Private Sub btnAdd_Click(sender As Object, e As EventArgs) Handles btnAdd.Click, btnExtra.Click
        If txtName.Text <> "" Then
            lstNames.Items.Add(txtName.Text)
            lblStatus.Text = "Added"
        End If
    End Sub
    Private Sub Timer1_Tick(sender As Object, e As EventArgs) Handles Timer1.Tick
        Me.Invalidate()
    End Sub
End Class`);
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ON btnAdd.Click OR btnExtra.Click'),{},{timeout:30000});
    const clickPseudo=await pseudo.inputValue();
    for(const expected of ['EVENT HANDLER btnAdd_Click','ENDEVENT','lstNames.Items.Add(txtName.Text)','lblStatus.Text ← "Added"']) assert.ok(clickPseudo.includes(expected),'Missing event pseudocode: '+expected);
    assert.ok(!clickPseudo.includes('UNSUPPORTED VB.NET'));
    assert.ok((await chart.locator('text > title').allTextContents()).some(label=>label.includes('ON btnAdd.Click OR btnExtra.Click')),'Missing event entry');
    const routines=page.locator('#routine');
    assert.equal(await routines.locator('option').count(),2);
    await routines.selectOption('1');
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ON Timer1.Tick'),{},{timeout:30000});
    assert.ok(!(await pseudo.inputValue()).includes('lstNames.Items.Add'));
    await routines.selectOption('0');
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ON btnAdd.Click OR btnExtra.Click'),{},{timeout:30000});
    console.log('PASS: published browser Windows Forms event handler regression');
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
    // C# front end: exercise the same published graph and XML exporters.
    await page.locator('#language').selectOption('cs');
    const cs=page.getByRole('textbox',{name:'C# code',exact:true});
    await cs.fill(`int total = 0;
int[] numbers = { 2, 4, 6 };
for (int i = 0; i < numbers.Length; i++) {
    if (i == 1) continue;
    total += numbers[i];
}
Console.WriteLine(total);`);
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ENDFOR'),{},{timeout:30000});
    const csPseudo=await pseudo.inputValue();
    for(const expected of ['total ← 0','FOR (i ← 0; i < numbers.Length; i ← i + 1)','IF i = 1 THEN','CONTINUE','total ← total + (numbers[i])','OUTPUT total']) assert.ok(csPseudo.includes(expected),'Missing C# pseudocode: '+expected+'\n'+csPseudo);
    const csLabels=await chart.locator('text > title').allTextContents();
    for(const expected of ['i ← 0','i < numbers.Length?','i ← i + 1','CONTINUE','OUTPUT total']) assert.ok(csLabels.includes(expected),'Missing C# flowchart label: '+expected);
    const [csDownload]=await Promise.all([page.waitForEvent('download'),page.getByRole('button',{name:'Export Draw.io XML'}).click()]);
    const csXml=await fs.readFile(await csDownload.path(),'utf8');
    const csGraph=await page.evaluate(xml=>{
      const doc=new DOMParser().parseFromString(xml,'application/xml');
      const cells=[...doc.querySelectorAll('diagram:first-child mxCell')];
      const id=label=>cells.find(c=>c.getAttribute('value')===label)?.getAttribute('id');
      const edge=(a,b)=>cells.some(c=>c.getAttribute('source')===id(a)&&c.getAttribute('target')===id(b)&&id(a)&&id(b));
      return {valid:!doc.querySelector('parsererror'),pages:doc.querySelectorAll('diagram').length,continueToIncrement:edge('CONTINUE','i ← i + 1'),incrementToCondition:edge('i ← i + 1','i < numbers.Length?')};
    },csXml);
    assert.deepEqual(csGraph,{valid:true,pages:2,continueToIncrement:true,incrementToCondition:true});
    await cs.fill(`class Player {
      int health;
      public Player(int initial) { this.health = initial; }
      public int Factorial(int n) { if (n <= 1) return 1; return n * Factorial(n - 1); }
    }`);
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ME.health ← initial'),{},{timeout:30000});
    assert.equal(await routines.locator('option').count(),2);
    await routines.selectOption('1');
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('FUNCTION Factorial'),{},{timeout:30000});
    assert.ok((await chart.locator('text > title').allTextContents()).some(s=>s.includes('Player.Factorial(n)')));
    await cs.fill(`int x = 0;
while (x < 2) { x++; }
do { x--; } while (x > 0);
switch (x) { case 0: Console.WriteLine("zero"); break; default: Console.WriteLine("other"); break; }
try { Console.WriteLine(x); } catch (Exception ex) { Console.WriteLine(ex.Message); } finally { Console.WriteLine("done"); }`);
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ENDTRY'),{},{timeout:30000});
    const controlPseudo=await pseudo.inputValue();
    for(const expected of ['WHILE x < 2','REPEAT','UNTIL NOT (x > 0)','CASE OF x','OTHERWISE:','CATCH ex : Exception','FINALLY']) assert.ok(controlPseudo.includes(expected),'Missing C# control pseudocode: '+expected);
    const controlLabels=await chart.locator('text > title').allTextContents();
    for(const expected of ['x < 2?','x > 0?','SELECT value = 0?','FINALLY (normal completion)']) assert.ok(controlLabels.includes(expected),'Missing C# control flow: '+expected);
    await cs.fill('using var reader = new StreamReader("Data.txt"); Console.WriteLine(reader.ReadLine());');
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('UNSUPPORTED C#'),{},{timeout:30000});
    assert.ok((await chart.locator('text > title').allTextContents()).some(s=>s.includes('Flowchart not expanded')));
    await page.locator('#language').selectOption('vb');
    await code.fill('Console.WriteLine("VB still works")');
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('OUTPUT "VB still works"'),{},{timeout:30000});
    console.log('PASS: published C# conversion, loops, routine selection, unsupported-flow guard, XML and switch back to VB.NET');
    assert.deepEqual(errors,[]);
    console.log('Browser smoke test passed: conversion, graph, edited XML, stale source, invalid syntax.');
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
