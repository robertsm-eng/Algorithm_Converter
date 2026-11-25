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

    Private Sub CheckPseudocodeAccuracy()
        Dim source = String.Join(vbLf, {"Dim total As Integer = 0", "Dim name As String = ""Alice""", "Dim numbers(4) As Integer", "Dim values As New List(Of Integer)", "total += 5", "numbers(0) = total", "Console.WriteLine(numbers(0))", "Console.WriteLine(numbers.Length)", "Console.WriteLine(values.Count)", "Console.WriteLine(values(0))", "Console.WriteLine(name.Length)", "Console.WriteLine(name.ToUpper())", "Console.WriteLine(name.Substring(0, 3))", "Console.WriteLine(Math.Sqrt(total))", "Console.WriteLine(Compute(total))", "Console.WriteLine(""AndAlso >= Math.Sqrt(x) USERINPUT"")", "If total > 0 AndAlso total < 10 Then", "Console.WriteLine(total)", "End If"})
        Dim result = Converter.Parse(source)
        Require(result.Errors.Count = 0, String.Join("; ", result.Errors))
        Dim routine = result.Routines.Single()
        Dim pseudo = Converter.Pseudocode(routine)
        Dim graph = Flowchart.Build(routine)
        For Each expected In {"total ← 0", "total ← total + 5", "numbers[0] ← total", "OUTPUT numbers[0]", "OUTPUT LEN(numbers)", "OUTPUT LEN(values)", "OUTPUT values[0]", "OUTPUT LEN(name)", "OUTPUT TO_UPPER(name)", "OUTPUT SLICE(name, 0, 3)", "OUTPUT SQRT(total)", "OUTPUT Compute(total)", "OUTPUT ""AndAlso >= Math.Sqrt(x) USERINPUT"""}
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
        Require(Converter.Pseudocode(method.Routines.Single()).Contains("SUBROUTINE Answer()"), "Subroutine header missing")
        Require(Converter.Pseudocode(method.Routines.Single()).Contains("ENDSUBROUTINE"), "Subroutine footer missing")
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

    Private Sub Require(condition As Boolean, message As String)
        If Not condition Then Throw New Exception(message)
    End Sub
End Module
