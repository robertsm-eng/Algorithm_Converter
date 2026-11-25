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

    ' Recognise the standard generic collection type spellings; do not infer from method names.
    Private Shared Function CollectionName(t As TypeSyntax) As GenericNameSyntax
        If t Is Nothing Then Return Nothing
        Dim generic = TryCast(t, GenericNameSyntax)
        If generic Is Nothing Then
            Dim qualified = TryCast(t, QualifiedNameSyntax)
            If qualified Is Nothing OrElse Not Regex.IsMatch(qualified.Left.ToString(), "^(?:Global\.)?System\.Collections\.Generic$", RegexOptions.IgnoreCase) Then Return Nothing
            generic = TryCast(qualified.Right, GenericNameSyntax)
        End If
        If generic Is Nothing Then Return Nothing
        Dim count = generic.TypeArgumentList.Arguments.Count
        Select Case generic.Identifier.ValueText.ToLowerInvariant()
            Case "list", "stack", "queue", "hashset"
                If count = 1 Then Return generic
            Case "dictionary"
                If count = 2 Then Return generic
        End Select
        Return Nothing
    End Function

    Friend Shared Function CollectionCategory(t As TypeSyntax) As String
        Dim generic = CollectionName(t)
        Return If(generic Is Nothing, "unknown", generic.Identifier.ValueText.ToLowerInvariant())
    End Function

    Friend Shared Function CollectionTypeLabel(t As TypeSyntax) As String
        Dim generic = CollectionName(t)
        If generic Is Nothing Then Return ""
        Dim args = generic.TypeArgumentList.Arguments.Select(Function(a) RoutineSignatures.TypeLabel(a.ToString())).ToList()
        Select Case generic.Identifier.ValueText.ToLowerInvariant()
            Case "dictionary" : Return "DICTIONARY OF (" & String.Join(", ", args) & ")"
            Case "hashset" : Return "SET OF " & args(0)
            Case Else : Return generic.Identifier.ValueText.ToUpperInvariant() & " OF " & args(0)
        End Select
    End Function

    Friend Shared Function StreamCategory(t As TypeSyntax) As String
        If t Is Nothing Then Return "unknown"
        Dim match = Regex.Match(t.ToString(), "^(?:(?:Global\.)?System\.IO\.)?(StreamReader|StreamWriter)$", RegexOptions.IgnoreCase)
        If Not match.Success Then Return "unknown"
        Return If(match.Groups(1).Value.Equals("StreamReader", StringComparison.OrdinalIgnoreCase), "reader", "writer")
    End Function

    Private Shared Function IsPath(expression As ExpressionSyntax, types As Dictionary(Of String, String)) As Boolean
        Return Kind(expression) = "StringLiteralExpression" OrElse TypeOfLocal(expression, types) = "string"
    End Function

    Private Shared Function FileCreation(creation As ObjectCreationExpressionSyntax, types As Dictionary(Of String, String), warnings As List(Of String)) As String
        Dim category = StreamCategory(creation.Type)
        If category = "unknown" Then Return ""
        ' A one-argument stream constructor can accept a Stream, not just a path.
        Dim args = creation.ArgumentList
        If creation.Initializer Is Nothing AndAlso args IsNot Nothing AndAlso args.Arguments.Count > 0 Then
            Dim first = TryCast(args.Arguments(0), SimpleArgumentSyntax)
            If first IsNot Nothing AndAlso first.NameColonEquals Is Nothing AndAlso IsPath(first.Expression, types) Then
                Dim mode = If(category = "reader", "READ", "WRITE")
                If args.Arguments.Count = 1 Then Return "OPENFILE(" & Render(first.Expression, types, warnings) & ", " & mode & ")"
                If category = "writer" AndAlso args.Arguments.Count = 2 Then
                    Dim second = TryCast(args.Arguments(1), SimpleArgumentSyntax)
                    If second IsNot Nothing AndAlso second.NameColonEquals Is Nothing Then
                        Dim k = Kind(second.Expression)
                        If k = "TrueLiteralExpression" OrElse k = "FalseLiteralExpression" Then
                            Return "OPENFILE(" & Render(first.Expression, types, warnings) & ", " & If(k = "TrueLiteralExpression", "APPEND", "WRITE") & ")"
                        End If
                    End If
                End If
            End If
        End If
        warnings.Add("Stream constructor retained: path type, append flag or advanced overload could not be established from syntax.")
        Return ""
    End Function

    Friend Shared Function IsFileIO(expression As ExpressionSyntax, types As Dictionary(Of String, String)) As Boolean
        Dim invocation = TryCast(expression, InvocationExpressionSyntax)
        If invocation Is Nothing Then Return False
        Dim member = TryCast(invocation.Expression, MemberAccessExpressionSyntax)
        If member Is Nothing OrElse member.Expression Is Nothing Then Return False
        Dim name = member.Name.Identifier.ValueText.ToUpperInvariant()
        Dim category = TypeOfLocal(member.Expression, types)
        If category = "reader" Then Return {"READ", "READLINE", "READTOEND", "READBLOCK", "PEEK"}.Contains(name)
        If category = "writer" Then Return {"WRITE", "WRITELINE"}.Contains(name)
        If Regex.IsMatch(member.Expression.ToString(), "^(?:(?:Global\.)?System\.IO\.)?File$", RegexOptions.IgnoreCase) AndAlso Not types.ContainsKey("File") Then
            Return {"READALLTEXT", "READALLLINES", "READALLBYTES", "WRITEALLTEXT", "WRITEALLLINES", "WRITEALLBYTES", "APPENDALLTEXT", "APPENDALLLINES"}.Contains(name)
        End If
        Return False
    End Function

    Friend Shared Function IsIndexed(category As String) As Boolean
        Return category = "array" OrElse category = "list" OrElse category = "dictionary"
    End Function

    Friend Shared Function KnownLocals(nodes As IEnumerable(Of SyntaxNode)) As Dictionary(Of String, String)
        Dim types As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each node In nodes.Where(Function(n) Kind(n) = "LocalDeclarationStatement")
            For Each d In node.ChildNodes().OfType(Of VariableDeclaratorSyntax)()
                Dim clause = If(d.AsClause Is Nothing, "", d.AsClause.ToString())
                For Each name In d.Names
                    Dim key = name.Identifier.ValueText
                    Dim category = "unknown"
                    Dim declared = TryCast(d.AsClause, SimpleAsClauseSyntax)
                    Dim created = TryCast(d.AsClause, AsNewClauseSyntax)
                    If name.ArrayBounds IsNot Nothing OrElse name.ArrayRankSpecifiers.Count > 0 OrElse (declared IsNot Nothing AndAlso TypeOf declared.Type Is ArrayTypeSyntax) Then
                        category = "array"
                    ElseIf Regex.IsMatch(clause, "^As\s+(?:System\.)?String$", RegexOptions.IgnoreCase) AndAlso name.ToString() = key Then
                        category = "string"
                    ElseIf declared IsNot Nothing Then
                        category = CollectionCategory(declared.Type)
                        If category = "unknown" Then category = StreamCategory(declared.Type)
                    ElseIf created IsNot Nothing Then
                        Dim obj = TryCast(created.NewExpression, ObjectCreationExpressionSyntax)
                        If obj IsNot Nothing Then
                            category = CollectionCategory(obj.Type)
                            If category = "unknown" Then category = StreamCategory(obj.Type)
                        End If
                    End If
                    If d.AsClause Is Nothing AndAlso d.Initializer IsNot Nothing Then
                        Dim obj = TryCast(d.Initializer.Value, ObjectCreationExpressionSyntax)
                        If obj IsNot Nothing Then category = StreamCategory(obj.Type)
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
        Dim creation = TryCast(n, ObjectCreationExpressionSyntax)
        If creation IsNot Nothing Then
            Dim fileLabel = FileCreation(creation, types, warnings)
            If fileLabel <> "" Then Return fileLabel
            Dim collectionLabel = CollectionTypeLabel(creation.Type)
            If collectionLabel <> "" Then
                If creation.Initializer Is Nothing AndAlso (creation.ArgumentList Is Nothing OrElse creation.ArgumentList.Arguments.Count = 0) Then
                    Return "NEW " & collectionLabel
                End If
                warnings.Add("Collection constructor arguments or initializers are retained in VB.NET notation to preserve capacity, contents and comparer behaviour.")
            End If
        End If
        Dim callNode = TryCast(n, InvocationExpressionSyntax)
        If callNode IsNot Nothing Then
            Dim target = callNode.Expression.ToString()
            Dim consoleInput = Regex.IsMatch(target, "^(?:(?:Global\.)?System\.)?Console\.ReadLine$", RegexOptions.IgnoreCase) AndAlso Not types.ContainsKey("Console")
            Dim mathCall = Regex.Match(target, "^(?:(?:Global\.)?System\.)?Math\.(Sqrt|Abs|Floor|Ceiling|Sin|Cos|Tan|Log10|Exp)$", RegexOptions.IgnoreCase)
            Dim category = TypeOfLocal(callNode.Expression, types)
            Dim member = TryCast(callNode.Expression, MemberAccessExpressionSyntax)
            Dim stringMethod = member IsNot Nothing AndAlso member.Expression IsNot Nothing AndAlso TypeOfLocal(member.Expression, types) = "string"
            If consoleInput OrElse (mathCall.Success AndAlso Not types.ContainsKey("Math")) OrElse IsIndexed(category) OrElse stringMethod Then
                Dim args = Arguments(callNode, types, warnings)
                If args IsNot Nothing Then
                    If consoleInput AndAlso args.Count = 0 Then Return "USERINPUT"
                    If mathCall.Success AndAlso args.Count = 1 AndAlso Not types.ContainsKey("Math") Then Return mathCall.Groups(1).Value.ToUpperInvariant() & "(" & args(0) & ")"
                    If (category = "array" AndAlso args.Count > 0) OrElse ((category = "list" OrElse category = "dictionary") AndAlso args.Count = 1) Then Return target & "[" & String.Join(", ", args) & "]"
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
            If (category = "string" OrElse category = "array") AndAlso memberName.Equals("Length", StringComparison.OrdinalIgnoreCase) Then Return "LEN(" & Render(access.Expression, types, warnings) & ")"
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
            Dim simpleType = TryCast(d.AsClause, SimpleAsClauseSyntax)
            If Not constant AndAlso d.Names.Count = 1 AndAlso simpleType IsNot Nothing Then
                Dim identifier = d.Names(0)
                Dim nameText = identifier.Identifier.Text
                ' An initialized array reference has a rank but no allocated bounds in its
                ' declaration (e.g. Dim fields() As String = line.Split(",")). Keep its
                ' type and render its initializer without inventing an allocation.
                Dim rankSuffix = Regex.Match(identifier.ToString(), "^\w+\s*(\([ ,]*\))$")
                If d.Initializer IsNot Nothing AndAlso Regex.IsMatch(nameText, "^\w+$") Then
                    Dim arrayTypeText As String = Nothing
                    If rankSuffix.Success AndAlso Not TypeOf simpleType.Type Is ArrayTypeSyntax Then
                        arrayTypeText = simpleType.Type.ToString() & rankSuffix.Groups(1).Value
                    ElseIf identifier.ToString() = nameText AndAlso TypeOf simpleType.Type Is ArrayTypeSyntax Then
                        arrayTypeText = simpleType.Type.ToString()
                    End If
                    If arrayTypeText IsNot Nothing Then
                        lines.Add("DECLARE " & nameText & " : " & RoutineSignatures.TypeLabel(arrayTypeText) & " ← " & Convert(d.Initializer.Value.ToString(), types, warnings))
                        Continue For
                    End If
                End If
                ' VB array declaration bounds are inclusive upper indices, not element counts.
                If d.Initializer Is Nothing AndAlso identifier.ArrayBounds IsNot Nothing AndAlso identifier.ArrayRankSpecifiers.Count = 0 AndAlso Regex.IsMatch(nameText, "^\w+$") AndAlso Not TypeOf simpleType.Type Is ArrayTypeSyntax Then
                    Dim bounds = identifier.ArrayBounds.Arguments.Select(Function(a) a.ToString().Trim()).ToList()
                    If bounds.Count > 0 AndAlso bounds.All(Function(b) Regex.IsMatch(b, "^\d{1,9}$")) Then
                        lines.Add("DECLARE " & nameText & " : ARRAY[" & String.Join(", ", bounds.Select(Function(b) "0.." & b)) & "] OF " & RoutineSignatures.TypeLabel(simpleType.Type.ToString()))
                        Continue For
                    End If
                End If
                If Regex.IsMatch(identifier.ToString(), "^\w+$") Then
                    Dim collectionLabel = CollectionTypeLabel(simpleType.Type)
                    If collectionLabel <> "" Then
                        lines.Add("DECLARE " & nameText & " : " & collectionLabel & If(d.Initializer Is Nothing, "", " ← " & Convert(d.Initializer.Value.ToString(), types, warnings)))
                        If d.Initializer Is Nothing Then warnings.Add("An uninitialised collection reference has the default value Nothing; DECLARE does not create a collection.")
                        Continue For
                    End If
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
