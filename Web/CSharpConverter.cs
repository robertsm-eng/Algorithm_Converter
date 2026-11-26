using System.Text;
using AlgorithmCore;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Web;

// Syntax-only C# front end. The existing VB.NET intermediate representation,
// flowchart layout, SVG and diagrams.net XML exporters are reused unchanged.
internal sealed class CSharpConverter
{
    private readonly ParseResult result = new();
    private readonly Stack<(string Break, string? Continue)> loops = new();
    private bool unsafeFlow;
    private readonly HashSet<Routine> incomplete = new();

    internal static (ParseResult Result, Func<Routine, Routine> GraphRoutine) Parse(string source)
    {
        var parser = new CSharpConverter();
        parser.ReadSource(source);
        return (parser.result, parser.ForGraph);
    }

    private Routine ForGraph(Routine r) => !incomplete.Contains(r) ? r : new Routine
    {
        Name = r.Name, Header = "", EntryLabel = r.EntryLabel, ExitLabel = r.ExitLabel,
        Body = new List<Instruction> { Item("unsupported", "Flowchart not expanded: unsupported C# control flow. See pseudocode and warnings.") }
    };

    private void Warn(string message) { if (!result.Warnings.Contains(message)) result.Warnings.Add(message); }
    private static Instruction Item(string kind, string text) => new() { Kind = kind, Text = text };
    private static string TypeName(string type) => type switch
    {
        "int" => "INTEGER", "string" => "STRING", "bool" => "BOOLEAN", "double" => "REAL",
        "char" => "CHARACTER", "object" => "OBJECT", "var" => "INFERRED", _ => type
    };
    private static string TypeName(TypeSyntax type) => type is ArrayTypeSyntax a
        ? "ARRAY" + string.Concat(a.RankSpecifiers.Select(r => r.Rank == 1 ? "" : $" ({r.Rank} DIMENSIONS)")) + " OF " + TypeName(a.ElementType)
        : CollectionType(type) ?? TypeName(type.ToString());

    // Only standard collection spellings; preserve unfamiliar generic types.
    private static string? CollectionType(TypeSyntax type)
    {
        var g = type as GenericNameSyntax ?? (type as QualifiedNameSyntax)?.Right as GenericNameSyntax;
        if (g is null || (type is GenericNameSyntax && HasLocalTypeName(type,g.Identifier.ValueText))) return null;
        if (type is QualifiedNameSyntax q && q.Left.ToString() is not ("System.Collections.Generic" or "global::System.Collections.Generic")) return null;
        string? name = g.Identifier.ValueText switch {
            "List" => "LIST", "Stack" => "STACK", "Queue" => "QUEUE", "Dictionary" => "DICTIONARY", "HashSet" => "SET", _ => null
        };
        var args = g.TypeArgumentList.Arguments;
        if (name is null || args.Count != (name == "DICTIONARY" ? 2 : 1)) return null;
        var labels = args.Select(TypeName);
        return name + " OF " + (args.Count == 1 ? labels.First() : "(" + string.Join(", ", labels) + ")");
    }
    private static bool HasLocalTypeName(TypeSyntax type, string name) => type.SyntaxTree.GetRoot().DescendantNodes().Any(n=>
        n is BaseTypeDeclarationSyntax t && t.Identifier.ValueText==name || n is UsingDirectiveSyntax u && u.Alias?.Name.Identifier.ValueText==name);
    private static string? StreamType(TypeSyntax? type) => type is IdentifierNameSyntax id && HasLocalTypeName(type,id.Identifier.ValueText) ? null : type?.ToString() switch {
        "StreamReader" or "System.IO.StreamReader" or "global::System.IO.StreamReader" => "READ",
        "StreamWriter" or "System.IO.StreamWriter" or "global::System.IO.StreamWriter" => "WRITE",
        _ => null
    };
    // Conservative lexical lookup: no inference from names, no lookups across sibling blocks.
    private static TypeSyntax? DeclaredType(IdentifierNameSyntax reference)
    {
        var name = reference.Identifier.ValueText;
        foreach (var scope in reference.Ancestors())
        {
            IEnumerable<VariableDeclarationSyntax> declarations = scope switch {
                BlockSyntax b => b.Statements.OfType<LocalDeclarationStatementSyntax>().Select(d=>d.Declaration),
                CompilationUnitSyntax c when !reference.Ancestors().Any(n=>n is TypeDeclarationSyntax) => c.Members.OfType<GlobalStatementSyntax>().Select(g=>g.Statement).OfType<LocalDeclarationStatementSyntax>().Select(d=>d.Declaration),
                UsingStatementSyntax u when u.Declaration is not null => new[] {u.Declaration},
                ForStatementSyntax f when f.Declaration is not null => new[] {f.Declaration},
                TypeDeclarationSyntax t => t.Members.OfType<FieldDeclarationSyntax>().Select(d=>d.Declaration),
                _ => Array.Empty<VariableDeclarationSyntax>()
            };
            foreach (var d in declarations)
            foreach (var v in d.Variables)
            {
                if (v.Identifier.ValueText != name || (scope is not TypeDeclarationSyntax && v.SpanStart >= reference.SpanStart)) continue;
                if (d.Type.ToString() != "var") return d.Type;
                if (v.Initializer?.Value is ObjectCreationExpressionSyntax creation) return creation.Type;
                if (v.Initializer?.Value is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword));
                return null;
            }
            var parameters = scope switch {
                BaseMethodDeclarationSyntax m => m.ParameterList.Parameters,
                LocalFunctionStatementSyntax l => l.ParameterList.Parameters,
                _ => default(SeparatedSyntaxList<ParameterSyntax>)
            };
            foreach (var parameter in parameters) if (parameter.Identifier.ValueText == name) return parameter.Type;
            if (scope is ForEachStatementSyntax each && each.Identifier.ValueText == name) return each.Type;
            if (scope is CatchClauseSyntax c && c.Declaration?.Identifier.ValueText == name) return c.Declaration.Type;
            if (scope is AccessorDeclarationSyntax a && name == "value" && a.Parent?.Parent is PropertyDeclarationSyntax prop) return prop.Type;
        }
        return null;
    }
    private static bool IsStringPath(ExpressionSyntax e) => e is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression)
        || e is IdentifierNameSyntax id && DeclaredType(id)?.ToString() is ("string" or "String" or "System.String" or "global::System.String");
    private string? CreationLabel(ObjectCreationExpressionSyntax creation)
    {
        if (creation.Initializer is not null || creation.ArgumentList is null) return null;
        var args = creation.ArgumentList.Arguments;
        if (args.Count == 0 && CollectionType(creation.Type) is string collection) return "NEW " + collection;
        var mode = StreamType(creation.Type);
        if (mode is null) return null;
        if (args.Count >= 1 && args.All(a=>a.NameColon is null && a.RefKindKeyword.RawKind==0) && IsStringPath(args[0].Expression))
        {
            if (args.Count == 1) return "OPENFILE("+Expr(args[0].Expression)+", "+mode+")";
            if (mode == "WRITE" && args.Count == 2 && args[1].Expression is LiteralExpressionSyntax flag && (flag.IsKind(SyntaxKind.TrueLiteralExpression) || flag.IsKind(SyntaxKind.FalseLiteralExpression)))
                return "OPENFILE("+Expr(args[0].Expression)+", "+(flag.IsKind(SyntaxKind.TrueLiteralExpression)?"APPEND":"WRITE")+")";
        }
        Warn("C# stream constructor overload retained in source-style notation; file mode is not inferred.");
        return null;
    }
    private static bool HasFileIo(SyntaxNode node) => node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(i =>
        i.Expression is MemberAccessExpressionSyntax m && m.Expression is IdentifierNameSyntax id &&
        (StreamType(DeclaredType(id)) switch {
            "READ" => m.Name.Identifier.ValueText is "Read" or "ReadLine" or "ReadToEnd" or "ReadBlock",
            "WRITE" => m.Name.Identifier.ValueText is "Write" or "WriteLine",
            _ => false
        }));
    private static string? ArrayCreationLabel(ArrayCreationExpressionSyntax creation)
    {
        if (creation.Initializer is not null || creation.Type.RankSpecifiers.Count != 1) return null;
        var sizes = creation.Type.RankSpecifiers[0].Sizes;
        if (sizes.Any(s=>s is not LiteralExpressionSyntax l || !l.IsKind(SyntaxKind.NumericLiteralExpression) || l.Token.Value is not int n || n<=0)) return null;
        return "NEW ARRAY["+string.Join(", ", sizes.Select(s=>"0.."+((int)((LiteralExpressionSyntax)s).Token.Value! - 1)))+"] OF "+TypeName(creation.Type.ElementType);
    }
    private static bool HasCreation(SyntaxNode n) => n is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax;


    private void ReadSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) { result.Errors.Add("Enter C# code first."); return; }
        if (source.Length > 200000) { result.Errors.Add("Please convert a smaller selection (maximum 200,000 characters)."); return; }
        // Top-level statements, complete types and standalone methods. Wrap only if needed.
        var candidates = new[] { source, "class SnippetContainer {\n" + source + "\n}", "class SnippetContainer {\nvoid Snippet() {\n" + source + "\n}\n}" };
        CompilationUnitSyntax? root = null;
        Diagnostic[]? best = null;
        int offset = 0, bestOffset = 0;
        for (int i=0; i<candidates.Length; i++)
        {
            var tree = CSharpSyntaxTree.ParseText(candidates[i]);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (best is null || errors.Length < best.Length) { best = errors; bestOffset = i; }
            if (errors.Length == 0) { root = tree.GetCompilationUnitRoot(); offset = i; break; }
        }
        if (root is null)
        {
            foreach (var d in best!.Take(25)) result.Errors.Add($"Line {Math.Max(1, d.Location.GetLineSpan().StartLinePosition.Line + 1 - bestOffset)}: {d.GetMessage()}");
            return;
        }
        var globals = root.Members.OfType<GlobalStatementSyntax>().Select(g => g.Statement).Where(s => s is not LocalFunctionStatementSyntax).ToList();
        if (globals.Count > 0) AddRoutine("Code snippet", "", "", "START", globals, null);
        foreach (var n in root.DescendantNodes().Where(n => n is MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax or PropertyDeclarationSyntax))
        {
            // A method inside an unsupported local type/lambda is not an independent source routine.
            switch (n)
            {
                case MethodDeclarationSyntax m when m.Body is not null || m.ExpressionBody is not null:
                    if (offset == 2 && m.Identifier.ValueText == "Snippet")
                        AddRoutine("Code snippet", "", "", "START", m.Body!.Statements, null);
                    else ReadMethod(m, m.Identifier.Text, m.ParameterList.Parameters, m.ReturnType, m.Body, m.ExpressionBody, m.Modifiers, offset, m.TypeParameterList?.ToString() ?? "");
                    break;
                case LocalFunctionStatementSyntax l when l.Body is not null || l.ExpressionBody is not null:
                    ReadMethod(l, l.Identifier.Text, l.ParameterList.Parameters, l.ReturnType, l.Body, l.ExpressionBody, l.Modifiers, offset, l.TypeParameterList?.ToString() ?? "");
                    break;
                case PropertyDeclarationSyntax property:
                    ReadProperty(property, offset);
                    break;
                case ConstructorDeclarationSyntax c when c.Body is not null || c.ExpressionBody is not null:
                    ReadMethod(c, c.Identifier.Text, c.ParameterList.Parameters, null, c.Body, c.ExpressionBody, c.Modifiers, offset, "", c.Initializer);
                    break;
            }
        }
        if (root.DescendantNodes().Any(n => n is IndexerDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax or DestructorDeclarationSyntax))
            Warn("C# indexer, operator and destructor bodies are not converted as separate routines in this release.");
        if (root.DescendantNodes().Any(n => n is AnonymousFunctionExpressionSyntax or QueryExpressionSyntax))
            Warn("C# LINQ and lambda expressions are retained as source expressions, not expanded.");
        if (result.Routines.Count == 0) result.Errors.Add("No executable C# method or statements were found.");
    }

    private void ReadMethod(SyntaxNode node, string name, SeparatedSyntaxList<ParameterSyntax> parameters, TypeSyntax? returnType, BlockSyntax? body, ArrowExpressionClauseSyntax? arrow, SyntaxTokenList modifiers, int offset, string generic, ConstructorInitializerSyntax? initializer = null)
    {
        var owner = string.Join(".", node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.Text).Where(t => offset == 0 || t != "SnippetContainer"));
        var qualified = owner.Length == 0 ? name : owner + "." + name;
        bool ctor = returnType is null;
        bool isVoid = returnType?.ToString() == "void";
        var args = string.Join(", ", parameters.Select(p => (p.Modifiers.Count == 0 ? "BYVAL" : string.Join(" ", p.Modifiers.Select(t=>t.IsKind(SyntaxKind.RefKeyword)?"BYREF":t.Text.ToUpperInvariant()))) + " " + p.Identifier.Text + " : " + (p.Type is null ? "INFERRED" : TypeName(p.Type)) + (p.Default is null ? "" : " = " + Expr(p.Default.Value))));
        var names = string.Join(", ", parameters.Select(p => p.Identifier.Text));
        var kind = ctor ? "CONSTRUCTOR" : isVoid ? "SUBROUTINE" : "FUNCTION";
        var prefix = string.Join(" ", modifiers.Where(t => t.IsKind(SyntaxKind.OverrideKeyword) || t.IsKind(SyntaxKind.VirtualKeyword) || t.IsKind(SyntaxKind.StaticKeyword) || t.IsKind(SyntaxKind.AsyncKeyword)).Select(t=>t.IsKind(SyntaxKind.StaticKeyword)?"SHARED":t.Text.ToUpperInvariant()));
        var signature = (prefix.Length == 0 ? "" : prefix + " ") + kind + " " + name + generic + "(" + args + ")" + (!ctor && !isVoid ? " RETURNS " + TypeName(returnType!) : "");
        var r = AddRoutine(qualified + "(" + args + ")", signature, "END" + kind, "START\n" + qualified + "(" + names + ")", body?.Statements ?? default, arrow is null ? null : (ctor || isVoid || arrow.Expression is ThrowExpressionSyntax ? ExpressionItem(arrow.Expression) : Item("return", "RETURN " + Expr(arrow.Expression))));
        r.SignatureNote = ClassContext(node, offset);
        if (ctor) Warn("C# constructor diagrams represent explicit constructor code and initializer calls; implicit base calls and field initializers are not expanded.");
        if (initializer is not null)
            r.Body.Insert(0, Item("call", (initializer.ThisOrBaseKeyword.IsKind(SyntaxKind.BaseKeyword) ? "PARENT" : "ME") + ".CONSTRUCTOR(" + string.Join(", ", initializer.ArgumentList.Arguments.Select(a=>Expr(a))) + ")"));
        if (modifiers.Any(t=>t.IsKind(SyntaxKind.AsyncKeyword)) || node.DescendantNodes().Any(n=>n is YieldStatementSyntax))
        { incomplete.Add(r); Warn("Async/iterator routines retain pseudocode but their flowcharts are not expanded."); }
        if ((node is MethodDeclarationSyntax m && m.ConstraintClauses.Count > 0) || (node is LocalFunctionStatementSyntax l && l.ConstraintClauses.Count > 0))
            Warn("Generic constraints are not shown in C# routine signatures.");
    }

    private static string ClassContext(SyntaxNode node, int offset)
    {
        var type = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (type is null || (offset != 0 && type.Identifier.ValueText == "SnippetContainer")) return "";
        var lines = new List<string> { "// " + (type is ClassDeclarationSyntax ? "CLASS" : type is StructDeclarationSyntax ? "STRUCT" : type is InterfaceDeclarationSyntax ? "INTERFACE" : "RECORD") + " " + type.Identifier.Text + (type.BaseList is null ? "" : " : " + string.Join(", ", type.BaseList.Types.Select(t=>t.Type.ToString()))) };
        foreach (var f in type.Members.OfType<FieldDeclarationSyntax>())
            foreach (var v in f.Declaration.Variables)
                lines.Add("// " + (f.Modifiers.Count == 0 ? "PRIVATE" : string.Join(" ",f.Modifiers.Select(m=>m.Text.ToUpperInvariant()))) + " " + v.Identifier.Text + " : " + TypeName(f.Declaration.Type) + (v.Initializer is null ? "" : " (field initializer retained in source)"));
        return string.Join("\n", lines);
    }
    private void ReadProperty(PropertyDeclarationSyntax p, int offset)
    {
        var owner = string.Join(".", p.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(t=>t.Identifier.Text).Where(t=>offset==0||t!="SnippetContainer"));
        var name = (owner.Length==0?"":owner+".")+p.Identifier.Text;
        void AddAccessor(string kind, BlockSyntax? body, ArrowExpressionClauseSyntax? arrow)
        {
            string args = kind=="GET"?"":"BYVAL value : "+TypeName(p.Type);
            var r = AddRoutine(name+" — "+kind, kind+" "+p.Identifier.Text+"("+args+")"+(kind=="GET"?" RETURNS "+TypeName(p.Type):""), "END"+kind,
                "START\n"+name+"."+kind+"("+(kind=="GET"?"":"value")+")", body?.Statements??default,
                arrow is null?null:kind=="GET" && arrow.Expression is not ThrowExpressionSyntax?Item("return","RETURN "+Expr(arrow.Expression)):ExpressionItem(arrow.Expression));
            r.SignatureNote=ClassContext(p,offset);
        }
        if (p.ExpressionBody is not null) AddAccessor("GET",null,p.ExpressionBody);
        if (p.AccessorList is null) return;
        foreach(var a in p.AccessorList.Accessors)
        {
            if (a.Body is null && a.ExpressionBody is null) { Warn("C# auto-properties are not expanded into invented algorithms."); continue; }
            if (a.IsKind(SyntaxKind.GetAccessorDeclaration)) AddAccessor("GET",a.Body,a.ExpressionBody);
            else if (a.IsKind(SyntaxKind.SetAccessorDeclaration)) AddAccessor("SET",a.Body,a.ExpressionBody);
            else Warn("C# init accessors retain their original source and are not expanded.");
        }
    }
    private Routine AddRoutine(string name, string signature, string footer, string entry, IEnumerable<StatementSyntax> statements, Instruction? expression)
    {
        unsafeFlow = false; loops.Clear();
        var r = new Routine { Name = name, Header = "", Signature = signature, Footer = footer, EntryLabel = entry, ExitLabel = "END", Body = ReadList(statements) };
        if (expression is not null) r.Body.Add(expression);
        if (unsafeFlow) incomplete.Add(r);
        result.Routines.Add(r);
        return r;
    }
    private List<Instruction> ReadList(IEnumerable<StatementSyntax> statements)
    {
        var list = new List<Instruction>();
        var items = statements.ToList();
        for (int i=0; i<items.Count; i++)
        {
            var statement=items[i];
            if (statement is LocalDeclarationStatementSyntax d && d.UsingKeyword.RawKind != 0 && d.AwaitKeyword.RawKind == 0 && TryUsing(d.Declaration,out var resource))
            {
                // C# using declarations dispose at the end of the enclosing scope.
                resource.Body=ReadList(items.Skip(i+1)); list.Add(resource); break;
            }
            list.AddRange(Read(statement));
        }
        return list;
    }
    private List<Instruction> Body(StatementSyntax s) => Read(s);
    private List<Instruction> LoopBody(StatementSyntax s, string kind)
    {
        loops.Push((kind, kind));
        try { return Body(s); } finally { loops.Pop(); }
    }
    private Instruction Unsupported(StatementSyntax s, string reason)
    {
        unsafeFlow = true;
        Warn("C# " + reason + " is retained as source; its routine flowchart is not expanded.");
        return Item("unsupported", s.ToString());
    }
    private List<Instruction> Read(StatementSyntax s)
    {
        switch (s)
        {
            case BlockSyntax b: return ReadList(b.Statements);
            case EmptyStatementSyntax: return new();
            case LocalFunctionStatementSyntax: return new(); // separately selectable; declaration does not execute
            case LocalDeclarationStatementSyntax d when d.UsingKeyword.RawKind != 0 || d.AwaitKeyword.RawKind != 0 || d.Declaration.Type is RefTypeSyntax:
                return new() { Unsupported(s, "using declaration or ref local") };
            case LocalDeclarationStatementSyntax d:
                return Declarations(d.Declaration, d.Modifiers.Any(t=>t.IsKind(SyntaxKind.ConstKeyword)));
            case ExpressionStatementSyntax e: return new() { ExpressionItem(e.Expression) };
            case IfStatementSyntax i:
                return new() { new Instruction { Kind = "if", Text = Expr(i.Condition), Body = Body(i.Statement), Other = i.Else is null ? new() : Body(i.Else.Statement) } };
            case WhileStatementSyntax w:
                return new() { new Instruction { Kind = "while", Text = Expr(w.Condition), Body = LoopBody(w.Statement, "while") } };
            case DoStatementSyntax d:
                return new() { new Instruction { Kind = "do", Text = Expr(d.Condition), PostTest = true, Body = LoopBody(d.Statement, "do") } };
            case ForStatementSyntax f: return ReadFor(f);
            case ForEachStatementSyntax f when f.AwaitKeyword.RawKind == 0 && f.Type is not RefTypeSyntax:
                return new() { new Instruction { Kind = "foreach", Text = "FOR EACH " + f.Identifier.Text + " IN " + Expr(f.Expression), Body = LoopBody(f.Statement, "for") } };
            case BreakStatementSyntax:
                return new() { loops.Count == 0 ? Unsupported(s,"break outside a supported loop/switch") : new Instruction { Kind="exit", Detail=loops.Peek().Break, Text="BREAK" } };
            case ContinueStatementSyntax:
                var target = loops.FirstOrDefault(x=>x.Continue is not null).Continue;
                return new() { target is null ? Unsupported(s,"continue outside a supported loop") : new Instruction { Kind="continue", Detail=target, Text="CONTINUE" } };
            case ReturnStatementSyntax r: return new() { Item("return", r.Expression is null ? "RETURN" : "RETURN " + Expr(r.Expression)) };
            case ThrowStatementSyntax t: return new() { Item("throw", t.Expression is null ? "RETHROW" : "THROW " + Expr(t.Expression)) };
            case SwitchStatementSyntax sw: return new() { ReadSwitch(sw) };
            case TryStatementSyntax tr: return new() { ReadTry(tr) };
            case UsingStatementSyntax u when u.AwaitKeyword.RawKind == 0 && u.Declaration is not null && TryUsing(u.Declaration,out var resource):
                resource.Body=Body(u.Statement); return new() {resource};
            default: return new() { Unsupported(s, s.Kind().ToString()) };
        }
    }
    private bool TryUsing(VariableDeclarationSyntax d, out Instruction resource)
    {
        resource=Item("unsupported",d.ToString());
        if(d.Variables.Count!=1) return false;
        var v=d.Variables[0];
        if(v.Initializer?.Value is not ObjectCreationExpressionSyntax c || StreamType(c.Type) is null || (d.Type.ToString()!="var" && StreamType(d.Type)!=StreamType(c.Type))) return false;
        resource=Item("using",v.Identifier.Text+" ← "+Expr(c)); resource.Detail=v.Identifier.Text;
        Warn("C# USING flowcharts show normal and explicit transfer cleanup; implicit exception paths are not drawn.");
        return true;
    }
    private List<Instruction> Declarations(VariableDeclarationSyntax declaration, bool constant = false)
    {
        return declaration.Variables.Select(v => {
            string name = v.Identifier.Text;
            string type = TypeName(declaration.Type);
            string text = v.Initializer is null ? "DECLARE " + name + " : " + type : (constant ? "CONSTANT " : "") + name + " ← " + Expr(v.Initializer.Value);
            string kind = v.Initializer is not null && (HasConsoleInput(v.Initializer.Value)||HasFileIo(v.Initializer.Value)) ? "io" : v.Initializer is not null && HasCreation(v.Initializer.Value) ? "call" : "process";
            return Item(kind,text);
        }).ToList();
    }
    private List<Instruction> ReadFor(ForStatementSyntax f)
    {
        var initial = f.Declaration is null ? f.Initializers.Select(ExpressionItem).ToList() : Declarations(f.Declaration);
        var updates = f.Incrementors.Select(ExpressionItem).ToList();
        if (f.Condition is null || updates.Count == 0) return new() { Unsupported(f,"for loop without a condition or iterator") };
        var condition = Expr(f.Condition);
        var init = string.Join("\n",initial.Select(x=>x.Text));
        var update = string.Join("\n",updates.Select(x=>x.Text));
        // A C# condition is re-evaluated each time; do NOT use VB's saved limit.
        // Put real initializers before the loop; use an explicit no-op setup if none.
        return new() { new Instruction { Kind="for", Text="FOR ("+string.Join("; ",initial.Select(x=>x.Text))+"; "+condition+"; "+string.Join("; ",updates.Select(x=>x.Text))+")", LoopInitialiser=init.Length==0?"No initialisation":init, LoopCondition=condition, LoopIncrement=update, Body=LoopBody(f.Statement,"for") } };
    }
    private Instruction ReadSwitch(SwitchStatementSyntax sw)
    {
        if (sw.Sections.Any(sec=>sec.Labels.Any(l=>l is not CaseSwitchLabelSyntax && l is not DefaultSwitchLabelSyntax) || sec.Labels.OfType<CaseSwitchLabelSyntax>().Any(l=>l.Value.DescendantTokens().Any(t=>t.IsKind(SyntaxKind.StringLiteralToken) && t.Text.Contains('\\')))))
            return Unsupported(sw,"switch patterns, guards or escaped case labels");
        var item=Item("select",Expr(sw.Expression));
        loops.Push(("select",null));
        try {
            foreach(var sec in sw.Sections) {
                var cases=sec.Labels.OfType<CaseSwitchLabelSyntax>().Select(l=>Expr(l.Value)).ToList();
                var stmts=sec.Statements.ToList();
                if(stmts.LastOrDefault() is BreakStatementSyntax) stmts.RemoveAt(stmts.Count-1);
                var body=ReadList(stmts);
                if(cases.Count>0) item.Branches.Add(new Instruction {Kind="case",Text=string.Join(", ",cases),Body=body});
                if(sec.Labels.Any(l=>l is DefaultSwitchLabelSyntax)) item.Branches.Add(new Instruction {Kind="else",Text="Else",Body=body});
            }
        } finally { loops.Pop(); }
        item.Branches=item.Branches.OrderBy(b=>b.Kind=="else"?1:0).ToList();
        return item;
    }
    private Instruction ReadTry(TryStatementSyntax tr)
    {
        var x=Item("try", ""); x.Body=ReadList(tr.Block.Statements); x.HasFinally=tr.Finally is not null;
        if(tr.Finally is not null) x.Other=ReadList(tr.Finally.Block.Statements);
        foreach(var c in tr.Catches) {
            var type=c.Declaration?.Type.ToString()??""; var name=c.Declaration?.Identifier.Text??"";
            var h=Item("catch", "CATCH"+(name.Length>0?" "+name+" : "+type:type.Length>0?" "+type:"")+(c.Filter is null?"":" WHEN "+Expr(c.Filter.FilterExpression)));
            h.CatchType=type; h.CatchVariable=name; h.Body=ReadList(c.Block.Statements); x.Branches.Add(h);
        }
        bool Simple(IEnumerable<Instruction> body) => body.All(i=>i.Kind is "process" or "call" or "io" or "throw");
        x.ExpandExceptionFlow=Simple(x.Body)&&Simple(x.Other)&&x.Branches.All(b=>Simple(b.Body))&&tr.Catches.All(c=>c.Filter is null);
        if(!x.ExpandExceptionFlow) { unsafeFlow=true; Warn("Complex C# exception paths are not expanded in the flowchart; structured pseudocode is retained."); }
        return x;
    }
    private Instruction ExpressionItem(ExpressionSyntax e)
    {
        if (e is ThrowExpressionSyntax thrown) return Item("throw", "THROW " + Expr(thrown.Expression));
        if (e is InvocationExpressionSyntax inv && (ConsoleMethod(inv,"WriteLine")||ConsoleMethod(inv,"Write")) && inv.ArgumentList.Arguments.Count<=1 && inv.ArgumentList.Arguments.All(a=>a.NameColon is null && a.RefKindKeyword.RawKind==0))
        {
            return Item("io", (ConsoleMethod(inv,"Write")?"OUTPUT (no newline) ":"OUTPUT ")+(inv.ArgumentList.Arguments.Count==0?"(blank line)":Expr(inv.ArgumentList.Arguments[0].Expression)));
        }
        string text;
        if(e is AssignmentExpressionSyntax a && a.IsKind(SyntaxKind.SimpleAssignmentExpression)) text=Expr(a.Left)+" ← "+Expr(a.Right);
        else if(e is AssignmentExpressionSyntax ca && ca.Left is IdentifierNameSyntax && (ca.OperatorToken.Text is "+=" or "-=" or "*=" or "/=" or "%="))
        {
            var left=Expr(ca.Left); var op=ca.OperatorToken.Text[..^1]; text=left+" ← "+left+" "+(op=="%"?"MOD":op)+" ("+Expr(ca.Right)+")";
            Warn("C# compound assignments are shown conceptually; implicit narrowing conversions are not represented.");
        }
        else if(e is PostfixUnaryExpressionSyntax post && post.Operand is IdentifierNameSyntax && (post.IsKind(SyntaxKind.PostIncrementExpression)||post.IsKind(SyntaxKind.PostDecrementExpression)))
            text=Expr(post.Operand)+" ← "+Expr(post.Operand)+(post.IsKind(SyntaxKind.PostIncrementExpression)?" + 1":" - 1");
        else if(e is PrefixUnaryExpressionSyntax pre && pre.Operand is IdentifierNameSyntax && (pre.IsKind(SyntaxKind.PreIncrementExpression)||pre.IsKind(SyntaxKind.PreDecrementExpression)))
            text=Expr(pre.Operand)+" ← "+Expr(pre.Operand)+(pre.IsKind(SyntaxKind.PreIncrementExpression)?" + 1":" - 1");
        else { text=Expr(e); if(e is AssignmentExpressionSyntax or PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax) Warn("Some C# compound operations are retained to preserve evaluation order or event subscription semantics."); }
        return Item(HasConsoleInput(e)||HasFileIo(e)?"io":e is InvocationExpressionSyntax || (e is AssignmentExpressionSyntax assignment && HasCreation(assignment.Right)) ?"call":"process",text);
    }
    private static bool ConsoleMethod(InvocationExpressionSyntax i, string name) => i.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.ValueText==name && (m.Expression.ToString() is "Console" or "System.Console" or "global::System.Console");
    private static bool HasConsoleInput(SyntaxNode n) => n.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(i=>ConsoleMethod(i,"ReadLine") && i.ArgumentList.Arguments.Count==0);
    private string Expr(SyntaxNode node)
    {
        // Rewrite actual Roslyn tokens, never text inside strings/comments. Keep lambdas,
        // interpolated strings and queries intact rather than translating their internals.
        var raw=node.ToString(); int start=node.SpanStart;
        if (node.DescendantNodesAndSelf().Any(n=>n is CheckedExpressionSyntax or AwaitExpressionSyntax or ConditionalAccessExpressionSyntax or SwitchExpressionSyntax))
            Warn("Advanced C# expressions are retained in source-style notation; their internal execution paths are not expanded.");
        var protectedNodes=node.DescendantNodesAndSelf().Where(n=>n is AnonymousFunctionExpressionSyntax or QueryExpressionSyntax or InterpolatedStringExpressionSyntax).ToList();
        bool Protected(SyntaxNodeOrToken n)=>protectedNodes.Any(p=>p.Span.Contains(n.Span));
        var edits=new List<(int Start,int Length,string Text)>();
        foreach(var creation in node.DescendantNodesAndSelf().Where(c=>c is ObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax).Where(c=>!Protected(c)))
        {
            if (edits.Any(e=>creation.SpanStart-start>=e.Start&&creation.Span.End-start<=e.Start+e.Length)) continue;
            if ((creation is ObjectCreationExpressionSyntax obj ? CreationLabel(obj) : ArrayCreationLabel((ArrayCreationExpressionSyntax)creation)) is string replacement) edits.Add((creation.SpanStart-start,creation.Span.Length,replacement));
        }
        foreach(var i in node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Where(i=>ConsoleMethod(i,"ReadLine")&&i.ArgumentList.Arguments.Count==0&&!Protected(i)&&!edits.Any(e=>i.SpanStart-start>=e.Start&&i.Span.End-start<=e.Start+e.Length)))
            edits.Add((i.SpanStart-start,i.Span.Length,"USERINPUT"));
        string Spaced(SyntaxToken t, string word) { int a=t.SpanStart-start,b=t.Span.End-start; return (a>0 && !char.IsWhiteSpace(raw[a-1])?" ":"")+word+(b<raw.Length && !char.IsWhiteSpace(raw[b])?" ":""); }
        foreach(var token in node.DescendantTokens())
        {
            if(Protected(token)||edits.Any(e=>token.SpanStart-start>=e.Start&&token.Span.End-start<=e.Start+e.Length)) continue;
            string? value=token.Kind() switch {
                SyntaxKind.EqualsEqualsToken=>"=", SyntaxKind.ExclamationEqualsToken=>"≠", SyntaxKind.LessThanEqualsToken=>"≤", SyntaxKind.GreaterThanEqualsToken=>"≥",
                SyntaxKind.AmpersandAmpersandToken=>Spaced(token, "AND THEN"), SyntaxKind.BarBarToken=>Spaced(token, "OR ELSE"),
                SyntaxKind.ExclamationToken when token.Parent is PrefixUnaryExpressionSyntax p && p.IsKind(SyntaxKind.LogicalNotExpression)=>"NOT ",
                SyntaxKind.PercentToken=>Spaced(token, "MOD"), SyntaxKind.TrueKeyword=>"TRUE", SyntaxKind.FalseKeyword=>"FALSE", SyntaxKind.NullKeyword=>"NULL",
                SyntaxKind.ThisKeyword=>"ME", SyntaxKind.BaseKeyword=>"PARENT", SyntaxKind.NewKeyword=>"NEW", _=>null };
            if(value is not null) edits.Add((token.SpanStart-start,token.Span.Length,value));
        }
        var sb=new StringBuilder(raw);
        foreach(var edit in edits.OrderByDescending(e=>e.Start)) {sb.Remove(edit.Start,edit.Length);sb.Insert(edit.Start,edit.Text);}
        // Do not normalize arbitrary whitespace in string literals. Whitespace around
        // translated operators may remain doubled, which is harmless and readable.
        return sb.ToString().Trim();
    }
    internal static string Pseudocode(Routine r)
    {
        var lines=new List<string>();
        if(!string.IsNullOrEmpty(r.SignatureNote)) lines.Add(r.SignatureNote);
        if(r.Signature.Length>0) lines.Add(r.Signature);
        Write(r.Body,r.Signature.Length>0?1:0,lines);
        if(r.Footer.Length>0) lines.Add(r.Footer);
        return string.Join("\n",lines);
    }
    private static void Write(IEnumerable<Instruction> body,int depth,List<string> lines)
    {
        string pad=new(' ',depth*4);
        foreach(var x in body) {
            void Line(string t)=>lines.Add(pad+t);
            switch(x.Kind) {
                case "if": Line("IF "+x.Text+" THEN");Write(x.Body,depth+1,lines);if(x.Other.Count>0){Line("ELSE");Write(x.Other,depth+1,lines);}Line("ENDIF");break;
                case "while": Line("WHILE "+x.Text);Write(x.Body,depth+1,lines);Line("ENDWHILE");break;
                case "do": Line("REPEAT");Write(x.Body,depth+1,lines);Line("UNTIL NOT ("+x.Text+")");break;
                case "for": case "foreach": Line(x.Text);Write(x.Body,depth+1,lines);Line("ENDFOR");break;
                case "select": Line("CASE OF "+x.Text);foreach(var b in x.Branches){Line("    "+(b.Kind=="else"?"OTHERWISE":b.Text)+":");Write(b.Body,depth+2,lines);}Line("ENDCASE");break;
                case "using": Line("USING "+x.Text);Write(x.Body,depth+1,lines);Line("ENDUSING // Automatically dispose "+x.Detail+" on exit");break;
                case "try": Line("TRY");Write(x.Body,depth+1,lines);foreach(var b in x.Branches){Line(b.Text);Write(b.Body,depth+1,lines);}if(x.HasFinally){Line("FINALLY");Write(x.Other,depth+1,lines);}Line("ENDTRY");break;
                case "unsupported": Line("// UNSUPPORTED C#: "+x.Text.Replace("\r","").Replace("\n","\n"+pad+"// "));break;
                default: Line(x.Text);break;
            }
        }
    }
}
