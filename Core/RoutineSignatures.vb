Imports System.Linq
Imports System.Collections.Generic
Imports System.Text.RegularExpressions
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.CodeAnalysis.VisualBasic

' Signatures are read from Roslyn nodes, not split at commas in generic types/defaults.
Friend NotInheritable Class RoutineSignatures
    Private Sub New()
    End Sub

    Friend Shared Function TypeLabel(raw As String) As String
        Dim text = raw.Trim()
        Dim array = Regex.Match(text, "^(.*?)\s*\(([ ,]*)\)$")
        If array.Success Then
            Dim rank = array.Groups(2).Value.Count(Function(ch) ch = ","c) + 1
            Return "ARRAY" & If(rank = 1, "", " (" & rank.ToString() & " DIMENSIONS)") & " OF " & TypeLabel(array.Groups(1).Value)
        End If
        Dim parsedType = SyntaxFactory.ParseTypeName(text, 0, True)
        If Not parsedType.ContainsDiagnostics Then
            Dim collection = PseudoExpressions.CollectionTypeLabel(parsedType)
            If collection <> "" Then Return collection
        End If
        Select Case text.ToUpperInvariant()
            Case "INTEGER", "SYSTEM.INT32" : Return "INTEGER"
            Case "DOUBLE", "SYSTEM.DOUBLE" : Return "REAL"
            Case "STRING", "SYSTEM.STRING" : Return "STRING"
            Case "BOOLEAN", "SYSTEM.BOOLEAN" : Return "BOOLEAN"
            Case "CHAR", "SYSTEM.CHAR" : Return "CHARACTER"
            Case "OBJECT", "SYSTEM.OBJECT" : Return "OBJECT"
            Case "BYTE", "SBYTE", "SHORT", "USHORT", "LONG", "ULONG", "UINTEGER", "SINGLE", "DECIMAL", "DATE" : Return text.ToUpperInvariant()
            Case Else : Return text
        End Select
    End Function

    Friend Shared Function ParameterCategory(p As ParameterSyntax) As String
        Dim raw = If(p.AsClause Is Nothing, "", p.AsClause.Type.ToString())
        If Regex.IsMatch(p.Identifier.ToString(), "\([ ,]*\)$") OrElse Regex.IsMatch(raw, "\([ ,]*\)$") Then Return "array"
        If Regex.IsMatch(raw, "^(?:System\.)?String$", RegexOptions.IgnoreCase) Then Return "string"
        Return If(p.AsClause Is Nothing, "unknown", PseudoExpressions.CollectionCategory(p.AsClause.Type))
    End Function

    Friend Shared Sub AddParameterTypes(method As MethodBaseSyntax, result As ParseResult)
        If method Is Nothing OrElse method.ParameterList Is Nothing Then Return
        For Each p In method.ParameterList.Parameters
            Dim name = p.Identifier.Identifier.ValueText
            result.LocalTypes(name) = If(result.LocalTypes.ContainsKey(name), "unknown", ParameterCategory(p))
        Next
    End Sub

    Friend Shared Sub Apply(r As Routine, method As MethodStatementSyntax, result As ParseResult)
        If method Is Nothing Then
            If r.Header <> "" Then result.Warnings.Add("This routine uses a constructor, accessor or operator header; its original VB.NET signature is retained.")
            Return
        End If
        Dim name = method.Identifier.Text
        If method.Modifiers.Any(Function(t) t.ValueText.Equals("Async", StringComparison.OrdinalIgnoreCase) OrElse t.ValueText.Equals("Iterator", StringComparison.OrdinalIgnoreCase)) OrElse method.TypeParameterList IsNot Nothing OrElse Not Regex.IsMatch(name, "^(?:\w+|\[\w+\])$") Then
            result.Warnings.Add("Advanced routine signature retained in VB.NET; generic, Async, Iterator or type-character semantics are not fully represented.")
            Return
        End If
        Dim isFunction = method.SubOrFunctionKeyword.ValueText.Equals("Function", StringComparison.OrdinalIgnoreCase)
        Dim args As New List(Of String)
        Dim names As New List(Of String)
        FormatParameters(method.ParameterList, result, args, names)
        r.Signature = If(isFunction, "FUNCTION ", "SUBROUTINE ") & name & "(" & String.Join(", ", args) & ")"
        If isFunction Then r.Signature &= " RETURNS " & TypeLabel(If(method.AsClause Is Nothing, "Object", method.AsClause.Type.ToString()))
        r.Footer = If(isFunction, "ENDFUNCTION", "ENDSUBROUTINE")
        r.EntryLabel = "START" & vbLf & name & "(" & String.Join(", ", names) & ")"
        r.ExitLabel = "END"
        If method.HandlesClause IsNot Nothing Then r.SignatureNote = "// " & method.HandlesClause.ToString()
        If method.ImplementsClause IsNot Nothing Then r.SignatureNote = (r.SignatureNote & vbLf & "// " & method.ImplementsClause.ToString()).Trim()
        If isFunction Then
            ' VB permits returning via the function-name result variable, even without Return.
            Dim implicitResult = "RETURN current result of " & name & " (default if unset)"
            ReplaceExit(r.Body, implicitResult, result)
            If CanFallThrough(r.Body) Then
                r.Body.Add(New Instruction With {.Kind = "return", .Text = implicitResult})
                result.Warnings.Add("A Function can reach End Function without an explicit Return; the current function result or its type default is returned.")
            End If
        Else
            ReplaceExit(r.Body, "RETURN", result)
        End If
    End Sub

    Friend Shared Sub FormatParameters(parameters As ParameterListSyntax, result As ParseResult, args As List(Of String), names As List(Of String))
        If parameters IsNot Nothing Then
            For Each p In parameters.Parameters
                Dim pname = p.Identifier.Identifier.Text
                names.Add(pname)
                Dim byReference = p.Modifiers.Any(Function(t) t.ValueText.Equals("ByRef", StringComparison.OrdinalIgnoreCase))
                Dim optionalParam = p.Modifiers.Any(Function(t) t.ValueText.Equals("Optional", StringComparison.OrdinalIgnoreCase))
                Dim isParamArray = p.Modifiers.Any(Function(t) t.ValueText.Equals("ParamArray", StringComparison.OrdinalIgnoreCase))
                Dim identifierText = p.Identifier.ToString()
                Dim suffix = identifierText.Substring(pname.Length).Trim()
                If p.AttributeLists.Count > 0 OrElse Not Regex.IsMatch(pname, "^(?:\w+|\[\w+\])$") OrElse (suffix <> "" AndAlso Not Regex.IsMatch(suffix, "^\([ ,]*\)$")) Then
                    args.Add(p.ToString())
                    result.Warnings.Add("Parameter attributes or type characters retained in VB.NET notation.")
                    Continue For
                End If
                Dim rawType = If(p.AsClause Is Nothing, "Object", p.AsClause.Type.ToString()) & suffix
                Dim prefix = If(optionalParam, "OPTIONAL ", "") & If(isParamArray, "PARAMARRAY ", "") & If(byReference, "BYREF ", "BYVAL ")
                Dim arg = prefix & pname & " : " & TypeLabel(rawType)
                If p.[Default] IsNot Nothing Then arg &= " = " & PseudoExpressions.Convert(p.[Default].Value.ToString(), result.LocalTypes, result.Warnings)
                args.Add(arg)
            Next
        End If
    End Sub

    Friend Shared Sub ReplaceExit(items As IEnumerable(Of Instruction), implicitResult As String, result As ParseResult)
        For Each item In items
            If item.Kind = "return" AndAlso (item.Text = "EXIT FUNCTION" OrElse item.Text = "EXIT PROPERTY") AndAlso implicitResult <> "RETURN" Then
                item.Text = implicitResult
                result.Warnings.Add("Exit Function or Exit Property returns the current function result (or the type default if unset).")
            ElseIf item.Kind = "return" AndAlso (item.Text = "EXIT SUB" OrElse item.Text = "EXIT PROPERTY") AndAlso implicitResult = "RETURN" Then
                item.Text = "RETURN"
            End If
            ReplaceExit(item.Body, implicitResult, result)
            ReplaceExit(item.Other, implicitResult, result)
            For Each branch In item.Branches
                ReplaceExit(branch.Body, implicitResult, result)
            Next
        Next
    End Sub

    ' Conservative fallthrough check: a loop or unsupported block is not assumed to return.
    Friend Shared Function CanFallThrough(items As List(Of Instruction)) As Boolean
        For Each item In items
            If item.Kind = "return" Then Return False
            If item.Kind = "if" AndAlso item.Other.Count > 0 AndAlso Not CanFallThrough(item.Body) AndAlso Not CanFallThrough(item.Other) AndAlso item.Branches.All(Function(b) Not CanFallThrough(b.Body)) Then Return False
        Next
        Return True
    End Function
End Class
