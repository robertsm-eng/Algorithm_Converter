// Permanent cross-language browser regression tests. No student code is executed.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const cases = [];
// Expectations are independent of the converter. A regex may allow harmless
// notation differences, but branch directions and return targets are explicit.
const add = (name,vb,cs,check={}) => cases.push({name,vb,cs,...check});
const branch = (from,yes,no) => [[from,yes,'Yes'],[from,no,'No']];
add('01 Sequence and arithmetic',`Dim x As Integer = 2\nx = x * 3\nConsole.WriteLine(x)`,`int x = 2;\nx = x * 3;\nConsole.WriteLine(x);`,{both:['x ← x * 3','OUTPUT x'],edges:[['x ← x * 3','OUTPUT x'],['OUTPUT x','END']]});
add('02 Console input',`Dim x As String = Console.ReadLine()\nConsole.WriteLine(x)`,`string x = Console.ReadLine();\nConsole.WriteLine(x);`,{both:['USERINPUT','OUTPUT x'],io:['USERINPUT','OUTPUT x']});
add('03 Two-way selection',`If x >= 5 Then\n Console.WriteLine("high")\nElse\n Console.WriteLine("low")\nEnd If\nConsole.WriteLine("done")`,`if (x >= 5) { Console.WriteLine("high"); } else { Console.WriteLine("low"); }\nConsole.WriteLine("done");`,{both:['IF x ≥ 5 THEN'],edges:[...branch('x ≥ 5?','OUTPUT "high"','OUTPUT "low"'),['OUTPUT "high"','OUTPUT "done"'],['OUTPUT "low"','OUTPUT "done"']]});
add('04 Selection without else',`If x <> 0 Then\n x = 2\nEnd If\nConsole.WriteLine(x)`,`if (x != 0) { x = 2; }\nConsole.WriteLine(x);`,{edges:branch('x ≠ 0?','x ← 2','OUTPUT x')});
add('05 Else-if chain',`If x < 0 Then\n Console.WriteLine("negative")\nElseIf x = 0 Then\n Console.WriteLine("zero")\nElse\n Console.WriteLine("positive")\nEnd If`,`if (x < 0) { Console.WriteLine("negative"); } else if (x == 0) { Console.WriteLine("zero"); } else { Console.WriteLine("positive"); }`,{edges:[...branch('x < 0?','OUTPUT "negative"','x = 0?'),...branch('x = 0?','OUTPUT "zero"','OUTPUT "positive"')]});
add('06 Nested selection',`If x > 0 Then\n If y > 0 Then\n  Console.WriteLine("both")\n End If\nEnd If\nConsole.WriteLine("done")`,`if (x > 0) { if (y > 0) { Console.WriteLine("both"); } }\nConsole.WriteLine("done");`,{edges:[...branch('x > 0?','y > 0?','OUTPUT "done"'),...branch('y > 0?','OUTPUT "both"','OUTPUT "done"')]});
add('07 While accumulation',`Dim total As Integer = 0\nWhile x > 0\n total = total + x\n x = x - 1\nEnd While\nConsole.WriteLine(total)`,`int total = 0;\nwhile (x > 0) { total = total + x; x = x - 1; }\nConsole.WriteLine(total);`,{edges:[...branch('x > 0?','total ← total + x','OUTPUT total'),['x ← x - 1','x > 0?']]});
add('08 Post-test loop',`Do\n x = x + 1\nLoop While x < 5\nConsole.WriteLine(x)`,`do { x = x + 1; } while (x < 5);\nConsole.WriteLine(x);`,{edges:[['START','x ← x + 1'],['x ← x + 1','x < 5?'],...branch('x < 5?','x ← x + 1','OUTPUT x')]});
add('09 Until condition polarity',`Do\n x = x + 1\nLoop Until x >= 5\nConsole.WriteLine(x)`,`do { x = x + 1; } while (x < 5);\nConsole.WriteLine(x);`,{vbEdges:branch('x ≥ 5?','OUTPUT x','x ← x + 1'),csEdges:branch('x < 5?','x ← x + 1','OUTPUT x')});
add('10 Counted loop ascending',`For i As Integer = 1 To 5\n Console.WriteLine(i)\nNext`,`for (int i = 1; i <= 5; i++) { Console.WriteLine(i); }`,{edges:[['i ← 1','i ≤ 5?'],...branch('i ≤ 5?','OUTPUT i','END'),['OUTPUT i','i ← i + 1'],['i ← i + 1','i ≤ 5?']]});
add('11 Counted loop descending',`For i As Integer = 10 To 2 Step -2\n Console.WriteLine(i)\nNext`,`for (int i = 10; i >= 2; i = i - 2) { Console.WriteLine(i); }`,{edges:[...branch('i ≥ 2?','OUTPUT i','END'),['i ← i - 2','i ≥ 2?']]});
add('12 Continue routes to increment',`For i As Integer = 0 To 4\n If i = 2 Then Continue For\n Console.WriteLine(i)\nNext`,`for (int i = 0; i <= 4; i++) { if (i == 2) continue; Console.WriteLine(i); }`,{edges:[[/^CONTINUE(?: FOR)?$/,'i ← i + 1'],['i ← i + 1','i ≤ 4?']]});
add('13 Break exits loop',`While x > 0\n If x = 2 Then Exit While\n x = x - 1\nEnd While\nConsole.WriteLine(x)`,`while (x > 0) { if (x == 2) break; x = x - 1; }\nConsole.WriteLine(x);`,{edges:[[/^(EXIT WHILE|BREAK)$/,'OUTPUT x'],['x ← x - 1','x > 0?']]});
add('14 Nested counters',`For i As Integer = 0 To 2\n For j As Integer = 0 To 3\n  Console.WriteLine(i * j)\n Next\nNext`,`for (int i = 0; i <= 2; i++) { for (int j = 0; j <= 3; j++) { Console.WriteLine(i * j); } }`,{edges:[['i ≤ 2?','j ← 0','Yes'],['j ≤ 3?','i ← i + 1','No'],['j ← j + 1','j ≤ 3?'],['i ← i + 1','i ≤ 2?']]});
add('15 Foreach iteration',`Dim items As New List(Of Integer)\nFor Each item As Integer In items\n Console.WriteLine(item)\nNext`,`var items = new List<int>();\nforeach (int item in items) { Console.WriteLine(item); }`,{both:['FOR EACH item IN items'],edges:branch('Next item available?','item ← next item','END')});
add('16 Array update',`Dim a(4) As Integer\na(2) = 7\nConsole.WriteLine(a(2))`,`int[] a = new int[5];\na[2] = 7;\nConsole.WriteLine(a[2]);`,{both:['ARRAY[0..4] OF INTEGER','a[2] ← 7','OUTPUT a[2]'],edges:[['a[2] ← 7','OUTPUT a[2]']]});
add('17 Two-dimensional array',`Dim grid(4, 4) As Integer\ngrid(2, 3) = 1\nConsole.WriteLine(grid(2, 3))`,`int[,] grid = new int[5, 5];\ngrid[2, 3] = 1;\nConsole.WriteLine(grid[2, 3]);`,{both:['ARRAY[0..4, 0..4] OF INTEGER','grid[2, 3] ← 1','OUTPUT grid[2, 3]']});
add('18 List methods and indexing',`Dim items As New List(Of String)\nitems.Add("A")\nitems.Add("B")\nitems.RemoveAt(0)\nConsole.WriteLine(items(0))`,`var items = new List<string>();\nitems.Add("A");\nitems.Add("B");\nitems.RemoveAt(0);\nConsole.WriteLine(items[0]);`,{both:['NEW LIST OF STRING','items.Add("A")','items.RemoveAt(0)','OUTPUT items[0]'],calls:['items.Add("A")','items.RemoveAt(0)']});
add('19 Dictionary lookup',`Dim scores As New Dictionary(Of String, Integer)\nscores("A") = 10\nIf scores.ContainsKey("A") Then\n Console.WriteLine(scores("A"))\nEnd If`,`var scores = new Dictionary<string, int>();\nscores["A"] = 10;\nif (scores.ContainsKey("A")) { Console.WriteLine(scores["A"]); }`,{both:['scores["A"] ← 10','OUTPUT scores["A"]'],edges:branch('scores.ContainsKey("A")?','OUTPUT scores["A"]','END')});
add('20 Stack reversal',`Dim history As New Stack(Of String)\nhistory.Push("A")\nhistory.Push("B")\nWhile history.Count > 0\n Console.WriteLine(history.Pop())\nEnd While`,`var history = new Stack<string>();\nhistory.Push("A");\nhistory.Push("B");\nwhile (history.Count > 0) { Console.WriteLine(history.Pop()); }`,{both:['NEW STACK OF STRING'],edges:[...branch('history.Count > 0?','OUTPUT history.Pop()','END'),['OUTPUT history.Pop()','history.Count > 0?']]});
add('21 Queue processing',`Dim pending As New Queue(Of Integer)\npending.Enqueue(3)\nWhile pending.Count > 0\n Console.WriteLine(pending.Dequeue())\nEnd While`,`var pending = new Queue<int>();\npending.Enqueue(3);\nwhile (pending.Count > 0) { Console.WriteLine(pending.Dequeue()); }`,{both:['NEW QUEUE OF INTEGER','pending.Enqueue(3)'],edges:[...branch('pending.Count > 0?','OUTPUT pending.Dequeue()','END'),['OUTPUT pending.Dequeue()','pending.Count > 0?']]});
add('22 Binary search',`Function Search(a() As Integer, target As Integer) As Integer\n Dim low As Integer = 0\n Dim high As Integer = a.Length - 1\n While low <= high\n  Dim mid As Integer = (low + high) \\ 2\n  If a(mid) = target Then\n   Return mid\n  ElseIf a(mid) < target Then\n   low = mid + 1\n  Else\n   high = mid - 1\n  End If\n End While\n Return -1\nEnd Function`,`int Search(int[] a, int target) {\n int low = 0; int high = a.Length - 1;\n while (low <= high) { int mid = (low + high) / 2;\n if (a[mid] == target) return mid; else if (a[mid] < target) low = mid + 1; else high = mid - 1; }\n return -1; }`,{both:['RETURN mid','RETURN -1','low ← mid + 1','high ← mid - 1'],edges:[...branch('a[mid] = target?','RETURN mid','a[mid] < target?'),['low ← mid + 1','low ≤ high?'],['high ← mid - 1','low ≤ high?'],['RETURN mid','END'],['RETURN -1','END']]});
add('23 Bubble sort',`Sub Bubble(a() As Integer, n As Integer)\n Dim i As Integer = 0\n While i < n - 1\n  Dim j As Integer = 0\n  While j < n - i - 1\n   If a(j) > a(j + 1) Then\n    Dim temp As Integer = a(j)\n    a(j) = a(j + 1)\n    a(j + 1) = temp\n   End If\n   j = j + 1\n  End While\n  i = i + 1\n End While\nEnd Sub`,`void Bubble(int[] a, int n) { int i = 0; while (i < n - 1) { int j = 0; while (j < n - i - 1) { if (a[j] > a[j + 1]) { int temp = a[j]; a[j] = a[j + 1]; a[j + 1] = temp; } j = j + 1; } i = i + 1; } }`,{both:['a[j] ← a[j + 1]','a[j + 1] ← temp'],edges:[['j ← j + 1','j < n - i - 1?'],['i ← i + 1','i < n - 1?']]});
add('24 Insertion sort',`Sub Insertion(a() As Integer, n As Integer)\n Dim i As Integer = 1\n While i < n\n  Dim key As Integer = a(i)\n  Dim j As Integer = i - 1\n  While j >= 0 AndAlso a(j) > key\n   a(j + 1) = a(j)\n   j = j - 1\n  End While\n  a(j + 1) = key\n  i = i + 1\n End While\nEnd Sub`,`void Insertion(int[] a, int n) { int i = 1; while (i < n) { int key = a[i]; int j = i - 1; while (j >= 0 && a[j] > key) { a[j + 1] = a[j]; j = j - 1; } a[j + 1] = key; i = i + 1; } }`,{both:['a[j + 1] ← a[j]','a[j + 1] ← key','AND THEN'],edges:[['i ← i + 1','i < n?']]});
add('25 Recursive factorial',`Function Factorial(n As Integer) As Integer\n If n <= 1 Then Return 1\n Return n * Factorial(n - 1)\nEnd Function`,`int Factorial(int n) { if (n <= 1) return 1; return n * Factorial(n - 1); }`,{both:['FUNCTION Factorial','RETURN n * Factorial(n - 1)'],edges:[...branch('n ≤ 1?','RETURN 1','RETURN n * Factorial(n - 1)'),['RETURN 1','END'],['RETURN n * Factorial(n - 1)','END']]});
add('26 Tree traversal',`Sub Traverse(node As Node)\n If node Is Nothing Then Return\n Traverse(node.Left)\n Console.WriteLine(node.Value)\n Traverse(node.Right)\nEnd Sub`,`void Traverse(Node node) { if (node == null) return; Traverse(node.Left); Console.WriteLine(node.Value); Traverse(node.Right); }`,{both:['Traverse(node.Left)','OUTPUT node.Value','Traverse(node.Right)'],edges:[['Traverse(node.Left)','OUTPUT node.Value'],['OUTPUT node.Value','Traverse(node.Right)'],['RETURN','END']]});
add('27 Finite state machine',`Select Case state\n Case 0\n  state = 1\n Case 1\n  state = 2\n Case Else\n  state = 0\nEnd Select\nConsole.WriteLine(state)`,`switch (state) { case 0: state = 1; break; case 1: state = 2; break; default: state = 0; break; }\nConsole.WriteLine(state);`,{both:['CASE OF state','state ← 1','state ← 2','state ← 0'],edges:[['state ← 1','OUTPUT state'],['state ← 2','OUTPUT state'],['state ← 0','OUTPUT state']]});
add('28 File reading loop',`Using reader As New StreamReader("Data.txt")\n While Not reader.EndOfStream\n  Console.WriteLine(reader.ReadLine())\n End While\nEnd Using\nConsole.WriteLine("done")`,`using (var reader = new StreamReader("Data.txt")) { while (!reader.EndOfStream) { Console.WriteLine(reader.ReadLine()); } }\nConsole.WriteLine("done");`,{both:['OPENFILE("Data.txt", READ)','ENDUSING'],edges:[...branch('NOT reader.EndOfStream?','OUTPUT reader.ReadLine()','reader.Dispose()'),['reader.Dispose()','OUTPUT "done"']]});
add('29 Resource disposal order',`Using reader As New StreamReader("In.txt")\n Using writer As New StreamWriter("Out.txt")\n  writer.WriteLine(reader.ReadLine())\n End Using\nEnd Using\nConsole.WriteLine("done")`,`using (var reader = new StreamReader("In.txt")) { using var writer = new StreamWriter("Out.txt"); writer.WriteLine(reader.ReadLine()); }\nConsole.WriteLine("done");`,{both:['OPENFILE("In.txt", READ)','OPENFILE("Out.txt", WRITE)'],io:['writer.WriteLine(reader.ReadLine())'],edges:[['writer.Dispose()','reader.Dispose()'],['reader.Dispose()','OUTPUT "done"']]});
add('30 Basic exception handling',`Try\n x = CInt(Console.ReadLine())\nCatch ex As FormatException\n Console.WriteLine("invalid")\nFinally\n Console.WriteLine("finished")\nEnd Try\nConsole.WriteLine("done")`,`try { x = int.Parse(Console.ReadLine()); } catch (FormatException ex) { Console.WriteLine("invalid"); } finally { Console.WriteLine("finished"); }\nConsole.WriteLine("done");`,{both:['CATCH ex : FormatException','FINALLY','ENDTRY'],nodes:['Exception raised?','Exception is FormatException?','FINALLY (normal completion)','FINALLY (exception pending)','EXCEPTION PROPAGATES']});
add('31 Circular queue index',`Dim rear As Integer = 0\nDim capacity As Integer = 5\nrear = (rear + 1) Mod capacity\nConsole.WriteLine(rear)`,`int rear = 0; int capacity = 5; rear = (rear + 1) % capacity;\nConsole.WriteLine(rear);`,{both:['rear ← (rear + 1) MOD capacity','OUTPUT rear']});
add('32 Collision detection event',`Public Class Form1\n Private Sub Timer_Tick(sender As Object, e As EventArgs) Handles gameTimer.Tick\n  If player.Bounds.IntersectsWith(wall.Bounds) Then\n   gameTimer.Stop()\n  End If\n  Me.Invalidate()\n End Sub\nEnd Class`,`class Form1 : Form { System.Windows.Forms.Timer gameTimer = new(); public Form1() { gameTimer.Tick += Timer_Tick; } void Timer_Tick(object sender, EventArgs e) { if (player.Bounds.IntersectsWith(wall.Bounds)) { gameTimer.Stop(); } this.Invalidate(); } }`,{routine:'Timer_Tick',both:['ON gameTimer.Tick','EVENT HANDLER Timer_Tick','ME.Invalidate()'],edges:[...branch('player.Bounds.IntersectsWith(wall.Bounds)?','gameTimer.Stop()','ME.Invalidate()'),['gameTimer.Stop()','ME.Invalidate()']]});
add('33 Dijkstra relaxation step',`Sub Relax(distance() As Integer, visited() As Boolean, u As Integer, v As Integer, weight As Integer)\n If Not visited(v) Then\n  Dim candidate As Integer = distance(u) + weight\n  If candidate < distance(v) Then\n   distance(v) = candidate\n  End If\n End If\nEnd Sub`,`void Relax(int[] distance, bool[] visited, int u, int v, int weight) { if (!visited[v]) { int candidate = distance[u] + weight; if (candidate < distance[v]) { distance[v] = candidate; } } }`,{both:['distance[v] ← candidate','candidate < distance[v]'],edges:branch('candidate < distance[v]?','distance[v] ← candidate','END')});
add('34 A-star minimum score selection',`Function Choose(open As List(Of Node)) As Node\n Dim best As Node = open(0)\n For Each item As Node In open\n  If item.F < best.F Then\n   best = item\n  End If\n Next\n Return best\nEnd Function`,`Node Choose(List<Node> open) { Node best = open[0]; foreach (Node item in open) { if (item.F < best.F) best = item; } return best; }`,{both:['open[0]','IF item.F < best.F THEN','RETURN best'],edges:[...branch('item.F < best.F?','best ← item','Next item available?'),['best ← item','Next item available?'],['RETURN best','END']]});
add('35 Connect Four directional check',`Function Four(grid(,) As Integer, x As Integer, y As Integer) As Boolean\n Dim token As Integer = grid(x, y)\n If token = 0 Then Return False\n For i As Integer = 1 To 3\n  If grid(x + i, y) <> token Then Return False\n Next\n Return True\nEnd Function`,`bool Four(int[,] grid, int x, int y) { int token = grid[x, y]; if (token == 0) return false; for (int i = 1; i <= 3; i++) { if (grid[x + i, y] != token) return false; } return true; }`,{both:['grid[x, y]','grid[x + i, y] ≠ token','RETURN FALSE','RETURN TRUE'],edges:[['i ≤ 3?','RETURN TRUE','No'],['grid[x + i, y] ≠ token?','RETURN FALSE','Yes'],['RETURN TRUE','END']]});
add('36 Merge two sorted runs',`Sub Merge(left() As Integer, right() As Integer, output() As Integer, n As Integer, m As Integer)\n Dim i As Integer = 0\n Dim j As Integer = 0\n Dim k As Integer = 0\n While i < n AndAlso j < m\n  If left(i) <= right(j) Then\n   output(k) = left(i)\n   i = i + 1\n  Else\n   output(k) = right(j)\n   j = j + 1\n  End If\n  k = k + 1\n End While\n While i < n\n  output(k) = left(i)\n  i = i + 1\n  k = k + 1\n End While\n While j < m\n  output(k) = right(j)\n  j = j + 1\n  k = k + 1\n End While\nEnd Sub`,`void Merge(int[] left, int[] right, int[] output, int n, int m) { int i = 0; int j = 0; int k = 0; while (i < n && j < m) { if (left[i] <= right[j]) { output[k] = left[i]; i = i + 1; } else { output[k] = right[j]; j = j + 1; } k = k + 1; } while (i < n) { output[k] = left[i]; i = i + 1; k = k + 1; } while (j < m) { output[k] = right[j]; j = j + 1; k = k + 1; } }`,{both:['output[k] ← left[i]','output[k] ← right[j]','AND THEN'],edges:[...branch('left[i] ≤ right[j]?','output[k] ← left[i]','output[k] ← right[j]')]});

// Intentional language difference: VB saves the bound; C# re-tests it.
add('37 Mutable loop bound semantics',`Dim limit As Integer = 4
For i As Integer = 0 To limit
 limit = limit - 1
Next`, `int limit = 4;
for (int i = 0; i <= limit; i++) { limit = limit - 1; }`,{vbEdges:[['saved limit for i ← limit i ← 0','i ≤ saved limit for i?'],['i ← i + 1','i ≤ saved limit for i?']],csEdges:[['i ← 0','i ≤ limit?'],['i ← i + 1','i ≤ limit?']]});

const norm = s => String(s ?? '').replace(/\r/g,'').replace(/\s+/g,' ').trim();
const matches = (text, pattern, exact=false) => pattern instanceof RegExp ? pattern.test(norm(text)) : exact ? norm(text) === norm(pattern) : norm(text).includes(norm(pattern));
const describe = x => x instanceof RegExp ? x.toString() : JSON.stringify(x);

async function run(browser) {
  const page = await browser.newPage({acceptDownloads:true});
  const failures = [], errors = [];
  let passed = 0;
  const parity = new Map();
  const parityCases = new Set(["03", "04", "06", "10", "11", "14"]);
  page.on('pageerror', error => errors.push(error.message));
  try {
    await page.goto('http://127.0.0.1:8765/',{waitUntil:'domcontentloaded'});
    await page.getByRole('textbox',{name:'VB.NET code',exact:true}).waitFor({timeout:120000});
    const pseudo = page.getByRole('textbox',{name:'Editable pseudocode',exact:true});
    const chart = page.locator('.flow-viewport svg');
    const exportButton = page.getByRole('button',{name:'Export Draw.io XML'});
    for (const lang of ['vb','cs']) {
      await page.locator('#language').selectOption(lang);
      const code = page.getByRole('textbox',{name:lang==='vb'?'VB.NET code':'C# code',exact:true});
      for (const test of cases) {
        const name = `${lang.toUpperCase()} ${test.name}`;
        let text = '', nodes = [], edges = [];
        try {
          await code.fill(test[lang]);
          await page.getByRole('button',{name:'Convert code',exact:true}).click();
          // Wait for the current conversion to finish, not for an expected answer.
          await page.waitForFunction(()=>{const s=document.querySelector('.notes .status')?.textContent||''; return /^(Converted |Check the |Conversion failed:)/.test(s);},null,{timeout:15000});
          if (test.routine) {
            const select = page.locator('#routine');
            const options = await select.locator('option').evaluateAll(opts=>opts.map(o=>({value:o.value,text:o.textContent})));
            const match = options.find(o=>o.text.includes(test.routine));
            assert.ok(match, 'Missing routine '+test.routine+'; available: '+JSON.stringify(options));
            await select.selectOption(match.value);
            await page.waitForFunction(marker=>document.querySelector('.notes .status')?.textContent.includes(marker),test.routine,{timeout:10000});
          }
          text = await pseudo.inputValue();
          const status = await page.locator('.notes .status').textContent();
          assert.match(status,/^Converted /,'Conversion failed: '+status+' '+await page.locator('.notes').textContent());
          assert.ok(text.trim(), 'Empty pseudocode');
          assert.doesNotMatch(text,/UNSUPPORTED (?:VB\.NET|C#)/i,'Supported algorithm was not converted');
          await chart.waitFor({timeout:10000});
          assert.equal(await exportButton.isEnabled(),true,'Export disabled');
          const svgLabels = await chart.locator('text > title').allTextContents();
          const [download] = await Promise.all([page.waitForEvent('download',{timeout:10000}),exportButton.click()]);
          const path = await download.path();
          const xml = await fs.readFile(path,'utf8');
          await download.delete();
          const graph = await page.evaluate(xml=>{
            const doc = new DOMParser().parseFromString(xml,'application/xml');
            const pages = [...doc.querySelectorAll('diagram')];
            const cells = [...(pages[0]?.querySelectorAll('mxCell')||[])];
            const nodes = cells.filter(c=>c.getAttribute('vertex')==='1').map(c=>{
              const g=c.querySelector('mxGeometry');
              return {id:c.getAttribute('id'),label:c.getAttribute('value'),style:c.getAttribute('style')||'',geometry:['x','y','width','height'].map(k=>g?.hasAttribute(k)?Number(g.getAttribute(k)):null)};
            });
            const edges = cells.filter(c=>c.getAttribute('edge')==='1').map(c=>({source:c.getAttribute('source'),target:c.getAttribute('target'),label:c.getAttribute('value')||''}));
            return {valid:!doc.querySelector('parsererror'),pages:pages.length,ids:cells.map(c=>c.getAttribute('id')),nodes,edges,pseudo:pages[1]?.querySelector('mxCell[id="pseudocode"]')?.getAttribute('value')};
          },xml);
          nodes=graph.nodes; edges=graph.edges;
          assert.equal(graph.valid,true,'Invalid XML');
          assert.equal(graph.pages,2,'Expected flowchart and editable pseudocode pages');
          assert.equal(String(graph.pseudo).replace(/\r/g,''),text.replace(/\r/g,''),'Exported pseudocode differs from editor');
          assert.equal(new Set(graph.ids).size,graph.ids.length,'Duplicate XML cell IDs');
          assert.deepEqual(svgLabels.map(norm).sort(),nodes.map(n=>norm(n.label)).sort(),'SVG and XML node labels differ');
          assert.ok(nodes.length>=2,'Missing flowchart nodes');
          const ids = new Map(nodes.map(n=>[n.id,n]));
          assert.ok(nodes.some(n=>/^START\b/.test(n.label)&&n.style.includes('rounded=1')),'Missing start terminator');
          assert.ok(nodes.some(n=>n.label==='END'&&n.style.includes('rounded=1')),'Missing end terminator');
          for(const n of nodes) {
            assert.ok(n.geometry.every(Number.isFinite)&&n.geometry[2]>0&&n.geometry[3]>0,'Invalid node geometry: '+n.label);
            assert.ok(!n.style.includes('dashed=1'),'Unexpected unsupported flowchart node: '+n.label);
          }
          for(let i=0;i<nodes.length;i++) for(let j=i+1;j<nodes.length;j++) {
            const [x,y,w,h]=nodes[i].geometry, [a,b,c,d]=nodes[j].geometry;
            assert.ok(!(x<a+c && a<x+w && y<b+d && b<y+h),`Overlapping shapes: ${nodes[i].label} / ${nodes[j].label}`);
          }
          for(const e of edges) assert.ok(ids.has(e.source)&&ids.has(e.target),'Dangling connector '+JSON.stringify(e));
          for(const n of nodes.filter(n=>n.style.includes('rhombus;'))) {
            const outgoing=edges.filter(e=>e.source===n.id).map(e=>e.label);
            assert.ok(outgoing.includes('Yes')&&outgoing.includes('No'),'Decision missing Yes/No branches: '+n.label+' '+outgoing);
          }
          for(const n of nodes.filter(n=>/^OUTPUT\b/.test(n.label))) assert.ok(n.style.includes('shape=parallelogram'),'OUTPUT not input/output: '+n.label);
          for(const expected of [...(test.both||[]),...(test[lang+'Pseudo']||[])]) assert.ok(matches(text,expected),'Missing pseudocode: '+describe(expected));
          for(const expected of test.nodes||[]) assert.ok(nodes.some(n=>matches(n.label,expected,true)),'Missing graph node: '+describe(expected));
          for(const expected of test.io||[]) assert.ok(nodes.some(n=>matches(n.label,expected)&&n.style.includes('shape=parallelogram')),'Missing I/O symbol: '+describe(expected));
          for(const expected of test.calls||[]) assert.ok(nodes.some(n=>matches(n.label,expected)&&n.style.includes('shape=process;')),'Missing predefined process: '+describe(expected));
          for(const [from,to,label] of [...(test.edges||[]),...(test[lang+'Edges']||[])]) {
            assert.ok(edges.some(e=>matches(ids.get(e.source)?.label,from,true)&&matches(ids.get(e.target)?.label,to,true)&&(label===undefined||e.label===label)),`Missing directed connector ${describe(from)} -> ${describe(to)}${label?' ['+label+']':''}`);
          }
          // Compare entire labelled graphs for representative genuinely equivalent cases.
          if (parityCases.has(test.name.slice(0,2))) {
            const signature = {
              nodes: nodes.map(n=>norm(n.label)+'|'+(n.style.includes('rhombus;')?'decision':n.style.includes('shape=parallelogram')?'io':n.style.includes('rounded=1')?'terminal':'other')).sort(),
              edges: edges.map(e=>JSON.stringify([norm(ids.get(e.source).label),e.label,norm(ids.get(e.target).label)])).sort()
            };
            if(lang==='vb') parity.set(test.name,signature);
            else if(parity.has(test.name)) assert.deepEqual(signature,parity.get(test.name),'Cross-language graph differs for equivalent algorithm');
          }
          console.log('PASS: '+name); passed++;
        } catch (e) {
          const details = `${name}: ${e.message}\nPseudocode:\n${text}\nNodes: ${JSON.stringify(nodes.map(n=>n.label))}\nEdges: ${JSON.stringify(edges.map(e=>[nodes.find(n=>n.id===e.source)?.label,e.label,nodes.find(n=>n.id===e.target)?.label]))}`;
          failures.push(details); console.error('FAIL: '+details);
        }
      }
    }
    console.log(`Algorithm regression summary: ${passed}/${cases.length*2} passed; ${failures.length} failed.`);
    assert.deepEqual(errors,[],'Browser runtime errors');
    assert.equal(failures.length,0,failures.map(f=>f.split('\n')[0]).join('\n'));
  } finally { await page.close(); }
}
module.exports=run;
if(require.main===module) {
  const {chromium}=require('playwright');
  (async()=>{const browser=await chromium.launch({headless:true});try{await run(browser);}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
}
