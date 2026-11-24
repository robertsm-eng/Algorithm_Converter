Imports AlgorithmCore
Imports System.Linq
Imports System.Xml.Linq

Module Program
    Sub Main()
        Check("Dim x As Integer = 5" & vbLf & "If x >= 3 Then" & vbLf & "Console.WriteLine(""yes"")" & vbLf & "Else" & vbLf & "Console.WriteLine(""no"")" & vbLf & "End If", "IF x ≥ 3 THEN", "decision")
        Check("For i As Integer = 1 To 10" & vbLf & "If i = 4 Then Continue For" & vbLf & "Next", "ENDFOR", "decision")
        CheckForLoopFlowchart()
        
        Check("Do" & vbLf & "x += 1" & vbLf & "Loop Until x = 5", "UNTIL x = 5", "decision")
        Check("While x < 10" & vbLf & "x += 1" & vbLf & "End While", "ENDWHILE", "decision")
        Check("Select Case x" & vbLf & "Case 1" & vbLf & "Console.WriteLine(x)" & vbLf & "Case Else" & vbLf & "x = 2" & vbLf & "End Select", "OTHERWISE", "decision")
        Check("Sub Example()" & vbLf & "Console.WriteLine(""a < b & c"")" & vbLf & "End Sub", "a < b & c", "io")
        Dim invalid = Converter.Parse("If Then")
        Require(invalid.Errors.Count > 0, "Invalid syntax should be reported")
        Console.WriteLine("All smoke tests passed.")
    End Sub
Private Sub CheckForLoopFlowchart()
    Dim source = "For i As Integer = 1 To 5" & vbLf &
                 "Console.WriteLine(i)" & vbLf &
                 "Next"

    Dim result = Converter.Parse(source)
    Require(result.Errors.Count = 0, String.Join("; ", result.Errors))

    Dim g = Flowchart.Build(result.Routines(0))

    Require(g.Nodes.Any(Function(n) n.Label = "i ← 1"), "FOR loop should show initialiser")
    Require(g.Nodes.Any(Function(n) n.Label = "i ≤ 5?"), "FOR loop should show condition")
    Require(g.Nodes.Any(Function(n) n.Label = "i ← i + 1"), "FOR loop should show increment")

    Console.WriteLine("PASS: FOR loop flowchart structure")
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
    Private Sub Require(condition As Boolean, message As String)
        If Not condition Then Throw New Exception(message)
    End Sub
End Module
