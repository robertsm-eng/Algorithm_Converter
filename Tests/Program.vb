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

    Private Sub Require(condition As Boolean, message As String)
        If Not condition Then Throw New Exception(message)
    End Sub
End Module
