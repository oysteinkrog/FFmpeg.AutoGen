using System;
using System.Collections.Generic;
using CppSharp;
using CppSharp.AST;
using CppSharp.Parser;
using ClangParser = CppSharp.ClangParser;

namespace FFmpeg.AutoGen.CppSharpUnsafeGenerator;

internal sealed class Parser
{
    private bool _hasParsingErrors;

    public string[] Defines { get; init; } = Array.Empty<string>();
    public string[] IncludeDirs { get; init; } = Array.Empty<string>();

    public ASTContext Parse(params string[] sourceFiles)
    {
        _hasParsingErrors = false;
        var context = ParseInternal(sourceFiles);
        if (_hasParsingErrors)
            throw new InvalidOperationException();
        return context;
    }

    private ASTContext ParseInternal(string[] sourceFiles)
    {
        var parserOptions = new ParserOptions
        {
            Verbose = true,
            ASTContext = new CppSharp.Parser.AST.ASTContext(),
            LanguageVersion = LanguageVersion.C99_GNU
        };

        // Use clang instead of MSVC on Linux
        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            parserOptions.AddIncludeDirs("/usr/lib/llvm-14/lib/clang/14.0.0/include");
            parserOptions.AddIncludeDirs("/usr/include");
            parserOptions.AddIncludeDirs("/usr/include/x86_64-linux-gnu");
            parserOptions.AddIncludeDirs("/usr/local/include");
            parserOptions.AddDefines("__STDC_CONSTANT_MACROS");
            parserOptions.AddDefines("__STDC_LIMIT_MACROS");
        }
        else
        {
            parserOptions.SetupMSVC(VisualStudioVersion.VS2019);
        }

        foreach (var includeDir in IncludeDirs) parserOptions.AddIncludeDirs(includeDir);

        foreach (var define in Defines) parserOptions.AddDefines(define);
        var result = ClangParser.ParseSourceFiles(sourceFiles, parserOptions);
        OnSourceFileParsed(sourceFiles, result);
        return ClangParser.ConvertASTContext(parserOptions.ASTContext);
    }

    private void OnSourceFileParsed(IEnumerable<string> files, ParserResult result)
    {
        switch (result.Kind)
        {
            case ParserResultKind.Success:
                Diagnostics.Message("Parsed '{0}'", string.Join(", ", files));
                break;
            case ParserResultKind.Error:
                Diagnostics.Error("Error parsing '{0}'", string.Join(", ", files));
                _hasParsingErrors = true;
                break;
            case ParserResultKind.FileNotFound:
                Diagnostics.Error("A file from '{0}' was not found", string.Join(",", files));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        for (uint i = 0; i < result.DiagnosticsCount; ++i)
        {
            var diagnostics = result.GetDiagnostics(i);

            var message =
                $"{diagnostics.FileName}({diagnostics.LineNumber},{diagnostics.ColumnNumber}): {diagnostics.Level.ToString().ToLower()}: {diagnostics.Message}";
            Diagnostics.Message(message);
        }
    }
}
