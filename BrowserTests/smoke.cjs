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
    // Theme regression: real Blazor toggle, SVG presentation and persistence.
    const themeButton = page.getByRole('button',{name:'Dark mode',exact:true});
    await themeButton.waitFor();
    assert.equal(await themeButton.getAttribute('aria-pressed'),'false');
    const originalSource = await code.inputValue();
    const originalPseudo = await pseudo.inputValue();
    const originalGraph = await page.locator('.flow-viewport svg').innerHTML();
    await themeButton.click();
    await page.waitForFunction(()=>document.documentElement.dataset.theme==='dark' && document.querySelector('#theme-toggle')?.getAttribute('aria-pressed')==='true');
    assert.equal(await code.evaluate(el=>getComputedStyle(el).backgroundColor),'rgb(17, 27, 41)');
    assert.equal(await pseudo.evaluate(el=>getComputedStyle(el).color),'rgb(227, 234, 244)');
    const darkChart = page.locator('.flow-viewport svg');
    assert.equal(await darkChart.locator('text[fill="#17263c"]').first().evaluate(el=>getComputedStyle(el).fill),'rgb(237, 243, 250)');
    assert.equal(await darkChart.locator('[fill="#dcfce7"]').first().evaluate(el=>getComputedStyle(el).fill),'rgb(32, 70, 56)');
    assert.equal(await code.inputValue(),originalSource);
    assert.equal(await pseudo.inputValue(),originalPseudo);
    assert.equal(await darkChart.innerHTML(),originalGraph);
    await page.reload({waitUntil:'domcontentloaded'});
    await code.waitFor({timeout:120000});
    await page.waitForFunction(()=>document.documentElement.dataset.theme==='dark' && document.querySelector('#theme-toggle')?.getAttribute('aria-pressed')==='true');
    await themeButton.click();
    await page.waitForFunction(()=>document.documentElement.dataset.theme==='light' && document.querySelector('#theme-toggle')?.getAttribute('aria-pressed')==='false');
    assert.equal(await code.evaluate(el=>getComputedStyle(el).backgroundColor),'rgb(251, 252, 254)');
    console.log('PASS: dark mode, readable flowchart, unchanged conversion and persisted preference');

    await code.fill('Dim x As Integer = 5\nIf x >= 3 Then\n    Console.WriteLine("yes")\nElse\n    Console.WriteLine("no")\nEnd If');
    await page.getByRole('button',{name:'Convert code',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes('IF x ≥ 3 THEN'));
    assert.match(await pseudo.inputValue(),/OUTPUT/);
    await page.locator('.flow-viewport svg').waitFor();
    // Verify the running VB assembly and the published UI, not merely the core test project.
    assert.match(await page.locator('.footer').textContent(), /VB build 1\.2\.1-rd2/);
    const resizeCases = [
      {name:'snippet', source:`Dim Numbers(4) As Integer
Numbers(0) = 42
ReDim Preserve Numbers(9)
ReDim Numbers(2)
Console.WriteLine("REDIM_SNIPPET_DONE")`, expected:['RESIZE PRESERVE Numbers[0..9]','RESIZE Numbers[0..2]'], marker:'REDIM_SNIPPET_DONE'},
      {name:'class method and nested WITH', source:`Public Class Form1
    Private Scores() As Integer
    Public Sub Grow()
        With Me
            ReDim Preserve .Scores(9)
        End With
        ReDim Scores(4)
        Console.WriteLine("REDIM_METHOD_DONE")
    End Sub
End Class`, expected:['RESIZE PRESERVE ME.Scores[0..9]','RESIZE Scores[0..4]'], marker:'REDIM_METHOD_DONE'},
      {name:'dynamic and multidimensional bounds', source:`Dim Grid(2, 3) As Integer
Dim Values(4) As Integer
Dim upper As Integer = 8
ReDim Preserve Grid(2, upper), Values(Values.Length)
ReDim Values(0 To upper - 1)
Console.WriteLine("REDIM_DYNAMIC_DONE")`, expected:['RESIZE PRESERVE Grid[0..2, 0..upper]','RESIZE PRESERVE Values[0..LEN(Values)]','RESIZE Values[0..upper - 1]'], marker:'REDIM_DYNAMIC_DONE'}
    ];
    for (const test of resizeCases) {
      await code.fill(test.source);
      await page.getByRole('button',{name:'Convert code',exact:true}).click();
      await page.waitForFunction(()=> /^(Converted |Check the |Conversion failed:)/.test(document.querySelector('.notes .status')?.textContent || ''), null, {timeout:30000});
      const actual=await pseudo.inputValue();
      const notes=await page.locator('.notes').textContent();
      console.log('ReDim '+test.name+' pseudocode:\n'+actual+'\n'+notes);
      assert.ok(actual.includes(test.marker),'Conversion missing output marker: '+notes);
      assert.ok(!/UNSUPPORTED VB\.NET/i.test(actual),actual);
      assert.ok(!/Unsupported ReDim/i.test(notes),notes);
      const resizeLabels=await page.locator('.flow-viewport svg text > title').allTextContents();
      for (const expected of test.expected) {
        assert.ok(actual.includes(expected),'Missing resize pseudocode: '+expected+'\n'+actual);
        assert.ok(resizeLabels.includes(expected),'Missing resize graph label: '+expected+'\n'+resizeLabels.join(' | '));
      }
      console.log('PASS: published ReDim '+test.name);
    }
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
    // Windows Forms: the supplied designer partial contains subscriptions and field types.
    console.log('Running C# event regression suite 1.2');
    const events=await convertCs(`using System;
using System.Windows.Forms;
partial class GameForm : Form {
    private int score;
    public event EventHandler GameOver;
    public GameForm() { InitializeComponent(); }
    private void Start_Click(object sender, EventArgs e) { score += 2; timer.Start(); }
    private void Form_Load(object sender, EventArgs e) { this.KeyPreview = true; }
    private void Timer_Tick(object sender, EventArgs e) { this.Invalidate(); }
    private void Form_KeyDown(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Space) timer.Stop(); }
    private void Form_Paint(object sender, PaintEventArgs e) { e.Graphics.FillRectangle(Brushes.Blue, 10, 10, 40, 40); }
    private void Finish() { GameOver?.Invoke(this, EventArgs.Empty); }
    private void OnGameOver(object sender, EventArgs e) { timer.Stop(); }
    private void Disconnect() { start.Click -= Start_Click; }
    private void Unbound_Click(object sender, EventArgs e) { score = 0; }
}
partial class GameForm {
    private Button start;
    private Button extra;
    private System.Windows.Forms.Timer timer;
    private void InitializeComponent() {
        start.Click += Start_Click;
        this.extra.Click += new System.EventHandler(this.Start_Click);
        this.Load += Form_Load;
        timer.Tick += Timer_Tick;
        KeyDown += Form_KeyDown;
        this.Paint += Form_Paint;
        GameOver += OnGameOver;
    }
}`,'ENDCONSTRUCTOR');
    async function pickRoutine(name, marker) {
      const options=await routines.locator('option').evaluateAll(opts=>opts.map(o=>({value:o.value,text:o.textContent})));
      const matches=options.filter(o=>o.text.includes(name));
      assert.equal(matches.length,1,JSON.stringify(options));
      await routines.selectOption(matches[0].value);
      await page.waitForFunction(marker=>document.querySelector('textarea[aria-label="Editable pseudocode"]').value.includes(marker),marker,{timeout:30000});
      return {text:await pseudo.inputValue(),labels:await chart.locator('text > title').allTextContents(),option:matches[0].text};
    }
    assert.equal(await routines.locator('option').count(),11);
    const click=await pickRoutine('GameForm.Start_Click(', 'EVENT HANDLER Start_Click');
    for(const expected of ['// ON start.Click','// ON ME.extra.Click','score ← score + (2)','timer.Start()','ENDEVENT']) assert.ok(click.text.includes(expected),click.text);
    assert.ok(click.labels.some(s=>s.includes('GameForm.Start_Click(sender, e)')&&s.includes('ON start.Click')&&s.includes('ON ME.extra.Click')),click.labels.join('\n'));
    assert.ok(click.option.includes('ON start.Click'));
    const [eventDownload]=await Promise.all([page.waitForEvent('download'),page.getByRole('button',{name:'Export Draw.io XML'}).click()]);
    const eventXml=await fs.readFile(await eventDownload.path(),'utf8');
    assert.ok(await page.evaluate(xml=>{
      const doc=new DOMParser().parseFromString(xml,'application/xml');
      const cells=[...doc.querySelectorAll('diagram:first-child mxCell')];
      return !doc.querySelector('parsererror')&&doc.querySelectorAll('diagram').length===2&&cells.some(c=>(c.getAttribute('value')??'').includes('ON start.Click'));
    },eventXml));
    for(const [name,trigger,body] of [['Form_Load','ME.Load','ME.KeyPreview ← TRUE'],['Timer_Tick','timer.Tick','ME.Invalidate()'],['Form_KeyDown','KeyDown','IF e.KeyCode = Keys.Space THEN'],['Form_Paint','ME.Paint','e.Graphics.FillRectangle(Brushes.Blue, 10, 10, 40, 40)'],['OnGameOver','GameOver','timer.Stop()']]) {
      const handler=await pickRoutine('GameForm.'+name+'(', 'EVENT HANDLER '+name);
      assert.ok(handler.text.includes('// ON '+trigger)&&handler.text.includes(body),handler.text);
      assert.ok(handler.labels.some(s=>s.includes('ON '+trigger)),handler.labels.join('\n'));
    }
    const finish=await pickRoutine('GameForm.Finish(', 'RAISEEVENT GameOver(ME, EventArgs.Empty) IF SUBSCRIBED');
    assert.ok(finish.labels.includes('RAISEEVENT GameOver(ME, EventArgs.Empty) IF SUBSCRIBED'));
    const disconnect=await pickRoutine('GameForm.Disconnect(', 'REMOVEHANDLER start.Click, Start_Click');
    assert.ok(disconnect.labels.includes('REMOVEHANDLER start.Click, Start_Click'));
    const unbound=await pickRoutine('GameForm.Unbound_Click(', 'SUBROUTINE Unbound_Click');
    assert.ok(!unbound.text.includes('EVENT HANDLER')&&!unbound.option.includes('ON '));
    const initEvents=await pickRoutine('GameForm.InitializeComponent(', 'ADDHANDLER start.Click, Start_Click');
    for(const expected of ['ADDHANDLER start.Click, Start_Click','ADDHANDLER ME.extra.Click, NEW System.EventHandler(ME.Start_Click)','ADDHANDLER ME.Load, Form_Load','ADDHANDLER timer.Tick, Timer_Tick','ADDHANDLER KeyDown, Form_KeyDown','ADDHANDLER ME.Paint, Form_Paint','ADDHANDLER GameOver, OnGameOver']) assert.ok(initEvents.text.includes(expected),initEvents.text);
    assert.ok(initEvents.labels.includes('ADDHANDLER timer.Tick, Timer_Tick'));
    const unknownEvents=await convertCs(`class Example {
    void Setup() { unknown.Click += Callback; }
    void Callback(object sender, EventArgs e) { Console.WriteLine("Callback"); }
}`,'unknown.Click += Callback');
    assert.ok(!unknownEvents.text.includes('ADDHANDLER'),unknownEvents.text);
    const unknownHandler=await pickRoutine('Example.Callback(', 'SUBROUTINE Callback');
    assert.ok(!unknownHandler.text.includes('EVENT HANDLER'));
    console.log('PASS: C# event subscriptions, partial designer class, multiple triggers, independent handlers, conditional raise, removal, XML and conservative fallback');
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
    await require('./algorithm-regression.cjs')(browser);
    console.log('Browser smoke test passed: conversion, graph, edited XML, stale source, invalid syntax.');
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
