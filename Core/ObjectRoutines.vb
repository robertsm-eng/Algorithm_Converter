Imports System.Linq
Imports System.Collections.Generic
Imports System.Text.RegularExpressions
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

' Type context describes declarations; it never inserts them as executable steps.
Friend NotInheritable Class ObjectRoutines
    Private Sub New()
    End Sub

    Private Shared Function Kind(n As SyntaxNode) As String
        Return CType(n.RawKind, SyntaxKind).ToString()
    End Function

    Private Shared Function IsClass(n As SyntaxNode) As Boolean
        Return Kind(n) = "ClassBlock" OrElse Kind(n) = "StructureBlock"
    End Function

    Private Shared Function TypeName(n As SyntaxNode) As String
        Dim header = n.ChildNodes().FirstOrDefault()
        Dim c = TryCast(header, ClassStatementSyntax)
        If c IsNot Nothing Then Return c.Identifier.Text
        Dim st = TryCast(header, StructureStatementSyntax)
        If st IsNot Nothing Then Return st.Identifier.Text
        Return ""
    End Function

    Friend Shared Function ApplySpecial(r As Routine, n As SyntaxNode, header As MethodBaseSyntax, result As ParseResult) As Boolean
        If header Is Nothing Then Return False
        Dim owner = n.Ancestors().FirstOrDefault(Function(a) IsClass(a))
        Dim ownerName = If(owner Is Nothing, "", TypeName(owner))
        Dim args As New List(Of String)
        Dim names As New List(Of String)
        Select Case Kind(n)
            Case "ConstructorBlock"
                RoutineSignatures.FormatParameters(header.ParameterList, result, args, names)
                Dim sharedConstructor = Regex.IsMatch(r.Header, "\bShared\s+Sub\s+New\b", RegexOptions.IgnoreCase)
                r.Signature = If(sharedConstructor, "SHARED ", "") & "CONSTRUCTOR(" & String.Join(", ", args) & ")"
                r.Footer = "ENDCONSTRUCTOR"
                r.EntryLabel = "START" & vbLf & If(ownerName = "", "Constructor", ownerName & ".New") & "(" & String.Join(", ", names) & ")"
                r.Name = "Constructor(" & String.Join(", ", args) & ")"
                RoutineSignatures.ReplaceExit(r.Body, "RETURN", result)
                If Not sharedConstructor Then result.Warnings.Add("Constructor flowcharts show the explicit body only; implicit base constructor calls and field initializers are not expanded.")
                Return True
            Case "GetAccessorBlock", "SetAccessorBlock"
                Dim propertyBlock = TryCast(n.Parent, PropertyBlockSyntax)
                If propertyBlock Is Nothing Then Return False
                Dim prop = propertyBlock.PropertyStatement
                Dim propertyName = prop.Identifier.Text
                If prop.ParameterList IsNot Nothing AndAlso prop.ParameterList.Parameters.Count > 0 Then
                    result.Warnings.Add("Indexed properties retain their VB.NET accessor signature; property index parameters are not expanded.")
                    Return False
                End If
                Dim getter = Kind(n) = "GetAccessorBlock"
                RoutineSignatures.FormatParameters(header.ParameterList, result, args, names)
                Dim verb = If(getter, "GET", "SET")
                r.Signature = verb & " " & propertyName & "(" & String.Join(", ", args) & ")"
                If getter Then
                    Dim declared = TryCast(prop.AsClause, SimpleAsClauseSyntax)
                    r.Signature &= " RETURNS " & RoutineSignatures.TypeLabel(If(declared Is Nothing, "Object", declared.Type.ToString()))
                End If
                r.Footer = "END" & verb
                r.Name = propertyName & " — " & verb
                r.EntryLabel = "START" & vbLf & If(ownerName = "", "", ownerName & ".") & propertyName & " " & verb & "(" & String.Join(", ", names) & ")"
                Dim implicitReturn = "RETURN current result of " & propertyName & " (default if unset)"
                RoutineSignatures.ReplaceExit(r.Body, If(getter, implicitReturn, "RETURN"), result)
                If getter AndAlso RoutineSignatures.CanFallThrough(r.Body) Then
                    r.Body.Add(New Instruction With {.Kind = "return", .Text = implicitReturn})
                    result.Warnings.Add("A property getter can return its current result or type default without an explicit Return.")
                End If
                Return True
        End Select
        Return False
    End Function

    Friend Shared Sub AddContext(r As Routine, n As SyntaxNode, result As ParseResult)
        Dim owner = n.Ancestors().FirstOrDefault(Function(a) IsClass(a))
        If owner Is Nothing Then Return
        Dim ownerName = String.Join(".", n.Ancestors().Where(Function(a) IsClass(a)).Reverse().Select(Function(a) TypeName(a)))
        If ownerName = "" Then Return
        r.Name = ownerName & " → " & r.Name
        Dim notes As New List(Of String)
        Dim declaration = If(Kind(owner) = "StructureBlock", "STRUCTURE ", "CLASS ") & ownerName
        For Each inheritance In owner.ChildNodes().Where(Function(a) Kind(a) = "InheritsStatement" OrElse Kind(a) = "ImplementsStatement")
            Dim text = inheritance.ToString().Trim()
            If text.StartsWith("Inherits ", StringComparison.OrdinalIgnoreCase) Then
                declaration &= " INHERITS " & text.Substring(9).Trim()
            Else
                notes.Add("// " & text)
            End If
        Next
        notes.Insert(0, "// " & declaration)
        ' The class header is context only. Do not make fields executable on every routine entry.
        For Each member In owner.ChildNodes()
            Select Case Kind(member)
                Case "FieldDeclaration"
                    Dim raw = member.ToString().Trim()
                    Dim fields = member.ChildNodes().OfType(Of VariableDeclaratorSyntax)().ToList()
                    If fields.Count = 1 AndAlso fields(0).Names.Count = 1 AndAlso fields(0).Initializer Is Nothing AndAlso Regex.IsMatch(fields(0).Names(0).ToString(), "^\w+$") Then
                        Dim asType = TryCast(fields(0).AsClause, SimpleAsClauseSyntax)
                        Dim modifier = Regex.Match(raw, "^(Public|Private|Protected|Friend)\s+", RegexOptions.IgnoreCase)
                        If asType IsNot Nothing AndAlso modifier.Success AndAlso raw.Substring(modifier.Length).StartsWith(fields(0).ToString(), StringComparison.Ordinal) Then
                            notes.Add("// " & modifier.Groups(1).Value.ToUpperInvariant() & " " & fields(0).Names(0).Identifier.Text & " : " & RoutineSignatures.TypeLabel(asType.Type.ToString()))
                            Continue For
                        End If
                    End If
                    notes.Add("// FIELD (VB.NET): " & Regex.Replace(raw, "[\r\n]+", " "))
                Case "PropertyStatement", "PropertyBlock"
                    Dim prop = If(Kind(member) = "PropertyBlock", member.ChildNodes().FirstOrDefault(), member)
                    If prop IsNot Nothing Then notes.Add("// PROPERTY (VB.NET): " & Regex.Replace(prop.ToString().Trim(), "[\r\n]+", " "))
            End Select
        Next
        If r.SignatureNote <> "" Then notes.Add(r.SignatureNote)
        r.SignatureNote = String.Join(vbLf, notes)
        Dim method = TryCast(n.ChildNodes().FirstOrDefault(), MethodStatementSyntax)
        If method IsNot Nothing AndAlso r.Signature <> "" Then
            Dim modifiers As New List(Of String)
            For Each token In method.Modifiers
                Select Case token.ValueText.ToUpperInvariant()
                    Case "OVERRIDES" : modifiers.Add("OVERRIDE")
                    Case "OVERRIDABLE" : modifiers.Add("VIRTUAL")
                    Case "SHARED" : modifiers.Add("SHARED")
                    Case "SHADOWS" : modifiers.Add("SHADOWS")
                    Case "NOTOVERRIDABLE" : modifiers.Add("FINAL")
                End Select
            Next
            If modifiers.Count > 0 Then r.Signature = String.Join(" ", modifiers) & " " & r.Signature
            If r.EntryLabel.StartsWith("START" & vbLf, StringComparison.Ordinal) Then
                r.EntryLabel = "START" & vbLf & ownerName & "." & r.EntryLabel.Substring(("START" & vbLf).Length)
            End If
        End If
    End Sub
End Class
