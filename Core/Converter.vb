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
    Public Property Signature As String = ""
    Public Property Footer As String = ""
    Public Property EntryLabel As String = "START"
    Public Property ExitLabel As String = "END"
    Public Property SignatureNote As String = ""
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
    Friend Property LocalTypes As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Friend Property KnownFunctions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
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
            result.LocalTypes = PseudoExpressions.KnownLocals(children.Skip(1).Take(children.Count - 2))
            Dim method = TryCast(children(0), MethodStatementSyntax)
            Dim methodBase = TryCast(children(0), MethodBaseSyntax)
            result.KnownFunctions.Clear()
            If n.Parent IsNot Nothing Then
                For Each sibling In n.Parent.ChildNodes().Where(Function(node) K(node) = "FunctionBlock")
                    Dim declaration = TryCast(sibling.ChildNodes().FirstOrDefault(), MethodStatementSyntax)
                    If declaration IsNot Nothing Then result.KnownFunctions.Add(declaration.Identifier.ValueText)
                Next
            End If
            RoutineSignatures.AddParameterTypes(methodBase, result)
            Dim r As New Routine With {.Name = routineName, .Header = If(offset = 2, "", header), .Body = ReadList(children.Skip(1).Take(children.Count - 2), result, False)}
            If offset <> 2 Then
                If method IsNot Nothing Then
                    RoutineSignatures.Apply(r, method, result)
                ElseIf Not ObjectRoutines.ApplySpecial(r, n, methodBase, result) Then
                    RoutineSignatures.Apply(r, method, result)
                End If
                ObjectRoutines.AddContext(r, n, result)
            End If
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

    Private Shared Function Expr(s As String, result As ParseResult) As String
        Return PseudoExpressions.Convert(s, result.LocalTypes, result.Warnings)
    End Function

    Private Shared Function Condition(header As SyntaxNode, result As ParseResult) As String
        Dim e = header.ChildNodes().OfType(Of ExpressionSyntax)().FirstOrDefault()
        If e IsNot Nothing Then Return Expr(e.ToString(), result)
        Dim clause = header.ChildNodes().FirstOrDefault(Function(n) K(n).EndsWith("Clause", StringComparison.Ordinal))
        If clause IsNot Nothing Then
            e = clause.ChildNodes().OfType(Of ExpressionSyntax)().FirstOrDefault()
            If e IsNot Nothing Then Return Expr(e.ToString(), result)
        End If
        Return ""
    End Function

    Private Shared Function IsIntegerLiteral(value As String) As Boolean
        Dim number As Long
        Return Long.TryParse(value.Replace(" ", ""), Globalization.NumberStyles.AllowLeadingSign, Globalization.CultureInfo.InvariantCulture, number)
    End Function

    ' Narrow syntactic proof for the common fixed local array pattern.
    ' Array.Length cannot change unless the array reference is replaced.
    ' If a reference is passed around, reassigned, resized or otherwise ambiguous,
    ' fall back to saving the loop bound at entry rather than guessing.
    Private Shared Function StableArrayBound(loopNode As ForBlockSyntax, result As ParseResult) As Boolean
        Dim m = Regex.Match(Clean(loopNode.ForStatement.ToValue.ToString()), "^([A-Za-z_]\w*)\s*\.\s*Length(?:\s*-\s*1)?$", RegexOptions.IgnoreCase)
        If Not m.Success Then Return False
        Dim arrayName = m.Groups(1).Value
        Dim category As String = Nothing
        If Not result.LocalTypes.TryGetValue(arrayName, category) OrElse category <> "array" Then Return False
        Dim routineNode = loopNode.Ancestors().FirstOrDefault(Function(a) IsRoutine(a))
        If routineNode Is Nothing Then Return False
        Dim descendants = routineNode.DescendantNodes().ToList()
        If descendants.Any(Function(a) K(a).Contains("Lambda") OrElse K(a).StartsWith("ReDim", StringComparison.OrdinalIgnoreCase) OrElse K(a).StartsWith("Erase", StringComparison.OrdinalIgnoreCase)) Then Return False
        Dim declarations = descendants.OfType(Of ModifiedIdentifierSyntax)().Where(Function(d) d.Identifier.ValueText.Equals(arrayName, StringComparison.OrdinalIgnoreCase)).ToList()
        If declarations.Count <> 1 Then Return False
        ' Require a simple explicitly allocated local array, not a parameter or a field.
        Dim declaration = declarations(0)
        Dim declarator = TryCast(declaration.Parent, VariableDeclaratorSyntax)
        If declarator Is Nothing OrElse declarator.Names.Count <> 1 OrElse declarator.Initializer IsNot Nothing Then Return False
        If Not Regex.IsMatch(declaration.ToString(), "^[A-Za-z_]\w*\s*\(\s*\d+\s*\)$") Then Return False
        If declarator.Parent Is Nothing OrElse K(declarator.Parent) <> "LocalDeclarationStatement" Then Return False
        If declarator.Parent.Parent IsNot routineNode Then
            If declarator.Parent.Parent Is Nothing OrElse Not declarator.Parent.Parent.Span.Equals(routineNode.Span) Then Return False
        End If
        If declarator.SpanStart >= loopNode.SpanStart Then Return False
        For Each reference In descendants.OfType(Of IdentifierNameSyntax)().Where(Function(id) id.Identifier.ValueText.Equals(arrayName, StringComparison.OrdinalIgnoreCase))
            Dim member = TryCast(reference.Parent, MemberAccessExpressionSyntax)
            If member IsNot Nothing AndAlso member.Expression IsNot Nothing AndAlso member.Expression.Span.Equals(reference.Span) AndAlso member.Name.Identifier.ValueText.Equals("Length", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim invocation = TryCast(reference.Parent, InvocationExpressionSyntax)
            If invocation IsNot Nothing AndAlso invocation.Expression.Span.Equals(reference.Span) Then Continue For
            Return False
        Next
        Return True
    End Function

    ' Use the call symbol for a direct call to a function declared in the same type.
    ' Nested calls in expressions are not hoisted, to preserve order and short-circuiting.
    Private Shared Function IsKnownFunctionCall(expression As ExpressionSyntax, result As ParseResult) As Boolean
        Dim invocation = TryCast(expression, InvocationExpressionSyntax)
        If invocation Is Nothing Then Return False
        Dim identifier = TryCast(invocation.Expression, IdentifierNameSyntax)
        If identifier Is Nothing Then Return False
        Dim name = identifier.Identifier.ValueText
        Return result.KnownFunctions.Contains(name) AndAlso Not result.LocalTypes.ContainsKey(name)
    End Function

    Private Shared Function ReadList(nodes As IEnumerable(Of SyntaxNode), result As ParseResult, Optional addBlockLocals As Boolean = True) As List(Of Instruction)
        Dim statements = nodes.ToList()
        Dim oldTypes = result.LocalTypes
        Try
            If addBlockLocals Then
                ' Only collect declarations belonging to this statement list. Descendant
                ' blocks get their own environments, so sibling locals never leak.
                result.LocalTypes = New Dictionary(Of String, String)(oldTypes, StringComparer.OrdinalIgnoreCase)
                For Each local In PseudoExpressions.KnownLocals(statements)
                    result.LocalTypes(local.Key) = If(oldTypes.ContainsKey(local.Key), "unknown", local.Value)
                Next
            End If
            Dim items As New List(Of Instruction)
            For Each n In statements
                items.Add(ReadNode(n, result))
            Next
            Return items
        Finally
            result.LocalTypes = oldTypes
        End Try
    End Function

    ' A single declared stream resource can be represented without inventing alias or lifetime semantics.
    Private Shared Function ReadUsing(block As UsingBlockSyntax, result As ParseResult) As Instruction
        Dim declarations = block.UsingStatement.Variables
        If declarations.Count = 1 AndAlso declarations(0).Names.Count = 1 Then
            Dim d = declarations(0)
            Dim identifier = d.Names(0)
            Dim creation As ObjectCreationExpressionSyntax = Nothing
            Dim newClause = TryCast(d.AsClause, AsNewClauseSyntax)
            If newClause IsNot Nothing Then
                creation = TryCast(newClause.NewExpression, ObjectCreationExpressionSyntax)
            ElseIf d.Initializer IsNot Nothing Then
                creation = TryCast(d.Initializer.Value, ObjectCreationExpressionSyntax)
            End If
            If creation IsNot Nothing AndAlso Regex.IsMatch(identifier.ToString(), "^\w+$") Then
                Dim category = PseudoExpressions.StreamCategory(creation.Type)
                Dim declared = TryCast(d.AsClause, SimpleAsClauseSyntax)
                If category <> "unknown" AndAlso (declared Is Nothing OrElse PseudoExpressions.StreamCategory(declared.Type) = category) Then
                    Dim name = identifier.Identifier.Text
                    Dim setup = name & " ← " & Expr(creation.ToString(), result)
                    Dim oldTypes = result.LocalTypes
                    result.LocalTypes = New Dictionary(Of String, String)(oldTypes, StringComparer.OrdinalIgnoreCase)
                    result.LocalTypes(identifier.Identifier.ValueText) = category
                    Dim body As List(Of Instruction)
                    Try
                        body = ReadList(block.Statements, result)
                    Finally
                        result.LocalTypes = oldTypes
                    End Try
                    result.Warnings.Add("USING diagrams show normal execution and explicit Return/Exit/Continue cleanup; implicit exception paths are not drawn.")
                    Return New Instruction With {.Kind = "using", .Text = setup, .Detail = name, .Body = body}
                End If
            End If
        End If
        result.Warnings.Add("Unsupported USING resource declaration: only one newly created StreamReader or StreamWriter is expanded. Resource lifetime and internal control flow are retained as an opaque block.")
        Return New Instruction With {.Kind = "unsupported", .Text = block.ToString()}
    End Function

    Private Shared Function ReadNode(n As SyntaxNode, result As ParseResult) As Instruction
        Dim usingBlock = TryCast(n, UsingBlockSyntax)
        If usingBlock IsNot Nothing Then Return ReadUsing(usingBlock, result)
        Dim kind = K(n)
        Dim c = n.ChildNodes().ToList()
        Dim raw = Clean(n.ToString())
        Dim item As New Instruction
        Select Case kind
            Case "MultiLineIfBlock"
                item.Kind = "if"
                item.Text = Condition(c(0), result)
                item.Body = ReadList(c.Skip(1).Take(c.Count - 2).Where(Function(x) K(x) <> "ElseIfBlock" AndAlso K(x) <> "ElseBlock"), result)
                For Each branch In c.Where(Function(x) K(x) = "ElseIfBlock" OrElse K(x) = "ElseBlock")
                    Dim b = branch.ChildNodes().ToList()
                    If K(branch) = "ElseBlock" Then
                        item.Other = ReadList(b.Skip(1), result)
                    Else
                        item.Branches.Add(New Instruction With {.Kind = "if", .Text = Condition(b(0), result), .Body = ReadList(b.Skip(1), result)})
                    End If
                Next
            Case "SingleLineIfStatement"
                item.Kind = "if"
                item.Text = Condition(n, result)
                item.Body = ReadList(c.OfType(Of StatementSyntax)(), result)
                Dim other = c.FirstOrDefault(Function(x) K(x) = "SingleLineElseClause")
                If other IsNot Nothing Then item.Other = ReadList(other.ChildNodes().OfType(Of StatementSyntax)(), result)
            Case "WhileBlock"
                item.Kind = "while"
                item.Text = Condition(c(0), result)
                item.Body = ReadList(c.Skip(1).Take(c.Count - 2), result)
            Case "SimpleDoLoopBlock", "DoWhileLoopBlock", "DoUntilLoopBlock", "DoLoopWhileBlock", "DoLoopUntilBlock"
                item.Kind = "do"
                Dim first = c(0)
                Dim last = c(c.Count - 1)
                item.PostTest = Condition(first, result) = "" AndAlso Condition(last, result) <> ""
                Dim test = If(item.PostTest, last, first)
                item.Text = Condition(test, result)
                item.IsUntil = Regex.IsMatch(Clean(test.ToString()), "^(Do|Loop)\s+Until\b", RegexOptions.IgnoreCase)
                item.Body = ReadList(c.Skip(1).Take(c.Count - 2), result)
            Case "ForBlock"
                item.Kind = "for"
                Dim fb = DirectCast(n, ForBlockSyntax)
                Dim controlName = Regex.Replace(Clean(fb.ForStatement.ControlVariable.ToString()), "\s+As\s+.+$", "", RegexOptions.IgnoreCase).Trim()
                Dim fromValue = Expr(fb.ForStatement.FromValue.ToString(), result)
                Dim toValue = Expr(fb.ForStatement.ToValue.ToString(), result)
                Dim stepValue = If(fb.ForStatement.StepClause Is Nothing, "1", Expr(fb.ForStatement.StepClause.StepValue.ToString(), result))
                item.Text = "FOR " & controlName & " ← " & fromValue & " TO " & toValue
                If fb.ForStatement.StepClause IsNot Nothing Then item.Text &= " STEP " & stepValue
                ' VB evaluates start, limit and step once, before assigning the counter.
                Dim stepNumber As Long
                Dim fixedStep = Long.TryParse(stepValue.Replace(" ", ""), Globalization.NumberStyles.AllowLeadingSign, Globalization.CultureInfo.InvariantCulture, stepNumber)
                Dim fixedLimit = IsIntegerLiteral(toValue) OrElse (fixedStep AndAlso StableArrayBound(fb, result))
                Dim increment = If(fixedStep, If(stepNumber < 0, " - " & stepValue.Trim().Substring(1).Trim(), " + " & stepValue.Trim().TrimStart("+"c).Trim()), "")
                If fixedStep AndAlso fixedLimit Then
                    item.LoopInitialiser = controlName & " ← " & fromValue
                    item.LoopCondition = controlName & If(stepNumber < 0, " ≥ ", " ≤ ") & toValue
                    item.LoopIncrement = controlName & " ← " & controlName & increment
                Else
                    ' Capture only expressions that need saving, in VB evaluation order.
                    ' Spaces distinguish these explanatory names from source identifiers.
                    Dim startTemp = "saved start for " & controlName
                    Dim limitTemp = "saved limit for " & controlName
                    Dim stepTemp = "saved step for " & controlName
                    Dim setup As New List(Of String)
                    Dim initial = fromValue
                    If Not IsIntegerLiteral(fromValue) Then
                        setup.Add(startTemp & " ← " & fromValue)
                        initial = startTemp
                    End If
                    Dim bound = toValue
                    If Not fixedLimit Then
                        setup.Add(limitTemp & " ← " & toValue)
                        bound = limitTemp
                    End If
                    If Not fixedStep Then setup.Add(stepTemp & " ← " & stepValue)
                    setup.Add(controlName & " ← " & initial)
                    item.LoopInitialiser = String.Join(vbLf, setup)
                    If fixedStep Then
                        item.LoopCondition = controlName & If(stepNumber < 0, " ≥ ", " ≤ ") & bound
                        item.LoopIncrement = controlName & " ← " & controlName & increment
                    Else
                        item.LoopCondition = "(" & stepTemp & " ≥ 0 AND " & controlName & " ≤ " & bound & ") OR (" & stepTemp & " < 0 AND " & controlName & " ≥ " & bound & ")"
                        item.LoopIncrement = controlName & " ← " & controlName & " + " & stepTemp
                    End If
                End If
                item.Body = ReadList(c.Skip(1).Take(c.Count - 2), result)
                If Regex.IsMatch(Clean(c.Last().ToString()), "^Next\s+.*,") Then result.Warnings.Add("A NEXT statement naming multiple loop variables is not fully supported; use separate NEXT statements.")
            Case "ForEachBlock"
                item.Kind = "foreach"
                Dim loopHeader = DirectCast(c(0), ForEachStatementSyntax)
                Dim variable = Regex.Replace(Clean(loopHeader.ControlVariable.ToString()), "\s+As\s+.+$", "", RegexOptions.IgnoreCase).Trim()
                item.Text = "FOR EACH " & variable & " IN " & Expr(loopHeader.Expression.ToString(), result)
                item.Body = ReadList(c.Skip(1).Take(c.Count - 2), result)
            Case "SelectBlock"
                item.Kind = "select"
                item.Text = Condition(c(0), result)
                For Each branch In c.Where(Function(x) K(x) = "CaseBlock" OrElse K(x) = "CaseElseBlock")
                    Dim b = branch.ChildNodes().ToList()
                    Dim label = Regex.Replace(Expr(b(0).ToString(), result), "^Case\s+", "", RegexOptions.IgnoreCase)
                    item.Branches.Add(New Instruction With {.Kind = If(label.Equals("Else", StringComparison.OrdinalIgnoreCase), "else", "case"), .Text = label, .Body = ReadList(b.Skip(1), result)})
                Next
            Case "LocalDeclarationStatement"
                item.Kind = "process"
                item.Text = PseudoExpressions.Declaration(n, result.LocalTypes, result.Warnings)
                Dim declarations = n.ChildNodes().OfType(Of VariableDeclaratorSyntax)().ToList()
                If declarations.Count = 1 AndAlso declarations(0).Names.Count = 1 Then
                    Dim d = declarations(0)
                    If d.Initializer IsNot Nothing AndAlso (IsKnownFunctionCall(d.Initializer.Value, result) OrElse TypeOf d.Initializer.Value Is ObjectCreationExpressionSyntax) Then item.Kind = "call"
                    Dim newClause = TryCast(d.AsClause, AsNewClauseSyntax)
                    If newClause IsNot Nothing AndAlso TypeOf newClause.NewExpression Is ObjectCreationExpressionSyntax Then item.Kind = "call"
                    If d.Initializer IsNot Nothing AndAlso PseudoExpressions.IsFileIO(d.Initializer.Value, result.LocalTypes) Then item.Kind = "io"
                End If
            Case "SimpleAssignmentStatement", "AddAssignmentStatement", "SubtractAssignmentStatement", "MultiplyAssignmentStatement", "DivideAssignmentStatement", "IntegerDivideAssignmentStatement", "ExponentiateAssignmentStatement", "ConcatenateAssignmentStatement", "LeftShiftAssignmentStatement", "RightShiftAssignmentStatement"
                Dim a = TryCast(n, AssignmentStatementSyntax)
                If a IsNot Nothing Then
                    Dim left = Expr(a.Left.ToString(), result)
                    Dim right = Expr(a.Right.ToString(), result)
                    Dim op = a.OperatorToken.Text
                    If op = "=" AndAlso (IsKnownFunctionCall(a.Right, result) OrElse TypeOf a.Right Is ObjectCreationExpressionSyntax) Then item.Kind = "call"
                    If op = "=" AndAlso PseudoExpressions.IsFileIO(a.Right, result.LocalTypes) Then item.Kind = "io"
                    If op <> "=" AndAlso Not Regex.IsMatch(left, "^\w+$") Then
                        item.Text = left & " " & op & " " & right
                        result.Warnings.Add("Compound assignment to a complex target is retained to avoid evaluating the target twice.")
                    Else
                        Dim rhs = If(Regex.IsMatch(right, "^(?:[+-]?\d+(?:\.\d+)?|[\w]+)$"), right, "(" & right & ")")
                        item.Text = left & " ← " & If(op = "=", right, left & " " & op.Substring(0, op.Length - 1) & " " & rhs)
                    End If
                Else
                    item.Text = Expr(raw, result)
                End If
            Case "ExpressionStatement", "CallStatement"
                Dim invocation = n.ChildNodes().OfType(Of InvocationExpressionSyntax)().FirstOrDefault()
                Dim target = If(invocation Is Nothing, "", Clean(invocation.Expression.ToString()))
                If Regex.IsMatch(target, "^(?:(?:Global\.)?System\.)?Console\.(WriteLine|Write)$", RegexOptions.IgnoreCase) AndAlso Not result.LocalTypes.ContainsKey("Console") Then
                    Dim args = PseudoExpressions.Arguments(invocation, result.LocalTypes, result.Warnings)
                    If args IsNot Nothing AndAlso args.Count <= 1 Then
                        item.Kind = "io"
                        item.Text = If(target.EndsWith(".Write", StringComparison.OrdinalIgnoreCase), "OUTPUT (no newline) ", "OUTPUT ") & If(args.Count = 0, "(blank line)", args(0))
                    Else
                        item.Kind = "io"
                        item.Text = raw
                        result.Warnings.Add("Formatted or named Console output is retained as VB.NET to preserve its formatting.")
                    End If
                Else
                    item.Text = If(invocation Is Nothing, raw, Expr(invocation.ToString(), result))
                    If invocation IsNot Nothing Then
                        Dim category As String = Nothing
                        Dim isIndex = result.LocalTypes.TryGetValue(target, category) AndAlso PseudoExpressions.IsIndexed(category)
                        If Not isIndex Then
                            item.Kind = If(PseudoExpressions.IsFileIO(invocation, result.LocalTypes), "io", "call")
                            If invocation.ArgumentList Is Nothing Then item.Text &= "()"
                        End If
                    End If
                End If
            Case "ReturnStatement"
                item.Kind = "return"
                Dim value = n.ChildNodes().OfType(Of ExpressionSyntax)().FirstOrDefault()
                item.Text = If(value Is Nothing, "RETURN", "RETURN " & Expr(value.ToString(), result))
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
        If item.Kind = "process" AndAlso OutsideStrings(item.Text, Function(t) Regex.Replace(t, "\bUSERINPUT\b", ChrW(1).ToString())).Contains(ChrW(1)) Then item.Kind = "io"
        Return item
    End Function

    Public Shared Function Pseudocode(r As Routine) As String
        Dim lines As New List(Of String)
        If r.SignatureNote <> "" Then lines.Add(r.SignatureNote)
        If r.Signature <> "" Then
            lines.Add(r.Signature)
            WriteList(r.Body, 1, lines)
            lines.Add(r.Footer)
        Else
            If r.Header <> "" Then lines.Add("// " & r.Header)
            WriteList(r.Body, 0, lines)
        End If
        Return String.Join(Environment.NewLine, lines)
    End Function

    Private Shared Sub WriteList(items As IEnumerable(Of Instruction), depth As Integer, lines As List(Of String))
        Dim indent As String = New String(" "c, depth * 4)
        For Each x In items
            Select Case x.Kind
                Case "using"
                    lines.Add(indent & "USING " & x.Text)
                    WriteList(x.Body, depth + 1, lines)
                    lines.Add(indent & "ENDUSING // Automatically dispose " & x.Detail & " on exit")
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
