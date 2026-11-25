Imports System.Linq
Imports System.Collections.Generic
Imports System.Text.RegularExpressions
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

' Conservative, syntax-based rendering. Unknown calls are never guessed to be arrays.
Friend NotInheritable Class PseudoExpressions
    Private Sub New()
    End Sub

    Private Shared Function Kind(n As SyntaxNode) As String
        Return CType(n.RawKind, SyntaxKind).ToString()
    End Function

    Friend Shared Function KnownLocals(nodes As IEnumerable(Of SyntaxNode)) As Dictionary(Of String, String)
        Dim types As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each node In nodes.Where(Function(n) Kind(n) = "LocalDeclarationStatement")
            For Each d In node.ChildNodes().OfType(Of VariableDeclaratorSyntax)()
                Dim clause = If(d.AsClause Is Nothing, "", d.AsClause.ToString())
                For Each name In d.Names
                    Dim key = name.Identifier.ValueText
                    Dim category = "unknown"
                    If Regex.IsMatch(clause, "^As\s+(?:System\.)?String$", RegexOptions.IgnoreCase) AndAlso name.ToString() = key Then
                        category = "string"
                    ElseIf name.ToString().StartsWith(key & "(", StringComparison.OrdinalIgnoreCase) OrElse Regex.IsMatch(clause, "^As\s+.+\([,\s]*\)$", RegexOptions.IgnoreCase) Then
                        category = "array"
                    ElseIf Regex.IsMatch(clause, "^As\s+(?:New\s+)?(?:System\.Collections\.Generic\.)?List\s*\(Of\s+", RegexOptions.IgnoreCase) Then
                        category = "list"
                    End If
                    types(key) = If(types.ContainsKey(key), "unknown", category)
                Next
            Next
        Next
        Return types
    End Function

    Private Shared Function TypeOfLocal(n As ExpressionSyntax, types As Dictionary(Of String, String)) As String
        Dim id = TryCast(n, IdentifierNameSyntax)
        If id Is Nothing Then Return "unknown"
        Dim found As String = Nothing
        If types.TryGetValue(id.Identifier.ValueText, found) Then Return found
        Return "unknown"
    End Function

    Private Shared Function Normalize(s As String) As String
        Dim pieces = Regex.Split(s, "(""(?:""""|[^""])*"")")
        For i = 0 To pieces.Length - 1 Step 2
            Dim t = Regex.Replace(pieces(i), "[\r\n\t]+|[ ]{2,}", " ")
            t = Regex.Replace(t, "\b(And|Or|Not|Xor|Mod|True|False)\b", Function(m) m.Value.ToUpperInvariant(), RegexOptions.IgnoreCase)
            t = Regex.Replace(t, "\bAndAlso\b", "AND THEN", RegexOptions.IgnoreCase)
            t = Regex.Replace(t, "\bOrElse\b", "OR ELSE", RegexOptions.IgnoreCase)
            pieces(i) = t.Replace("<>", "≠").Replace(">=", "≥").Replace("<=", "≤")
        Next
        Return String.Concat(pieces).Trim()
    End Function

    Friend Shared Function Convert(text As String, types As Dictionary(Of String, String), warnings As List(Of String)) As String
        Dim expression = SyntaxFactory.ParseExpression(text)
        If expression.ContainsDiagnostics OrElse expression.FullSpan.Length <> text.Length Then Return Normalize(text)
        Return Render(expression, types, warnings)
    End Function

    Friend Shared Function Arguments(callNode As InvocationExpressionSyntax, types As Dictionary(Of String, String), warnings As List(Of String)) As List(Of String)
        Dim args As New List(Of String)
        If callNode.ArgumentList Is Nothing Then Return args
        For Each arg In callNode.ArgumentList.Arguments
            Dim simple = TryCast(arg, SimpleArgumentSyntax)
            If simple Is Nothing OrElse simple.NameColonEquals IsNot Nothing Then Return Nothing
            args.Add(Render(simple.Expression, types, warnings))
        Next
        Return args
    End Function

    Private Shared Function Render(n As SyntaxNode, types As Dictionary(Of String, String), warnings As List(Of String)) As String
        Dim raw = n.ToString()
        Dim k = Kind(n)
        Select Case k
            Case "MeExpression" : Return "ME"
            Case "MyBaseExpression" : Return "PARENT"
            Case "MyClassExpression"
                warnings.Add("MyClass is retained because it bypasses virtual dispatch; ME would not preserve its meaning.")
                Return raw
        End Select
        If k.Contains("Lambda") OrElse k.Contains("InterpolatedString") OrElse k.StartsWith("Xml", StringComparison.Ordinal) Then
            warnings.Add("Complex string, XML or lambda expression retained in VB.NET notation.")
            Return raw
        End If
        If k.EndsWith("LiteralExpression", StringComparison.Ordinal) Then
            If k = "TrueLiteralExpression" OrElse k = "FalseLiteralExpression" Then Return raw.ToUpperInvariant()
            If k = "NothingLiteralExpression" Then warnings.Add("Nothing is retained: VB.NET can use it for either a null reference or a value type's default value.")
            Return raw
        End If
        If k.EndsWith("CastExpression", StringComparison.Ordinal) OrElse k = "PredefinedCastExpression" Then
            warnings.Add("VB.NET casts are retained to preserve rounding, type conversion and null behaviour.")
        End If
        Dim callNode = TryCast(n, InvocationExpressionSyntax)
        If callNode IsNot Nothing Then
            Dim target = callNode.Expression.ToString()
            Dim consoleInput = Regex.IsMatch(target, "^(?:(?:Global\.)?System\.)?Console\.ReadLine$", RegexOptions.IgnoreCase) AndAlso Not types.ContainsKey("Console")
            Dim mathCall = Regex.Match(target, "^(?:(?:Global\.)?System\.)?Math\.(Sqrt|Abs|Floor|Ceiling|Sin|Cos|Tan|Log10|Exp)$", RegexOptions.IgnoreCase)
            Dim category = TypeOfLocal(callNode.Expression, types)
            Dim member = TryCast(callNode.Expression, MemberAccessExpressionSyntax)
            Dim stringMethod = member IsNot Nothing AndAlso member.Expression IsNot Nothing AndAlso TypeOfLocal(member.Expression, types) = "string"
            If consoleInput OrElse (mathCall.Success AndAlso Not types.ContainsKey("Math")) OrElse category = "array" OrElse category = "list" OrElse stringMethod Then
                Dim args = Arguments(callNode, types, warnings)
                If args IsNot Nothing Then
                    If consoleInput AndAlso args.Count = 0 Then Return "USERINPUT"
                    If mathCall.Success AndAlso args.Count = 1 AndAlso Not types.ContainsKey("Math") Then Return mathCall.Groups(1).Value.ToUpperInvariant() & "(" & args(0) & ")"
                    If (category = "array" OrElse category = "list") AndAlso args.Count > 0 Then Return target & "[" & String.Join(", ", args) & "]"
                    If stringMethod Then
                        Dim receiver = Render(member.Expression, types, warnings)
                        Dim method = member.Name.Identifier.ValueText.ToUpperInvariant()
                        If method = "TOUPPER" AndAlso args.Count = 0 Then Return "TO_UPPER(" & receiver & ")"
                        If method = "TOLOWER" AndAlso args.Count = 0 Then Return "TO_LOWER(" & receiver & ")"
                        If method = "SUBSTRING" AndAlso args.Count = 2 Then
                            ' Explicit start/count convention; do not confuse it with inclusive end-index SUBSTRING.
                            Return "SLICE(" & receiver & ", " & args(0) & ", " & args(1) & ")"
                        End If
                    End If
                End If
            End If
        End If
        Dim access = TryCast(n, MemberAccessExpressionSyntax)
        If access IsNot Nothing AndAlso access.Expression IsNot Nothing Then
            Dim category = TypeOfLocal(access.Expression, types)
            Dim memberName = access.Name.Identifier.ValueText
            If (Kind(access.Expression) = "MyBaseExpression" OrElse Kind(access.Expression) = "MeExpression") AndAlso memberName.Equals("New", StringComparison.OrdinalIgnoreCase) Then
                Return Render(access.Expression, types, warnings) & ".CONSTRUCTOR"
            End If
            If Regex.IsMatch(access.Expression.ToString(), "^(?:(?:Global\.)?System\.)?Math$", RegexOptions.IgnoreCase) AndAlso Not types.ContainsKey("Math") AndAlso memberName.Equals("PI", StringComparison.OrdinalIgnoreCase) Then Return "PI"
            If ((category = "string" OrElse category = "array") AndAlso memberName.Equals("Length", StringComparison.OrdinalIgnoreCase)) OrElse (category = "list" AndAlso memberName.Equals("Count", StringComparison.OrdinalIgnoreCase)) Then Return "LEN(" & Render(access.Expression, types, warnings) & ")"
        End If
        ' Preserve the syntax of all other expressions, rendering only parsed child nodes and operator tokens.
        ' Rebuilding from spans keeps string literals and escaped identifiers intact.
        Dim edits As New List(Of (Start As Integer, Length As Integer, Value As String))
        For Each child In n.ChildNodes()
            edits.Add((child.SpanStart - n.SpanStart, child.Span.Length, Render(child, types, warnings)))
        Next
        For Each token In n.ChildTokens()
            Dim replacement = token.Text
            Select Case CType(token.RawKind, SyntaxKind).ToString()
                Case "AndKeyword", "OrKeyword", "NotKeyword", "XorKeyword", "ModKeyword"
                    replacement = replacement.ToUpperInvariant()
                Case "NewKeyword" : replacement = "NEW"
                Case "AndAlsoKeyword" : replacement = "AND THEN"
                Case "OrElseKeyword" : replacement = "OR ELSE"
                Case "LessThanGreaterThanToken" : replacement = "≠"
                Case "GreaterThanEqualsToken" : replacement = "≥"
                Case "LessThanEqualsToken" : replacement = "≤"
            End Select
            If replacement <> token.Text Then edits.Add((token.SpanStart - n.SpanStart, token.Span.Length, replacement))
        Next
        For Each edit In edits.OrderByDescending(Function(e) e.Start)
            raw = raw.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value)
        Next
        Return raw
    End Function

    Friend Shared Function Declaration(n As SyntaxNode, types As Dictionary(Of String, String), warnings As List(Of String)) As String
        Dim raw = n.ToString()
        Dim lines As New List(Of String)
        Dim constant = Regex.IsMatch(raw, "^Const\b", RegexOptions.IgnoreCase)
        If Not Regex.IsMatch(raw, "^(Dim|Const)\b", RegexOptions.IgnoreCase) Then
            warnings.Add("Declaration modifiers retained in VB.NET notation.")
            Return raw
        End If
        For Each d In n.ChildNodes().OfType(Of VariableDeclaratorSyntax)()
            Dim clause = If(d.AsClause Is Nothing, "", d.AsClause.ToString())
            Dim asNew = TryCast(d.AsClause, AsNewClauseSyntax)
            If d.Names.Count = 1 AndAlso Regex.IsMatch(d.Names(0).ToString(), "^\w+$") AndAlso Not constant AndAlso asNew IsNot Nothing AndAlso TypeOf asNew.NewExpression Is ObjectCreationExpressionSyntax AndAlso d.Initializer Is Nothing Then
                lines.Add(d.Names(0).Identifier.Text & " ← " & Convert(asNew.NewExpression.ToString(), types, warnings))
                Continue For
            End If
            If d.Names.Count = 1 AndAlso Regex.IsMatch(d.Names(0).ToString(), "^\w+$") AndAlso Not constant AndAlso d.Initializer IsNot Nothing AndAlso TypeOf d.Initializer.Value Is ObjectCreationExpressionSyntax Then
                Dim declaredType = TryCast(d.AsClause, SimpleAsClauseSyntax)
                If d.AsClause Is Nothing OrElse declaredType IsNot Nothing Then
                    lines.Add(If(declaredType Is Nothing, "", "DECLARE ") & d.Names(0).Identifier.Text & If(declaredType Is Nothing, "", " : " & RoutineSignatures.TypeLabel(declaredType.Type.ToString())) & " ← " & Convert(d.Initializer.Value.ToString(), types, warnings))
                    Continue For
                End If
            End If
            Dim typeMatch = Regex.Match(clause, "^As\s+(Boolean|Byte|SByte|Short|UShort|Integer|UInteger|Long|ULong|Single|Double|Decimal|Char|String|Date|Object)$", RegexOptions.IgnoreCase)
            If d.Names.Count <> 1 OrElse Not Regex.IsMatch(d.Names(0).ToString(), "^\w+$") OrElse (clause <> "" AndAlso Not typeMatch.Success) Then
                ' Keep array bounds, As New, multi-name declarations and custom types explicit.
                lines.Add(If(constant, "CONSTANT ", "DECLARE ") & d.ToString())
                warnings.Add("Complex declarations retain VB.NET type and bound syntax to preserve allocation and initialization.")
                Continue For
            End If
            Dim name = d.Names(0).Identifier.Text
            Dim dtype = If(typeMatch.Success, typeMatch.Groups(1).Value.ToUpperInvariant(), "")
            If d.Initializer Is Nothing Then
                lines.Add("DECLARE " & name & If(dtype = "", "", " : " & dtype))
                warnings.Add("Uninitialised VB.NET declarations use the default value of their declared type.")
            Else
                Dim value = Convert(d.Initializer.Value.ToString(), types, warnings)
                Dim prefix = If(constant, "CONSTANT ", "")
                ' A declaration with a nontrivial implicit conversion retains its type.
                Dim simple = dtype = "" OrElse (dtype = "INTEGER" AndAlso Regex.IsMatch(value, "^\d{1,9}$")) OrElse (dtype = "STRING" AndAlso (Kind(d.Initializer.Value) = "StringLiteralExpression" OrElse value = "USERINPUT")) OrElse (dtype = "BOOLEAN" AndAlso (value = "TRUE" OrElse value = "FALSE"))
                If simple Then
                    lines.Add(prefix & name & " ← " & value)
                Else
                    lines.Add(If(constant, "CONSTANT ", "DECLARE ") & name & " : " & dtype & " ← " & value)
                End If
            End If
        Next
        Return If(lines.Count = 0, raw, String.Join(vbLf, lines))
    End Function
End Class
