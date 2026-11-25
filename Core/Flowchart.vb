Imports System.Linq
Imports System.Collections.Generic
Imports System.Xml.Linq
Imports System.Text.RegularExpressions

Public Class FlowNode
    Public Property Id As String
    Public Property Label As String
    Public Property Kind As String
    Public Property X As Integer
    Public Property Y As Integer
    Public Property Width As Integer = 280
    Public Property Height As Integer = 76
    Public Property Lines As New List(Of String)
End Class

Public Class FlowEdge
    Public Property Source As String
    Public Property Target As String
    Public Property Label As String
    Public Property Back As Boolean
    Public ReadOnly Points As New List(Of FlowPoint)
    Public Property SourcePort As String = "bottom"
    Public Property TargetPort As String = "top"
    Public Property LabelX As Integer
    Public Property LabelY As Integer
End Class

Public Class Flowchart
    Public ReadOnly Nodes As New List(Of FlowNode)
    Public ReadOnly Edges As New List(Of FlowEdge)
    Friend ReadOnly Regions As New List(Of FlowRegion)
    Private nextId As Integer
    Private endId As String
    Private exceptionEndId As String
    Private Class LoopTarget
        Public Property Kind As String
        Public Property ExitId As String
        Public Property ContinueId As String
        Public Property Cleanups As New List(Of CleanupScope)
    End Class

    Private Class CleanupScope
        Public Property Resource As String
        Public ReadOnly Routes As New Dictionary(Of String, String)
    End Class

    Private Function CleanupEntry(scope As CleanupScope, target As String) As String
        Dim entry As String = Nothing
        If scope.Routes.TryGetValue(target, entry) Then Return entry
        entry = AddNode(scope.Resource & ".Dispose()", "call")
        scope.Routes.Add(target, entry)
        Connect(entry, target)
        Return entry
    End Function

    Private Function TransferTarget(target As String, scopes As List(Of CleanupScope)) As String
        ' Construct outer disposal first so the runtime path executes inner disposal first.
        For i = scopes.Count - 1 To 0 Step -1
            target = CleanupEntry(scopes(i), target)
        Next
        Return target
    End Function

    Public Shared Function Build(routine As Routine) As Flowchart
        Dim g As New Flowchart
        Dim endLabel = If(String.IsNullOrWhiteSpace(routine.ExitLabel), "END", routine.ExitLabel)
        g.endId = g.AddNode(endLabel, "terminal")
        Dim targets As New List(Of LoopTarget) From {New LoopTarget With {.Kind = "routine-return", .ExitId = g.endId}}
        Dim first = g.BuildList(routine.Body, g.endId, targets)
        Dim start = g.AddNode(GetEntryLabel(routine), "terminal")
        g.Connect(start, first)
        If Not String.IsNullOrEmpty(g.exceptionEndId) Then g.RemoveUnreachable(start)
        FlowLayout.Arrange(g, start)
        Return g
    End Function

    ' An unconditional throw can leave unused normal/finally branches. Remove them
    ' before layout; include loop-back edges when finding executable paths.
    Private Sub RemoveUnreachable(start As String)
        Dim reachable As New HashSet(Of String) From {start}
        Dim pending As New Queue(Of String)
        Dim outgoing = Edges.ToLookup(Function(e) e.Source)
        pending.Enqueue(start)
        While pending.Count > 0
            For Each edge In outgoing(pending.Dequeue())
                If reachable.Add(edge.Target) Then pending.Enqueue(edge.Target)
            Next
        End While
        Nodes.RemoveAll(Function(n) Not reachable.Contains(n.Id))
        Edges.RemoveAll(Function(e) Not reachable.Contains(e.Source) OrElse Not reachable.Contains(e.Target))
        Regions.RemoveAll(Function(r) Not reachable.Contains(r.EntryId) OrElse Not reachable.Contains(r.FollowingId))
        For Each region In Regions
            region.Members.RemoveAll(Function(id) Not reachable.Contains(id))
        Next
    End Sub

    ' Prefer the converted signature, but recover the routine name and parameters
    ' from the original Roslyn header if no named entry was supplied.
    Private Shared Function GetEntryLabel(routine As Routine) As String
        Dim label = If(String.IsNullOrWhiteSpace(routine.EntryLabel), "START", routine.EntryLabel)
        If Not String.Equals(label.Trim(), "START", StringComparison.OrdinalIgnoreCase) Then Return label
        If String.IsNullOrWhiteSpace(routine.Header) Then Return "START"

        Dim tree = Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(
            "Module EntryLabelModule" & vbLf & routine.Header & vbLf & "End Module")
        Dim method = tree.GetRoot().DescendantNodes().OfType(Of Microsoft.CodeAnalysis.VisualBasic.Syntax.MethodStatementSyntax)().FirstOrDefault()
        If method IsNot Nothing AndAlso Not method.Identifier.IsMissing AndAlso method.Identifier.Text <> "" Then
            Dim names As New List(Of String)
            If method.ParameterList IsNot Nothing Then
                For Each parameter In method.ParameterList.Parameters
                    names.Add(parameter.Identifier.Identifier.Text)
                Next
            End If
            Return "START" & vbLf & method.Identifier.Text & "(" & String.Join(", ", names) & ")"
        End If

        ' Preserve plain START for snippets and unsupported routine headers.
        Return "START"
    End Function

    Private Function AddNode(label As String, kind As String) As String
        nextId += 1
        Dim id = "n" & nextId.ToString()
        Dim node As New FlowNode With {.Id = id, .Label = label, .Kind = kind}
        FlowLayout.SizeNode(node)
        Nodes.Add(node)
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
        Dim firstNode = Nodes.Count
        Dim entry = BuildItemCore(x, following, targets)
        If {"for", "foreach", "while", "do"}.Contains(x.Kind) Then
            Regions.Add(New FlowRegion With {.EntryId = entry, .FollowingId = following,
                        .Members = Nodes.Skip(firstNode).Select(Function(n) n.Id).ToList()})
        End If
        Return entry
    End Function

    Private Function BuildItemCore(x As Instruction, following As String, targets As List(Of LoopTarget)) As String
        Select Case x.Kind
            Case "try"
                Return BuildTry(x, following, targets)
            Case "using"
                Dim scope As New CleanupScope With {.Resource = x.Detail}
                Dim cleanup = CleanupEntry(scope, following)
                Dim nested As New List(Of LoopTarget)
                For Each t In targets
                    Dim wrapped As New LoopTarget With {.Kind = t.Kind, .ExitId = t.ExitId, .ContinueId = t.ContinueId, .Cleanups = New List(Of CleanupScope)(t.Cleanups)}
                    wrapped.Cleanups.Insert(0, scope)
                    nested.Add(wrapped)
                Next
                Dim body = BuildList(x.Body, cleanup, nested)
                ' Do not show an unused normal cleanup after an unconditional early exit.
                If body <> cleanup AndAlso Not Edges.Any(Function(e) e.Target = cleanup) Then
                    Edges.RemoveAll(Function(e) e.Source = cleanup)
                    Nodes.RemoveAll(Function(v) v.Id = cleanup)
                End If
                Dim setup = AddNode(x.Text, "call")
                Connect(setup, body)
                Return setup
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
                Dim parts = Regex.Match(x.Text, "^FOR EACH\s+(.+?)\s+IN\s+(.+)$", RegexOptions.IgnoreCase)
                Dim variable = If(parts.Success, Regex.Replace(parts.Groups(1).Value, "\s+As\s+.+$", "", RegexOptions.IgnoreCase), "loop variable")
                Dim collection = If(parts.Success, parts.Groups(2).Value, x.Text)
                Dim decision = AddNode("Next item available?", "decision")
                Dim nested As New List(Of LoopTarget)(targets)
                nested.Add(New LoopTarget With {.Kind = "for", .ExitId = following, .ContinueId = decision})
                Dim firstEdge = Edges.Count
                Dim body = BuildList(x.Body, decision, nested)
                Dim retrieve = AddNode(variable & " ← next item", "process")
                Connect(retrieve, body)
                For Each edge In Edges.Skip(firstEdge)
                    If edge.Target = decision Then edge.Back = True
                Next
                Connect(decision, retrieve, "Yes")
                Connect(decision, following, "No")
                Dim setup = AddNode("Create iterator for " & collection, "process")
                Connect(setup, decision)
                Return setup
            Case "while", "do"
                Return BuildConditionalLoop(x, following, targets)
            Case "select"
                Dim nested As New List(Of LoopTarget)(targets)
                nested.Add(New LoopTarget With {.Kind = "select", .ExitId = following, .ContinueId = following})
                Dim no = following
                ' Evaluate the selector once, especially when it is a function call.
                Dim selector = "SELECT value"
                Dim setup = AddNode(selector & " ← " & x.Text, "process")
                For i = x.Branches.Count - 1 To 0 Step -1
                    Dim b = x.Branches(i)
                    Dim body = BuildList(b.Body, following, nested)
                    If b.Kind = "else" Then
                        no = body
                    Else
                        Dim d = AddNode(CaseCondition(selector, b.Text) & "?", "decision")
                        Connect(d, body, "Yes")
                        Connect(d, no, "No")
                        no = d
                    End If
                Next
                Connect(setup, no)
                Return setup
            Case Else
                Dim id = AddNode(DisplayLabel(x), DisplayKind(x))
                If x.Kind = "throw" Then
                    Connect(id, PropagateException(targets))
                ElseIf x.Kind = "return" Then
                    Dim t = targets.Last(Function(z) z.Kind = "routine-return")
                    Connect(id, TransferTarget(t.ExitId, t.Cleanups))
                ElseIf x.Kind = "exit" OrElse x.Kind = "continue" Then
                    Dim t = targets.LastOrDefault(Function(z) z.Kind = x.Detail)
                    If t Is Nothing Then
                        Connect(id, following)
                    Else
                        Connect(id, TransferTarget(If(x.Kind = "exit", t.ExitId, t.ContinueId), t.Cleanups))
                    End If
                Else
                    Connect(id, following)
                End If
                Return id
        End Select
    End Function

    ' The exceptional end is separate from a successful routine return.
    Private Function PropagateException(targets As List(Of LoopTarget)) As String
        If String.IsNullOrEmpty(exceptionEndId) Then exceptionEndId = AddNode("EXCEPTION PROPAGATES", "terminal")
        Dim t = targets.Last(Function(z) z.Kind = "routine-return")
        Return TransferTarget(exceptionEndId, t.Cleanups)
    End Function

    Private Function BuildTry(x As Instruction, following As String, targets As List(Of LoopTarget)) As String
        If Not x.ExpandExceptionFlow Then
            Dim opaque = AddNode("TRY block - control flow not expanded (see pseudocode)", "unsupported")
            Connect(opaque, following, "If completes normally")
            Return opaque
        End If
        Dim failure = PropagateException(targets)
        Dim normal = following
        Dim pendingException = failure
        If x.HasFinally AndAlso x.Other.Count > 0 Then
            ' Finally executes on both routes. An exception from Finally itself propagates,
            ' rather than being caught by handlers belonging to the same Try.
            normal = BuildExceptionSequence(x.Other, following, failure, following)
            pendingException = BuildExceptionSequence(x.Other, failure, failure, failure)
            Dim normalEntry = AddNode("FINALLY (normal completion)", "process")
            Connect(normalEntry, normal)
            normal = normalEntry
            Dim exceptionalEntry = AddNode("FINALLY (exception pending)", "process")
            Connect(exceptionalEntry, pendingException)
            pendingException = exceptionalEntry
        End If
        Dim dispatch = pendingException
        For i = x.Branches.Count - 1 To 0 Step -1
            Dim handler = x.Branches(i)
            ' Exceptions in Catch are not caught by another handler of this same Try.
            Dim handlerBody = BuildExceptionSequence(handler.Body, normal, pendingException, normal)
            If handler.CatchVariable <> "" Then
                Dim bind = AddNode(handler.CatchVariable & " ← caught exception", "process")
                Connect(bind, handlerBody)
                handlerBody = bind
            End If
            If handler.CatchType = "" Then
                dispatch = handlerBody
            Else
                Dim test = AddNode("Exception is " & handler.CatchType & "?", "decision")
                Connect(test, handlerBody, "Yes")
                Connect(test, dispatch, "No")
                dispatch = test
            End If
        Next
        Return BuildExceptionSequence(x.Body, normal, dispatch, normal)
    End Function

    ' Each ordinary operation has two possible outcomes. The diamond describes an
    ' exceptional transfer, not an extra source-code conditional. A throw never falls through.
    Private Function BuildExceptionSequence(items As List(Of Instruction), normal As String, exceptional As String, exitTry As String) As String
        Dim count = items.Count
        For i = 0 To items.Count - 1
            If items(i).Kind = "throw" OrElse items(i).Kind = "exit" Then
                count = i + 1
                Exit For
            End If
        Next
        Dim current = normal
        For i = count - 1 To 0 Step -1
            Dim item = items(i)
            Dim id = AddNode(DisplayLabel(item), If(item.Kind = "throw" OrElse item.Kind = "exit", "process", DisplayKind(item)))
            If item.Kind = "throw" Then
                Connect(id, exceptional)
            ElseIf item.Kind = "exit" Then
                Connect(id, exitTry)
            ElseIf current = exceptional Then
                Connect(id, current)
            Else
                Dim outcome = AddNode("Exception raised?", "decision")
                Connect(id, outcome)
                Connect(outcome, current, "No")
                Connect(outcome, exceptional, "Yes")
            End If
            current = id
        Next
        Return current
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

    Private Shared Function DisplayLabel(x As Instruction) As String
        ' Only simplify a single, explicitly initialized variable declaration.
        If x.Kind = "process" OrElse x.Kind = "io" Then
            Dim m = Regex.Match(x.Text, "^DECLARE\s+(\w+)\s+As\s+([\w.]+)\s*←\s*(.+)$", RegexOptions.IgnoreCase)
            If m.Success AndAlso Not m.Groups(3).Value.Contains(",") Then
                Return m.Groups(1).Value & " ← " & m.Groups(3).Value
            End If
        End If
        Return x.Text
    End Function

    Private Shared Function DisplayKind(x As Instruction) As String
        ' A conservative recognition of standalone calls, not calls in assignments.
        If x.Kind = "process" Then
            Dim text = x.Text
            If Not text.Contains("←") AndAlso
                (Regex.IsMatch(text, "^(?:Call\s+)?[\w.]+\s*\(.*\)$", RegexOptions.IgnoreCase) OrElse
                 Regex.IsMatch(text, "^Call\s+[\w.]+$", RegexOptions.IgnoreCase)) Then Return "call"
        End If
        Return x.Kind
    End Function

    ' Split CASE alternatives only at top-level commas, outside strings and nested expressions.
    Private Shared Function CaseCondition(selector As String, text As String) As String
        If text.Contains("#") OrElse text.Contains("$" & ChrW(34)) Then Return selector & " matches CASE " & text
        Dim clauses As New List(Of String)
        Dim start = 0
        Dim depth = 0
        Dim quoted = False
        For i = 0 To text.Length - 1
            Dim ch = text(i)
            If ch = """"c Then
                quoted = Not quoted
            ElseIf Not quoted Then
                If ch = "("c OrElse ch = "["c OrElse ch = "{"c Then depth += 1
                If ch = ")"c OrElse ch = "]"c OrElse ch = "}"c Then depth -= 1
                If depth = 0 Then
                    If ch = ","c Then
                        clauses.Add(text.Substring(start, i - start).Trim())
                        start = i + 1
                    End If
                End If
            End If
        Next
        clauses.Add(text.Substring(start).Trim())
        ' Interpret individual alternatives.
        Dim tests As New List(Of String)
        For Each clause In clauses
            Dim relation = Regex.Match(clause, "^(?:Is\s+)?(≥|≤|≠|>=|<=|<>|=|>|<)\s*(.+)$", RegexOptions.IgnoreCase)
            If relation.Success Then
                tests.Add(selector & " " & relation.Groups(1).Value & " " & CaseOperand(relation.Groups(2).Value))
            Else
                Dim split = TopLevelTo(clause)
                If split >= 0 Then
                    tests.Add("(" & selector & " ≥ " & CaseOperand(clause.Substring(0, split)) & " AND " & selector & " ≤ " & CaseOperand(clause.Substring(split + 4)) & ")")
                Else
                    tests.Add(selector & " = " & CaseOperand(clause))
                End If
            End If
        Next
        Return String.Join(" OR ", tests)
    End Function

    Private Shared Function CaseOperand(text As String) As String
        text = text.Trim()
        If Regex.IsMatch(text, "^[+-]?\d+(?:\.\d+)?$|^[\w.]+$|^""(?:""""|[^""])*""$") Then Return text
        Return "(" & text & ")"
    End Function

    Private Shared Function TopLevelTo(text As String) As Integer
        Dim depth = 0
        Dim quoted = False
        For i = 0 To text.Length - 1
            Dim ch = text(i)
            If ch = """"c Then
                quoted = Not quoted
            ElseIf Not quoted Then
                If ch = "("c OrElse ch = "["c OrElse ch = "{"c Then depth += 1
                If ch = ")"c OrElse ch = "]"c OrElse ch = "}"c Then depth -= 1
                If depth = 0 AndAlso i > 0 AndAlso i + 4 <= text.Length AndAlso text.Substring(i, 4).Equals(" To ", StringComparison.OrdinalIgnoreCase) Then Return i
            End If
        Next
        Return -1
    End Function

    Public Function ToDrawIo(Optional pseudocode As String = Nothing) As XDocument
        Dim root As New XElement("root", New XElement("mxCell", New XAttribute("id", "0")), New XElement("mxCell", New XAttribute("id", "1"), New XAttribute("parent", "0")))
        For Each n In Nodes
            Dim style = "whiteSpace=wrap;html=0;strokeColor=#334155;fillColor=#ffffff;fontColor=#172033;fontFamily=Consolas;fontSize=13;"
            Select Case n.Kind
                Case "terminal" : style &= "rounded=1;arcSize=50;fillColor=#dcfce7;"
                Case "decision" : style &= "rhombus;fillColor=#fef3c7;"
                Case "io" : style &= "shape=parallelogram;perimeter=parallelogramPerimeter;fillColor=#dbeafe;"
                Case "call" : style &= "shape=process;size=0.07;"
                Case "unsupported" : style &= "dashed=1;fillColor=#fee2e2;"
            End Select
            root.Add(New XElement("mxCell", New XAttribute("id", n.Id), New XAttribute("value", n.Label), New XAttribute("style", style), New XAttribute("vertex", "1"), New XAttribute("parent", "1"), New XElement("mxGeometry", New XAttribute("x", n.X), New XAttribute("y", n.Y), New XAttribute("width", n.Width), New XAttribute("height", n.Height), New XAttribute("as", "geometry"))))
        Next
        For i = 0 To Edges.Count - 1
            Dim e = Edges(i)
            Dim style = "noEdgeStyle=1;edgeStyle=none;orthogonal=1;rounded=0;html=0;endArrow=block;endFill=1;" &
                FlowLayout.PortStyle(e.SourcePort, True) & FlowLayout.PortStyle(e.TargetPort, False)
            Dim geometry As New XElement("mxGeometry", New XAttribute("relative", "1"), New XAttribute("as", "geometry"))
            If e.Points.Count > 2 Then
                geometry.Add(New XElement("Array", New XAttribute("as", "points"),
                    e.Points.Skip(1).Take(e.Points.Count - 2).Select(Function(pt) New XElement("mxPoint", New XAttribute("x", pt.X), New XAttribute("y", pt.Y)))))
            End If
            root.Add(New XElement("mxCell", New XAttribute("id", "e" & i), New XAttribute("value", e.Label), New XAttribute("style", style), New XAttribute("edge", "1"), New XAttribute("parent", "1"), New XAttribute("source", e.Source), New XAttribute("target", e.Target), geometry))
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
