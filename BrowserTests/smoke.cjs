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
    console.log('PASS: C# basic control flow, loops, recursive routines and XML');
    // C# OOP, collections and resource lifetime. Test the published app, not a mock.
    console.log('Running C# parity regression suite 1.1');
    async function convertCs(source, marker) {
      await cs.fill(source);
      await page.getByRole('button',{name:'Convert code',exact:true}).click();
      try {
        await page.waitForFunction(marker=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes(marker),marker,{timeout:30000});
      } catch (err) {
        console.error('Expected marker: '+marker+'\nSource:\n'+source+'\nActual pseudocode:\n'+await pseudo.inputValue()+'\nStatus and warnings:\n'+await page.locator('.notes').innerText());
        throw err;
      }
      const text=await pseudo.inputValue();
      assert.ok(!text.includes('UNSUPPORTED C#'),text);
      const labels=await chart.locator('text > title').allTextContents();
      assert.ok(!labels.some(x=>x.includes('Flowchart not expanded')),labels.join('\n'));
      console.log('PASS: C# conversion marker '+marker);
      return {text,labels};
    }
    const oop=await convertCs(`class Player : Entity {
      private int health;
      public Player(int initial) : base() { this.health = initial; }
      public int Health { get { return this.health; } set { this.health = value; } }
      public override void Move() { base.Move(); }
    }`,'ENDCONSTRUCTOR');
    for(const expected of ['// CLASS Player : Entity','// PRIVATE health : INTEGER','PARENT.CONSTRUCTOR()','ME.health ← initial']) assert.ok(oop.text.includes(expected),oop.text);
    assert.ok(oop.labels.includes('PARENT.CONSTRUCTOR()'));
    assert.equal(await routines.locator('option').count(),4);
    await routines.selectOption('1');
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ENDGET'));
    assert.ok((await pseudo.inputValue()).includes('RETURN ME.health'));
    assert.ok((await chart.locator('text > title').allTextContents()).some(s=>s.includes('Player.Health.GET()')));
    await routines.selectOption('2');
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('ENDSET'));
    assert.ok((await pseudo.inputValue()).includes('ME.health ← value'));
    await routines.selectOption('3');
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('PARENT.Move()'));
    const collections=await convertCs(`var players = new List<string>();
var scores = new Dictionary<string, int>();
var history = new Stack<string>();
var pending = new Queue<string>();
var seen = new HashSet<int>();
int[,] grid = new int[5, 5];
players.Add("Alice");
if (players.Count > 0) { scores[players[0]] = 100; pending.Enqueue(players[0]); }
history.Push(pending.Dequeue());
grid[2, 3] = 1;
Console.WriteLine(history.Pop());`,'OUTPUT history.Pop()');
    for(const expected of ['players ← NEW LIST OF STRING','scores ← NEW DICTIONARY OF (STRING, INTEGER)','history ← NEW STACK OF STRING','pending ← NEW QUEUE OF STRING','seen ← NEW SET OF INTEGER','grid ← NEW ARRAY[0..4, 0..4] OF INTEGER','players.Add("Alice")','IF players.Count > 0 THEN','scores[players[0]] ← 100','history.Push(pending.Dequeue())','grid[2, 3] ← 1']) assert.ok(collections.text.includes(expected),collections.text);
    assert.ok(collections.labels.includes('scores[players[0]] ← 100'));
    const files=await convertCs(`string input = "Scores.csv";
string output = "Passed.csv";
using (var reader = new StreamReader(input)) {
    using var writer = new StreamWriter(output, true);
    while (!reader.EndOfStream) {
        string line = reader.ReadLine();
        string[] fields = line.Split(',');
        if (int.Parse(fields[1]) >= 50) writer.WriteLine(fields[0]);
    }
}
Console.WriteLine("CS CSV complete");`,'CS CSV complete');
    for(const expected of ['USING reader ← OPENFILE(input, READ)','USING writer ← OPENFILE(output, APPEND)','WHILE NOT reader.EndOfStream','line ← reader.ReadLine()','writer.WriteLine(fields[0])','ENDUSING // Automatically dispose writer on exit']) assert.ok(files.text.includes(expected),files.text);
    for(const expected of ['reader.Dispose()','writer.Dispose()','line ← reader.ReadLine()','writer.WriteLine(fields[0])']) assert.ok(files.labels.includes(expected),files.labels.join('\n'));
    const [fd]=await Promise.all([page.waitForEvent('download'),page.getByRole('button',{name:'Export Draw.io XML'}).click()]);
    const fileXml=await fs.readFile(await fd.path(),'utf8');
    const cleanup=await page.evaluate(xml=>{
      const doc=new DOMParser().parseFromString(xml,'application/xml');
      const cells=[...doc.querySelectorAll('diagram:first-child mxCell')];
      const id=label=>cells.find(c=>c.getAttribute('value')===label)?.getAttribute('id');
      const edge=(a,b)=>!!id(a)&&!!id(b)&&cells.some(c=>c.getAttribute('source')===id(a)&&c.getAttribute('target')===id(b));
      const style=label=>cells.find(c=>c.getAttribute('value')===label)?.getAttribute('style')??'';
      return {valid:!doc.querySelector('parsererror'),pages:doc.querySelectorAll('diagram').length,order:edge('writer.Dispose()','reader.Dispose()'),after:edge('reader.Dispose()','OUTPUT "CS CSV complete"'),readIo:style('line ← reader.ReadLine()').includes('parallelogram'),writeIo:style('writer.WriteLine(fields[0])').includes('parallelogram')};
    },fileXml);
    assert.deepEqual(cleanup,{valid:true,pages:2,order:true,after:true,readIo:true,writeIo:true});
    const early=await convertCs(`string ReadFirst() { using var reader = new StreamReader("Data.txt"); return reader.ReadLine(); }`,'RETURN reader.ReadLine()');
    assert.ok(early.labels.includes('reader.Dispose()'));
    const [ed]=await Promise.all([page.waitForEvent('download'),page.getByRole('button',{name:'Export Draw.io XML'}).click()]);
    const earlyXml=await fs.readFile(await ed.path(),'utf8');
    assert.ok(await page.evaluate(xml=>{
      const doc=new DOMParser().parseFromString(xml,'application/xml');const cells=[...doc.querySelectorAll('diagram:first-child mxCell')];
      const id=label=>cells.find(c=>c.getAttribute('value')===label)?.getAttribute('id');
      const edge=(a,b)=>!!id(a)&&!!id(b)&&cells.some(c=>c.getAttribute('source')===id(a)&&c.getAttribute('target')===id(b));
      return edge('RETURN reader.ReadLine()','reader.Dispose()')&&edge('reader.Dispose()','END');
    },earlyXml));
    const equivalent=await convertCs('var players = new List<string>(); players.Add("Alice"); Console.WriteLine(players.Count);','OUTPUT players.Count');
    // A known StreamReader is supported; an unresolved resource factory is not.
    await cs.fill('using var reader = AcquireReader(); Console.WriteLine(reader.ReadLine());');
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('UNSUPPORTED C#'),{},{timeout:30000});
    assert.ok((await chart.locator('text > title').allTextContents()).some(s=>s.includes('Flowchart not expanded')));
    console.log('PASS: C# properties, collections, nested CSV, using declaration, disposal order and early return');
    await page.locator('#language').selectOption('vb');
    await code.fill('Dim players As New List(Of String)\nplayers.Add("Alice")\nConsole.WriteLine(players.Count)');
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('OUTPUT players.Count'));
    const normalize=s=>s.replace(/\r/g,'').trim();
    assert.equal(normalize(await pseudo.inputValue()),normalize(equivalent.text));
    assert.deepEqual((await chart.locator('text > title').allTextContents()).sort(),equivalent.labels.sort());
    console.log('PASS: equivalent VB.NET and C# collection algorithm labels and pseudocode');
    await code.fill('Console.WriteLine("VB still works")');
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('OUTPUT "VB still works"'),{},{timeout:30000});
    console.log('PASS: published C# conversion, loops, routine selection, unsupported-flow guard, XML and switch back to VB.NET');
    assert.deepEqual(errors,[]);
    console.log('Browser smoke test passed: conversion, graph, edited XML, stale source, invalid syntax.');
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
