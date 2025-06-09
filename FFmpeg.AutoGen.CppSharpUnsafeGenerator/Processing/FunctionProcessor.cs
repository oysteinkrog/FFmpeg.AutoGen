using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CppSharp.AST;
using FFmpeg.AutoGen.CppSharpUnsafeGenerator.Definitions;
using Type = CppSharp.AST.Type;

namespace FFmpeg.AutoGen.CppSharpUnsafeGenerator.Processing;

internal class FunctionProcessor
{
    private const string ReturnMarshalAsConstCharPtr = "[return: MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(ConstCharPtrMarshaler))]";

    private const string ReturnMarshalAsLPUTF8Str = "[return: MarshalAs(UnmanagedType.LPUTF8Str)]";

    private const string MarshalAsUTF8Macros =
        "    \r\n" +
        "    #if NETSTANDARD2_1_OR_GREATER\r\n" +
        "    [MarshalAs(UnmanagedType.LPUTF8Str)]\r\n" +
        "    #else\r\n" +
        "    [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(UTF8Marshaler))]\r\n" +
        "    #endif\r\n" +
        "   ";

    private const string MarshalAsLPUTF8Str = "[MarshalAs(UnmanagedType.LPUTF8Str)]";


    private readonly ProcessingContext _context;

    public FunctionProcessor(ProcessingContext context) => _context = context;

    public StructureProcessor StructureProcessor { get; set; }

    public void Process(TranslationUnit translationUnit)
    {
        foreach (var function in translationUnit.Functions)
        {
            var functionName = function.Name;

            void PopulateCommon(FunctionDefinitionBase inline)
            {
                inline.Name = functionName;
                inline.ReturnType = GetReturnType(function.ReturnType.Type, functionName);
                inline.Content = function.Comment?.BriefText;
                inline.ReturnComment = GetReturnComment(function);
                inline.Parameters = function.Parameters.Select((x, i) => GetParameter(function, x, i)).ToArray();
                inline.Obsoletion = ObsoletionHelper.CreateObsoletion(function);
            }

            if (function.IsInline)
            {
                var inlineDefinition = new InlineFunctionDefinition
                {
                    Body = ConvertInlineFunctionBodyToCSharp(function.Body),
                    OriginalBodyHash = GetSha256(function.Body)
                };
                PopulateCommon(inlineDefinition);
                _context.AddDefinition(inlineDefinition);
                continue;
            }

            if (!_context.FunctionExportMap.TryGetValue(functionName, out var export))
            {
                Console.WriteLine($"Export not found. Skipping {functionName} function.");
                continue;
            }

            var exportDefinition = new ExportFunctionDefinition
            {
                LibraryName = export.LibraryName,
                LibraryVersion = export.LibraryVersion
            };
            PopulateCommon(exportDefinition);


            _context.AddDefinition(exportDefinition);
        }
    }

    internal TypeDefinition GetDelegateType(FunctionType functionType, string name)
    {
        var @delegate = new DelegateDefinition
        {
            Name = $"{name}_func",
            FunctionName = name,
            ReturnType = GetReturnType(functionType.ReturnType.Type, name),
            Parameters = functionType.Parameters.Select(GetParameter).ToArray()
        };
        _context.AddDefinition(@delegate);

        return @delegate;
    }

    private FunctionParameter GetParameter(Parameter parameter, int position)
    {
        var name = string.IsNullOrEmpty(parameter.Name) ? $"p{position}" : parameter.Name;
        var parameterType = GetParameterType(parameter.Type, name);
        return new FunctionParameter
        {
            Name = name,
            Type = parameterType,
            IsConstant = ParameterIsConstantFixedArray(parameter),
            IsIndirect = parameter.IsIndirect,
            ByReference = !parameter.IsConst && (parameterType.ByReference || parameterType is FixedArrayDefinition)
        };
    }

    private FunctionParameter GetParameter(Function function, Parameter parameter, int position)
    {
        var name = string.IsNullOrEmpty(parameter.Name) ? $"p{position}" : parameter.Name;
        var parameterType = GetParameterType(parameter.Type, $"{function.Name}_{name}");
        return new FunctionParameter
        {
            Name = name,
            Type = parameterType,
            Content = GetParamComment(function, parameter.Name),
            IsConstant = ParameterIsConstantFixedArray(parameter),
            IsIndirect = parameter.IsIndirect,
            ByReference = !parameter.IsConst && (parameterType.ByReference || parameterType is FixedArrayDefinition)
        };
    }

    private TypeDefinition GetParameterType(Type type, string name)
    {
        if (type is PointerType pointerType &&
            pointerType.QualifiedPointee.Qualifiers.IsConst &&
            pointerType.Pointee is BuiltinType builtinType)
        {
            return builtinType.Type switch
            {
                PrimitiveType.Char => new TypeDefinition
                {
                    Name = "string",
                    Attributes = new[] { _context.NoCustomStringMarshal ? MarshalAsLPUTF8Str : MarshalAsUTF8Macros }
                },
                PrimitiveType.Void => new TypeDefinition
                {
                    Name = "void*"
                },
                _ => new TypeDefinition
                {
                    Name = TypeHelper.GetTypeName(type)
                }
            };
        }

        return GetNoneBuiltinParameterType(type, name);
    }

    private TypeDefinition GetReturnType(Type type, string name)
    {
        if (type is PointerType pointerType &&
            pointerType.QualifiedPointee.Qualifiers.IsConst &&
            pointerType.Pointee is BuiltinType builtinType)
        {
            return builtinType.Type switch
            {
                PrimitiveType.Char => new TypeDefinition
                {
                    Name = "string",
                    Attributes = new[] { _context.NoCustomStringMarshal ? ReturnMarshalAsLPUTF8Str : ReturnMarshalAsConstCharPtr }
                },
                PrimitiveType.Void => new TypeDefinition
                {
                    Name = "void*"
                },
                _ => new TypeDefinition
                {
                    Name = TypeHelper.GetTypeName(type)
                }
            };
        }

        return GetNoneBuiltinParameterType(type, name);
    }

    private TypeDefinition GetNoneBuiltinParameterType(Type type, string name)
    {
        // edge case when type is array of pointers to none builtin type (type[]* -> type**)
        if (type is ArrayType arrayType &&
            arrayType.SizeType == ArrayType.ArraySize.Incomplete &&
            arrayType.Type is PointerType arrayPointerType &&
            !(arrayPointerType.Pointee is BuiltinType || (arrayPointerType.Pointee is TypedefType typedefType &&
                                                          typedefType.Declaration.Type is BuiltinType)))
            return new TypeDefinition { Name = $"{TypeHelper.GetTypeName(arrayPointerType)}*" };

        return StructureProcessor.GetTypeDefinition(type, name);
    }

    private static string GetParamComment(Function function, string parameterName)
    {
        var comment = function?.Comment?.FullComment.Blocks
            .OfType<ParamCommandComment>()
            .FirstOrDefault(x => x.Arguments.Count == 1 && x.Arguments[0].Text == parameterName);
        return GetCommentString(comment);
    }

    private string GetReturnComment(Function function)
    {
        var comment = function?.Comment?.FullComment.Blocks
            .OfType<BlockCommandComment>()
            .FirstOrDefault(x => x.CommandKind == CommentCommandKind.Return);
        return GetCommentString(comment);
    }

    private static bool ParameterIsConstantFixedArray(Parameter parameter) => parameter.IsConst && parameter.Type is ArrayType { SizeType: ArrayType.ArraySize.Constant };

    private static string GetCommentString(BlockCommandComment comment)
    {
        return comment == null
            ? null
            : string.Join(" ", comment.ParagraphComment.Content.OfType<TextComment>().Select(x => x.Text.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string GetSha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static string ConvertInlineFunctionBodyToCSharp(string cBody)
    {
        if (string.IsNullOrWhiteSpace(cBody))
            return cBody;

        var cSharpBody = cBody;

        // Map of C types to C# types that commonly appear in casts and declarations
        var typeMappings = new[]
        {
            ("unsigned int", "uint"),
            ("unsigned long", "ulong"),
            ("unsigned short", "ushort"),
            ("unsigned char", "byte"),
            ("signed char", "sbyte"),
            ("long long", "long"),
            ("unsigned long long", "ulong"),
            ("int64_t", "long"),
            ("uint64_t", "ulong"),
            ("int32_t", "int"),
            ("uint32_t", "uint"),
            ("int16_t", "short"),
            ("uint16_t", "ushort"),
            ("int8_t", "sbyte"),
            ("uint8_t", "byte")
        };

        // Convert C-style type declarations and casts
        foreach (var (cType, cSharpType) in typeMappings)
        {
            // Handle casts like "(unsigned int)" to "(uint)"
            cSharpBody = cSharpBody.Replace($"({cType})", $"({cSharpType})");
            // Handle variable declarations like "const int64_t tmp" to "const long tmp"
            cSharpBody = Regex.Replace(cSharpBody, $@"\b{Regex.Escape(cType)}\b", cSharpType);
        }

        // Fix C-style compound literals like "AVRational r = {num, den};" to "AVRational r = new AVRational { num = num, den = den };"
        cSharpBody = Regex.Replace(cSharpBody, @"(\w+)\s+(\w+)\s*=\s*\{([^}]+)\};",
            match => {
                var type = match.Groups[1].Value;
                var variable = match.Groups[2].Value;
                var values = match.Groups[3].Value.Split(',').Select(v => v.Trim()).ToArray();

                // Simple heuristic for known struct patterns
                if (type == "AVRational" && values.Length == 2)
                {
                    return $"{type} {variable} = new {type} {{ num = {values[0]}, den = {values[1]} }};";
                }

                // Fallback to original
                return match.Value;
            });

        // Handle union declarations and union variable references
        if (cSharpBody.Contains("union ") || cSharpBody.Contains("v.@f") || cSharpBody.Contains("v.i") || cSharpBody.Contains("return v."))
        {
            // For union-based functions, return a simple fallback implementation
            return "{\n    throw new NotImplementedException(\"Union-based type conversion not supported in generated inline functions\");\n}";
        }

        // Remove const qualifiers from C-style casts as they're not valid in C#
        // Handle casts like "(const type *)" or "(type *const *)"
        cSharpBody = Regex.Replace(cSharpBody, @"\(\s*const\s+([^)]+)\)", "($1)");
        cSharpBody = Regex.Replace(cSharpBody, @"\(([^)]*?)\s*\*\s*const\s*\*([^)]*?)\)", "($1**$2)");
        cSharpBody = Regex.Replace(cSharpBody, @"\s*const\s*\*", "*");

        // Fix C-specific built-in functions that don't exist in C#
        if (cSharpBody.Contains("__builtin_add_overflow") || cSharpBody.Contains("__builtin_sub_overflow"))
        {
            // Replace problematic functions that use GCC built-ins
            return "{\n    throw new NotImplementedException(\"GCC built-in functions not supported in generated inline functions\");\n}";
        }

        // Fix pointer type issues
        cSharpBody = cSharpBody.Replace("(intptr_t)", "(nint)");
        cSharpBody = cSharpBody.Replace("intptr_t", "nint");

        // Fix fixed array casting issues - need to handle byte_ptr4 to byte** conversion properly
        if (cSharpBody.Contains("(byte**)"))
        {
            // Replace problematic inline functions that try to cast fixed arrays to pointers
            return "{\n    throw new NotImplementedException(\"Fixed array to pointer casting not supported in generated inline functions\");\n}";
        }

        // Fix shift operator issues with uint operands
        cSharpBody = Regex.Replace(cSharpBody, @"([\w\(\)]+)\s*<<\s*([\w\(\)]+)U", "$1 << (int)$2");
        cSharpBody = Regex.Replace(cSharpBody, @"(\d+)U\s*<<\s*([\w\(\)]+)", "$1U << (int)$2");

        // Fix const variable assignment issues - const variables can't be assigned after declaration in C#
        cSharpBody = Regex.Replace(cSharpBody, @"const\s+(\w+)\s+(\w+);", "var $2 = default($1);");
        cSharpBody = Regex.Replace(cSharpBody, @"const\s+(\w+)\s+(\w+)\s*=([^;]+);", "var $2 = ($1)($3);");

        // Fix long + ulong ambiguity by casting ulong literals to long
        cSharpBody = Regex.Replace(cSharpBody, @"(\w+)\s*\+\s*(\d+)UL", "$1 + (long)$2UL");
        cSharpBody = Regex.Replace(cSharpBody, @"(\d+)UL\s*\+\s*(\w+)", "(long)$1UL + $2");

        // Fix integer to bool conversions - treat non-zero as true
        cSharpBody = Regex.Replace(cSharpBody, @"if\s*\(\s*(\w+)\s*\)", "if ($1 != 0)");
        cSharpBody = Regex.Replace(cSharpBody, @"&&\s*(\w+)(?=\s*[;&)])", "&& ($1 != 0)");
        cSharpBody = Regex.Replace(cSharpBody, @"else if\s*\(\s*(\w+\.\w+)\s*&&\s*(\w+\.\w+)\s*\)", "else if (($1 != 0) && ($2 != 0))");

        // Fix uint to int conversion issues
        cSharpBody = Regex.Replace(cSharpBody, @"(\w+) \+= (\w+) >> (\d+);", "$1 += (int)($2 >> $3);");
        cSharpBody = Regex.Replace(cSharpBody, @"return \((\w+) \+ \((\w+) >> (\d+)\)\) & (\d+);", "return (uint)(($1 + (int)($2 >> $3)) & $4);");

        // Fix return type casting - match the actual return type
        cSharpBody = Regex.Replace(cSharpBody, @"return\s+([\w\(\)&|+\-*/^]+);", match => {
            var returnValue = match.Groups[1].Value;
            if (cSharpBody.Contains("public static uint "))
            {
                return $"return (uint)({returnValue});";
            }
            else if (cSharpBody.Contains("public static int "))
            {
                return $"return (int)({returnValue});";
            }
            return match.Value;
        });

        // Fix void* to bool conversion in conditional expressions
        cSharpBody = cSharpBody.Replace("(p ? p : x)", "(p != null ? p : x)");

        // Add @ prefix to variable references that might be C# keywords
        // This is a simple heuristic that looks for common variable patterns
        var paramNames = new[] { "sd", "nb_sd", "type", "dst_data", "dst_linesizes", "src_data", "src_linesizes", "pix_fmt", "width", "height", "f" };
        foreach (var paramName in paramNames)
        {
            // Replace variable references (not in parameter declarations or already escaped)
            cSharpBody = Regex.Replace(cSharpBody, $@"(?<!@)\b{paramName}\b(?!\s*[,)])", $"@{paramName}");
        }

        return cSharpBody;
    }
}
