Imports System.Linq
Imports System.Collections.Generic
Imports System.Xml.Linq

Public Class FlowNode
    Public Property Id As String
    Public Property Label As String
    Public Property Kind As String
    Public Property X As Integer
    Public Property Y As Integer
    Public Property Width As Integer = 280
    Public Property Height As Integer = 76
End Class

Public Class FlowEdge
    Public Property Source As String
    Public Property Target As String
    Public Property Label As String
    Public Property Back As Boolean
End Class

Public Class Flowchart
    Public ReadOnly Nodes As New List(Of FlowNode)
    Public ReadOnly Edges As New List(Of FlowEdge)
    Private nextId As Integer
    Private endId As String
    Private Class LoopTarget
        Public Property Kind As String
        Public Property ExitId As String
        Public Property ContinueId As String
    End Class

    Public Shared Function Build(routine As Routine) As Flowchart
        Dim g As New Flowchart
        g.endId = g.AddNode("END", "terminal")
        Dim first = g.BuildList(routine.Body, g.endId, New List(Of LoopTarget))
        Dim start = g.AddNode("START", "terminal")
        g.Connect(start, first)
        g.Layout(start)
        Return g
    End Function

    Private Function AddNode(label As String, kind As String) As String
        nextId += 1
        Dim id = "n" & nextId.ToString()
        Dim h = Math.Max(76, Math.Min(240, 32 + CInt(Math.Ceiling(label.Length / 30.0)) * 17))
        Nodes.Add(New FlowNode With {.Id = id, .Label = label, .Kind = kind, .Height = If(kind = "decision", Math.Max(115, h + 35), h)})
        Return id
    End Function

    Private Sub Connect(source As String, target As String, Optional label As String = "", Optional back As Boolean = False)
        If target <> "" Then Edges.Add(New FlowEdge With {.Source = source, .Target = target, .Label = label, .Back = back})
    End Sub

    Private Function BuildList(items As List(Of Instruction), following As String, targets As List(Of LoopTarget)) As String
        Dim current = following
        For i = items.Count - 1 To 0 Step -1
            current = BuildItem(items(i), current, targets)
        Next
        Return current
    End Function

    Private Function BuildItem(x As Instruction, following As String, targets As List(Of LoopTarget)) As String
        Select Case x.Kind
            Case "if"
                Dim no = BuildList(x.Other, following, targets)
                For i = x.Branches.Count - 1 To 0 Step -1
                    Dim b = x.Branches(i)
                    Dim d = AddNode(b.Text & "?", "decision")
                    Connect(d, BuildList(b.Body, following, targets), "Yes")
                    Connect(d, no, "No")
                    no = d
                Next
                Dim id = AddNode(x.Text & "?", "decision")
                Connect(id, BuildList(x.Body, following, targets), "Yes")
                Connect(id, no, "No")
                Return id
            Case "for"
                Dim setupLabel = If(x.LoopInitialiser <> "", x.LoopInitialiser, x.Text)
                Dim testLabel = If(x.LoopCondition <> "", x.LoopCondition & "?", "Next iteration available?")
                Dim incrementLabel = If(x.LoopIncrement <> "", x.LoopIncrement, "Advance loop iterator")
                Dim decision = AddNode(testLabel, "decision")
                Dim incrementNode = AddNode(incrementLabel, "process")
                Connect(incrementNode, decision, "", True)
                Dim nested As New List(Of LoopTarget)(targets)
                nested.Add(New LoopTarget With {.Kind = "for", .ExitId = following, .ContinueId = incrementNode})
                Dim body = BuildList(x.Body, incrementNode, nested)
                Connect(decision, body, "Yes")
                Connect(decision, following, "No")
                Dim setup = AddNode(setupLabel, "process")
                Connect(setup, decision)
                Return setup
            Case "foreach"
                Dim decision = AddNode("Next item available?", "decision")
                Dim repeat = AddNode("Advance to next item", "process")
                Connect(repeat, decision, "", True)
                Dim nested As New List(Of LoopTarget)(targets)
                nested.Add(New LoopTarget With {.Kind = "for", .ExitId = following, .ContinueId = repeat})
                Dim body = BuildList(x.Body, repeat, nested)
                Connect(decision, body, "Yes")
                Connect(decision, following, "No")
                Dim setup = AddNode(x.Text, "process")
                Connect(setup, decision)
                Return setup
            Case "while", "do"
                Return BuildConditionalLoop(x, following, targets)
            Case "select"
                Dim nested As New List(Of LoopTarget)(targets)
                nested.Add(New LoopTarget With {.Kind = "select", .ExitId = following, .ContinueId = following})
                Dim no = following
                Dim setup = AddNode("Evaluate SELECT " & x.Text, "process")
                For i = x.Branches.Count - 1 To 0 Step -1
                    Dim b = x.Branches(i)
                    Dim body = BuildList(b.Body, following, nested)
                    If b.Kind = "else" Then
                        no = body
                    Else
                        Dim d = AddNode("Matches CASE " & b.Text & "?", "decision")
                        Connect(d, body, "Yes")
                        Connect(d, no, "No")
                        no = d
                    End If
                Next
                Connect(setup, no)
                Return setup
            Case Else
                Dim id = AddNode(x.Text, x.Kind)
                If x.Kind = "return" Then
                    Connect(id, endId)
                ElseIf x.Kind = "exit" OrElse x.Kind = "continue" Then
                    Dim t = targets.LastOrDefault(Function(z) z.Kind = x.Detail)
                    Connect(id, If(t Is Nothing, following, If(x.Kind = "exit", t.ExitId, t.ContinueId)))
                Else
                    Connect(id, following)
                End If
                Return id
        End Select
    End Function

    Private Function BuildConditionalLoop(x As Instruction, following As String, targets As List(Of LoopTarget)) As String
        Dim unconditional = x.Text = ""
        Dim decision = AddNode(If(unconditional, "LOOP FOREVER", x.Text & "?"), If(unconditional, "process", "decision"))
        Dim nested As New List(Of LoopTarget)(targets)
        nested.Add(New LoopTarget With {.Kind = x.Kind, .ExitId = following, .ContinueId = decision})
        Dim firstBodyEdge = Edges.Count
        Dim body = BuildList(x.Body, decision, nested)
        Dim repeatLabel = If(unconditional, "", If(x.IsUntil, "No", "Yes"))
        Dim exitLabel = If(x.IsUntil, "Yes", "No")

        If x.PostTest AndAlso Not unconditional Then
            ' Enter the body first. The condition repeats by going back to its entry.
            Connect(decision, body, repeatLabel, True)
            Connect(decision, following, exitLabel)
            Return body
        End If

        ' Every path from this body to its pre-test is a loop-back edge.
        ' This includes the ends of nested branches and explicit CONTINUE statements.
        For Each edge In Edges.Skip(firstBodyEdge)
            If edge.Target = decision Then edge.Back = True
        Next
        Connect(decision, body, repeatLabel, body = decision)
        If Not unconditional Then Connect(decision, following, exitLabel)
        Return decision
    End Function

    Private Sub Layout(start As String)
        ' Longest forward path gives joins room below both branches. Cyclic edges are ignored.
        Dim levels As New Dictionary(Of String, Integer) From {{start, 0}}
        For pass = 1 To Nodes.Count
            Dim changed = False
            For Each e In Edges.Where(Function(v) Not v.Back)
                If Not levels.ContainsKey(e.Source) Then Continue For
                Dim proposed = levels(e.Source) + 1
                If Not levels.ContainsKey(e.Target) OrElse levels(e.Target) < proposed Then
                    levels(e.Target) = proposed
                    changed = True
                End If
            Next
            If Not changed Then Exit For
        Next
        Dim last = If(levels.Count = 0, 0, levels.Values.Max())
        For Each n In Nodes.Where(Function(v) Not levels.ContainsKey(v.Id))
            last += 1
            levels(n.Id) = last
        Next
        Dim y = 40
        For Each group In Nodes.GroupBy(Function(n) levels(n.Id)).OrderBy(Function(v) v.Key)
            Dim col = 0
            For Each n In group.OrderByDescending(Function(v) Integer.Parse(v.Id.Substring(1)))
                n.X = 40 + col * 360
                n.Y = y
                col += 1
            Next
            y += group.Max(Function(n) n.Height) + 85
        Next
    End Sub

    Public Function ToDrawIo(Optional pseudocode As String = Nothing) As XDocument
        Dim root As New XElement("root", New XElement("mxCell", New XAttribute("id", "0")), New XElement("mxCell", New XAttribute("id", "1"), New XAttribute("parent", "0")))
        For Each n In Nodes
            Dim style = "whiteSpace=wrap;html=0;strokeColor=#334155;fillColor=#ffffff;fontColor=#172033;"
            Select Case n.Kind
                Case "terminal" : style &= "rounded=1;arcSize=50;fillColor=#dcfce7;"
                Case "decision" : style &= "rhombus;fillColor=#fef3c7;"
                Case "io" : style &= "shape=parallelogram;perimeter=parallelogramPerimeter;fillColor=#dbeafe;"
                Case "unsupported" : style &= "dashed=1;fillColor=#fee2e2;"
            End Select
            root.Add(New XElement("mxCell", New XAttribute("id", n.Id), New XAttribute("value", n.Label), New XAttribute("style", style), New XAttribute("vertex", "1"), New XAttribute("parent", "1"), New XElement("mxGeometry", New XAttribute("x", n.X), New XAttribute("y", n.Y), New XAttribute("width", n.Width), New XAttribute("height", n.Height), New XAttribute("as", "geometry"))))
        Next
        For i = 0 To Edges.Count - 1
            Dim e = Edges(i)
            Dim style = "edgeStyle=orthogonalEdgeStyle;rounded=0;html=0;endArrow=block;endFill=1;"
            If e.Source = e.Target Then
                style = "edgeStyle=loopEdgeStyle;orthogonalLoop=1;rounded=0;html=0;endArrow=block;endFill=1;exitX=0.5;exitY=1;entryX=0;entryY=0.5;"
            ElseIf e.Back Then
                style &= "exitX=0;exitY=0.5;entryX=0;entryY=0.5;"
            End If
            root.Add(New XElement("mxCell", New XAttribute("id", "e" & i), New XAttribute("value", e.Label), New XAttribute("style", style), New XAttribute("edge", "1"), New XAttribute("parent", "1"), New XAttribute("source", e.Source), New XAttribute("target", e.Target), New XElement("mxGeometry", New XAttribute("relative", "1"), New XAttribute("as", "geometry"))))
        Next
        Dim file As New XElement("mxfile", New XAttribute("host", "app.diagrams.net"), New XElement("diagram", New XAttribute("id", "algorithm"), New XAttribute("name", "Flowchart"), New XElement("mxGraphModel", New XAttribute("grid", "1"), New XAttribute("gridSize", "10"), New XAttribute("page", "0"), root)))
        If pseudocode IsNot Nothing Then
            Dim lineCount = pseudocode.Split(ChrW(10)).Length
            Dim textCell As New XElement("mxCell", New XAttribute("id", "pseudocode"), New XAttribute("value", pseudocode), New XAttribute("vertex", "1"), New XAttribute("parent", "1"), New XAttribute("style", "text;html=0;align=left;verticalAlign=top;whiteSpace=wrap;rounded=0;fontFamily=Consolas;fontSize=14;spacing=12;"), New XElement("mxGeometry", New XAttribute("x", 40), New XAttribute("y", 40), New XAttribute("width", 900), New XAttribute("height", Math.Max(120, lineCount * 22 + 40)), New XAttribute("as", "geometry")))
            Dim textRoot As New XElement("root", New XElement("mxCell", New XAttribute("id", "0")), New XElement("mxCell", New XAttribute("id", "1"), New XAttribute("parent", "0")), textCell)
            file.Add(New XElement("diagram", New XAttribute("id", "pseudo"), New XAttribute("name", "Editable pseudocode"), New XElement("mxGraphModel", New XAttribute("page", "0"), textRoot)))
        End If
        Return New XDocument(file)
    End Function
End Class
