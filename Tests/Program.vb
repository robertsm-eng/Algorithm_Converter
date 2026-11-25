Imports AlgorithmCore
Imports System.Linq
Imports System.Xml.Linq

Module Program
    Sub Main()
        Check("Dim x As Integer = 5" & vbLf & "If x >= 3 Then" & vbLf & "Console.WriteLine(""yes"")" & vbLf & "Else" & vbLf & "Console.WriteLine(""no"")" & vbLf & "End If", "IF x ≥ 3 THEN", "decision")
        Check("For i As Integer = 1 To 10" & vbLf & "If i = 4 Then Continue For" & vbLf & "Next", "ENDFOR", "decision")
        Check("Do" & vbLf & "x += 1" & vbLf & "Loop Until x = 5", "UNTIL x = 5", "decision")
        Check("While x < 10" & vbLf & "x += 1" & vbLf & "End While", "ENDWHILE", "decision")
        Check("Select Case x" & vbLf & "Case 1" & vbLf & "Console.WriteLine(x)" & vbLf & "Case Else" & vbLf & "x = 2" & vbLf & "End Select", "OTHERWISE", "decision")
        Check("Sub Example()" & vbLf & "Console.WriteLine(""a < b & c"")" & vbLf & "End Sub", "a < b & c", "io")
        CheckForLoopFlowchart()
        CheckConditionalLoop("While x < 10", "End While", False, False)
        CheckConditionalLoop("Do While x < 10", "Loop", False, False)
        CheckConditionalLoop("Do Until x < 10", "Loop", False, True)
        CheckConditionalLoop("Do", "Loop While x < 10", True, False)
        CheckConditionalLoop("Do", "Loop Until x < 10", True, True)
        CheckLoopControl()
        CheckEmptyLoop()
        CheckUnconditionalLoop()
        CheckLayout()
        CheckDisplay()
        CheckPseudocodeAccuracy()
        CheckDynamicFor()
        CheckArrayForFlowchart()
        CheckRoutineSignatures()
        CheckRoutineReturns()
        CheckRoutineCalls()
        CheckObjectRoutines()
        CheckAdvancedCollections()
        CheckFileHandling()
        CheckUsingCleanup()
        CheckCsvUsingRegression()
        CheckBlockLocalTypes()
        Dim invalid = Converter.Parse("If Then")
        Require(invalid.Errors.Count > 0, "Invalid syntax should be reported")
        Console.WriteLine("All smoke tests passed.")
    End Sub
    Private Sub Check(source As String, expected As String, kind As String)
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Require(result.Routines.Count = 1, "Expected one routine")
        Dim p = Converter.Pseudocode(result.Routines(0))
        Require(p.Contains(expected), "Missing pseudocode: " & expected & vbLf & p)
        Dim g = Flowchart.Build(result.Routines(0))
        Require(g.Nodes.Any(Function(n) n.Kind = kind), "Missing node kind " & kind)
        Dim xml = XDocument.Parse(g.ToDrawIo().ToString())
        Dim cells = xml.Descendants("mxCell").ToList()
        Dim ids = cells.Select(Function(c) CStr(c.Attribute("id"))).ToHashSet()
        Require(ids.Count = cells.Count, "Duplicate XML IDs")
        For Each edge In cells.Where(Function(c) c.Attribute("edge") IsNot Nothing)
            Require(ids.Contains(CStr(edge.Attribute("source"))) AndAlso ids.Contains(CStr(edge.Attribute("target"))), "Broken XML edge")
        Next
        Dim withText = XDocument.Parse(g.ToDrawIo(p).ToString())
        Require(withText.Root.Elements("diagram").Count() = 2, "Expected editable pseudocode page")
        Require(withText.Descendants("mxCell").Any(Function(c) CStr(c.Attribute("id")) = "pseudocode" AndAlso CStr(c.Attribute("value")) = p), "Pseudocode export did not round-trip")
        Console.WriteLine("PASS: " & expected)
    End Sub
    Private Sub CheckForLoopFlowchart()
        Dim g = GraphOf("For i As Integer = 1 To 5" & vbLf & "Console.WriteLine(i)" & vbLf & "Next")
        Require(g.Nodes.Any(Function(n) n.Label = "i ← 1"), "FOR loop should show initialiser")
        Require(g.Nodes.Any(Function(n) n.Label = "i ≤ 5?"), "FOR loop should show condition")
        Require(g.Nodes.Any(Function(n) n.Label = "i ← i + 1"), "FOR loop should show increment")
        Console.WriteLine("PASS: FOR loop flowchart structure")
    End Sub

    Private Function GraphOf(source As String) As Flowchart
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Require(result.Routines.Count = 1, "Expected one routine")
        Return Flowchart.Build(result.Routines(0))
    End Function

    Private Function NodeId(g As Flowchart, label As String) As String
        Return g.Nodes.Single(Function(n) n.Label = label).Id
    End Function

    Private Sub RequireEdge(g As Flowchart, source As String, target As String, label As String, back As Boolean)
        Require(g.Edges.Any(Function(e) e.Source = source AndAlso e.Target = target AndAlso e.Label = label AndAlso e.Back = back),
                "Missing edge: " & source & " -> " & target & " (" & label & ", back=" & back.ToString() & ")")
    End Sub

    Private Sub CheckConditionalLoop(header As String, footer As String, postTest As Boolean, untilLoop As Boolean)
        Dim g = GraphOf(header & vbLf & "Console.WriteLine(x)" & vbLf & footer)
        Dim start = NodeId(g, "START")
        Dim finish = NodeId(g, "END")
        Dim decision = NodeId(g, "x < 10?")
        Dim body = NodeId(g, "OUTPUT x")
        Require(g.Nodes.Count = 4, "Conditional loop should have exactly four nodes")
        RequireEdge(g, start, If(postTest, body, decision), "", False)
        RequireEdge(g, body, decision, "", Not postTest)
        RequireEdge(g, decision, body, If(untilLoop, "No", "Yes"), postTest)
        RequireEdge(g, decision, finish, If(untilLoop, "Yes", "No"), False)
        Dim xml = g.ToDrawIo()
        Require(xml.Descendants("mxCell").Count(Function(c) c.Attribute("edge") IsNot Nothing) = 4, "Expected four XML connectors")
        Console.WriteLine("PASS: " & header & " / " & footer)
    End Sub

    Private Sub CheckLoopControl()
        Dim g = GraphOf("While x < 10" & vbLf & "If x = 5 Then Continue While" & vbLf & "If x = 6 Then Exit While" & vbLf & "Console.WriteLine(x)" & vbLf & "End While" & vbLf & "Console.WriteLine(99)")
        Dim decision = NodeId(g, "x < 10?")
        RequireEdge(g, NodeId(g, "CONTINUE WHILE"), decision, "", True)
        RequireEdge(g, NodeId(g, "EXIT WHILE"), NodeId(g, "OUTPUT 99"), "", False)
        RequireEdge(g, NodeId(g, "OUTPUT x"), decision, "", True)
        Dim post = GraphOf("Do" & vbLf & "Continue Do" & vbLf & "Loop Until x = 10")
        RequireEdge(post, NodeId(post, "CONTINUE DO"), NodeId(post, "x = 10?"), "", False)
        Dim nested = GraphOf("While x < 10" & vbLf & "While y < 5" & vbLf & "Console.WriteLine(y)" & vbLf & "End While" & vbLf & "End While")
        RequireEdge(nested, NodeId(nested, "OUTPUT y"), NodeId(nested, "y < 5?"), "", True)
        RequireEdge(nested, NodeId(nested, "y < 5?"), NodeId(nested, "x < 10?"), "No", True)
        Console.WriteLine("PASS: loop control and nested loops")
    End Sub

    Private Sub CheckEmptyLoop()
        Dim g = GraphOf("While x < 10" & vbLf & "End While")
        Dim decision = NodeId(g, "x < 10?")
        RequireEdge(g, decision, decision, "Yes", True)
        RequireEdge(g, decision, NodeId(g, "END"), "No", False)
        Console.WriteLine("PASS: empty conditional loop")
    End Sub

    Private Sub CheckUnconditionalLoop()
        Dim g = GraphOf("Do" & vbLf & "Console.WriteLine(x)" & vbLf & "Loop")
        Dim entry = NodeId(g, "LOOP FOREVER")
        RequireEdge(g, entry, NodeId(g, "OUTPUT x"), "", False)
        RequireEdge(g, NodeId(g, "OUTPUT x"), entry, "", True)
        Require(Not g.Edges.Any(Function(e) e.Target = NodeId(g, "END")), "Unconditional loop should not have an automatic exit")
        Console.WriteLine("PASS: unconditional loop")
    End Sub

    Private Sub CheckLayout()
        Dim g = GraphOf(String.Join(vbLf, {
            "For i As Integer = 1 To 5",
            "    While j < 3",
            "        If j = 1 Then",
            "            Console.WriteLine(j)",
            "        Else",
            "            If i = 2 Then Continue While",
            "            Console.WriteLine(i)",
            "        End If",
            "        j += 1",
            "    End While",
            "Next",
            "Console.WriteLine(99)"}))
        Dim increment = g.Nodes.Single(Function(n) n.Label = "i ← i + 1")
        Dim afterLoop = g.Nodes.Single(Function(n) n.Label = "OUTPUT 99")
        Require(afterLoop.Y > increment.Y, "Loop continuation must be below the body")
        Require(afterLoop.X + afterLoop.Width \ 2 = increment.X + increment.Width \ 2, "Loop continuation must rejoin main lane")
        CheckGeometry(g)
        CheckGeometry(GraphOf("While x < 10" & vbLf & "End While"))
        CheckGeometry(GraphOf("Do" & vbLf & "Console.WriteLine(x)" & vbLf & "Loop Until x = 10"))
        CheckGeometry(GraphOf("If x = 1 Then" & vbLf & "Console.WriteLine(1)" & vbLf & "Else" & vbLf & "Console.WriteLine(2)" & vbLf & "End If" & vbLf & "Console.WriteLine(3)"))
        Console.WriteLine("PASS: layout, nesting, branch joins, connector geometry and XML waypoints")
    End Sub

    Private Sub CheckGeometry(g As Flowchart)
        Dim xml = g.ToDrawIo()
        For i = 0 To g.Edges.Count - 1
            Dim edge = g.Edges(i)
            Require(edge.Points.Count >= 2, "Every connector needs a route")
            For j = 1 To edge.Points.Count - 1
                Dim a = edge.Points(j - 1), b = edge.Points(j)
                Require(a.X = b.X OrElse a.Y = b.Y, "Connector is not orthogonal")
                For Each n In g.Nodes
                    If n.Id = edge.Source OrElse n.Id = edge.Target Then Continue For
                    Dim intersects As Boolean
                    If a.X = b.X Then
                        intersects = a.X > n.X AndAlso a.X < n.X + n.Width AndAlso Math.Min(a.Y, b.Y) < n.Y + n.Height AndAlso Math.Max(a.Y, b.Y) > n.Y
                    Else
                        intersects = a.Y > n.Y AndAlso a.Y < n.Y + n.Height AndAlso Math.Min(a.X, b.X) < n.X + n.Width AndAlso Math.Max(a.X, b.X) > n.X
                    End If
                    Require(Not intersects, "Connector crosses an unrelated shape: " & n.Label)
                Next
            Next
            Dim edgeId = "e" & i
            Dim cell = xml.Descendants("mxCell").Single(Function(c) CStr(c.Attribute("id")) = edgeId)
            Dim waypoints = cell.Descendants("mxPoint").ToList()
            Require(waypoints.Count = edge.Points.Count - 2, "XML did not preserve route waypoints")
            For j = 0 To waypoints.Count - 1
                Require(CInt(waypoints(j).Attribute("x")) = edge.Points(j + 1).X AndAlso CInt(waypoints(j).Attribute("y")) = edge.Points(j + 1).Y, "XML waypoint mismatch")
            Next
        Next
    End Sub

    Private Sub CheckDisplay()
        Dim g = GraphOf(String.Join(vbLf, {"Select Case x", "Case 1 To 3, 7", "    ShowResult()", "Case Is >= 10", "    Console.WriteLine(x)", "Case Else", "    Console.WriteLine(0)", "End Select"}))
        Require(g.Nodes.Any(Function(n) n.Label = "SELECT value ← x"), "SELECT should evaluate its selector once")
        Require(g.Nodes.Any(Function(n) n.Label = "(SELECT value ≥ 1 AND SELECT value ≤ 3) OR SELECT value = 7?"), "Missing range or alternative CASE comparison")
        Require(g.Nodes.Any(Function(n) n.Label = "SELECT value ≥ 10?"), "Missing relational CASE comparison")
        Require(g.Nodes.Any(Function(n) n.Kind = "call" AndAlso n.Label = "ShowResult()"), "Missing predefined process symbol")
        CheckGeometry(g)
        Dim eachGraph = GraphOf("For Each item As Integer In values" & vbLf & "Console.WriteLine(item)" & vbLf & "Next")
        Require(eachGraph.Nodes.Any(Function(n) n.Label = "item ← next item"), "FOR EACH should retrieve an item before its body")
        RequireEdge(eachGraph, NodeId(eachGraph, "Next item available?"), NodeId(eachGraph, "item ← next item"), "Yes", False)
        CheckGeometry(eachGraph)
        Dim longText = New String("a"c, 230)
        Dim largeGraph = GraphOf("Console.WriteLine(""" & longText & """)")
        Dim output = largeGraph.Nodes.Single(Function(n) n.Kind = "io")
        Require(output.Lines.Count > 3 AndAlso output.Height > 100, "Long labels should wrap and grow")
        Require(String.Concat(output.Lines).Contains(longText), "Long label was truncated")
        Console.WriteLine("PASS: SELECT CASE, FOR EACH, calls and long labels")
    End Sub

    Private Sub CheckAdvancedCollections()
        Dim source = String.Join(vbLf, {"Dim Grid(4, 4) As Integer", "Dim Players As New List(Of Player)", "Dim History As New Stack(Of String)", "Dim Pending As New Queue(Of Integer)", "Dim Scores As New Dictionary(Of String, Integer)", "Dim Seen As New HashSet(Of Integer)", "Grid(1, 2) = 7", "Players.Add(NewPlayer)", "Players.RemoveAt(0)", "Console.WriteLine(Players.Count)", "Console.WriteLine(Players(0))", "History.Push(""move"")", "Item = History.Pop()", "Console.WriteLine(History.Peek())", "Pending.Enqueue(7)", "Item = Pending.Dequeue()", "Scores(""Alice"") = 10", "If Scores.ContainsKey(""Alice"") Then", "Console.WriteLine(Scores(""Alice""))", "End If", "Seen.Add(7)", "Console.WriteLine(Seen.Contains(7))", "Console.WriteLine(Unknown(3))"})
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Dim routine = result.Routines.Single()
        Dim pseudo = Converter.Pseudocode(routine)
        Dim graph = Flowchart.Build(routine)
        Dim xml = graph.ToDrawIo(pseudo)
        Dim expected = {"DECLARE Grid : ARRAY[0..4, 0..4] OF INTEGER", "Players ← NEW LIST OF Player", "History ← NEW STACK OF STRING", "Pending ← NEW QUEUE OF INTEGER", "Scores ← NEW DICTIONARY OF (STRING, INTEGER)", "Seen ← NEW SET OF INTEGER", "Grid[1, 2] ← 7", "Players.Add(NewPlayer)", "Players.RemoveAt(0)", "OUTPUT Players.Count", "OUTPUT Players[0]", "History.Push(""move"")", "Item ← History.Pop()", "OUTPUT History.Peek()", "Pending.Enqueue(7)", "Item ← Pending.Dequeue()", "Scores[""Alice""] ← 10", "OUTPUT Scores[""Alice""]", "Seen.Add(7)", "OUTPUT Seen.Contains(7)", "OUTPUT Unknown(3)"}
        For Each label In expected
            Require(pseudo.Contains(label, StringComparison.Ordinal), "Missing collection pseudocode: " & label & vbLf & pseudo)
            Require(graph.Nodes.Any(Function(n) n.Label = label), "Missing collection node: " & label)
            Require(xml.Descendants("mxCell").Any(Function(c) CStr(c.Attribute("value")) = label), "Missing collection XML label: " & label)
        Next
        Require(graph.Nodes.Any(Function(n) n.Kind = "decision" AndAlso n.Label = "Scores.ContainsKey(""Alice"")?"), "Dictionary condition lost")
        Require(graph.Nodes.Any(Function(n) n.Kind = "call" AndAlso n.Label = "Players.Add(NewPlayer)"), "Collection method call symbol missing")
        Require(xml.Descendants("mxCell").Any(Function(c) CStr(c.Attribute("id")) = "pseudocode" AndAlso CStr(c.Attribute("value")) = pseudo), "Collection pseudocode export failed")
        CheckGeometry(graph)
        Dim parameterGraph = GraphOf(String.Join(vbLf, {"Sub Example(values As Dictionary(Of String, Integer), items As List(Of Integer))", "Console.WriteLine(values(""x""))", "Console.WriteLine(items.Count)", "Console.WriteLine(items(0))", "End Sub"}))
        Require(parameterGraph.Nodes.Any(Function(n) n.Label = "OUTPUT values[""x""]"), "Dictionary parameter indexing missing")
        Require(parameterGraph.Nodes.Any(Function(n) n.Label = "OUTPUT items.Count"), "Count property should be retained")
        Require(parameterGraph.Nodes.Any(Function(n) n.Label = "OUTPUT items[0]"), "List parameter indexing missing")
        Dim nested = GraphOf(String.Join(vbLf, {"Dim groups As New System.Collections.Generic.Dictionary(Of String, List(Of Integer))", "Console.WriteLine(groups(""red""))", "Console.WriteLine(groups.Count)"}))
        Require(nested.Nodes.Any(Function(n) n.Label = "groups ← NEW DICTIONARY OF (STRING, LIST OF INTEGER)"), "Nested generic type lost")
        Require(nested.Nodes.Any(Function(n) n.Label = "OUTPUT groups[""red""]"), "Qualified dictionary indexing missing")
        Require(nested.Nodes.Any(Function(n) n.Label = "OUTPUT groups.Count"), "Dictionary Count changed")
        Dim complex = Converter.Parse(String.Join(vbLf, {"Dim items As New List(Of Integer)(20)", "Dim other As New List(Of Integer) From {1, 2}", "Dim upper As Integer = 4", "Dim dynamicArray(upper) As Integer"}))
        Require(complex.Errors.Count = 0, String.Join("; ", complex.Errors))
        Dim complexPseudo = Converter.Pseudocode(complex.Routines.Single())
        Require(complexPseudo.Contains("List(Of Integer)(20)") AndAlso complexPseudo.Contains("From {1, 2}"), "Collection constructor details were lost")
        Require(complexPseudo.Contains("dynamicArray(upper)"), "Dynamic bound should retain explicit VB syntax")
        Require(complex.Warnings.Any(Function(w) w.Contains("Collection constructor arguments")), "Complex constructor warning missing")
        Console.WriteLine("PASS: advanced collections, array bounds, method notation and export")
    End Sub

    Private Sub CheckPseudocodeAccuracy()
        Dim source = String.Join(vbLf, {"Dim total As Integer = 0", "Dim name As String = ""Alice""", "Dim numbers(4) As Integer", "Dim values As New List(Of Integer)", "total += 5", "numbers(0) = total", "Console.WriteLine(numbers(0))", "Console.WriteLine(numbers.Length)", "Console.WriteLine(values.Count)", "Console.WriteLine(values(0))", "Console.WriteLine(name.Length)", "Console.WriteLine(name.ToUpper())", "Console.WriteLine(name.Substring(0, 3))", "Console.WriteLine(Math.Sqrt(total))", "Console.WriteLine(Compute(total))", "Console.WriteLine(""AndAlso >= Math.Sqrt(x) USERINPUT"")", "If total > 0 AndAlso total < 10 Then", "Console.WriteLine(total)", "End If"})
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Dim routine = result.Routines.Single()
        Dim pseudo = Converter.Pseudocode(routine)
        Dim graph = Flowchart.Build(routine)
        For Each expected In {"total ← 0", "total ← total + 5", "numbers[0] ← total", "OUTPUT numbers[0]", "OUTPUT LEN(numbers)", "OUTPUT values.Count", "OUTPUT values[0]", "OUTPUT LEN(name)", "OUTPUT TO_UPPER(name)", "OUTPUT SLICE(name, 0, 3)", "OUTPUT SQRT(total)", "OUTPUT Compute(total)", "OUTPUT ""AndAlso >= Math.Sqrt(x) USERINPUT"""}
            Require(pseudo.Contains(expected, StringComparison.Ordinal), "Missing pseudocode: " & expected & vbLf & pseudo)
            Require(graph.Nodes.Any(Function(n) n.Label = expected), "Missing flowchart label: " & expected)
        Next
        Require(pseudo.Contains("AND THEN"), "Short-circuit condition was lost")
        Require(graph.Nodes.Any(Function(n) n.Kind = "decision" AndAlso n.Label.Contains("AND THEN")), "Short-circuit condition missing in chart")
        Dim xml = graph.ToDrawIo(pseudo)
        Require(xml.Descendants("mxCell").Any(Function(c) CStr(c.Attribute("value")) = "OUTPUT LEN(name)"), "XML lost converted label")
        Dim unknown = GraphOf("Console.WriteLine(unknown.Length)" & vbLf & "Console.WriteLine(Find(2))")
        Require(unknown.Nodes.Any(Function(n) n.Label = "OUTPUT unknown.Length"), "Unknown property was incorrectly rewritten")
        Require(unknown.Nodes.Any(Function(n) n.Label = "OUTPUT Find(2)"), "Function was mistaken for indexing")
        Dim complex = Converter.Parse("items(GetIndex()) += 1")
        Require(complex.Warnings.Any(Function(w) w.Contains("evaluating the target twice")), "Complex compound assignment should warn")
        Require(Converter.Pseudocode(complex.Routines.Single()).Contains("+= 1"), "Complex compound assignment should be retained")
        Dim method = Converter.Parse("Function Answer() As Integer" & vbLf & "Return 42" & vbLf & "End Function")
        Require(Converter.Pseudocode(method.Routines.Single()).Contains("FUNCTION Answer() RETURNS INTEGER"), "Function header missing")
        Require(Converter.Pseudocode(method.Routines.Single()).Contains("ENDFUNCTION"), "Function footer missing")
        Console.WriteLine("PASS: pseudocode accuracy, types, strings, calls and export")
    End Sub

    Private Sub CheckDynamicFor()
        Dim graph = GraphOf("For i As Integer = StartValue() To LimitValue() Step StepValue()" & vbLf & "Console.WriteLine(i)" & vbLf & "Next")
        Dim setup = graph.Nodes.Single(Function(n) n.Label.Contains("StartValue()"))
        Require(setup.Label.Contains("LimitValue()") AndAlso setup.Label.Contains("StepValue()"), "FOR expressions must be captured at entry")
        Dim decision = graph.Nodes.Single(Function(n) n.Kind = "decision")
        Require(decision.Label.Contains("≥ 0 AND") AndAlso decision.Label.Contains("< 0 AND"), "Dynamic STEP must test both directions")
        Require(Not decision.Label.Contains("LimitValue()") AndAlso Not decision.Label.Contains("StepValue()"), "FOR bound/step should not be evaluated repeatedly")
        Dim descending = GraphOf("For i As Integer = 10 To 1 Step -2" & vbLf & "Console.WriteLine(i)" & vbLf & "Next")
        Require(descending.Nodes.Any(Function(n) n.Label = "i ≥ 1?"), "Descending comparison missing")
        Require(descending.Nodes.Any(Function(n) n.Label = "i ← i - 2"), "Descending increment missing")
        Console.WriteLine("PASS: FOR loop bounds and dynamic step")
    End Sub

    Private Sub CheckArrayForFlowchart()
        Dim source = String.Join(vbLf, {"Dim Total As Integer = 0", "Dim Name As String = ""Alice""", "Dim Numbers(4) As Integer", "For i As Integer = 0 To Numbers.Length - 1", "Numbers(i) = i * 2", "Total += Numbers(i)", "Next", "Console.WriteLine(Name.Length)", "Console.WriteLine(Name.Substring(0, 3))", "Console.WriteLine(Math.Sqrt(Total))"})
        Dim parsed = Converter.Parse(source)
        Require(parsed.Errors.Count = 0, String.Join("; ", parsed.Errors))
        Dim routine = parsed.Routines.Single()
        Dim graph = Flowchart.Build(routine)
        Dim setup = NodeId(graph, "i ← 0")
        Dim decision = NodeId(graph, "i ≤ LEN(Numbers) - 1?")
        Dim body = NodeId(graph, "Numbers[i] ← i * 2")
        Dim sum = NodeId(graph, "Total ← Total + (Numbers[i])")
        Dim increment = NodeId(graph, "i ← i + 1")
        Dim afterLoop = NodeId(graph, "OUTPUT LEN(Name)")
        Require(graph.Edges.Any(Function(e) e.Source = setup AndAlso e.Target = decision), "Missing initialisation path")
        Require(graph.Edges.Any(Function(e) e.Source = decision AndAlso e.Target = body AndAlso e.Label = "Yes"), "Missing loop entry")
        Require(graph.Edges.Any(Function(e) e.Source = body AndAlso e.Target = sum), "Missing loop body path")
        Require(graph.Edges.Any(Function(e) e.Source = sum AndAlso e.Target = increment), "Missing increment path")
        Require(graph.Edges.Any(Function(e) e.Source = increment AndAlso e.Target = decision AndAlso e.Back), "Missing loop return")
        Require(graph.Edges.Any(Function(e) e.Source = decision AndAlso e.Target = afterLoop AndAlso e.Label = "No"), "Missing exit path")
        Require(Not graph.Nodes.Any(Function(n) n.Label.Contains("saved ") OrElse n.Label.Contains("(loop ")), "Fixed local array requires no saved values")
        Require(Converter.Pseudocode(routine).Contains("FOR i ← 0 TO LEN(Numbers) - 1"), "Pseudocode changed unexpectedly")
        Dim xml = graph.ToDrawIo(Converter.Pseudocode(routine))
        Require(xml.Descendants("mxCell").Any(Function(c) CStr(c.Attribute("value")) = "i ≤ LEN(Numbers) - 1?"), "XML missing simplified condition")

        Dim resized = GraphOf(String.Join(vbLf, {"Dim Numbers(4) As Integer", "For i As Integer = 0 To Numbers.Length - 1", "ReDim Numbers(10)", "Next"}))
        Require(resized.Nodes.Any(Function(n) n.Label.Contains("saved limit for i ← LEN(Numbers) - 1")), "Resized array must capture original bound")
        Require(resized.Nodes.Any(Function(n) n.Label = "i ≤ saved limit for i?"), "Resized array must test captured bound")
        Require(resized.Nodes.Any(Function(n) n.Label = "i ← i + 1"), "Literal step should not use a temporary")
        Dim variableLimit = GraphOf(String.Join(vbLf, {"Dim n As Integer = 5", "For i As Integer = 0 To n", "n -= 1", "Next"}))
        Require(variableLimit.Nodes.Any(Function(n) n.Label.Contains("saved limit for i ← n")), "Mutable limit must be saved")
        Require(variableLimit.Nodes.Any(Function(n) n.Label = "i ← i + 1"), "Default step must stay explicit")
        Console.WriteLine("PASS: readable fixed-array loops and safe saved loop bounds")
    End Sub

    Private Sub CheckRoutineSignatures()
        Dim source = String.Join(vbLf, {"Private Function CalculateArea(ByVal Radius As Double) As Double", "Dim Area As Double = Math.PI * Radius ^ 2", "Return Area", "End Function"})
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Dim r = result.Routines.Single()
        Dim pseudo = Converter.Pseudocode(r)
        Require(pseudo.StartsWith("FUNCTION CalculateArea(BYVAL Radius : REAL) RETURNS REAL"), "Incorrect function signature: " & pseudo)
        Require(pseudo.Contains("PI * Radius ^ 2"), "Math.PI missing")
        Require(pseudo.EndsWith("ENDFUNCTION"), "Function footer missing")
        Dim g = Flowchart.Build(r)
        RequireEdge(g, NodeId(g, "RETURN Area"), NodeId(g, "END"), "", False)
        Require(g.Nodes.Any(Function(n) n.Kind = "terminal" AndAlso n.Label = "START" & vbLf & "CalculateArea(Radius)"), "Named entry missing")
        Require(g.Nodes.Where(Function(n) n.Kind = "return").Count() = 1, "Unexpected implicit result")
        CheckGeometry(g)
        Dim doc = g.ToDrawIo(pseudo)
        Require(doc.Descendants("mxCell").Any(Function(c) CStr(c.Attribute("id")) = "pseudocode" AndAlso CStr(c.Attribute("value")) = pseudo), "Routine pseudocode export failed")

        Dim subSource = String.Join(vbLf, {"Sub Update(ByRef score As Integer, names() As String, Optional amount As Integer = 1)", "score += amount", "Console.WriteLine(names(0))", "Exit Sub", "End Sub"})
        Dim subResult = Converter.Parse(subSource)
        Require(subResult.Errors.Count = 0, String.Join("; ", subResult.Errors))
        Dim subRoutine = subResult.Routines.Single()
        Dim subPseudo = Converter.Pseudocode(subRoutine)
        Require(subPseudo.StartsWith("SUBROUTINE Update(BYREF score : INTEGER, BYVAL names : ARRAY OF STRING, OPTIONAL BYVAL amount : INTEGER = 1)"), "Incorrect parameter signature: " & subPseudo)
        Require(subPseudo.Contains("OUTPUT names[0]"), "Array parameter indexing not translated")
        Require(subPseudo.EndsWith("ENDSUBROUTINE"), "Subroutine footer missing")
        Dim subGraph = Flowchart.Build(subRoutine)
        RequireEdge(subGraph, NodeId(subGraph, "RETURN"), NodeId(subGraph, "END"), "", False)
        Dim lists = Converter.Parse(String.Join(vbLf, {"Sub Example(values As List(Of Integer), grid As Integer(,))", "Console.WriteLine(values.Count)", "Console.WriteLine(grid(1, 2))", "End Sub"}))
        Require(lists.Errors.Count = 0, String.Join("; ", lists.Errors))
        Dim listPseudo = Converter.Pseudocode(lists.Routines.Single())
        Require(listPseudo.Contains("ARRAY (2 DIMENSIONS) OF INTEGER"), "Array rank missing")
        Require(listPseudo.Contains("OUTPUT values.Count") AndAlso listPseudo.Contains("OUTPUT grid[1, 2]"), "Parameter types not used for indexing")
        Console.WriteLine("PASS: routine headers, parameter passing, array parameters, named terminals and XML")
    End Sub

    Private Sub CheckRoutineReturns()
        Dim recursive = Converter.Parse(String.Join(vbLf, {"Function Factorial(n As Integer) As Integer", "If n <= 1 Then", "Return 1", "Else", "Return n * Factorial(n - 1)", "End If", "End Function"}))
        Require(recursive.Errors.Count = 0, String.Join("; ", recursive.Errors))
        Dim g = Flowchart.Build(recursive.Routines.Single())
        RequireEdge(g, NodeId(g, "RETURN 1"), NodeId(g, "END"), "", False)
        RequireEdge(g, NodeId(g, "RETURN n * Factorial(n - 1)"), NodeId(g, "END"), "", False)
        Require(Not g.Nodes.Any(Function(n) n.Label.Contains("current result")), "Exhaustive branches must not introduce fallthrough")
        CheckGeometry(g)
        Dim legacy = Converter.Parse(String.Join(vbLf, {"Function Answer(flag As Boolean) As Integer", "Answer = 42", "If flag Then Exit Function", "Answer = 99", "End Function"}))
        Require(legacy.Errors.Count = 0, String.Join("; ", legacy.Errors))
        Dim legacyGraph = Flowchart.Build(legacy.Routines.Single())
        Dim returns = legacyGraph.Nodes.Where(Function(n) n.Label = "RETURN current result of Answer (default if unset)").ToList()
        Require(returns.Count = 2, "Explicit Exit Function and implicit end must both return the current result")
        For Each ret In returns
            RequireEdge(legacyGraph, ret.Id, NodeId(legacyGraph, "END"), "", False)
        Next
        Require(legacy.Warnings.Any(Function(w) w.Contains("current function result")), "Legacy return needs a warning")
        Console.WriteLine("PASS: recursive expressions, early returns and VB function result semantics")
    End Sub

    Private Sub CheckRoutineCalls()
        Dim result = Converter.Parse(String.Join(vbLf, {"Module Example", "Sub Main()", "Dim x As Integer", "x = DoubleIt(3)", "ShowResult(x)", "End Sub", "Function DoubleIt(n As Integer) As Integer", "Return n * 2", "End Function", "Sub ShowResult(n As Integer)", "Console.WriteLine(n)", "End Sub", "End Module"}))
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Require(result.Routines.Count = 3, "Multiple routine discovery changed")
        Dim graph = Flowchart.Build(result.Routines(0))
        Require(graph.Nodes.Any(Function(n) n.Kind = "call" AndAlso n.Label = "x ← DoubleIt(3)"), "Direct function assignment needs predefined process")
        Require(graph.Nodes.Any(Function(n) n.Kind = "call" AndAlso n.Label = "ShowResult(x)"), "Subroutine call needs predefined process")
        Dim unknown = GraphOf("Dim values(4) As Integer" & vbLf & "x = values(0)")
        Require(unknown.Nodes.Any(Function(n) n.Kind = "process" AndAlso n.Label = "x ← values[0]"), "Indexing must not become a call")
        CheckGeometry(graph)
        Console.WriteLine("PASS: multiple routines, direct function calls and array access")
    End Sub

    Private Sub CheckObjectRoutines()
        Dim source = String.Join(vbLf, {"Public Class Entity", "Public Sub New()", "End Sub", "Public Overridable Sub Move()", "End Sub", "End Class", "Public Class Player", "Inherits Entity", "Private health As Integer", "Public Sub New(initial As Integer)", "MyBase.New()", "Me.health = initial", "End Sub", "Public Overrides Sub Move()", "MyBase.Move()", "End Sub", "Public Property Score As Integer", "Get", "Return Me.health", "End Get", "Set(value As Integer)", "Me.health = value", "End Set", "End Property", "End Class", "Module Demo", "Sub Main()", "Dim p As New Player(10)", "Dim e As Entity = New Player(20)", "p.Move()", "Console.WriteLine(""Me MyBase New"")", "End Sub", "End Module"})
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Require(result.Routines.Count = 7, "Expected separate constructors, methods, property accessors and Main")
        Dim ctor = result.Routines.Single(Function(r) r.Name.StartsWith("Player → Constructor("))
        Dim pseudo = Converter.Pseudocode(ctor)
        Require(pseudo.Contains("// CLASS Player INHERITS Entity"), "Missing inheritance context")
        Require(pseudo.Contains("// PRIVATE health : INTEGER"), "Missing field context")
        Require(pseudo.Contains("CONSTRUCTOR(BYVAL initial : INTEGER)"), "Missing constructor signature")
        Require(pseudo.Contains("ME.health ← initial"), "Missing ME assignment")
        Require(pseudo.Contains("PARENT.CONSTRUCTOR()"), "Missing base constructor call")
        Dim g = Flowchart.Build(ctor)
        Require(g.Nodes.Any(Function(n) n.Kind = "terminal" AndAlso n.Label = "START" & vbLf & "Player.New(initial)"), "Constructor start label missing")
        Require(g.Nodes.Any(Function(n) n.Kind = "call" AndAlso n.Label = "PARENT.CONSTRUCTOR()"), "Base constructor call symbol missing")
        Require(Not g.Nodes.Any(Function(n) n.Label.Contains("PRIVATE health")), "Field declaration must not execute at every routine entry")
        CheckGeometry(g)
        Dim move = result.Routines.Single(Function(r) r.Name.StartsWith("Player → Move"))
        Require(Converter.Pseudocode(move).Contains("OVERRIDE SUBROUTINE Move()"), "Override missing")
        Require(result.Routines.Any(Function(r) r.Name.StartsWith("Entity → Move")), "Class names must distinguish same-named methods")
        Require(Flowchart.Build(move).Nodes.Any(Function(n) n.Label = "PARENT.Move()" AndAlso n.Kind = "call"), "Base method call missing")
        Dim getter = result.Routines.Single(Function(r) r.Name = "Player → Score — GET")
        Dim setter = result.Routines.Single(Function(r) r.Name = "Player → Score — SET")
        Require(Converter.Pseudocode(getter).Contains("GET Score() RETURNS INTEGER"), "Getter signature missing")
        Require(Converter.Pseudocode(setter).Contains("SET Score(BYVAL value : INTEGER)"), "Setter signature missing")
        Dim getterGraph = Flowchart.Build(getter)
        RequireEdge(getterGraph, NodeId(getterGraph, "RETURN ME.health"), NodeId(getterGraph, "END"), "", False)
        Require(Flowchart.Build(setter).Nodes.Any(Function(n) n.Label = "ME.health ← value"), "Setter assignment missing")
        Dim main = result.Routines.Last()
        Dim mainGraph = Flowchart.Build(main)
        Require(mainGraph.Nodes.Any(Function(n) n.Label = "p ← NEW Player(10)" AndAlso n.Kind = "call"), "Object creation missing")
        Require(mainGraph.Nodes.Any(Function(n) n.Label = "DECLARE e : Entity ← NEW Player(20)"), "Declared base type lost")
        Require(mainGraph.Nodes.Any(Function(n) n.Label = "p.Move()" AndAlso n.Kind = "call"), "Object method call missing")
        Require(mainGraph.Nodes.Any(Function(n) n.Label = "OUTPUT ""Me MyBase New"""), "Object keywords must not change string literals")
        CheckGeometry(mainGraph)
        Dim xml = g.ToDrawIo(pseudo)
        Require(xml.Descendants("mxCell").Any(Function(c) CStr(c.Attribute("value")) = "START" & vbLf & "Player.New(initial)"), "Constructor XML label missing")
        Require(xml.Descendants("mxCell").Any(Function(c) CStr(c.Attribute("id")) = "pseudocode" AndAlso CStr(c.Attribute("value")) = pseudo), "OOP pseudocode XML did not round-trip")
        Console.WriteLine("PASS: object creation, class-qualified routines, inheritance, constructors and properties")
    End Sub

    Private Sub CheckFileHandling()
        Dim source = String.Join(vbLf, {"Dim path As String = ""data.csv""", "Dim reader As New StreamReader(path)", "Dim writer As New System.IO.StreamWriter(""output.csv"", True)", "While Not reader.EndOfStream", "Dim line As String = reader.ReadLine()", "writer.WriteLine(line)", "End While", "reader.Close()", "writer.Close()", "File.WriteAllText(""done.txt"", ""done"")"})
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Dim pseudo = Converter.Pseudocode(result.Routines(0))
        Dim g = Flowchart.Build(result.Routines(0))
        For Each label In {"reader ← OPENFILE(path, READ)", "writer ← OPENFILE(""output.csv"", APPEND)", "DECLARE line : STRING ← reader.ReadLine()", "writer.WriteLine(line)", "reader.Close()", "writer.Close()"}
            Require(pseudo.Contains(label) AndAlso g.Nodes.Any(Function(n) n.Label = label), "File notation missing: " & label)
        Next
        Require(g.Nodes.Any(Function(n) n.Kind = "io" AndAlso n.Label = "DECLARE line : STRING ← reader.ReadLine()"), "File read should use IO symbol")
        Require(g.Nodes.Any(Function(n) n.Kind = "io" AndAlso n.Label = "writer.WriteLine(line)"), "File write should use IO symbol")
        Require(g.Nodes.Any(Function(n) n.Kind = "io" AndAlso n.Label.StartsWith("File.WriteAllText(")), "Static file write should use IO symbol")
        Require(g.Nodes.Any(Function(n) n.Kind = "decision" AndAlso n.Label = "NOT reader.EndOfStream?"), "EOF condition lost")
        CheckGeometry(g)
        Dim advanced = Converter.Parse("Dim reader As New StreamReader(stream)")
        Require(Converter.Pseudocode(advanced.Routines(0)).Contains("NEW StreamReader(stream)"), "Ambiguous stream argument must not become a file path")
        Require(advanced.Warnings.Any(Function(w) w.Contains("Stream constructor retained")), "Ambiguous stream constructor needs a warning")
        Dim write = GraphOf("Dim writer As New StreamWriter(""test.txt"", False)")
        Require(write.Nodes.Any(Function(n) n.Label = "writer ← OPENFILE(""test.txt"", WRITE)"), "Overwrite mode lost")
        Console.WriteLine("PASS: file open modes, input/output, EOF and conservative overloads")
    End Sub

    Private Sub CheckUsingCleanup()
        Dim source = String.Join(vbLf, {"Using reader As New StreamReader(""data.txt"")", "While Not reader.EndOfStream", "Dim line As String = reader.ReadLine()", "Console.WriteLine(line)", "End While", "End Using", "Console.WriteLine(""done"")"})
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Dim pseudo = Converter.Pseudocode(result.Routines(0))
        Require(pseudo.Contains("USING reader ← OPENFILE(""data.txt"", READ)") AndAlso pseudo.Contains("ENDUSING // Automatically dispose reader on exit"), "USING lifetime missing")
        Dim g = Flowchart.Build(result.Routines(0))
        Dim disposal = NodeId(g, "reader.Dispose()")
        RequireEdge(g, NodeId(g, "NOT reader.EndOfStream?"), disposal, "No", False)
        RequireEdge(g, disposal, NodeId(g, "OUTPUT ""done"""), "", False)
        Require(g.Nodes.Any(Function(n) n.Kind = "io" AndAlso n.Label.Contains("reader.ReadLine()")), "USING resource type lost")
        CheckGeometry(g)
        Require(g.ToDrawIo(pseudo).Descendants("mxCell").Any(Function(c) CStr(c.Attribute("value")) = "reader.Dispose()"), "XML disposal node missing")

        Dim nested = GraphOf(String.Join(vbLf, {"Function ReadFirst() As String", "Using reader As New StreamReader(""data.txt"")", "Using writer As New StreamWriter(""log.txt"")", "Return reader.ReadLine()", "End Using", "End Using", "End Function"}))
        Dim ret = NodeId(nested, "RETURN reader.ReadLine()")
        Dim inner = NodeId(nested, "writer.Dispose()")
        Dim outer = NodeId(nested, "reader.Dispose()")
        RequireEdge(nested, ret, inner, "", False)
        RequireEdge(nested, inner, outer, "", False)
        RequireEdge(nested, outer, NodeId(nested, "END"), "", False)
        CheckGeometry(nested)

        Dim exitGraph = GraphOf(String.Join(vbLf, {"While running", "Using reader As New StreamReader(""data.txt"")", "Exit While", "End Using", "End While", "Console.WriteLine(""done"")"}))
        Dim exitCleanup = NodeId(exitGraph, "reader.Dispose()")
        RequireEdge(exitGraph, NodeId(exitGraph, "EXIT WHILE"), exitCleanup, "", False)
        RequireEdge(exitGraph, exitCleanup, NodeId(exitGraph, "OUTPUT ""done"""), "", False)
        CheckGeometry(exitGraph)

        Dim continueGraph = GraphOf(String.Join(vbLf, {"While running", "Using reader As New StreamReader(""data.txt"")", "Continue While", "End Using", "End While"}))
        Dim continueCleanup = NodeId(continueGraph, "reader.Dispose()")
        RequireEdge(continueGraph, NodeId(continueGraph, "CONTINUE WHILE"), continueCleanup, "", False)
        RequireEdge(continueGraph, continueCleanup, NodeId(continueGraph, "running?"), "", True)
        CheckGeometry(continueGraph)

        Dim inside = GraphOf(String.Join(vbLf, {"Using reader As New StreamReader(""data.txt"")", "While running", "Continue While", "End While", "End Using"}))
        RequireEdge(inside, NodeId(inside, "CONTINUE WHILE"), NodeId(inside, "running?"), "", True)
        RequireEdge(inside, NodeId(inside, "running?"), NodeId(inside, "reader.Dispose()"), "No", False)
        CheckGeometry(inside)
        Dim unsupported = Converter.Parse(String.Join(vbLf, {"Using reader", "Console.WriteLine(reader.ReadLine())", "End Using"}))
        Require(unsupported.Errors.Count = 0 AndAlso unsupported.Warnings.Any(Function(w) w.Contains("Unsupported USING")), "Unsupported resource form must be flagged")
        Require(Flowchart.Build(unsupported.Routines(0)).Nodes.Any(Function(n) n.Kind = "unsupported"), "Unsupported USING should remain opaque")
        Console.WriteLine("PASS: USING normal, return, exit, continue and nested cleanup paths")
    End Sub

    Private Sub CheckCsvUsingRegression()
        Dim source = String.Join(vbLf, {
            "Dim InputPath As String = ""Scores.csv""",
            "Dim OutputPath As String = ""Passed.csv""",
            "",
            "Using Reader As New StreamReader(InputPath)",
            "    Using Writer As New StreamWriter(OutputPath)",
            "",
            "        While Not Reader.EndOfStream",
            "            Dim Line As String = Reader.ReadLine()",
            "            Dim Fields() As String = Line.Split("","")",
            "",
            "            Dim Name As String = Fields(0)",
            "            Dim Score As Integer = CInt(Fields(1))",
            "",
            "            If Score >= 50 Then",
            "                Writer.WriteLine(Name)",
            "            End If",
            "        End While",
            "",
            "    End Using",
            "End Using",
            "",
            "Console.WriteLine(""Processing complete"")"})
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Require(result.Routines.Count = 1, "Expected a single CSV routine")
        Dim pseudo = Converter.Pseudocode(result.Routines(0))
        Console.WriteLine(pseudo)
        Require(Not pseudo.Contains("UNSUPPORTED VB.NET"), "CSV source must not become an opaque block")
        For Each expected In {"USING Reader ← OPENFILE(InputPath, READ)", "USING Writer ← OPENFILE(OutputPath, WRITE)", "WHILE NOT Reader.EndOfStream", "Fields[0]", "Fields[1]", "IF Score ≥ 50 THEN", "Writer.WriteLine(Name)"}
            Require(pseudo.Contains(expected), "CSV pseudocode missing: " & expected)
        Next
        Dim g = Flowchart.Build(result.Routines(0))
        Require(Not g.Nodes.Any(Function(n) n.Kind = "unsupported"), "CSV flowchart contains opaque block")
        Require(g.Nodes.Any(Function(n) n.Kind = "io" AndAlso n.Label.Contains("Reader.ReadLine()")), "CSV read symbol missing")
        Require(g.Nodes.Any(Function(n) n.Kind = "io" AndAlso n.Label = "Writer.WriteLine(Name)"), "CSV write symbol missing")
        RequireEdge(g, NodeId(g, "NOT Reader.EndOfStream?"), NodeId(g, "Writer.Dispose()"), "No", False)
        RequireEdge(g, NodeId(g, "Writer.Dispose()"), NodeId(g, "Reader.Dispose()"), "", False)
        RequireEdge(g, NodeId(g, "Reader.Dispose()"), NodeId(g, "OUTPUT ""Processing complete"""), "", False)
        Require(pseudo.Contains("DECLARE Fields : ARRAY OF STRING ← Line.Split("",""" & ")"), "CSV array declaration not translated")
        Require(g.Nodes.Any(Function(n) n.Label.Contains("Fields[0]")), "CSV flowchart array index missing")
        Require(g.ToDrawIo(pseudo).Descendants("mxCell").Any(Function(c) If(CStr(c.Attribute("value")), "").Contains("Fields[1]")), "CSV XML array index missing")
        Console.WriteLine("PASS: exact nested CSV USING regression")
    End Sub

    Private Sub CheckBlockLocalTypes()
        Dim source = String.Join(vbLf, {"If flag Then", "Dim values(3) As Integer", "Console.WriteLine(values(0))", "Else", "Dim values As String = ""abc""", "Console.WriteLine(values.Length)", "End If", "Console.WriteLine(values(0))"})
        Dim g = GraphOf(source)
        Require(g.Nodes.Any(Function(n) n.Label = "OUTPUT values[0]"), "Array in a nested block not recognised")
        Require(g.Nodes.Any(Function(n) n.Label = "OUTPUT LEN(values)"), "Sibling string type not recognised")
        Require(g.Nodes.Any(Function(n) n.Label = "OUTPUT values(0)"), "Block-local array type leaked outside its scope")
        Console.WriteLine("PASS: block-local types and sibling scope isolation")
    End Sub

    Private Sub Require(condition As Boolean, message As String)
        If Not condition Then Throw New Exception(message)
    End Sub
End Module
