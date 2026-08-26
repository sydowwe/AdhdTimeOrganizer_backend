using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// FixXUnit1051
// -------------
// Rewrites calls flagged by xUnit1051 ("...should use TestContext.Current.CancellationToken")
// by appending TestContext.Current.CancellationToken as the LAST argument of the flagged
// invocation. Works off the locations reported by `dotnet build` so it only touches the exact
// calls the analyzer complained about — no guessing, no semantic model needed.
//
// Usage:
//   FixXUnit1051 <warnings-file> [path-filter]
//     <warnings-file>  text file containing MSBuild output lines (the build log)
//     [path-filter]    optional case-insensitive substring; only files whose full path
//                      contains it are touched (use it to go folder-by-folder)

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: FixXUnit1051 <warnings-file> [path-filter]");
    return 2;
}

var warningsFile = args[0];
var pathFilter = args.Length > 1 ? args[1] : null;

if (!File.Exists(warningsFile))
{
    Console.Error.WriteLine($"warnings file not found: {warningsFile}");
    return 2;
}

// MSBuild line: <abs-path>.cs(<line>,<col>): warning xUnit1051: ...
var lineRegex = new Regex(@"^(?<file>.*\.cs)\((?<line>\d+),(?<col>\d+)\): warning xUnit1051",
    RegexOptions.Compiled);

// file -> set of (line, col) one-based positions
var byFile = new Dictionary<string, HashSet<(int Line, int Col)>>(StringComparer.OrdinalIgnoreCase);

foreach (var raw in File.ReadLines(warningsFile))
{
    var m = lineRegex.Match(raw.Trim());
    if (!m.Success) continue;

    var file = m.Groups["file"].Value;
    if (pathFilter is not null &&
        file.IndexOf(pathFilter, StringComparison.OrdinalIgnoreCase) < 0)
    {
        continue;
    }

    if (!byFile.TryGetValue(file, out var set))
    {
        set = new HashSet<(int, int)>();
        byFile[file] = set;
    }
    set.Add((int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["col"].Value)));
}

if (byFile.Count == 0)
{
    Console.WriteLine("No xUnit1051 warnings matched. Nothing to do.");
    return 0;
}

var ctArg = SyntaxFactory.Argument(
    SyntaxFactory.ParseExpression("TestContext.Current.CancellationToken"))
    .WithLeadingTrivia(SyntaxFactory.Space);

// Methods where the CancellationToken is NOT a simple trailing optional parameter, so
// appending it positionally would bind to the wrong overload (e.g. DbSet.FindAsync takes
// (params object?[]) vs (object?[], CancellationToken) — appending makes the token a key
// value). These are reported and left for a human to fix.
var skipMethods = new HashSet<string>(StringComparer.Ordinal) { "FindAsync" };

int filesChanged = 0, callsFixed = 0, skipped = 0;
var manual = new List<string>();

foreach (var (file, positions) in byFile.OrderBy(kvp => kvp.Key))
{
    if (!File.Exists(file))
    {
        Console.Error.WriteLine($"  (missing) {file}");
        continue;
    }

    var rawBytes = File.ReadAllBytes(file);
    var hadBom = rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF;
    var original = File.ReadAllText(file);
    var text = SourceText.From(original);
    var tree = CSharpSyntaxTree.ParseText(text);
    var root = tree.GetRoot();

    // Resolve each (line,col) to the OUTERMOST invocation expression starting at that position.
    var targets = new HashSet<InvocationExpressionSyntax>();
    foreach (var (line, col) in positions)
    {
        if (line - 1 >= text.Lines.Count) continue;
        var pos = text.Lines[line - 1].Start + (col - 1);
        if (pos < 0 || pos >= text.Length) continue;

        var token = root.FindToken(pos);
        // Among invocations whose span starts exactly at pos, take the largest (outermost) —
        // that is the trailing call in a chain like CreateClient().PostAsJsonAsync(...).
        InvocationExpressionSyntax? best = null;
        foreach (var inv in token.Parent!.AncestorsAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (inv.SpanStart != pos) continue;
            if (best is null || inv.Span.End > best.Span.End) best = inv;
        }

        if (best is null)
        {
            Console.Error.WriteLine($"  (no invocation) {Path.GetFileName(file)}:{line},{col}");
            skipped++;
            continue;
        }

        // Skip methods whose CancellationToken isn't a trailing optional parameter.
        var methodName = best.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            GenericNameSyntax g => g.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
        if (methodName is not null && skipMethods.Contains(methodName))
        {
            manual.Add($"{file}:{line}  ({methodName} — fix by hand)");
            skipped++;
            continue;
        }

        // Idempotency: skip if it already passes the token as the last argument.
        var lastArg = best.ArgumentList.Arguments.LastOrDefault();
        if (lastArg is not null &&
            lastArg.Expression.ToString().Replace(" ", "")
                .EndsWith("TestContext.Current.CancellationToken"))
        {
            skipped++;
            continue;
        }

        targets.Add(best);
    }

    if (targets.Count == 0) continue;

    var newRoot = root.ReplaceNodes(targets, (orig, current) =>
    {
        var inv = (InvocationExpressionSyntax)current;
        var newArgs = inv.ArgumentList.AddArguments(ctArg);
        return inv.WithArgumentList(newArgs);
    });

    var newText = newRoot.ToFullString();
    if (newText != original)
    {
        // Preserve the file's original BOM state so unrelated lines don't churn.
        File.WriteAllText(file, newText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: hadBom));
        filesChanged++;
        callsFixed += targets.Count;
        Console.WriteLine($"  fixed {targets.Count,3}  {file}");
    }
}

Console.WriteLine();
Console.WriteLine($"Files changed: {filesChanged}   Calls fixed: {callsFixed}   Skipped: {skipped}");
if (manual.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"Needs manual fix ({manual.Count}):");
    foreach (var m in manual) Console.WriteLine($"  {m}");
}
return 0;
