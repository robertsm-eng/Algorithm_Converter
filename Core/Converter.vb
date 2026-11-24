Imports System.Linq
Imports System.Collections.Generic
Imports System.Text.RegularExpressions
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Public Class Routine
    Public Property Name As String
    Public Property Header As String
    Public Property Body As List(Of Instruction)
    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Class Instruction
    Public Property Kind As String = "process"
    Public Property Text As String = ""
    Public Property Detail As String = ""
    Public Property Body As New List(Of Instruction)
    Public Property Other As New List(Of Instruction)
    Public Property Branches As New List(Of Instruction)
    Public Property IsUntil As Boolean
    Public Property PostTest As Boolean

    Public Property LoopInitialiser As String = ""
    Public Property LoopCondition As String = ""
    Public Property LoopIncrement As String = ""
End Class

Public Class ParseResult
    Public Property Routines As New List(Of Routine)
    Public Property Warnings As New List(Of String)
    Public Property Errors As New List(Of String)
End Class

Public NotInheritable Class Converter
    Private Sub New()
    End Sub

    Private Shared Function K(n As SyntaxNode) As String
        Return CType(n.RawKind, SyntaxKind).ToString()
    End Function

    Private Shared Function IsRoutine(n As SyntaxNode) As Boolean
        Return {"SubBlock", "FunctionBlock", "ConstructorBlock", "OperatorBlock", "GetAccessorBlock", "SetAccessorBlock", "AddHandlerAccessorBlock", "RemoveHandlerAccessorBlock", "RaiseEventAccessorBlock"}.Contains(K(n))
    End Function

    Public Shared Function Parse(source As String) As ParseResult
        Dim result As New ParseResult
        If String.IsNullOrWhiteSpace(source) Then
            result.Errors.Add("Enter VB.NET code first.")
            Return result
        End If
        If source.Length > 200000 Then
            result.Errors.Add("Please convert a smaller selection (maximum 200,000 characters).")
            Return result
        End If
        Dim candidates = {source, "Module SnippetModule" & vbLf & source & vbLf & "End Module", "Module SnippetModule" & vbLf & "Sub Main()" & vbLf & source & vbLf & "End Sub" & vbLf & "End Module"}
        Dim chosen As SyntaxTree = Nothing
        Dim offset As Integer = 0
        Dim best As SyntaxTree = Nothing
        Dim bestOffset As Integer = 0
        Dim bestCount As Integer = Integer.MaxValue
        For i As Integer = 0 To candidates.Length - 1
            Dim tree = VisualBasicSyntaxTree.ParseText(candidates(i))
            Dim errors = tree.GetDiagnostics().Count(Function(d) d.Severity = DiagnosticSeverity.Error)
            Dim hasRoutine = tree.GetRoot().DescendantNodes().Any(Function(n) IsRoutine(n))
            If errors < bestCount Then
                best = tree
                bestCount = errors
                bestOffset = i
            End If
            If errors = 0 AndAlso hasRoutine Then
                chosen = tree
                offset = i
                Exit For
            End If
        Next
        If chosen Is Nothing Then
            For Each d In best.GetDiagnostics().Where(Function(x) x.Severity = DiagnosticSeverity.Error).Take(25)
                Dim line = Math.Max(1, d.Location.GetLineSpan().StartLinePosition.Line + 1 - bestOffset)
                result.Errors.Add($"Line {line}: {d.GetMessage()}")
            Next
            If result.Errors.Count = 0 Then result.Errors.Add("No executable subroutine or statements were found.")
            Return result
        End If
        For Each n In chosen.GetRoot().DescendantNodes().Where(Function(x) IsRoutine(x))
            Dim children = n.ChildNodes().ToList()
            If children.Count < 2 Then Continue For
            Dim header = Clean(children(0).ToString())
            Dim routineName = header
            Dim m = Regex.Match(header, "\b(?:Sub|Function|Operator|Get|Set)\s+([\w\[\]]+)", RegexOptions.IgnoreCase)
            If m.Success Then routineName = m.Groups(1).Value & " — " & header
            If offset = 2 AndAlso routineName.StartsWith("Main —", StringComparison.OrdinalIgnoreCase) Then routineName = "Code snippet"
            Dim r As New Routine With {.Name = routineName, .Header = If(offset = 2, "", header), .Body = ReadList(children.Skip(1).Take(children.Count - 2), result)}
            result.Routines.Add(r)
        Next
        If chosen.GetRoot().DescendantNodes().Any(Function(n) K(n).Contains("LambdaExpression")) Then result.Warnings.Add("Lambda expressions are retained as VB.NET expressions, not expanded into separate algorithms.")
        result.Warnings = result.Warnings.Distinct().ToList()
        Return result
    End Function

    Private Shared Function Clean(s As String) As String
        Return OutsideStrings(s, Function(x) Regex.Replace(x, "[\r\n\t]+|[ ]{2,}", " ")).Trim()
    End Function

    ' Protect quoted strings (including VB doubled quotes) from keyword substitutions.
    Private Shared Function OutsideStrings(s As String, transform As Func(Of String, String)) As String
        Dim pieces = Regex.Split(s, "(""(?:""""|[^""])*"")")
        For i = 0 To pieces.Length - 1 Step 2
            pieces(i) = transform(pieces(i))
        Next
        Return String.Concat(pieces)
    End Function

    Private Shared Function Expr(s As String) As String
        Return OutsideStrings(Clean(s), Function(x)
            x = Regex.Replace(x, "\bConsole\s*\.\s*ReadLine\s*\(\s*\)", "USERINPUT", RegexOptions.IgnoreCase)
            x = Regex.Replace(x, "\bAndAlso\b|\bAnd\b", "AND", RegexOptions.IgnoreCase)
            x = Regex.Replace(x, "\bOrElse\b|\bOr\b", "OR", RegexOptions.IgnoreCase)
            x = Regex.Replace(x, "\b(Not|Xor|Mod|True|False)\b", Function(m) m.Value.ToUpperInvariant(), RegexOptions.IgnoreCase)
            Return x.Replace("<>", "≠").Replace(">=", "≥").Replace("<=", "≤")
        End Function)
    End Function

    Private Shared Function Condition(header As SyntaxNode) As String
        Dim e = header.ChildNodes().OfType(Of ExpressionSyntax)().FirstOrDefault()
        If e IsNot Nothing Then Return Expr(e.ToString())
        Dim clause = header.ChildNodes().FirstOrDefault(Function(n) K(n).EndsWith("Clause", StringComparison.Ordinal))
        If clause IsNot Nothing Then
            e = clause.ChildNodes().OfType(Of ExpressionSyntax)().FirstOrDefault()
            If e IsNot Nothing Then Return Expr(e.ToString())
        End If
        Return ""
    End Function

    Private Shared Function ReadList(nodes As IEnumerable(Of SyntaxNode), result As ParseResult) As List(Of Instruction)
        Dim items As New List(Of Instruction)
        For Each n In nodes
            items.Add(ReadNode(n, result))
        Next
        Return items
    End Function

    Private Shared Function ReadNode(n As SyntaxNode, result As ParseResult) As Instruction
        Dim kind = K(n)
        Dim c = n.ChildNodes().ToList()
        Dim raw = Clean(n.ToString())
        Dim item As New Instruction
        Select Case kind
            Case "MultiLineIfBlock"
                item.Kind = "if"
                item.Text = Condition(c(0))
                item.Body = ReadList(c.Skip(1).Take(c.Count - 2).Where(Function(x) K(x) <> "ElseIfBlock" AndAlso K(x) <> "ElseBlock"), result)
                For Each branch In c.Where(Function(x) K(x) = "ElseIfBlock" OrElse K(x) = "ElseBlock")
                    Dim b = branch.ChildNodes().ToList()
                    If K(branch) = "ElseBlock" Then
                        item.Other = ReadList(b.Skip(1), result)
                    Else
                        item.Branches.Add(New Instruction With {.Kind = "if", .Text = Condition(b(0)), .Body = ReadList(b.Skip(1), result)})
                    End If
                Next
            Case "SingleLineIfStatement"
                item.Kind = "if"
                item.Text = Condition(n)
                item.Body = ReadList(c.OfType(Of StatementSyntax)(), result)
                Dim other = c.FirstOrDefault(Function(x) K(x) = "SingleLineElseClause")
                If other IsNot Nothing Then item.Other = ReadList(other.ChildNodes().OfType(Of StatementSyntax)(), result)
            Case "WhileBlock"
                item.Kind = "while"
                item.Text = Condition(c(0))
                item.Body = ReadList(c.Skip(1).Take(c.Count - 2), result)
            Case "SimpleDoLoopBlock", "DoWhileLoopBlock", "DoUntilLoopBlock", "DoLoopWhileBlock", "DoLoopUntilBlock"
                item.Kind = "do"
                Dim first = c(0)
                Dim last = c(c.Count - 1)
                item.PostTest = Condition(first) = "" AndAlso Condition(last) <> ""
                Dim test = If(item.PostTest, last, first)
                item.Text = Condition(test)
                item.IsUntil = Regex.IsMatch(Clean(test.ToString()), "^(Do|Loop)\s+Until\b", RegexOptions.IgnoreCase)
                item.Body = ReadList(c.Skip(1).Take(c.Count - 2), result)
            Case "ForBlock"
    item.Kind = "for"

    Dim fb = TryCast(n, ForBlockSyntax)
    If fb IsNot Nothing Then
        Dim controlText = Expr(fb.ForStatement.ControlVariable.ToString())
        Dim controlName = Regex.Replace(controlText, "\s+As\s+.+$", "", RegexOptions.IgnoreCase).Trim()

        Dim fromValue = Expr(fb.ForStatement.FromValue.ToString())
        Dim toValue = Expr(fb.ForStatement.ToValue.ToString())

        Dim stepValue As String = "1"
        If fb.ForStatement.StepClause IsNot Nothing Then
            stepValue = Expr(fb.ForStatement.StepClause.StepValue.ToString())
        End If

        Dim isDescending = Regex.IsMatch(stepValue, "^\s*-\s*")
        Dim stepAmount = stepValue.Trim()

        If stepAmount.StartsWith("+") Then
            stepAmount = stepAmount.Substring(1).Trim()
        End If
        If stepAmount.StartsWith("-") Then
            stepAmount = stepAmount.Substring(1).Trim()
        End If
        If stepAmount = "" Then stepAmount = "1"

        item.Text = "FOR " & controlName & " ← " & fromValue & " TO " & toValue
        If fb.ForStatement.StepClause IsNot Nothing Then
            item.Text &= " STEP " & stepValue
        End If

        item.LoopInitialiser = controlName & " ← " & fromValue
        item.LoopCondition = controlName & " " & If(isDescending, "≥", "≤") & " " & toValue
        item.LoopIncrement = controlName & " ← " & controlName & If(isDescending, " - ", " + ") & stepAmount

        If fb.ForStatement.StepClause IsNot Nothing AndAlso Not Regex.IsMatch(stepValue, "^\s*[+-]?\s*\d+(\.\d+)?\s*$") Then
            result.Warnings.Add("FOR loop flowchart assumes an ascending loop unless a literal negative STEP is supplied.")
        End If
    Else
        item.Text = Expr(c(0).ToString())
        item.Text = Regex.Replace(item.Text, "^For\b", "FOR", RegexOptions.IgnoreCase)
    End If

    item.Body = ReadList(c.Skip(1).Take(c.Count - 2), result)

    If Regex.IsMatch(Clean(c.Last().ToString()), "^Next\s+.*,") Then
        result.Warnings.Add("A NEXT statement naming multiple loop variables is not fully supported; use separate NEXT statements.")
    End If

Case "ForEachBlock"
    item.Kind = "foreach"
    item.Text = Expr(c(0).ToString())
    item.Text = Regex.Replace(item.Text, "^For\s+Each\b", "FOR EACH", RegexOptions.IgnoreCase)
    item.Body = ReadList(c.Skip(1).Take(c.Count - 2), result)
            Case "SelectBlock"
                item.Kind = "select"
                item.Text = Condition(c(0))
                For Each branch In c.Where(Function(x) K(x) = "CaseBlock" OrElse K(x) = "CaseElseBlock")
                    Dim b = branch.ChildNodes().ToList()
                    Dim label = Regex.Replace(Expr(b(0).ToString()), "^Case\s+", "", RegexOptions.IgnoreCase)
                    item.Branches.Add(New Instruction With {.Kind = If(label.Equals("Else", StringComparison.OrdinalIgnoreCase), "else", "case"), .Text = label, .Body = ReadList(b.Skip(1), result)})
                Next
            Case "LocalDeclarationStatement"
                item.Kind = "process"
                item.Text = OutsideStrings(raw, Function(x) Regex.Replace(x, "\bDim\b", "DECLARE", RegexOptions.IgnoreCase))
                ' Only replace initializer equals, not comparisons inside the initializer.
                Dim parts = n.ChildNodes().OfType(Of VariableDeclaratorSyntax)().Select(Function(d)
                    Dim decl = Clean(d.ToString())
                    If d.Initializer Is Nothing Then Return Expr(decl)
                    Dim idx = d.Initializer.SpanStart - d.SpanStart
                    Dim original = d.ToString()
                    Return Expr(original.Substring(0, idx).TrimEnd()) & " ← " & Expr(d.Initializer.Value.ToString())
                End Function).ToList()
                If parts.Count > 0 Then item.Text = If(Regex.IsMatch(raw, "^Const\b", RegexOptions.IgnoreCase), "CONSTANT ", "DECLARE ") & String.Join(", ", parts)
            Case "SimpleAssignmentStatement", "AddAssignmentStatement", "SubtractAssignmentStatement", "MultiplyAssignmentStatement", "DivideAssignmentStatement", "IntegerDivideAssignmentStatement", "ExponentiateAssignmentStatement", "ConcatenateAssignmentStatement", "LeftShiftAssignmentStatement", "RightShiftAssignmentStatement"
                Dim a = TryCast(n, AssignmentStatementSyntax)
                If a IsNot Nothing Then
                    Dim left = Expr(a.Left.ToString())
                    Dim right = Expr(a.Right.ToString())
                    Dim op = a.OperatorToken.Text
                    item.Text = left & " ← " & If(op = "=", right, left & " " & op.Substring(0, op.Length - 1) & " (" & right & ")")
                    If op <> "=" AndAlso Not Regex.IsMatch(left, "^\w+$") Then result.Warnings.Add("Compound assignment to a complex target is shown in expanded pseudocode; VB evaluates its target only once.")
                Else
                    item.Text = Expr(raw)
                End If
            Case "ExpressionStatement", "CallStatement"
                Dim output = Regex.Match(raw, "^(?:Call\s+)?Console\s*\.\s*(WriteLine|Write)\s*\((.*)\)$", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
                If output.Success Then
                    item.Kind = "io"
                    item.Text = If(output.Groups(1).Value.Equals("Write", StringComparison.OrdinalIgnoreCase), "OUTPUT (no newline) ", "OUTPUT ") & If(output.Groups(2).Value.Trim() = "", "(blank line)", Expr(output.Groups(2).Value))
                Else
                    item.Text = Expr(raw)
                End If
            Case "ReturnStatement"
                item.Kind = "return"
                item.Text = Regex.Replace(Expr(raw), "^Return\b", "RETURN", RegexOptions.IgnoreCase)
            Case "ExitSubStatement", "ExitFunctionStatement", "ExitPropertyStatement", "ExitOperatorStatement"
                item.Kind = "return"
                item.Text = raw.ToUpperInvariant()
            Case "ExitForStatement", "ExitWhileStatement", "ExitDoStatement", "ExitSelectStatement", "ContinueForStatement", "ContinueWhileStatement", "ContinueDoStatement"
                item.Kind = If(kind.StartsWith("Exit", StringComparison.Ordinal), "exit", "continue")
                item.Text = raw.ToUpperInvariant()
                item.Detail = item.Text.Split(" "c).Last().ToLowerInvariant()
            Case "EmptyStatement"
                item.Text = "(empty statement)"
            Case Else
                item.Kind = "unsupported"
                item.Text = raw
                result.Warnings.Add("Unsupported construct: " & kind & ". Shown as an opaque VB.NET block; internal control flow is not represented.")
        End Select
        If item.Kind = "process" AndAlso item.Text.Contains("USERINPUT", StringComparison.Ordinal) Then item.Kind = "io"
        Return item
    End Function

    Public Shared Function Pseudocode(r As Routine) As String
        Dim lines As New List(Of String)
        If r.Header <> "" Then lines.Add("// " & r.Header)
        WriteList(r.Body, 0, lines)
        Return String.Join(Environment.NewLine, lines)
    End Function

    Private Shared Sub WriteList(items As IEnumerable(Of Instruction), depth As Integer, lines As List(Of String))
        Dim indent As String = New String(" "c, depth * 4)
        For Each x In items
            Select Case x.Kind
                Case "if"
                    lines.Add(indent & "IF " & x.Text & " THEN")
                    WriteList(x.Body, depth + 1, lines)
                    For Each b In x.Branches
                        lines.Add(indent & "ELSE IF " & b.Text & " THEN")
                        WriteList(b.Body, depth + 1, lines)
                    Next
                    If x.Other.Count > 0 Then
                        lines.Add(indent & "ELSE")
                        WriteList(x.Other, depth + 1, lines)
                    End If
                    lines.Add(indent & "ENDIF")
                Case "while"
                    lines.Add(indent & "WHILE " & x.Text)
                    WriteList(x.Body, depth + 1, lines)
                    lines.Add(indent & "ENDWHILE")
                Case "for", "foreach"
                    lines.Add(indent & x.Text)
                    WriteList(x.Body, depth + 1, lines)
                    lines.Add(indent & "ENDFOR")
                Case "do"
                    If x.PostTest Then
                        lines.Add(indent & "REPEAT")
                        WriteList(x.Body, depth + 1, lines)
                        lines.Add(indent & "UNTIL " & If(x.IsUntil, x.Text, "NOT (" & x.Text & ")"))
                    ElseIf x.Text = "" Then
                        lines.Add(indent & "LOOP FOREVER")
                        WriteList(x.Body, depth + 1, lines)
                        lines.Add(indent & "ENDLOOP")
                    Else
                        lines.Add(indent & "WHILE " & If(x.IsUntil, "NOT (" & x.Text & ")", x.Text))
                        WriteList(x.Body, depth + 1, lines)
                        lines.Add(indent & "ENDWHILE")
                    End If
                Case "select"
                    lines.Add(indent & "CASE OF " & x.Text)
                    For Each b In x.Branches
                        lines.Add(indent & "    " & If(b.Kind = "else", "OTHERWISE", b.Text) & ":")
                        WriteList(b.Body, depth + 2, lines)
                    Next
                    lines.Add(indent & "ENDCASE")
                Case "unsupported"
                    lines.Add(indent & "// UNSUPPORTED VB.NET: " & x.Text)
                Case Else
                    lines.Add(indent & x.Text)
            End Select
        Next
    End Sub
End Class
