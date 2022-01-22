using System.Text;
using Cesium.CodeGen;
using Cesium.CodeGen.Contexts;
using Cesium.CodeGen.Extensions;
using Cesium.Parser;
using Cesium.Preprocessor;
using Mono.Cecil;
using Yoakke.C.Syntax;
using Yoakke.Streams;

namespace Cesium.Compiler;

internal static class Compilation
{
    public static async Task<int> Compile(
        IEnumerable<string> inputFilePaths,
        string outputFilePath,
        TargetRuntimeDescriptor targetRuntime)
    {
        Console.WriteLine($"Generating assembly {outputFilePath}.");
        var assemblyContext = CreateAssembly(outputFilePath, targetRuntime);

        foreach (var inputFilePath in inputFilePaths)
        {
            Console.WriteLine($"Processing input file \"{inputFilePath}\".");
            await GenerateCode(assemblyContext, inputFilePath);
        }

        SaveAssembly(assemblyContext, targetRuntime.Kind, outputFilePath);

        return 0;
    }

    private static AssemblyContext CreateAssembly(string outputFilePath, TargetRuntimeDescriptor targetRuntime)
    {
        var moduleKind = Path.GetExtension(outputFilePath).ToLowerInvariant() switch
        {
            ".exe" => ModuleKind.Console,
            ".dll" => ModuleKind.Dll,
            var o => throw new Exception($"Unknown file extension: {o}.")
        };
        var assemblyName = Path.GetFileNameWithoutExtension(outputFilePath);
        var defaultImportAssemblies = new []
        {
            typeof(Math).Assembly, // System.Runtime.dll
            typeof(Console).Assembly, // System.Console.dll
            typeof(Runtime.StdLibFunctions).Assembly
        };
        return AssemblyContext.Create(
            new AssemblyNameDefinition(assemblyName, new Version()),
            moduleKind,
            targetRuntime,
            defaultImportAssemblies);
    }

    private static Task<string> Preprocess(string compilationFileDirectory, TextReader reader)
    {
        var currentProcessPath = Path.GetDirectoryName(Environment.ProcessPath)
                                 ?? throw new Exception("Cannot determine path to the compiler executable.");

        var stdLibDirectory = Path.Combine(currentProcessPath, "stdlib");
        var includeContext = new FileSystemIncludeContext(stdLibDirectory, compilationFileDirectory);
        var preprocessorLexer = new CPreprocessorLexer(reader);
        var preprocessor = new CPreprocessor(preprocessorLexer, includeContext);
        return preprocessor.ProcessSource();
    }

    private static async Task GenerateCode(AssemblyContext context, string inputFilePath)
    {
        var compilationFileDirectory = Path.GetDirectoryName(inputFilePath)!;

        await using var input = new FileStream(inputFilePath, FileMode.Open);
        using var reader = new StreamReader(input, Encoding.UTF8);

        var content = await Preprocess(compilationFileDirectory, reader);
        var lexer = new CLexer(content);
        var parser = new CParser(lexer);
        var translationUnitParseError = parser.ParseTranslationUnit();
        if (translationUnitParseError.IsError)
        {
            switch (translationUnitParseError.Error.Got)
            {
                case CToken token:
                    throw new Exception($"Error during parsing {inputFilePath}. Error at position {translationUnitParseError.Error.Position}. Got {token.LogicalText}.");
                case char ch:
                    throw new Exception($"Error during parsing {inputFilePath}. Error at position {translationUnitParseError.Error.Position}. Got {ch}.");
                default:
                    throw new Exception($"Error during parsing {inputFilePath}. Error at position {translationUnitParseError.Error.Position}.");
            }
        }

        var translationUnit = translationUnitParseError.Ok.Value;

        if (parser.TokenStream.Peek().Kind != CTokenType.End)
            throw new Exception($"Excessive output after the end of a translation unit at {lexer.Position}.");

        context.EmitTranslationUnit(translationUnit.ToIntermediate());
    }

    private static void SaveAssembly(
        AssemblyContext context,
        SystemAssemblyKind targetFrameworkKind,
        string outputFilePath)
    {
        context.Assembly.Write(outputFilePath);

        // This part should go to Cesium.SDK eventually together with
        // runtimeconfig.json generation
        var compilerRuntime = Path.Combine(AppContext.BaseDirectory, "Cesium.Runtime.dll");
        var outputExecutablePath = Path.GetDirectoryName(outputFilePath) ?? Environment.CurrentDirectory;
        var applicationRuntime = Path.Combine(outputExecutablePath, "Cesium.Runtime.dll");
        File.Copy(compilerRuntime, applicationRuntime, true);

        if (context.Module.Kind == ModuleKind.Console && targetFrameworkKind == SystemAssemblyKind.SystemRuntime)
        {
            var runtimeConfigFilePath = Path.ChangeExtension(outputFilePath, "runtimeconfig.json");
            Console.WriteLine($"Generating a .NET 6 runtime config at {runtimeConfigFilePath}.");
            File.WriteAllText(runtimeConfigFilePath, RuntimeConfig.EmitNet6());
        }
    }
}
